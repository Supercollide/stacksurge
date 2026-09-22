using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackSurge.UI;
using UnityEngine;
using Unity.Services.Analytics;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using OneSignalSDK;
using OneSignalSDK.Notifications;

#if UNITY_ANDROID
using Unity.Notifications.Android;
#elif UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace StackSurge.Meta
{
    /// <summary>Kinds of remote push this game sends; mirrors the "type" field in the Cloud Code payloads.</summary>
    public enum PushKind
    {
        Unknown,
        FriendRequest,
        FriendRequestAccepted,
        FriendBeatScore
    }

    public class NotificationService : MonoBehaviour
    {
        private static NotificationService _instance;
        public static NotificationService Instance => _instance;

        public const string LeaderboardChannelId = "leaderboard_updates";
        public const string FriendsChannelId = "friends_activity";
        public const string RemindersChannelId = "game_reminders";

        // OneSignal user tags mirroring the local toggles (usable for dashboard segments / filters).
        private const string TagNotifyLeaderboard = "notify_leaderboard";
        private const string TagNotifyFriends = "notify_friends";
        private const string TagNotifyReminders = "notify_reminders";

        private SaveData _save;
        private bool _isInitialized = false;
        private bool _oneSignalReady = false;
        private SynchronizationContext _mainThread;

        /// <summary>
        /// Resolves another player's public social preferences so we can skip pushes they opted out of.
        /// Set by the game bootstrap (backed by FriendsService / Cloud Save).
        /// </summary>
        public Func<string, Task<SocialPrefs>> RecipientPrefsResolver { get; set; }

        /// <summary>Raised on the main thread when the user taps a remote push. The game routes to the right screen.</summary>
        public event Action<PushKind> OnPushOpened;

        public static void EnsureInstance(SaveData save)
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("[NotificationService]");
                _instance = obj.AddComponent<NotificationService>();
                DontDestroyOnLoad(obj);
            }
            _instance.Initialize(save);
        }

        public void Initialize(SaveData save)
        {
            _save = save;
            if (_isInitialized) return;

            _mainThread = SynchronizationContext.Current;

            SetupNotificationChannels();
            InitOneSignal();
            SubscribeToAuthEvents();

            _isInitialized = true;
            Debug.Log("[NotificationService] Initialized notification channels and push SDK.");
        }

        // ── Auth lifecycle ──────────────────────────────────────────────────
        /// <summary>
        /// Keeps the OneSignal binding in step with UGS sign-in state, so a late sign-in (reconnect)
        /// or an account switch is bound without any caller having to remember to do it.
        /// </summary>
        private void SubscribeToAuthEvents()
        {
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth == null) return;
                auth.SignedIn -= OnAuthSignedIn;
                auth.SignedIn += OnAuthSignedIn;
                auth.SignedOut -= OnAuthSignedOut;
                auth.SignedOut += OnAuthSignedOut;
            }
            catch (Exception ex)
            {
                // AuthenticationService.Instance throws if UGS Core was never initialized (offline start).
                Debug.Log("[NotificationService] Auth events unavailable: " + ex.Message);
            }
        }

        private void OnAuthSignedIn()
        {
            string playerId = AuthenticationService.Instance?.PlayerId;
            RunOnMainThread(() => BindUserToPushService(playerId));
        }

        private void OnAuthSignedOut()
        {
            RunOnMainThread(() =>
            {
#if !UNITY_EDITOR
                try { if (_oneSignalReady) OneSignal.Logout(); }
                catch (Exception ex) { Debug.LogWarning("[NotificationService] OneSignal.Logout warning: " + ex.Message); }
#endif
                Debug.Log("[NotificationService] Unbound push service on sign-out.");
            });
        }

        private void OnDestroy()
        {
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth != null)
                {
                    auth.SignedIn -= OnAuthSignedIn;
                    auth.SignedOut -= OnAuthSignedOut;
                }
            }
            catch { /* UGS not initialized */ }
        }

        // ── Main-thread helper ──────────────────────────────────────────────
        /// <summary>SDK callbacks may arrive off the main thread; Unity objects must only be touched on it.</summary>
        private void RunOnMainThread(Action action)
        {
            if (action == null) return;
            if (_mainThread == null || SynchronizationContext.Current == _mainThread)
            {
                action();
            }
            else
            {
                _mainThread.Post(_ => action(), null);
            }
        }

        // ── OneSignal ───────────────────────────────────────────────────────
        private void InitOneSignal()
        {
#if !UNITY_EDITOR
            try
            {
                string appId = OneSignalPushHelper.AppId;
                if (string.IsNullOrEmpty(appId))
                {
                    Debug.LogWarning("[NotificationService] OneSignal App ID missing; remote push disabled.");
                    return;
                }

                OneSignal.Initialize(appId);
                _oneSignalReady = true;
                Debug.Log($"[NotificationService] OneSignal SDK initialized with App ID: {appId}");

                // Foreground pushes: show our own toast and suppress the system banner so it is not shown twice.
                OneSignal.Notifications.ForegroundWillDisplay += OnForegroundWillDisplay;

                // Taps on a push (background or killed app): route to the relevant screen.
                OneSignal.Notifications.Clicked += OnNotificationClicked;

                OneSignal.Notifications.RequestPermissionAsync(true);

                AnalyticsService.Instance.StartDataCollection();

                if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                {
                    BindUserToPushService(AuthenticationService.Instance.PlayerId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NotificationService] OneSignal setup warning: {ex.Message}");
            }
#else
            Debug.Log("[NotificationService] Skipping push registration — not supported in Unity Editor. Run on a real device.");
#endif
        }

        private void OnForegroundWillDisplay(object sender, NotificationWillDisplayEventArgs e)
        {
            try
            {
                e.PreventDefault();
                var notif = e.Notification;
                string title = notif?.Title ?? "StackSurge";
                string body = notif?.Body ?? "";
                PushKind kind = ParsePushKind(notif?.AdditionalData);
                RunOnMainThread(() =>
                {
                    Debug.Log($"[OneSignal] Foreground push: {title} - {body}");
                    InAppNotificationView.Show(title, body, kind == PushKind.Unknown ? NotificationType.Reminder : NotificationType.FriendActivity);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Foreground handler warning: " + ex.Message);
            }
        }

        private void OnNotificationClicked(object sender, NotificationClickEventArgs e)
        {
            PushKind kind = ParsePushKind(e?.Notification?.AdditionalData);
            RunOnMainThread(() =>
            {
                Debug.Log($"[OneSignal] Push opened: {kind}");
                OnPushOpened?.Invoke(kind);
            });
        }

        private static PushKind ParsePushKind(IDictionary<string, object> data)
        {
            if (data == null || !data.TryGetValue("type", out var t) || t == null) return PushKind.Unknown;
            return t.ToString() switch
            {
                "friend_request"          => PushKind.FriendRequest,
                "friend_request_accepted" => PushKind.FriendRequestAccepted,
                "friend_beat_score"       => PushKind.FriendBeatScore,
                _                         => PushKind.Unknown
            };
        }

        /// <summary>
        /// Binds the authenticated UGS PlayerId to OneSignal so Cloud Code can target this device by external ID.
        /// Safe to call again after a late sign-in.
        /// </summary>
        public void BindUserToPushService(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            try
            {
#if !UNITY_EDITOR
                if (_oneSignalReady) OneSignal.Login(playerId);
#endif
                Debug.Log($"[NotificationService] Bound UGS PlayerId '{playerId}' to OneSignal.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NotificationService] OneSignal.Login warning: {ex.Message}");
            }

            SyncPreferenceTags();
        }

        /// <summary>Mirrors the local notification toggles to OneSignal user tags. Call whenever a toggle changes.</summary>
        public void SyncPreferenceTags()
        {
            if (_save == null) return;
#if !UNITY_EDITOR
            if (!_oneSignalReady) return;
            try
            {
                OneSignal.User.AddTags(new Dictionary<string, string>
                {
                    { TagNotifyLeaderboard, _save.NotifyLeaderboardDrops ? "true" : "false" },
                    { TagNotifyFriends,     _save.NotifyFriendActivity   ? "true" : "false" },
                    { TagNotifyReminders,   _save.NotifyReminders        ? "true" : "false" }
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Tag sync warning: " + ex.Message);
            }
#endif
        }

        private void SetupNotificationChannels()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = "default",
                Name = "General Notifications",
                Importance = Importance.High,
                Description = "General game updates and push notifications",
            });
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = LeaderboardChannelId,
                Name = "Leaderboard Rank Updates",
                Importance = Importance.High,
                Description = "Notifications when your position on the leaderboard drops",
            });
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = FriendsChannelId,
                Name = "Friend Activity",
                Importance = Importance.High,
                Description = "Notifications for new friend requests and social updates",
            });
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = RemindersChannelId,
                Name = "Game Reminders & Streaks",
                Importance = Importance.Default,
                Description = "Streak protection reminders and leaderboard reset countdowns",
            });
#endif
        }

        // ── Leaderboard Rank Drop Detection ─────────────────────────────────
        /// <summary>
        /// Compares the player's current global rank with the last one seen for that scope and period.
        /// Daily/weekly boards reset, so a rank from a previous period is treated as "first seen" rather than a drop.
        /// Only call this for the Global filter; friends-relative ranks are not comparable.
        /// </summary>
        public void CheckAndNotifyRankDrop(int currentRank, LeaderboardScope scope, string leaderboardName = "Leaderboard")
        {
            if (_save == null || !_save.NotifyLeaderboardDrops || currentRank <= 0) return;

            string periodKey = PeriodKeyFor(scope);
            int prevRank = GetSavedRank(scope);
            string prevPeriod = GetSavedRankPeriod(scope);

            bool firstSeen = prevRank <= 0 || prevPeriod != periodKey;

            SetSavedRank(scope, currentRank, periodKey);
            LocalProgress.Save(_save);

            if (firstSeen || currentRank <= prevRank) return;

            int placesDropped = currentRank - prevRank;
            string title = "📉 Leaderboard Rank Dropped";
            string message = $"You dropped {placesDropped} place{(placesDropped > 1 ? "s" : "")} — now #{currentRank} on {leaderboardName}. Play to reclaim your spot!";
            InAppNotificationView.Show(title, message, NotificationType.LeaderboardDrop);
        }

        private static string PeriodKeyFor(LeaderboardScope scope)
        {
            DateTime now = DateTime.UtcNow;
            switch (scope)
            {
                case LeaderboardScope.Daily:
                    return now.ToString("yyyy-MM-dd");
                case LeaderboardScope.Weekly:
                {
                    int week = System.Globalization.ISOWeek.GetWeekOfYear(now);
                    int year = System.Globalization.ISOWeek.GetYear(now);
                    return $"{year}-W{week:00}";
                }
                default:
                    return "all";
            }
        }

        private int GetSavedRank(LeaderboardScope scope) => scope switch
        {
            LeaderboardScope.Daily  => _save.LastKnownRankDaily,
            LeaderboardScope.Weekly => _save.LastKnownRankWeekly,
            _                       => _save.LastKnownRankAllTime,
        };

        private string GetSavedRankPeriod(LeaderboardScope scope) => scope switch
        {
            LeaderboardScope.Daily  => _save.LastKnownRankDailyPeriod,
            LeaderboardScope.Weekly => _save.LastKnownRankWeeklyPeriod,
            _                       => "all",
        };

        private void SetSavedRank(LeaderboardScope scope, int rank, string periodKey)
        {
            switch (scope)
            {
                case LeaderboardScope.Daily:
                    _save.LastKnownRankDaily = rank;
                    _save.LastKnownRankDailyPeriod = periodKey;
                    break;
                case LeaderboardScope.Weekly:
                    _save.LastKnownRankWeekly = rank;
                    _save.LastKnownRankWeeklyPeriod = periodKey;
                    break;
                default:
                    _save.LastKnownRankAllTime = rank;
                    break;
            }
        }

        // ── Friend activity: local toasts ───────────────────────────────────
        // Toasts are gated by the LOCAL player's Friend Activity toggle.

        public void ShowIncomingFriendRequestToast(string senderName)
        {
            if (_save != null && !_save.NotifyFriendActivity) return;
            InAppNotificationView.Show("New Friend Request 👋",
                $"{senderName ?? "Someone"} sent you a friend request. Open Friends to respond.",
                NotificationType.FriendActivity);
        }

        public void ShowNowFriendsToast(string friendName)
        {
            if (_save != null && !_save.NotifyFriendActivity) return;
            InAppNotificationView.Show("Friend Request Accepted!",
                $"You and {friendName ?? "your new friend"} are now friends! Compete on the Friends Leaderboard.",
                NotificationType.FriendActivity);
        }

        public void ShowBeatFriendToast(string friendName, int newScore)
        {
            if (_save != null && !_save.NotifyFriendActivity) return;
            InAppNotificationView.Show("New High Score!",
                $"You beat {friendName}'s score with {newScore:N0} points!",
                NotificationType.FriendActivity);
        }

        // ── Friend activity: remote pushes ──────────────────────────────────
        // Pushes are gated by the RECIPIENT's public preference, not the sender's toggle.

        public Task PushFriendRequestAsync(string recipientPlayerId, string senderName)
        {
            return DispatchIfRecipientAllowsAsync(recipientPlayerId, "NotifyFriendRequest", new Dictionary<string, object>
            {
                { "recipientPlayerId", recipientPlayerId },
                { "senderDisplayName", senderName ?? "A StackSurge Player" }
            });
        }

        public Task PushFriendRequestAcceptedAsync(string originalSenderPlayerId)
        {
            return DispatchIfRecipientAllowsAsync(originalSenderPlayerId, "NotifyFriendRequestAccepted", new Dictionary<string, object>
            {
                { "originalSenderPlayerId", originalSenderPlayerId },
                { "acceptorDisplayName",    LocalDisplayName() }
            });
        }

        public Task PushFriendBeatScoreAsync(string beatenFriendPlayerId, int newScore, string scope)
        {
            return DispatchIfRecipientAllowsAsync(beatenFriendPlayerId, "NotifyFriendBeatScore", new Dictionary<string, object>
            {
                { "beatenFriendPlayerId", beatenFriendPlayerId },
                { "scorerDisplayName",    LocalDisplayName() },
                { "newScore",             newScore },
                { "leaderboardScope",     scope ?? "AllTime" }
            });
        }

        private string LocalDisplayName()
        {
            if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn && !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName))
                return AuthenticationService.Instance.PlayerName;
            return !string.IsNullOrEmpty(_save?.PlayerDisplayName) ? _save.PlayerDisplayName : "A StackSurge Player";
        }

        private async Task DispatchIfRecipientAllowsAsync(string recipientPlayerId, string scriptName, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(recipientPlayerId)) return;

            if (RecipientPrefsResolver != null)
            {
                try
                {
                    SocialPrefs prefs = await RecipientPrefsResolver(recipientPlayerId);
                    if (prefs != null && !prefs.notifyFriendActivity)
                    {
                        Debug.Log($"[NotificationService] Skipping '{scriptName}' — recipient {recipientPlayerId} opted out of friend activity pushes.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NotificationService] Recipient prefs lookup failed, sending anyway: " + ex.Message);
                }
            }

            await CallCloudCodePushAsync(scriptName, args);
        }

        /// <summary>
        /// Calls a UGS Cloud Code script. The OneSignal REST key lives only in the deployed script.
        /// </summary>
        private async Task CallCloudCodePushAsync(string scriptName, Dictionary<string, object> args)
        {
            try
            {
                await CloudCodeService.Instance.CallEndpointAsync(scriptName, args);
                Debug.Log($"[NotificationService] Cloud Code push dispatched: {scriptName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NotificationService] Cloud Code push '{scriptName}' failed: {ex.Message}");
            }
        }

        // ── Scheduled Reminders ─────────────────────────────────────────────
        public const int StreakReminderNotificationId = 1001;
        public const int LeaderboardResetNotificationId = 1002;
        public const int InactivityReminderNotificationId = 1003;

        public void CancelAllScheduledNotifications()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllScheduledNotifications();
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
#endif
        }

        /// <summary>Cancels everything and re-schedules only the reminders the player has enabled.</summary>
        public void RescheduleReminders()
        {
            CancelAllScheduledNotifications();
            if (_save == null || !_save.NotifyReminders) return;

            ScheduleStreakProtectionReminder();
            ScheduleLeaderboardResetReminder();
            ScheduleInactivityReminder();
        }

        public void ScheduleStreakProtectionReminder()
        {
            if (_save == null || !_save.NotifyReminders) return;
            if (_save.Streak <= 0) return;

            // LocalProgress tracks play dates in UTC; skip if the streak is already safe for today.
            string todayUtc = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
            bool playedToday = _save.LastPlayDate == todayUtc;

            DateTime targetTime = DateTime.Today.AddHours(20);
            if (playedToday || DateTime.Now >= targetTime)
                targetTime = targetTime.AddDays(1);

            ScheduleLocalNotification(
                "Protect Your Daily Streak! 🔥",
                $"You have a {_save.Streak}-day streak! Play StackSurge today to keep your streak multiplier active.",
                targetTime,
                RemindersChannelId,
                StreakReminderNotificationId,
                "streak_reminder"
            );
        }

        public void ScheduleLeaderboardResetReminder()
        {
            if (_save == null || !_save.NotifyReminders) return;

            // Assumes the daily board resets at 00:00 UTC; keep in sync with the UGS dashboard reset schedule.
            DateTime nextResetUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(-2);
            DateTime targetLocal = nextResetUtc.ToLocalTime();

            if (targetLocal > DateTime.Now)
            {
                ScheduleLocalNotification(
                    "Leaderboard Resetting Soon! 🏆",
                    "The Daily Leaderboard resets in 2 hours. Stack high and lock in your top ranking!",
                    targetLocal,
                    RemindersChannelId,
                    LeaderboardResetNotificationId,
                    "leaderboard_reset_reminder"
                );
            }
        }

        public void ScheduleInactivityReminder()
        {
            if (_save == null || !_save.NotifyReminders) return;

            ScheduleLocalNotification(
                "We Miss You in StackSurge! 🧱",
                "Your high score is waiting! Jump back in and see if you can break your personal record.",
                DateTime.Now.AddDays(3),
                RemindersChannelId,
                InactivityReminderNotificationId,
                "inactivity_reminder"
            );
        }

        // ── Low-Level Local Notification Dispatcher ─────────────────────────
        public void ScheduleLocalNotification(
            string title,
            string body,
            DateTime fireTime,
            string channelId,
            int notificationId = -1,
            string iosIdentifier = null)
        {
#if UNITY_ANDROID
            // Icons: register "icon_small" / "icon_large" under Project Settings > Mobile Notifications > Android
            // to use custom drawables. Left unset here so Android falls back to the app icon instead of a blank glyph.
            var androidNotif = new AndroidNotification
            {
                Title = title,
                Text = body,
                FireTime = fireTime
            };

            if (notificationId > 0)
                AndroidNotificationCenter.SendNotificationWithExplicitID(androidNotif, channelId, notificationId);
            else
                AndroidNotificationCenter.SendNotification(androidNotif, channelId);
#elif UNITY_IOS
            var timeTrigger = new iOSNotificationCalendarTrigger
            {
                Year = fireTime.Year,
                Month = fireTime.Month,
                Day = fireTime.Day,
                Hour = fireTime.Hour,
                Minute = fireTime.Minute,
                Second = fireTime.Second
            };

            var iosNotif = new iOSNotification
            {
                Identifier = string.IsNullOrEmpty(iosIdentifier) ? Guid.NewGuid().ToString() : iosIdentifier,
                Title = title,
                Body = body,
                ShowInForeground = true,
                ForegroundPresentationOption = PresentationOption.Alert | PresentationOption.Sound,
                CategoryIdentifier = channelId,
                Trigger = timeTrigger
            };
            iOSNotificationCenter.ScheduleNotification(iosNotif);
#else
            Debug.Log($"[NotificationService Local Simulation] '{title}' - '{body}' scheduled for {fireTime}");
#endif
        }
    }
}

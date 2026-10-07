using System;
using System.Collections;
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
    /// <summary>
    /// What a tapped notification (remote push, local reminder or in-app toast) asks the game to open.
    /// The friend kinds mirror the "type" field in the Cloud Code payloads.
    /// </summary>
    public enum PushKind
    {
        Unknown,
        FriendRequest,
        FriendRequestAccepted,
        FriendBeatScore,
        /// <summary>A local reminder (streak, board reset, comeback) was tapped: land on the main menu, ready to play.</summary>
        Reminder
    }

    public class NotificationService : MonoBehaviour
    {
        private static NotificationService _instance;
        public static NotificationService Instance => _instance;

        /// <summary>Legacy channel from older builds. No longer registered (rank drops are in-game only) and deleted on startup.</summary>
        public const string LeaderboardChannelId = "leaderboard_updates";
        public const string FriendsChannelId = "friends_activity";
        public const string RemindersChannelId = "game_reminders";
        /// <summary>Beat-score pushes. NotifyFriendBeatScore.js targets this ID via existing_android_channel_id.</summary>
        public const string RivalryChannelId = "rivalry";
        private const string LegacyDefaultChannelId = "default";

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

        /// <summary>
        /// Raised on the main thread when the user taps a remote push (kept for compatibility).
        /// The tap is also stored as a pending route; prefer PendingRouteAvailable + TryConsumePendingRoute.
        /// </summary>
        public event Action<PushKind> OnPushOpened;

        /// <summary>
        /// Raised on the main thread whenever a tap (remote push, local reminder or in-app toast) leaves a route
        /// waiting. The game navigates when it is safe and calls TryConsumePendingRoute. On a cold start this can
        /// fire before anyone has subscribed, so also check TryConsumePendingRoute once loading is done.
        /// </summary>
        public event Action PendingRouteAvailable;

        /// <summary>Raised on the main thread when the device's push permission changes or a request completes.</summary>
        public event Action<bool> PushPermissionChanged;

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

            // The app is in the foreground now: clear reminders armed by the last session and
            // pick up a reminder tap that cold-started the app.
            OnAppForegrounded();
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

                // Taps on a push (background or killed app): stored as a pending route for the game.
                OneSignal.Notifications.Clicked += OnNotificationClicked;

                // Keeps the Settings banner in step when the player flips the OS switch.
                OneSignal.Notifications.PermissionChanged += OnPushPermissionChanged;

                // No permission request here: the game asks with PushPermissionPromptUI after the first run,
                // and Settings offers an "Enable" button while notifications are off.

                // Unity Analytics is started by AnalyticsConsent, only after the player agrees.

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
                var data = notif?.AdditionalData;
                PushKind kind = ParsePushKind(data);
                string otherPlayerId = ReadString(data, OtherPlayerIdKey(kind));
                string scope = ParseScope(data);
                RunOnMainThread(() => ShowForegroundPushToast(kind, title, body, otherPlayerId, scope));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Foreground handler warning: " + ex.Message);
            }
        }

        private void ShowForegroundPushToast(PushKind kind, string title, string body, string otherPlayerId, string scope)
        {
            Debug.Log($"[OneSignal] Foreground push ({kind}): {title} - {body}");

            bool isFriendEvent = kind == PushKind.FriendRequest || kind == PushKind.FriendRequestAccepted || kind == PushKind.FriendBeatScore;
            if (isFriendEvent)
            {
                if (_save != null && !_save.NotifyFriendActivity) return;
                // Requests are auto-declined on this device when the player blocks them; don't advertise one.
                if (kind == PushKind.FriendRequest && _save != null && !_save.AllowFriendRequests) return;
                // The Friends realtime event may already have shown this one; the first to arrive wins.
                if (!TryClaimFriendToast(kind, otherPlayerId)) return;
            }

            Action onTap = kind == PushKind.Unknown ? (Action)null : () => SetPendingRoute(kind, scope);
            InAppNotificationView.Show(title, body, isFriendEvent ? NotificationType.FriendActivity : NotificationType.Reminder, onTap);
        }

        private void OnNotificationClicked(object sender, NotificationClickEventArgs e)
        {
            var data = e?.Notification?.AdditionalData;
            PushKind kind = ParsePushKind(data);
            string scope = ParseScope(data);
            RunOnMainThread(() =>
            {
                Debug.Log($"[OneSignal] Push opened: {kind} (scope: {scope ?? "-"})");
                if (kind != PushKind.Unknown) SetPendingRoute(kind, scope);
                OnPushOpened?.Invoke(kind);
            });
        }

        private void OnPushPermissionChanged(object sender, NotificationPermissionChangedEventArgs e)
        {
            bool granted = e != null && e.Permission;
            RunOnMainThread(() => PushPermissionChanged?.Invoke(granted));
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

        /// <summary>The beat-score payload's leaderboard scope ("AllTime" / "Weekly" / "Daily"), or null.</summary>
        private static string ParseScope(IDictionary<string, object> data)
        {
            string scope = ReadString(data, "scope");
            return scope == "AllTime" || scope == "Weekly" || scope == "Daily" ? scope : null;
        }

        /// <summary>Payload key holding the other player's ID for each push type (set by the Cloud Code scripts).</summary>
        private static string OtherPlayerIdKey(PushKind kind) => kind switch
        {
            PushKind.FriendRequest         => "senderId",
            PushKind.FriendRequestAccepted => "acceptorId",
            PushKind.FriendBeatScore       => "scorerId",
            _                              => null
        };

        private static string ReadString(IDictionary<string, object> data, string key)
        {
            if (data == null || string.IsNullOrEmpty(key) || !data.TryGetValue(key, out var value) || value == null) return null;
            string s = value.ToString().Trim();
            return s.Length > 0 ? s : null;
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

        // ── Push permission ─────────────────────────────────────────────────
        /// <summary>
        /// Reads the device's push permission. Returns false when it is unknown: always in the Unity Editor,
        /// and on a device when the push SDK did not initialize.
        /// </summary>
        public bool TryGetPushPermission(out bool granted)
        {
            granted = false;
#if !UNITY_EDITOR
            if (!_oneSignalReady) return false;
            try
            {
                granted = OneSignal.Notifications.Permission;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Reading push permission failed: " + ex.Message);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>False once the OS will no longer show its own permission dialog (e.g. after two denials on Android 13+).</summary>
        public bool CanRequestPushPermission()
        {
#if !UNITY_EDITOR
            if (!_oneSignalReady) return false;
            try { return OneSignal.Notifications.CanRequestPermission; }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Reading CanRequestPermission failed: " + ex.Message);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Shows the OS permission dialog. With fallbackToSettings, OneSignal offers to open the app's system
        /// settings when the dialog can no longer be shown. Resolves to false in the Editor or on failure.
        /// Raises PushPermissionChanged with the result.
        /// </summary>
        public Task<bool> RequestPushPermissionAsync(bool fallbackToSettings)
        {
#if !UNITY_EDITOR
            if (_oneSignalReady) return RequestPushPermissionOnDeviceAsync(fallbackToSettings);
#endif
            return Task.FromResult(false);
        }

#if !UNITY_EDITOR
        private async Task<bool> RequestPushPermissionOnDeviceAsync(bool fallbackToSettings)
        {
            bool granted = false;
            try
            {
                granted = await OneSignal.Notifications.RequestPermissionAsync(fallbackToSettings);
                Debug.Log($"[NotificationService] Push permission request result: {granted}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Push permission request failed: " + ex.Message);
            }
            RunOnMainThread(() => PushPermissionChanged?.Invoke(granted));
            return granted;
        }
#endif

        private void SetupNotificationChannels()
        {
#if UNITY_ANDROID
            try
            {
                // Older builds registered these, but nothing posts to them (pushes target friends_activity / rivalry
                // and rank drops are in-game only). Delete them so they don't linger in the system settings.
                AndroidNotificationCenter.DeleteNotificationChannel(LegacyDefaultChannelId);
                AndroidNotificationCenter.DeleteNotificationChannel(LeaderboardChannelId);

                AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
                {
                    Id = FriendsChannelId,
                    Name = "Friend Activity",
                    Importance = Importance.High,
                    Description = "Notifications for new friend requests and social updates",
                });
                AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
                {
                    Id = RivalryChannelId,
                    Name = "Friend Rivalry",
                    Importance = Importance.Default,
                    Description = "Notifications when a friend beats your score",
                });
                AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
                {
                    Id = RemindersChannelId,
                    Name = "Game Reminders & Streaks",
                    Importance = Importance.Default,
                    Description = "Streak protection reminders and leaderboard reset countdowns",
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Channel setup warning: " + ex.Message);
            }
#endif
        }

        // ── Pending route (push / reminder / toast taps) ────────────────────
        private bool _hasPendingRoute;
        private PushKind _pendingKind;
        private string _pendingScope;
        private float _pendingRouteSetAt;
        // A tap that can't be honoured within this long (e.g. tapped mid-run, then the player kept playing)
        // is dropped instead of opening a panel out of nowhere minutes later.
        private const float PendingRouteMaxAgeSeconds = 120f;

        public bool HasPendingRoute => _hasPendingRoute;

        /// <summary>Returns and clears the waiting route. scope is the beat-score payload's scope or null.</summary>
        public bool TryConsumePendingRoute(out PushKind kind, out string scope)
        {
            kind = PushKind.Unknown;
            scope = null;
            if (!_hasPendingRoute) return false;

            bool expired = Time.realtimeSinceStartup - _pendingRouteSetAt > PendingRouteMaxAgeSeconds;
            kind = _pendingKind;
            scope = _pendingScope;
            _hasPendingRoute = false;
            _pendingKind = PushKind.Unknown;
            _pendingScope = null;
            if (expired)
            {
                Debug.Log($"[NotificationService] Dropped stale pending route: {kind}");
                kind = PushKind.Unknown;
                scope = null;
                return false;
            }
            return true;
        }

        /// <summary>Main thread only. A newer tap replaces an older one that was never consumed.</summary>
        private void SetPendingRoute(PushKind kind, string scope)
        {
            _pendingKind = kind;
            _pendingScope = scope;
            _hasPendingRoute = true;
            _pendingRouteSetAt = Time.realtimeSinceStartup;
            Debug.Log($"[NotificationService] Pending route set: {kind} (scope: {scope ?? "-"})");

            try { PendingRouteAvailable?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // ── Leaderboard Rank Drop Detection ─────────────────────────────────
        /// <summary>
        /// Compares the player's current global rank with the last one seen for that scope and period.
        /// Daily/weekly boards reset, so a rank from a previous period is treated as "first seen" rather than a drop.
        /// The rank is always recorded; the toggle only controls the toast.
        /// Only call this for the Global filter; friends-relative ranks are not comparable.
        /// </summary>
        public void CheckAndNotifyRankDrop(int currentRank, LeaderboardScope scope, string leaderboardName = "Leaderboard")
        {
            if (_save == null || currentRank <= 0) return;

            string periodKey = PeriodKeyFor(scope);
            int prevRank = GetSavedRank(scope);
            string prevPeriod = GetSavedRankPeriod(scope);

            bool firstSeen = prevRank <= 0 || prevPeriod != periodKey;

            if (firstSeen || prevRank != currentRank)
            {
                SetSavedRank(scope, currentRank, periodKey);
                LocalProgress.Save(_save);
            }

            if (!_save.NotifyLeaderboardDrops) return;
            if (firstSeen || currentRank <= prevRank) return;

            int placesDropped = currentRank - prevRank;
            string title = "Leaderboard Rank Dropped";
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
        // Toasts are gated by the LOCAL player's Friend Activity toggle and deduplicated per
        // "{kind}:{otherPlayerId}" for 60 s across the realtime Friends events and foreground pushes.

        private const float FriendToastDedupeSeconds = 60f;
        private readonly Dictionary<string, float> _recentFriendToasts = new Dictionary<string, float>();

        public void ShowIncomingFriendRequestToast(string senderName) => ShowIncomingFriendRequestToast(senderName, null);

        public void ShowIncomingFriendRequestToast(string senderName, string senderPlayerId)
        {
            if (_save != null && !_save.NotifyFriendActivity) return;
            if (!TryClaimFriendToast(PushKind.FriendRequest, senderPlayerId)) return;

            // Keeps the #1234 tag: it helps the player tell who is asking.
            string name = string.IsNullOrWhiteSpace(senderName) ? "Someone" : senderName.Trim();
            InAppNotificationView.Show("New Friend Request",
                $"{name} sent you a friend request. Tap to respond.",
                NotificationType.FriendActivity,
                () => SetPendingRoute(PushKind.FriendRequest, null));
        }

        public void ShowNowFriendsToast(string friendName) => ShowNowFriendsToast(friendName, null);

        public void ShowNowFriendsToast(string friendName, string friendPlayerId)
        {
            if (_save != null && !_save.NotifyFriendActivity) return;
            if (!TryClaimFriendToast(PushKind.FriendRequestAccepted, friendPlayerId)) return;

            string name = FriendlyName(friendName);
            InAppNotificationView.Show("Friend Request Accepted!",
                $"You and {(string.IsNullOrEmpty(name) ? "your new friend" : name)} are now friends! Compete on the Friends Leaderboard.",
                NotificationType.FriendActivity,
                () => SetPendingRoute(PushKind.FriendRequestAccepted, null));
        }

        public void ShowBeatFriendToast(string friendName, int newScore) => ShowBeatFriendsToast(new[] { friendName }, newScore);

        /// <summary>
        /// One toast per score submit, however many friends were passed. Order the names by the friends'
        /// scores, highest first; the first one is named and the rest are counted.
        /// </summary>
        public void ShowBeatFriendsToast(IReadOnlyList<string> friendNames, int newScore)
        {
            if (_save != null && !_save.NotifyFriendActivity) return;
            if (friendNames == null || friendNames.Count == 0) return;

            string first = FriendlyName(friendNames[0]);
            if (string.IsNullOrEmpty(first)) first = "a friend";
            int others = friendNames.Count - 1;

            string message = others == 0
                ? $"You passed {first} on the all-time board!"
                : $"You passed {first} and {others} other friend{(others == 1 ? "" : "s")} on the all-time board!";

            InAppNotificationView.Show("New personal best!", message, NotificationType.FriendActivity,
                () => SetPendingRoute(PushKind.FriendBeatScore, nameof(LeaderboardScope.AllTime)));
        }

        /// <summary>
        /// Claims the toast for one friend event. Returns false if the same event for the same player was
        /// already shown in the last 60 s. Without a player ID nothing can be matched, so it always shows.
        /// </summary>
        private bool TryClaimFriendToast(PushKind kind, string otherPlayerId)
        {
            if (string.IsNullOrEmpty(otherPlayerId)) return true;

            float now = Time.realtimeSinceStartup;
            string key = kind + ":" + otherPlayerId;
            if (_recentFriendToasts.TryGetValue(key, out float shownAt) && now - shownAt < FriendToastDedupeSeconds)
            {
                Debug.Log($"[NotificationService] Duplicate friend toast suppressed: {key}");
                return false;
            }

            if (_recentFriendToasts.Count >= 32) PruneRecentFriendToasts(now);
            _recentFriendToasts[key] = now;
            return true;
        }

        private void PruneRecentFriendToasts(float now)
        {
            var expired = new List<string>();
            foreach (var kv in _recentFriendToasts)
                if (now - kv.Value >= FriendToastDedupeSeconds) expired.Add(kv.Key);
            foreach (string key in expired) _recentFriendToasts.Remove(key);
        }

        /// <summary>"Alex#4821" → "Alex". Leaves names without a 4-digit discriminator unchanged.</summary>
        public static string FriendlyName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return rawName?.Trim() ?? "";

            string name = rawName.Trim();
            int hash = name.Length - 5;
            if (hash > 0 && name[hash] == '#'
                && char.IsDigit(name[hash + 1]) && char.IsDigit(name[hash + 2])
                && char.IsDigit(name[hash + 3]) && char.IsDigit(name[hash + 4]))
            {
                string stripped = name.Substring(0, hash).TrimEnd();
                if (stripped.Length > 0) return stripped;
            }
            return name;
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
        /// The scripts answer { success, error? } instead of throwing, so a failed delivery is read from the result.
        /// </summary>
        private async Task CallCloudCodePushAsync(string scriptName, Dictionary<string, object> args)
        {
            try
            {
                var result = await CloudCodeService.Instance.CallEndpointAsync<Dictionary<string, object>>(scriptName, args);
                if (result != null && result.TryGetValue("success", out var ok) && ok is bool success && !success)
                {
                    result.TryGetValue("error", out var error);
                    Debug.LogWarning($"[NotificationService] Cloud Code push '{scriptName}' not delivered: {error ?? "unknown error"}");
                    return;
                }

                if (result != null && result.TryGetValue("skipped", out var skipped) && skipped != null)
                {
                    Debug.Log($"[NotificationService] Cloud Code push '{scriptName}' skipped: {skipped}");
                    return;
                }

                Debug.Log($"[NotificationService] Cloud Code push dispatched: {scriptName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NotificationService] Cloud Code push '{scriptName}' failed: {ex.Message}");
            }
        }

        // ── Scheduled Reminders ─────────────────────────────────────────────
        // Reminders are armed only while the app is in the background (OnAppBackgrounded) and cancelled as soon
        // as it returns (OnAppForegrounded), so they never fire mid-run and always reflect the latest save.
        public const int StreakReminderNotificationId = 1001;
        public const int LeaderboardResetNotificationId = 1002;
        /// <summary>Comeback ladder, day 1 (kept under its old name for compatibility).</summary>
        public const int InactivityReminderNotificationId = 1003;
        public const int ComebackD3NotificationId = 1004;
        public const int ComebackD7NotificationId = 1005;

        // IntentData on Android / Data on iOS, read back from the notification that opened the app.
        public const string IntentStreak = "streak";
        public const string IntentLeaderboardReset = "lb_reset";
        public const string IntentComebackD1 = "comeback_d1";
        public const string IntentComebackD3 = "comeback_d3";
        public const string IntentComebackD7 = "comeback_d7";

        private const string ReminderGroup = "reminders";
        /// <summary>Status-bar glyph from Assets/Plugins/Android/OneSignalConfig.androidlib/src/main/res/drawable-*.</summary>
        private const string SmallIconName = "ic_stat_onesignal_default";
        private static readonly Color BrandColor = new Color(0.25f, 0.45f, 0.95f, 1f);

        private const int DefaultReminderHour = 19;   // used until the player has some play history
        private const int EarliestReminderHour = 10;
        private const int LatestReminderHour = 21;
        private const int StreakQuietHour = 22;       // no late streak nudge when leaving at or after 22:00
        private static readonly TimeSpan MinReminderGap = TimeSpan.FromHours(3);
        private static readonly TimeSpan LateStreakNudgeDelay = TimeSpan.FromMinutes(15);

        private struct ReminderSlot
        {
            public int Id;
            public string Intent;
            public string IosId;
        }

        private static readonly ReminderSlot StreakSlot      = new ReminderSlot { Id = StreakReminderNotificationId,     Intent = IntentStreak,           IosId = "streak_reminder" };
        private static readonly ReminderSlot ResetSlot       = new ReminderSlot { Id = LeaderboardResetNotificationId,   Intent = IntentLeaderboardReset, IosId = "leaderboard_reset_reminder" };
        private static readonly ReminderSlot ComebackD1Slot  = new ReminderSlot { Id = InactivityReminderNotificationId, Intent = IntentComebackD1,       IosId = "inactivity_reminder" };
        private static readonly ReminderSlot ComebackD3Slot  = new ReminderSlot { Id = ComebackD3NotificationId,         Intent = IntentComebackD3,       IosId = "comeback_d3_reminder" };
        private static readonly ReminderSlot ComebackD7Slot  = new ReminderSlot { Id = ComebackD7NotificationId,         Intent = IntentComebackD7,       IosId = "comeback_d7_reminder" };
        private static readonly ReminderSlot[] AllReminderSlots = { StreakSlot, ResetSlot, ComebackD1Slot, ComebackD3Slot, ComebackD7Slot };

        private struct PlannedReminder
        {
            public ReminderSlot Slot;
            public string Title;
            public string Body;
            public DateTime FireTime;
            public int Priority; // lower wins when two land within MinReminderGap: streak > board reset > comeback
        }

        private string _lastHandledReminderIntent;

        /// <summary>
        /// Call when the app goes to the background (OnApplicationPause(true)). Arms every enabled reminder
        /// from the current save. Idempotent: our previous reminders are cancelled first.
        /// </summary>
        public void OnAppBackgrounded()
        {
            if (!_isInitialized) return;

            CancelOurReminders(dismissDisplayed: false);
            if (_save == null || !_save.NotifyReminders) return;

            // The app can stay alive across midnight: never remind about a streak that already lapsed.
            if (LocalProgress.NormalizeDailyState(_save)) LocalProgress.Save(_save);

            string lateNudgeBefore = _save.LastLateStreakNudgeDay;
            foreach (var reminder in PlanReminders(DateTime.Now))
                ScheduleReminder(reminder);
            if (_save.LastLateStreakNudgeDay != lateNudgeBefore) LocalProgress.Save(_save);
        }

        /// <summary>
        /// Call when the app returns to the foreground (OnApplicationPause(false)). Cancels our scheduled
        /// reminders, removes ours from the tray (by ID only, so OneSignal pushes stay), and turns a tapped
        /// reminder into a pending route (PushKind.Reminder). Also runs once from Initialize.
        /// </summary>
        public void OnAppForegrounded()
        {
            CancelOurReminders(dismissDisplayed: true);
            if (_save != null && LocalProgress.NormalizeDailyState(_save)) LocalProgress.Save(_save);
            CheckReminderLaunchIntent();
        }

        /// <summary>Call once per completed run: records the local hour so reminders arrive when the player usually plays.</summary>
        public void RecordRunCompleted()
        {
            if (_save == null) return;
            LocalProgress.RecordPlayHour(_save, DateTime.Now.Hour);
            LocalProgress.Save(_save);
        }

        public void CancelAllScheduledNotifications()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllScheduledNotifications();
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
#endif
        }

        /// <summary>
        /// Settings-toggle hook (name kept for existing callers). Reminders are only armed while the app is in
        /// the background, so this just cancels ours, scheduled and displayed; OnAppBackgrounded re-arms whatever
        /// is enabled the next time the app is left.
        /// </summary>
        public void RescheduleReminders()
        {
            CancelOurReminders(dismissDisplayed: true);
        }

        /// <summary>Arms the streak reminder alone (normally OnAppBackgrounded plans all reminders together).</summary>
        public void ScheduleStreakProtectionReminder() => ScheduleSlots(StreakReminderNotificationId);

        /// <summary>Arms the daily-board reset reminder alone (normally OnAppBackgrounded plans all reminders together).</summary>
        public void ScheduleLeaderboardResetReminder() => ScheduleSlots(LeaderboardResetNotificationId);

        /// <summary>Arms the comeback ladder (day 1/3/7) alone (normally OnAppBackgrounded plans all reminders together).</summary>
        public void ScheduleInactivityReminder() => ScheduleSlots(InactivityReminderNotificationId, ComebackD3NotificationId, ComebackD7NotificationId);

        private void ScheduleSlots(params int[] ids)
        {
            if (_save == null || !_save.NotifyReminders) return;
            foreach (var reminder in PlanReminders(DateTime.Now))
                if (Array.IndexOf(ids, reminder.Slot.Id) >= 0) ScheduleReminder(reminder);
        }

        private void CancelOurReminders(bool dismissDisplayed)
        {
            try
            {
                foreach (var slot in AllReminderSlots)
                {
#if UNITY_ANDROID
                    AndroidNotificationCenter.CancelScheduledNotification(slot.Id);
                    // Never CancelAllDisplayedNotifications: it would also clear unread OneSignal pushes.
                    if (dismissDisplayed) AndroidNotificationCenter.CancelDisplayedNotification(slot.Id);
#elif UNITY_IOS
                    iOSNotificationCenter.RemoveScheduledNotification(slot.IosId);
                    if (dismissDisplayed) iOSNotificationCenter.RemoveDeliveredNotification(slot.IosId);
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Cancel reminders warning: " + ex.Message);
            }
        }

        private void ScheduleReminder(PlannedReminder r)
        {
            ScheduleLocalNotification(r.Title, r.Body, r.FireTime, RemindersChannelId, r.Slot.Id, r.Slot.IosId, r.Slot.Intent);
            Debug.Log($"[NotificationService] Reminder '{r.Slot.Intent}' armed for {r.FireTime:yyyy-MM-dd HH:mm} local.");
        }

        // ── Reminder planning ───────────────────────────────────────────────
        private List<PlannedReminder> PlanReminders(DateTime now)
        {
            var candidates = new List<PlannedReminder>(5);
            int hour = UsualReminderHour();

            if (TryPlanStreakReminder(now, hour, out var streak)) candidates.Add(streak);
            if (TryPlanLeaderboardResetReminder(now, out var reset)) candidates.Add(reset);
            AddComebackLadder(now, hour, candidates);

            // Never two of ours within 3 h of each other: the higher-priority one keeps its slot.
            candidates.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.FireTime.CompareTo(b.FireTime));
            var accepted = new List<PlannedReminder>(candidates.Count);
            foreach (var candidate in candidates)
            {
                bool clashes = false;
                foreach (var kept in accepted)
                {
                    if ((kept.FireTime - candidate.FireTime).Duration() < MinReminderGap) { clashes = true; break; }
                }
                if (!clashes) accepted.Add(candidate);
            }
            return accepted;
        }

        /// <summary>
        /// The local hour the player most often finishes a run (the mode, not the mean, so a few late-night
        /// runs don't drag it), clamped to 10–21. 19:00 until there is any history.
        /// </summary>
        private int UsualReminderHour()
        {
            int[] histogram = _save?.PlayHourHistogram;
            int bestHour = DefaultReminderHour;
            int bestCount = 0;
            if (histogram != null)
            {
                for (int h = 0; h < histogram.Length && h < 24; h++)
                {
                    int count = histogram[h];
                    // Ties go to the hour closest to the default evening slot.
                    if (count > bestCount || (count == bestCount && count > 0
                        && Math.Abs(h - DefaultReminderHour) < Math.Abs(bestHour - DefaultReminderHour)))
                    {
                        bestHour = h;
                        bestCount = count;
                    }
                }
            }
            return Mathf.Clamp(bestHour, EarliestReminderHour, LatestReminderHour);
        }

        /// <summary>
        /// Streaks count local days. Played today: safe until tomorrow ends, so remind tomorrow. Played yesterday:
        /// today is the last chance. The reminder lands at the usual play hour; if that has already passed on the
        /// at-risk day, one nudge 15 minutes after leaving, but only when the player leaves before 22:00.
        /// </summary>
        private bool TryPlanStreakReminder(DateTime now, int hour, out PlannedReminder reminder)
        {
            reminder = default;
            if (_save.Streak < 2) return false;
            if (!LocalProgress.TryGetLastPlayDay(_save, out DateTime lastPlay)) return false;

            DateTime atRiskDay = lastPlay.Date.AddDays(1);
            if (atRiskDay < now.Date) return false; // already lapsed (NormalizeDailyState zeroes it too)

            DateTime fire = atRiskDay.AddHours(hour);
            if (fire <= now)
            {
                // The usual hour already passed on the at-risk day: one late nudge if it's still before 22:00.
                if (now.Date != atRiskDay || now >= atRiskDay.AddHours(StreakQuietHour)) return false;
                // Only once per at-risk day, however many times the player leaves the app that evening.
                string dayKey = atRiskDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (_save.LastLateStreakNudgeDay == dayKey) return false;
                _save.LastLateStreakNudgeDay = dayKey;
                fire = now + LateStreakNudgeDelay;
            }

            reminder = new PlannedReminder
            {
                Slot = StreakSlot,
                Title = $"🔥 {_save.Streak}-day streak on the line",
                Body = "One quick game keeps it going.",
                FireTime = fire,
                Priority = 0
            };
            return true;
        }

        /// <summary>
        /// Two hours before the UTC daily board resets, for players who already have a score on today's board,
        /// and only if that lands between 10:00 and 21:00 local time.
        /// </summary>
        private bool TryPlanLeaderboardResetReminder(DateTime now, out PlannedReminder reminder)
        {
            reminder = default;

            DateTime nowUtc = now.ToUniversalTime();
            string todayUtc = nowUtc.Date.ToString("yyyy-MM-dd"); // same format as LocalProgress.DailyDate
            if (_save.DailyDate != todayUtc || _save.DailyBest <= 0) return false;

            // Assumes the daily board resets at 00:00 UTC; keep in sync with the UGS dashboard reset schedule.
            DateTime resetUtc = DateTime.SpecifyKind(nowUtc.Date.AddDays(1), DateTimeKind.Utc);
            DateTime fireLocal = resetUtc.AddHours(-2).ToLocalTime();

            if (fireLocal <= now.AddMinutes(1)) return false;
            TimeSpan timeOfDay = fireLocal.TimeOfDay;
            if (timeOfDay < TimeSpan.FromHours(EarliestReminderHour) || timeOfDay > TimeSpan.FromHours(LatestReminderHour)) return false;

            // Inexact alarms (Android 12+, ScheduleExactAlarms off) can land up to ~1 h late, so no hour count in the copy.
            reminder = new PlannedReminder
            {
                Slot = ResetSlot,
                Title = "Today's board closes soon",
                Body = $"Your best today: {_save.DailyBest:N0}. One more run?",
                FireTime = fireLocal,
                Priority = 1
            };
            return true;
        }

        /// <summary>Day 1 / 3 / 7 after leaving, at the usual play hour. Cancelled as soon as the player is back.</summary>
        private void AddComebackLadder(DateTime now, int hour, List<PlannedReminder> into)
        {
            DateTime today = now.Date;

            string d1Body = _save.AllTimeHigh > 0
                ? $"Your best is {_save.AllTimeHigh:N0}. Think you can top it?"
                : "A fresh stack is waiting. Got 2 minutes?";

            into.Add(new PlannedReminder { Slot = ComebackD1Slot, Title = "Ready for another round?",       Body = d1Body,                            FireTime = today.AddDays(1).AddHours(hour), Priority = 2 });
            into.Add(new PlannedReminder { Slot = ComebackD3Slot, Title = "A fresh daily board is waiting", Body = "Grab a spot near the top today.", FireTime = today.AddDays(3).AddHours(hour), Priority = 2 });
            into.Add(new PlannedReminder { Slot = ComebackD7Slot, Title = "Your stack misses you",          Body = "One quick game?",                 FireTime = today.AddDays(7).AddHours(hour), Priority = 2 });
        }

        // ── Reminder taps ───────────────────────────────────────────────────
        private static bool IsOurReminderIntent(string data) =>
            data == IntentStreak || data == IntentLeaderboardReset ||
            data == IntentComebackD1 || data == IntentComebackD3 || data == IntentComebackD7;

        /// <summary>
        /// The launch intent stays the same across later resumes, so each tapped notification is handled once
        /// (keyed by ID, intent and fire time).
        /// </summary>
        private void CheckReminderLaunchIntent()
        {
#if UNITY_ANDROID
            try
            {
                var opened = AndroidNotificationCenter.GetLastNotificationIntent();
                if (opened == null) return;

                string data = opened.Notification.IntentData;
                if (!IsOurReminderIntent(data)) return;

                string key = $"{opened.Id}|{data}|{opened.Notification.FireTime.Ticks}";
                if (key == _lastHandledReminderIntent) return;
                _lastHandledReminderIntent = key;

                Debug.Log($"[NotificationService] Opened from reminder '{data}'.");
                SetPendingRoute(PushKind.Reminder, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NotificationService] Reading the launch notification failed: " + ex.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            StartCoroutine(CheckIosReminderResponse());
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private IEnumerator CheckIosReminderResponse()
        {
            var op = iOSNotificationCenter.QueryLastRespondedNotification();
            yield return op;
            if (op.State != QueryLastRespondedNotificationState.HaveRespondedNotification) yield break;

            iOSNotification responded = op.Notification;
            string data = responded?.Data;
            if (!IsOurReminderIntent(data)) yield break;

            string key = responded.Identifier + "|" + data;
            if (key == _lastHandledReminderIntent) yield break;
            _lastHandledReminderIntent = key;

            Debug.Log($"[NotificationService] Opened from reminder '{data}'.");
            SetPendingRoute(PushKind.Reminder, null);
        }
#endif

        // ── Low-Level Local Notification Dispatcher ─────────────────────────
        public void ScheduleLocalNotification(
            string title,
            string body,
            DateTime fireTime,
            string channelId,
            int notificationId = -1,
            string iosIdentifier = null,
            string intentData = null)
        {
#if UNITY_ANDROID
            var androidNotif = new AndroidNotification
            {
                Title = title,
                Text = body,
                FireTime = fireTime,
                SmallIcon = SmallIconName,                // white status-bar glyph instead of a flattened app icon
                Color = BrandColor,
                Style = NotificationStyle.BigTextStyle,   // full body when expanded instead of a ~45-character cut
                ShouldAutoCancel = true,                  // tapping it clears it from the tray
                ShowInForeground = false,                 // never pop over a running game
                Group = ReminderGroup,
                IntentData = intentData
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
                ShowInForeground = false,
                ForegroundPresentationOption = PresentationOption.Alert | PresentationOption.Sound,
                CategoryIdentifier = channelId,
                ThreadIdentifier = ReminderGroup,
                Trigger = timeTrigger
            };
            if (!string.IsNullOrEmpty(intentData)) iosNotif.Data = intentData;
            iOSNotificationCenter.ScheduleNotification(iosNotif);
#else
            Debug.Log($"[NotificationService Local Simulation] '{title}' - '{body}' scheduled for {fireTime}");
#endif
        }
    }
}

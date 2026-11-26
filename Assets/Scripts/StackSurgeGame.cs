using System;
using System.Collections;
using System.Threading.Tasks;
using StackSurge.Core;
using StackSurge.Meta;
using StackSurge.Settings;
using StackSurge.UI;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StackSurge
{
    /// <summary>
    /// Main game loop: queue, placement, bomb, cascades, rising pressure, scoring, meta.
    /// View logic has been migrated strictly to GameView.
    /// </summary>
    public class StackSurgeGame : MonoBehaviour
    {
        [SerializeField] StackSurgeSettings _settings;
        [SerializeField] ChallengeDefinition[] _challengeAssets;

        [SerializeField] GameView _view;
        [SerializeField] LeaderboardManager _leaderboardManager;
        [SerializeField] FriendsManagerUI _friendsManagerUI;
        GameBoard _board;
        ScoreService _score;
        SaveData _save;
        ChallengeTracker _challenges;
        ChallengeProvider _provider;
        LeaderboardService _leaderboardService;
        FriendsService _friendsService;
        AchievementService _achievements;
        AchievementsUI _achievementsUI;

        TileKind _current;
        TileKind _next;

        float _timeAlive;
        ChallengeDisplayData[] _challengesAtRunStart;
        bool _allDoneBonusAtRunStart;
        // Counted for challenges (rows cleared, perfect clears, bombs set off, 5-in-a-line matches).
        int _runRows, _runPerfects, _runBombs, _runMatch5s;
        float _riseAccumulator;
        float _slowFillBuff;
        bool _playing;
        bool _resolvingMatches;
        int _tilesPlacedThisRun;

        // Every second spent resolving is a second the board refuses taps, so these stay short.
        [SerializeField] float _blinkTime = 0.2f;
        const float BombBlinkSeconds = 0.18f;
        const float RiseSlideSeconds = 0.12f;
        const float PostWaveSeconds = 0.04f;

        // One-tap buffer: a tap that lands while the board is resolving is replayed when it settles.
        const float TapBufferSeconds = 0.7f; // covers a full single clear (landing + pop + fall)
        int _bufferedColumn = -1;
        float _bufferedTapTime;

        bool _settingsFrozeClock;

        // ── Tutorial Fields & Properties ─────────────────────────────────────
        private bool _inTutorial = false;
        private int _tutorialRequiredColumn = -1;
        private TutorialController _tutorialController;
        private Coroutine _tutorialCoroutine;

        internal GameBoard Board => _board;
        internal GameView View => _view;
        internal SaveData Save => _save;

        internal TileKind Current
        {
            get => _current;
            set => _current = value;
        }

        internal TileKind Next
        {
            get => _next;
            set => _next = value;
        }

        internal bool InTutorial
        {
            get => _inTutorial;
            set => _inTutorial = value;
        }

        internal int TutorialRequiredColumn
        {
            get => _tutorialRequiredColumn;
            set => _tutorialRequiredColumn = value;
        }

        internal bool ResolvingMatches => _resolvingMatches;

        internal void InitializeBoardForTutorial()
        {
            _board = new GameBoard(_settings.Columns, _settings.Rows);
            _score = new ScoreService();
            _score.Reset(_settings.ComboWindowSeconds);
            _timeAlive = 0f;
            _riseAccumulator = 0f;
            _slowFillBuff = 0f;
            _playing = true;
            _resolvingMatches = false;
            ClearTapBuffer();
            _view.ResetBoardVisuals();
        }

        internal void RunUpdateHud() => UpdateHud();
        internal void RunBeginRun() => BeginRun();

        internal IEnumerator RunRiseRowAndResolve()
        {
            yield return StartCoroutine(RiseRowAndResolve());
        }

        internal IEnumerator RunResolveMatchesAnim()
        {
            yield return StartCoroutine(ResolveMatchesAnimCoroutine());
        }

        public void StartTutorial()
        {
            StopAllCoroutines();
            ClearTapBuffer();
            Time.timeScale = 1f; // Ensure clock runs — WaitForSeconds blocks if timeScale == 0
            _view.HideMainMenu();
            _view.HideGameOver();
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            _view.SetTutorialActive(true); // Lock all non-tutorial buttons
            _view.SetInstructionalTextActive(false);
            _tutorialCoroutine = StartCoroutine(_tutorialController.PlayTutorialCoroutine());
        }

        async void Awake()
        {
            if (_settings == null) _settings = ScriptableObject.CreateInstance<StackSurgeSettings>();

            _save = LocalProgress.Load();
            _provider = new ChallengeProvider();

            // Match camera background to loading screen color to prevent first-frame flash
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = UiSkin.AppBackground;
            }

            if (_view == null) _view = GetComponent<GameView>();
            if (_view == null) _view = gameObject.AddComponent<GameView>();
            _view.Build(
                _settings, 
                OnColumnClicked, 
                Retry, 
                GoToMainMenuFromGameOver, 
                GetChallengeDisplayData, 
                GetTimeUntilReset, 
                ClaimReward,
                () => _save,
                OnPlayerNameChangedFromMainMenu,
                OnMainMenuPlayGame,
                OnMainMenuPlayTutorial,
                OnMainMenuToggleLeaderboard
            );

            _tutorialController = new TutorialController(this);
            _view.OnReplayTutorialTriggered = ReplayTutorialFromHelp;
            _view.OnNotificationSettingsTriggered = OpenNotificationSettings;
            _view.IsRunActive = () => _playing && !_inTutorial;
            _view.GetActiveRunScore = () => _playing && !_inTutorial && _score != null ? _score.TotalScore : -1;
            _view.OnQuitRunConfirmed = QuitRunToMainMenu;
            _view.OnChallengesOpened = RefreshChallenges;

            // Build the toast UI now (avoids a hitch on the first toast) and hold toasts while the player is busy.
            InAppNotificationView.EnsureInstance();
            SfxPlayer.EnsureInstance(); // loads the clips now so the first sound doesn't hitch
            UiSprites.Warm();
            InAppNotificationView.IsBusy = () =>
                _inTutorial || (_playing && Time.timeScale > 0f) || (_view != null && _view.IsLoadingScreenActive);

            // Game starts only after loading screen finishes fading
            _view.OnLoadingDone = OnLoadingScreenFinished;

            // Analytics consent prompt, shown once the loading screen is done (OnLoadingScreenFinished).
            GameObject consentObj = new GameObject("[AnalyticsConsentUI]");
            _analyticsConsentUI = consentObj.AddComponent<AnalyticsConsentUI>();
            // A push tap that launched the app waits behind the consent prompt; open it once the player answers.
            _analyticsConsentUI.Initialize(_save, onDecided: () => { if (!TryRoutePendingPush() && !TryOfferPlayerSwitch()) TryPromptUpdate(); });

            // UGS / Challenges async initialization
            _view.UpdateLoadingStatus("CONNECTING TO SERVICES...");
            // Inspector assets, if any, are a fixed set; otherwise each day is picked from the challenge pool.
            await _provider.InitializeAsync(_save, _challengeAssets);

            // Starts Unity Analytics only if the player already agreed (no-op otherwise).
            AnalyticsConsent.Apply(_save);

            SetUpAchievements();

            _friendsService = new FriendsService(_provider, _save);
            if (_friendsManagerUI != null)
            {
                _friendsManagerUI.Initialize(_friendsService);
            }

            // Ensure Friends SDK is initialized and publish Online presence
            _ = _friendsService.EnsureInitializedAsync();

            _leaderboardService = new LeaderboardService(_provider, _save, _friendsService);
            RegisterLeaderboardCallbacks();
            if (_leaderboardManager != null)
            {
                _leaderboardManager.OwnBadge = () => AchievementCatalog.PublicBadge(_save.Achievements);
                _leaderboardManager.BadgeTapped = ShowBadgeInfo;
            }
            if (_leaderboardManager != null) _leaderboardManager.Prefetch(); // the first open already has its list

            _view.UpdateLoadingStatus(_provider.IsOnline ? "ONLINE" : "NO INTERNET, PLAYING OFFLINE");
            _provider.WentOnline -= OnWentOnline;
            _provider.WentOnline += OnWentOnline;
            _challenges = new ChallengeTracker(_provider);

            // Initialize Notification System & Programmatic Settings UI
            NotificationService.EnsureInstance(_save);
            if (NotificationService.Instance != null)
            {
                // Remote pushes honour the RECIPIENT's public preference (read from Cloud Save).
                NotificationService.Instance.RecipientPrefsResolver = id => _friendsService.GetSocialPrefsAsync(id);
                // Push / reminder / toast taps are queued as a pending route and opened when it is safe (see TryRoutePendingPush).
                NotificationService.Instance.PendingRouteAvailable -= OnPendingRouteAvailable;
                NotificationService.Instance.PendingRouteAvailable += OnPendingRouteAvailable;
            }

            GameObject notifSettingsObj = new GameObject("[NotificationSettingsUI]");
            _notificationSettingsUI = notifSettingsObj.AddComponent<NotificationSettingsUI>();
            _notificationSettingsUI.Initialize(
                _save,
                onSettingsChanged: () =>
                {
                    NotificationService.Instance?.RescheduleReminders();
                    NotificationService.Instance?.SyncPreferenceTags();
                },
                onSocialSettingsChanged: () => { _ = _friendsService?.PublishSocialPrefsAsync(); },
                font: _view.UiFont,
                playGames: _provider.PlayGames);
            _notificationSettingsUI.Closed += OnNotificationSettingsClosed;
            SetUpPlayGames();
            SetUpAppUpdates();

            _view.EnableStartButton();
        }

        private NotificationSettingsUI _notificationSettingsUI;

        // ── Google Play Games: the switch-player dialog ───────────────────────
        SwitchPlayerUI _switchPlayerUI;
        bool _switchOfferPending;      // the Play Games account belongs to another player; ask when the menu is free
        bool _switchOfferedThisLaunch; // found by itself, the dialog opens once per launch at most

        void SetUpPlayGames()
        {
            var account = _provider.PlayGames;
            _switchPlayerUI = new GameObject("[SwitchPlayerUI]").AddComponent<SwitchPlayerUI>();
            _switchPlayerUI.Initialize(_view.UiFont);
            account.Changed += OnPlayGamesChanged;
            account.ChoiceRequested += OpenSwitchPlayer;
            account.PlayerSwitched += OnPlayerSwitched;
            account.PlayerRestored += OnPlayerRestored;
            OnPlayGamesChanged(); // the launch check may have finished already
            _ = RestorePlayerNameAsync(force: false); // a new install that signed in with Play Games gets its name back
        }

        void OnPlayGamesChanged()
        {
            var account = _provider.PlayGames;
            if (account.State != PlayGamesAccount.LinkState.LinkedElsewhere) { _switchOfferPending = false; return; }
            // From the Sign in button (Settings is open) the player is waiting for this; found by itself at launch,
            // only once per launch and not after the player chose to keep this phone's player.
            bool fromButton = _notificationSettingsUI != null && _notificationSettingsUI.IsOpen;
            if (!fromButton && (_switchOfferedThisLaunch || account.KeptThisPlayer)) return;
            _switchOfferPending = true;
            TryOfferPlayerSwitch();
        }

        /// <summary>Opens the switch dialog if it's waiting and the player is on the menu with nothing else asking.</summary>
        bool TryOfferPlayerSwitch()
        {
            if (!_switchOfferPending || _switchPlayerUI == null || _switchPlayerUI.IsOpen) return false;
            if (_playing || _inTutorial || _view == null) return false;
            if (_view.IsLoadingScreenActive || _view.IsGameOverShowing) return false;
            if (AnalyticsConsent.NeedsPrompt(_save) || PushPermissionPromptUI.IsOpen) return false;
            _switchOfferPending = false;
            _switchOfferedThisLaunch = true;
            OpenSwitchPlayer();
            return true;
        }

        void OpenSwitchPlayer()
        {
            var account = _provider.PlayGames;
            if (_switchPlayerUI == null || _switchPlayerUI.IsOpen || account.State != PlayGamesAccount.LinkState.LinkedElsewhere) return;

            string name = _save.PlayerDisplayName;
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth.IsSignedIn && !string.IsNullOrEmpty(auth.PlayerName)) name = auth.PlayerName;
            }
            catch (Exception) { /* offline start: the saved name will do */ }
            var thisPhone = new PlayGamesAccount.PlayerSummary
            {
                Name = name,
                BestScore = _save.AllTimeHigh,
                Badge = AchievementCatalog.PublicBadge(_save.Achievements),
            };
            _switchPlayerUI.Show(thisPhone,
                loadOther: () => account.PeekLinkedPlayerAsync(ReadSignedInPlayerAsync),
                switchAsync: account.SwitchToLinkedPlayerAsync,
                keep: account.KeepThisPlayer);
        }

        /// <summary>Name, all-time best and badge of whoever is signed in (the other player, during the look).</summary>
        async Task<PlayGamesAccount.PlayerSummary> ReadSignedInPlayerAsync()
        {
            string name = null;
            try { name = await AuthenticationService.Instance.GetPlayerNameAsync(autoGenerate: false); }
            catch (Exception e) { Debug.LogWarning("[StackSurgeGame] Couldn't read the other player's name: " + e.Message); }
            var entry = _leaderboardService != null ? await _leaderboardService.GetPlayerEntryAsync(LeaderboardScope.AllTime) : null;
            return new PlayGamesAccount.PlayerSummary
            {
                Name = !string.IsNullOrEmpty(name) ? name : entry?.PlayerName,
                BestScore = (int)(entry?.Score ?? 0),
                Badge = await AchievementService.LoadSignedInPublicBadgeAsync(),
            };
        }

        /// <summary>This phone's player is back after the look at the other one; the sign-out stopped the Friends SDK.</summary>
        void OnPlayerRestored()
        {
            if (_friendsService != null) _ = _friendsService.RestartForSignedInPlayerAsync();
        }

        /// <summary>
        /// This phone now plays as the Play Games account's player. Its name comes to the menu, friends start for it,
        /// and this phone's progress merges in: badges (the larger numbers win), the best score on the all-time board,
        /// and today's challenge progress.
        /// </summary>
        async void OnPlayerSwitched()
        {
            try
            {
                _save.PendingPlayerName = ""; // a rename this phone hadn't sent yet was for the old player
                LocalProgress.Save(_save);
                await RestorePlayerNameAsync(force: true);
                if (_friendsService != null) await _friendsService.RestartForSignedInPlayerAsync();
                if (_leaderboardService != null) await _leaderboardService.SubmitAllTimeBestAsync(_save.AllTimeHigh);
                await _provider.SaveAsync();
                if (_achievements != null) await _achievements.SyncAsync();
                if (_leaderboardManager != null) _leaderboardManager.Prefetch();
                _view.RefreshMainMenuStats();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StackSurgeGame] Catching up after the player switch failed: " + e.Message);
            }
        }

        /// <summary>
        /// Takes the signed-in player's name for this phone ("Addi#3310" shows as "Addi"). With <paramref name="force"/>
        /// (after a switch) always; otherwise only when this phone has no name and is linked to Play Games, which is a
        /// new install that got its player back. A switched-to player without a name gets this phone's.
        /// </summary>
        async Task RestorePlayerNameAsync(bool force)
        {
            if (!_provider.IsOnline) return;
            if (!force && (!string.IsNullOrEmpty(_save.PlayerDisplayName) || _provider.PlayGames.State != PlayGamesAccount.LinkState.SignedIn)) return;

            string name;
            try { name = await AuthenticationService.Instance.GetPlayerNameAsync(autoGenerate: false); }
            catch (Exception e)
            {
                Debug.LogWarning("[StackSurgeGame] Couldn't read the player name: " + e.Message);
                return;
            }
            if (string.IsNullOrEmpty(name))
            {
                if (force && !string.IsNullOrEmpty(_save.PlayerDisplayName) && _leaderboardService != null)
                    await _leaderboardService.SetPlayerNameAsync(_save.PlayerDisplayName);
                return;
            }
            int hash = name.LastIndexOf('#');
            _save.PlayerDisplayName = hash > 0 ? name.Substring(0, hash) : name;
            LocalProgress.Save(_save);
            _view.RefreshMainMenuStats();
        }
        private AnalyticsConsentUI _analyticsConsentUI;

        // ── Achievements ─────────────────────────────────────────────────────
        void SetUpAchievements()
        {
            _achievements = new AchievementService(_save, _provider);
            _achievements.PublicBadgeChanged += OnPublicBadgeChanged;
            _provider.AllChallengesDone += day => AnnounceBadges(_achievements.RecordAllChallengesDone(day));

            _achievementsUI = new GameObject("[AchievementsUI]").AddComponent<AchievementsUI>();
            _achievementsUI.Initialize(_achievements, _view.UiFont);
            _view.OnAchievementsTriggered = OpenAchievements;
            _view.GetProfileBadge = () => AchievementCatalog.PublicBadge(_save.Achievements);
            _view.GetProfilePlayerId = OwnPlayerId;
            _view.GetAutoPlayerName = AutoPlayerName;
            _view.IsMenuCovered = () => _achievementsUI != null && _achievementsUI.IsOpen;
            _achievementsUI.Closed += _view.PlayProfileChanges; // a badge picked in there repaints the card once it closes

            // Best score and streak from before achievements existed can already earn badges; the toast waits for the menu.
            AnnounceBadges(_achievements.CheckNewTiers());
            _ = SyncAchievementsAsync();
        }

        async Task SyncAchievementsAsync()
        {
            await _achievements.SyncAsync(); // a reinstall gets its badges back here
            if (this == null) return;
            AnnounceBadges(_achievements.CloudMergeEarned);
            _view.RefreshMainMenuStats();
        }

        const string LastPlayerIdKey = "last_player_id";

        /// <summary>
        /// This player's ID (it picks their block on the profile card when no badge is on show). Remembered, so the card
        /// shows the same block before sign-in finishes or offline; "" before the first sign-in.
        /// </summary>
        string OwnPlayerId()
        {
            string id = null;
            try
            {
                if (AuthenticationService.Instance.IsSignedIn) id = AuthenticationService.Instance.PlayerId;
            }
            catch (Exception) { } // Unity Services not initialised (offline start)
            if (string.IsNullOrEmpty(id)) return PlayerPrefs.GetString(LastPlayerIdKey, "");
            if (PlayerPrefs.GetString(LastPlayerIdKey, "") != id) PlayerPrefs.SetString(LastPlayerIdKey, id);
            return id;
        }

        const string AutoNameKey = "auto_player_name", AutoNameIdKey = "auto_player_name_id";

        /// <summary>The name Unity gave this player (without its "#1234"), for the profile card until they pick one; "" if not fetched yet.</summary>
        string AutoPlayerName()
        {
            string id = OwnPlayerId();
            return !string.IsNullOrEmpty(id) && PlayerPrefs.GetString(AutoNameIdKey, "") == id ? PlayerPrefs.GetString(AutoNameKey, "") : "";
        }

        /// <summary>
        /// Fetches the generated name for a player who hasn't picked one (Unity makes one the first time it's asked, as the
        /// leaderboard does on a first score) and remembers it, so the card shows it offline too.
        /// </summary>
        async Task LoadAutoPlayerNameAsync()
        {
            if (!_provider.IsOnline || !string.IsNullOrEmpty(_save.PlayerDisplayName) || !string.IsNullOrEmpty(AutoPlayerName())) return;
            string id = OwnPlayerId();
            if (string.IsNullOrEmpty(id)) return;
            string name;
            try { name = await AuthenticationService.Instance.GetPlayerNameAsync(autoGenerate: true); }
            catch (Exception e)
            {
                Debug.LogWarning("[StackSurgeGame] Couldn't read the generated name: " + e.Message);
                return;
            }
            if (this == null || string.IsNullOrEmpty(name)) return;
            int hash = name.LastIndexOf('#');
            PlayerPrefs.SetString(AutoNameKey, hash > 0 ? name.Substring(0, hash) : name);
            PlayerPrefs.SetString(AutoNameIdKey, id);
            _view.RefreshMainMenuStats();
        }

        void OnPublicBadgeChanged()
        {
            if (_friendsService != null) _ = _friendsService.PublishSocialPrefsAsync();
            if (_view != null) _view.RefreshMainMenuStats();
        }

        public void OpenAchievements()
        {
            if (_achievementsUI == null || _achievementsUI.IsOpen) return;
            if (_inTutorial) return;
            _achievementsUI.Show();
        }

        /// <summary>
        /// A toast for badges just earned. Toasts wait while a run is being played, so these show on the game-over
        /// screen or the menu. Tapping one opens Achievements.
        /// </summary>
        /// <summary>New-badge toasts stay up longer than others: they arrive as a run ends, while the player reads their score.</summary>
        const float BadgeToastSeconds = 6f;

        void AnnounceBadges(System.Collections.Generic.List<(AchievementDef def, int tier)> earned)
        {
            if (earned == null || earned.Count == 0) return;
            var (def, tier) = earned[0];
            Sprite icon = BadgeArt.Trimmed(def.Key, tier);

            if (earned.Count == 1)
            {
                InAppNotificationView.Show($"New badge: {def.Name}",
                    $"Tier {tier} of {AchievementCatalog.Tiers}, for {def.GoalText(tier)}. Tap to see your badges.",
                    NotificationType.Info, OpenAchievements, icon, BadgeToastSeconds);
                return;
            }

            // Two lines fit in the toast: name up to three, then "and N more".
            int named = earned.Count > 3 ? 2 : earned.Count;
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < named; i++)
            {
                if (i > 0) names.Append(i == named - 1 && named == earned.Count ? " and " : ", ");
                names.Append(earned[i].def.Name);
            }
            if (named < earned.Count) names.Append($" and {earned.Count - named} more");
            InAppNotificationView.Show($"{earned.Count} new badges", $"{names}. Tap to see them.",
                NotificationType.Info, OpenAchievements, icon, BadgeToastSeconds);
        }

        /// <summary>A badge on the leaderboard was tapped: say what it's for.</summary>
        void ShowBadgeInfo(string badge, string playerName)
        {
            if (!AchievementCatalog.TryParseBadge(badge, out var def, out int tier)) return;
            string who = string.IsNullOrEmpty(playerName) ? "This player" : playerName;
            InAppNotificationView.Show($"{def.Name}, tier {tier} of {AchievementCatalog.Tiers}",
                $"{who} earned it for {def.GoalText(tier)}.", NotificationType.Info, null, BadgeArt.Trimmed(def.Key, tier));
        }

        /// <summary>
        /// Single place that wires every leaderboard callback. Called at bootstrap and again after a rename,
        /// so the friends-related callbacks are never dropped.
        /// </summary>
        void RegisterLeaderboardCallbacks()
        {
            if (_leaderboardManager == null || _leaderboardService == null) return;

            _leaderboardManager.SetLeaderboardCallbacks(
                scope => _leaderboardService.GetTopScoresAsync(scope),
                scope => _leaderboardService.GetPlayerEntryAsync(scope),
                async name =>
                {
                    _save.PlayerDisplayName = name;
                    LocalProgress.Save(_save);
                    await _leaderboardService.SetPlayerNameAsync(name);
                    _view.RefreshMainMenuStats();
                },
                () => _save.PlayerDisplayName,
                onAddFriend: async targetPlayerId =>
                {
                    if (_friendsService == null) return FriendRequestResult.Failed;
                    var result = await _friendsService.SendFriendRequestAsync(targetPlayerId);
                    Debug.Log($"[StackSurgeGame] Leaderboard Add Friend to {targetPlayerId}: {result}");
                    return result;
                },
                getFriendsScores: (scope, friendIds) => _leaderboardService.GetFriendsScoresAsync(scope, friendIds),
                getFriendIds: async () => _friendsService != null ? await _friendsService.GetFriendPlayerIdsAsync() : new System.Collections.Generic.List<string>(),
                getRelationState: id => _friendsService != null ? _friendsService.GetRelationState(id) : FriendRelationState.None,
                getSocialPrefs: ids => _friendsService.GetSocialPrefsBatchAsync(ids),
                refreshFriends: () => _friendsService != null ? _friendsService.RefreshAsync() : Task.CompletedTask
            );
        }

        void OnPendingRouteAvailable() => TryRoutePendingPush();

        /// <summary>
        /// Brings today's challenges up to date between runs: a new UTC day's set at once, and Remote Config edits at
        /// most every 30 minutes. A run keeps the set it started with (BeginRun moves to a new day itself).
        /// </summary>
        async void RefreshChallenges()
        {
            if (_provider == null || _playing) return;
            bool changed;
            try { changed = await _provider.RefreshAsync(); }
            catch (Exception e) { Debug.LogWarning("[StackSurgeGame] Challenge refresh failed: " + e.Message); return; }
            if (!changed || this == null) return;
            _view.RefreshChallengesIfOpen();
            _view.RefreshMainMenuStats();
        }

        /// <summary>
        /// The game started offline and a retry has signed in. Friends and the leaderboard catch up on their own
        /// (they listen to WentOnline too); this starts the rest of what launch skipped.
        /// </summary>
        void OnWentOnline()
        {
            AnalyticsConsent.Apply(_save); // only when the player agreed; a no-op otherwise
            _ = RestorePlayerNameAsync(force: false); // a reconnect can sign in with Play Games on a new install
            _ = LoadAutoPlayerNameAsync();
            NotificationService.Instance?.BindSignedInPlayer();
            _view.RefreshMainMenuStats(); // the player ID is known now, which picks their block when no badge is on show
            if (_leaderboardManager != null)
            {
                if (_leaderboardManager.IsOpen) _ = _leaderboardManager.RefreshLeaderboard();
                else _leaderboardManager.Prefetch();
            }
        }

        /// <summary>
        /// Opens the screen for a tapped push, reminder or toast. Only when the player is on the main menu: never
        /// during loading, a run, the tutorial or the game-over panel (the route stays pending until then).
        /// </summary>
        bool TryRoutePendingPush()
        {
            var notifications = NotificationService.Instance;
            if (notifications == null || !notifications.HasPendingRoute) return false;
            if (_playing || _inTutorial || _view == null) return false;
            if (_view.IsLoadingScreenActive || _view.IsGameOverShowing) return false;
            if (AnalyticsConsent.NeedsPrompt(_save) || PushPermissionPromptUI.IsOpen) return false; // modal is up
            if (!notifications.TryConsumePendingRoute(out PushKind kind, out string scope)) return false;

            switch (kind)
            {
                case PushKind.FriendRequest:
                    _friendsManagerUI?.OpenPanel(FriendsTab.PendingRequests);
                    break;
                case PushKind.FriendRequestAccepted:
                    _friendsManagerUI?.OpenPanel(FriendsTab.FriendsList);
                    break;
                case PushKind.FriendBeatScore:
                    if (_leaderboardManager != null)
                    {
                        if (Enum.TryParse(scope, out LeaderboardScope parsed)) _leaderboardManager.SetLeaderboardScope(parsed);
                        _leaderboardManager.SetFilterMode(LeaderboardFilterMode.FriendsOnly);
                        _leaderboardManager.ShowLeaderboard();
                    }
                    break;
                // PushKind.Reminder: the main menu (ready to play) is already the destination.
            }
            return true;
        }

        public void OpenNotificationSettings()
        {
            if (_notificationSettingsUI == null || _notificationSettingsUI.IsOpen) return;

            // Opened mid-run (from Help): keep the run frozen until the dialog closes.
            if (!_settingsFrozeClock && _playing && !_inTutorial && Time.timeScale > 0f)
            {
                _settingsFrozeClock = true;
                Time.timeScale = 0f;
            }
            _notificationSettingsUI.Show();
        }

        void OnNotificationSettingsClosed()
        {
            if (!_settingsFrozeClock) return;
            _settingsFrozeClock = false;
            if (!_view.IsPaused) Time.timeScale = 1f;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Notification shade, incoming call, system dialog: freeze the run so rows don't rise unseen.
            if (!hasFocus) AutoPauseRun();
        }

        /// <summary>Pauses an active run (never auto-resumes). No-op if already paused or a modal froze the clock.</summary>
        void AutoPauseRun()
        {
            if (!_playing || _inTutorial || _view == null) return;
            if (_view.IsPaused || Time.timeScale <= 0f) return;
            ClearTapBuffer();
            _view.PauseGame();
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                AutoPauseRun();
                // A downloaded update installs while the game is in the background (Google does it silently), unless
                // a run is in progress: that would close the game on it.
                if (_appUpdate != null && _appUpdate.Current == PlayAppUpdate.Stage.Ready && !_playing && !_inTutorial)
                    _appUpdate.Restart();
                // Reminders are only armed while the app is in the background, so they never fire mid-game.
                NotificationService.Instance?.OnAppBackgrounded();
                if (_friendsService != null) _ = _friendsService.PublishPresenceAsync(PresenceStatus.Offline);
            }
            else
            {
                // Cancels armed reminders, picks up a tapped reminder, and re-normalizes the streak for a new day.
                NotificationService.Instance?.OnAppForegrounded();
                if (_view != null) _view.RefreshMainMenuStats();
                if (_friendsService != null) _ = _friendsService.PublishPresenceAsync(PresenceStatus.Online);
                // If the game started offline, back in the foreground is a good moment to try signing in again.
                if (_provider != null) _ = _provider.TryReconnectAsync(userAction: false);
                RefreshChallenges(); // a new day's set, or Remote Config edits, without a restart
                _appUpdate?.Check(); // an update may have come out, or finished downloading, while the game was away
            }
        }

        void OnApplicationQuit()
        {
            if (_friendsService != null) _ = _friendsService.PublishPresenceAsync(PresenceStatus.Offline);
        }

        void Start()
        {
            // Game no longer starts here — it starts when the loading screen completes
        }

        void Update()
        {
            // Android's Back button / back gesture arrives as Escape in the Input System.
            var backKeyboard = Keyboard.current;
            if (backKeyboard != null && backKeyboard.escapeKey.wasPressedThisFrame) HandleBack();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.nKey.wasPressedThisFrame) OpenNotificationSettings();
                if (keyboard.hKey.wasPressedThisFrame && _view != null) _view.ToggleHelp();
            }
#endif

            if (_playing && !_inTutorial && _score != null) UpdateChainMeter();

            if (!_playing || _resolvingMatches || _inTutorial) return;

            _timeAlive += Time.deltaTime;
            _score.TickSurvivalBonus(Time.deltaTime, _board.GetOccupancy01());

            if (_slowFillBuff > 0f) _slowFillBuff -= Time.deltaTime;

            float interval = _slowFillBuff > 0f ? 8f : DifficultyCurve.GetRowIntervalSeconds(_timeAlive);
            if (_timeAlive >= _settings.InitialRiseGraceSeconds) _riseAccumulator += Time.deltaTime;
            if (_riseAccumulator >= interval)
            {
                _riseAccumulator = 0f;
                StartCoroutine(RiseRowAndResolve());
            }

            UpdateHud();

#if UNITY_EDITOR
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.bKey.wasPressedThisFrame) _slowFillBuff = 5f;
                if (kb.rKey.wasPressedThisFrame && !_resolvingMatches)
                    StartCoroutine(RiseRowAndResolve());
            }
#endif
        }

        /// <summary>
        /// Android Back: closes the top-most panel, otherwise pauses/resumes the run. On the main menu it does
        /// nothing, so a stray swipe never quits the game.
        /// </summary>
        void HandleBack()
        {
            if (_view == null) return;

            if (PushPermissionPromptUI.IsOpen) { PushPermissionPromptUI.Dismiss(); return; } // top-most modal
            if (_switchPlayerUI != null && _switchPlayerUI.IsOpen) { _switchPlayerUI.Back(); return; }
            if (_notificationSettingsUI != null && _notificationSettingsUI.IsOpen) { _notificationSettingsUI.Hide(); return; }
            if (_achievementsUI != null && _achievementsUI.IsOpen) { _achievementsUI.Back(); return; }
            if (_view.IsConfirmDialogOpen) { _view.HideConfirmDialog(); return; }
            if (_friendsManagerUI != null && _friendsManagerUI.IsOpen) { _friendsManagerUI.ClosePanel(); return; }
            if (_leaderboardManager != null && _leaderboardManager.IsOpen) { _leaderboardManager.HideLeaderboard(); return; }
            if (_view.IsChallengesOpen) { _view.ToggleChallenges(); return; }
            if (_view.IsHelpOpen) { _view.ToggleHelp(); return; }
            if (_view.IsPaused) { _view.ResumeGame(); return; }
            if (_inTutorial) return; // the tutorial is driven by its own dialog buttons
            if (_playing) { ClearTapBuffer(); _view.PauseGame(); return; }
            if (_view.IsGameOverShowing) { GoToMainMenuFromGameOver(); return; }
        }

        void BeginRun()
        {
            _view.SetTutorialActive(false); // Re-enable all buttons (covers tutorial->game and menu->game)
            _view.HideMainMenu();
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            if (_friendsManagerUI != null) _friendsManagerUI.ClosePanel();
            _board = new GameBoard(_settings.Columns, _settings.Rows);
            _score = new ScoreService();
            _score.Reset(_settings.ComboWindowSeconds);

            // Consume pending reward points to start with bonus points
            if (_provider != null)
            {
                int bonus = _provider.ConsumePendingRewards();
                if (bonus > 0)
                {
                    _score.AddDirectPoints(bonus);
                }
            }
            // Challenge progress as the run starts (after moving to a new day's set if the day changed); the Game Over
            // screen animates from here to show what the run added.
            _runRows = _runPerfects = _runBombs = _runMatch5s = 0;
            _challenges?.BeginRun();
            _challengesAtRunStart = GetChallengeDisplayData();
            _allDoneBonusAtRunStart = _provider != null && _provider.AllDoneBonusGranted;

            _timeAlive = 0f;
            _riseAccumulator = 0f;
            _slowFillBuff = 0f;
            _playing = true;
            _resolvingMatches = false;
            ClearTapBuffer();
            _view.ResetBoardVisuals();
            _current = RollIncomingTile();
            _next = RollIncomingTile();

            _tilesPlacedThisRun = 0;
            if (PlayerPrefs.GetInt("InstructionalTextDismissed", 0) == 0)
            {
                _view.SetInstructionalTextActive(true);
            }
            else
            {
                _view.SetInstructionalTextActive(false);
            }
            
            _view.HideGameOver();
            _view.RefreshGrid(_board.Cells);
            UpdateHud();
        }

        TileKind RollIncomingTile()
        {
            float wild = DifficultyCurve.GetWildChance(_timeAlive);
            if (UnityEngine.Random.value < wild) return TileKind.Wild;
            if (UnityEngine.Random.value < _settings.BombChance) return TileKind.Bomb;
            return (TileKind)UnityEngine.Random.Range((int)TileKind.Red, (int)TileKind.Purple + 1);
        }

        TileKind RollRisingCell()
        {
            if (UnityEngine.Random.value < DifficultyCurve.GetWildChance(_timeAlive)) return TileKind.Wild;
            return (TileKind)UnityEngine.Random.Range((int)TileKind.Red, (int)TileKind.Purple + 1);
        }

        public void OnColumnClicked(int col)
        {
            if (!_playing) return;
            if (Time.timeScale <= 0f) return; // paused, or a panel froze the run

            if (_resolvingMatches)
            {
                // Don't drop the tap: remember the latest one and replay it when the board settles (FinishResolve).
                if (!_inTutorial)
                {
                    _bufferedColumn = col;
                    _bufferedTapTime = Time.unscaledTime;
                }
                return;
            }

            // If in tutorial mode, only allow dropping in the required column
            if (_inTutorial && col != _tutorialRequiredColumn)
            {
                _view.RejectColumnTap(col, nudgeTiles: false);
                return;
            }

            int row = _board.GetLowestEmptyRow(col);
            if (row < 0)
            {
                // Full column: refuse the tap instead of ending the run. The run ends only when a rising row overflows.
                _view.RejectColumnTap(col);
                return;
            }

            if (!_inTutorial && PlayerPrefs.GetInt("InstructionalTextDismissed", 0) == 0)
            {
                _tilesPlacedThisRun++;
                if (_tilesPlacedThisRun >= 5)
                {
                    PlayerPrefs.SetInt("InstructionalTextDismissed", 1);
                    PlayerPrefs.Save();
                    _view.SetInstructionalTextActive(false);
                }
            }

            var placed = _current;
            if (_inTutorial)
            {
                _current = _next;
                // Defer changing _next to the tutorial controller setup
            }
            else
            {
                _current = _next;
                _next = RollIncomingTile();
            }

            SfxPlayer.Play(Sfx.Drop);

            if (placed == TileKind.Bomb)
            {
                StartCoroutine(ExplodeBombAndResolve(col, row));
                UpdateHud();
                return;
            }

            _board.SetCell(col, row, placed);
            _view.RefreshGrid(_board.Cells);
            _view.AnimateDrop(col, row);
            UpdateHud();

            StartCoroutine(ResolveMatchesAnimCoroutine());
        }

        IEnumerator ResolveMatchesAnimCoroutineInner()
        {
            bool emptyAfter = false;
            var matches = new System.Collections.Generic.HashSet<(int r, int c)>();

            while (true)
            {
                MatchFinder.CollectMatches(_board.Cells, _board.Width, _board.Height, matches, out int largestInWave);
                if (matches.Count == 0) break;

                // Let the tile that was just dropped land before its match pops.
                float landing = _view.DropRemaining;
                if (landing > 0f) yield return new WaitForSeconds(landing);

                // Sound as the pop starts, not after it, so a clear feels instant; the pitch climbs with the combo.
                // The wave is timed (for the combo window) here too, so the pitch and the score always agree.
                float waveTime = Time.time;
                float centreRow = 0f, centreCol = 0f;
                foreach (var m in matches) { centreRow += m.r; centreCol += m.c; }
                centreRow /= matches.Count;
                centreCol /= matches.Count;
                bool hasFullRow = HasEntireRowInMatch(matches);
                if (largestInWave >= 4 || hasFullRow) SfxPlayer.Play(Sfx.ClearBig);
                else SfxPlayer.Play(Sfx.Clear, SfxPlayer.ComboPitch(_score.PeekNextComboMultiplier(waveTime)));

                // Animate/blink tiles before clearing
                yield return _view.BlinkTilesCoroutine(matches, _board.Cells, _blinkTime);

                int cleared = matches.Count;

                foreach (var m in matches)
                {
                    _board.Cells[m.r, m.c] = TileKind.Empty;
                    _view.SetCellColor(m.r, m.c, TileKind.Empty);
                }

                yield return AnimateGravityCoroutine();

                bool perfect = _board.IsBoardEmpty();
                _runRows += CountFullRows(matches);
                if (perfect) _runPerfects++;
                if (largestInWave >= 5) _runMatch5s++;
                int prevCombo = _score.CurrentComboMultiplier;
                _score.RegisterClearWave(largestInWave, cleared, hasFullRow, perfect,
                    waveTime, out int pointsAdded, out int comboMult, out float carryMult);

                _view.TriggerClearShake(cleared);
                _view.ShowPointsPopup(centreRow, centreCol, pointsAdded, comboMult);

                // ── Reward toasts ────────────────────────────────────────────
                if (perfect)
                {
                    _view.ShowRewardToast("PERFECT CLEAR!", "+2000", RewardToastStyle.PerfectClear);
                    SfxPlayer.Play(Sfx.NewBest);
                }
                else if (hasFullRow)
                    _view.ShowRewardToast("ROW CLEAR", "+200", RewardToastStyle.RowClear);

                if (comboMult >= 2)
                    _view.ShowRewardToast($"COMBO x{comboMult}", string.Empty, RewardToastStyle.Combo);

                if (largestInWave >= 4 && carryMult > 1f)
                    _view.ShowRewardToast($"CARRY x{carryMult:0.#}", string.Empty, RewardToastStyle.Carry);
                // ─────────────────────────────────────────────────────────────

                _view.RefreshGrid(_board.Cells);
                UpdateHud();

                yield return new WaitForSeconds(PostWaveSeconds);

                if (perfect)
                {
                    emptyAfter = true;
                    break;
                }
            }

            if (emptyAfter)
            {
                _slowFillBuff = _settings.SlowFillBuffSeconds; // the rise line and bar show it (UpdateHud)
            }

            _challenges.TickRun(CurrentRunStats(), runEnded: false);
            UpdateHud();
        }

        int CountFullRows(System.Collections.Generic.HashSet<(int r, int c)> matchSet)
        {
            int rows = 0;
            for (int r = 0; r < _board.Height; r++)
            {
                bool full = true;
                for (int c = 0; c < _board.Width && full; c++) full = matchSet.Contains((r, c));
                if (full) rows++;
            }
            return rows;
        }

        RunStats CurrentRunStats() => new RunStats
        {
            SecondsAlive = _timeAlive,
            Score = _score.TotalScore,
            BestComboMultiplier = _score.BestComboMultiplier,
            RowsCleared = _runRows,
            PerfectClears = _runPerfects,
            Bombs = _runBombs,
            Match5s = _runMatch5s,
            LongestChain = _score.BestChain,
        };

        bool HasEntireRowInMatch(System.Collections.Generic.HashSet<(int r, int c)> matchSet)
        {
            for (int r = 0; r < _board.Height; r++)
            {
                bool ok = true;
                for (int c = 0; c < _board.Width; c++)
                {
                    if (!matchSet.Contains((r, c)))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }

        IEnumerator AnimateGravityCoroutine()
        {
            int[,] fallDistances = new int[_settings.Rows, _settings.Columns];
            bool anyFalls = false;
            for (int c = 0; c < _settings.Columns; c++)
            {
                int emptySum = 0;
                for (int r = 0; r < _settings.Rows; r++)
                {
                    if (_board.Cells[r, c] == TileKind.Empty) 
                        emptySum++;
                    else if (emptySum > 0)
                    {
                        fallDistances[r, c] = emptySum;
                        anyFalls = true;
                    }
                }
            }

            _board.ApplyGravity();

            if (anyFalls)
            {
                yield return _view.AnimateGravityCoroutine(fallDistances);
            }
        }

        IEnumerator ResolveMatchesAnimCoroutine()
        {
            _resolvingMatches = true;
            yield return ResolveMatchesAnimCoroutineInner();
            FinishResolve();
        }

        /// <summary>
        /// Ends a resolve sequence and replays a tap that arrived during it, if it is recent and its column
        /// still has room (a buffered tap must never land on a full column).
        /// </summary>
        void FinishResolve()
        {
            _resolvingMatches = false;
            if (_bufferedColumn < 0) return;

            int col = _bufferedColumn;
            float age = Time.unscaledTime - _bufferedTapTime;
            ClearTapBuffer();

            if (!_playing || _inTutorial || Time.timeScale <= 0f) return;
            if (age > TapBufferSeconds) return;
            if (_board.GetLowestEmptyRow(col) < 0) return;
            OnColumnClicked(col);
        }

        void ClearTapBuffer() => _bufferedColumn = -1;

        IEnumerator ExplodeBombAndResolve(int col, int row)
        {
            _resolvingMatches = true;
            _board.SetCell(col, row, TileKind.Bomb);
            _view.RefreshGrid(_board.Cells);
            _view.AnimateDrop(col, row);
            float landing = _view.DropRemaining;
            if (landing > 0f) yield return new WaitForSeconds(landing);

            yield return _view.BlinkBombAreaCoroutine(col, row, _board.Cells, BombBlinkSeconds);

            _board.ExplodeBomb3x3(col, row);
            _runBombs++;
            SfxPlayer.Play(Sfx.Bomb);
            Haptics.Medium();
            
            for (int r = 0; r < _settings.Rows; r++)
            for (int c = 0; c < _settings.Columns; c++)
                if (_board.Cells[r, c] == TileKind.Empty)
                    _view.SetCellColor(r, c, TileKind.Empty);

            yield return AnimateGravityCoroutine();
            _view.RefreshGrid(_board.Cells);

            yield return ResolveMatchesAnimCoroutineInner();
            FinishResolve();
        }

        IEnumerator RiseRowAndResolve()
        {
            _resolvingMatches = true;

            _view.SettleTiles(); // a tile still landing must not animate after the rows shift under it
            if (!_board.TryRiseRow(RollRisingCell))
            {
                EndRun();
                yield break;
            }
            SfxPlayer.Play(Sfx.Rise);

            _view.RefreshGrid(_board.Cells);

            yield return _view.SlideGridUpCoroutine(RiseSlideSeconds);

            yield return ResolveMatchesAnimCoroutineInner();
            FinishResolve();
        }

        void EndRun()
        {
            bool isNewBest = RecordRun();
            SfxPlayer.Play(isNewBest ? Sfx.NewBest : Sfx.GameOver);
            _view.ShowGameOver(new GameOverData
            {
                Score = _score.TotalScore,
                IsNewBest = isNewBest,
                SecondsSurvived = _timeAlive,
                DailyBest = _save.DailyBest,
                AllTimeHigh = _save.AllTimeHigh,
                Streak = _save.Streak,
                StreakRewardThisRun = _streakRewardThisRun,
                ChallengesBefore = _challengesAtRunStart,
                Challenges = GetChallengeDisplayData(),
                AllDoneBonusGranted = _provider != null && _provider.AllDoneBonusGranted,
                AllDoneBonusThisRun = _provider != null && _provider.AllDoneBonusGranted && !_allDoneBonusAtRunStart,
                ChallengeThisGame = ThisGameValues(),
            });
        }

        bool _streakRewardThisRun;

        /// <summary>What the run that just ended reached for each of today's challenges, on its own.</summary>
        int[] ThisGameValues()
        {
            var defs = _provider?.ActiveChallenges;
            if (defs == null) return null;
            var run = CurrentRunStats();
            var values = new int[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                values[i] = defs[i] == null ? 0 : ChallengeTracker.RunValue(defs[i].Type, run, runEnded: true);
            return values;
        }

        /// <summary>
        /// Saves the finished run: challenges, best scores, streak, cloud save and leaderboard submission.
        /// Shared by game over and by ending a run from the pause menu. Returns true for a new all-time best.
        /// </summary>
        bool RecordRun()
        {
            _playing = false;
            ClearTapBuffer();
            _challenges.TickRun(CurrentRunStats(), runEnded: true);
            int previousBest = _save.AllTimeHigh; // capture before RegisterRunEnd updates it
            LocalProgress.RegisterRunEnd(_score.TotalScore, _save);
            _streakRewardThisRun = StreakReward.TryGrant(_save, _provider);
            if (_achievements != null)
            {
                AnnounceBadges(_achievements.RecordRun(CurrentRunStats()));
                _ = _achievements.SaveAsync();
            }
            NotificationService.Instance?.RecordRunCompleted(); // learns the usual play hour for reminder timing

            if (_provider != null)
            {
                _ = _provider.SaveAsync();
            }

            if (_leaderboardService != null)
                _ = SubmitScoreAsync(_score.TotalScore, previousBest);

            return _score.TotalScore > previousBest;
        }

        /// <summary>Submits the run, then reloads the leaderboard in the background so it opens with this score in it.</summary>
        async Task SubmitScoreAsync(int score, int previousBest)
        {
            try { await _leaderboardService.SubmitScoreAsync(score, previousBest); }
            catch (Exception e) { Debug.LogWarning("[StackSurgeGame] Score submit failed: " + e.Message); }
            if (_leaderboardManager == null) return;
            int? rank = await _leaderboardManager.RefreshAfterSubmitAsync();
            if (this != null && _view != null) _view.SetGameOverRank(rank);
        }

        /// <summary>Pause → Main Menu (after the confirm dialog): the run counts, exactly like a game over.</summary>
        void QuitRunToMainMenu()
        {
            if (_playing && !_inTutorial)
            {
                StopAllCoroutines(); // an in-flight clear/rise sequence must not end the run a second time
                _resolvingMatches = false;
                RecordRun();
            }
            GoToMainMenuFromGameOver();
        }

        /// <summary>Help → Replay Tutorial. A run in progress is saved first rather than thrown away.</summary>
        void ReplayTutorialFromHelp()
        {
            if (_playing && !_inTutorial)
            {
                StopAllCoroutines();
                _resolvingMatches = false;
                RecordRun();
            }
            StartTutorial();
        }

        /// <summary>Leaves the tutorial early (Pause → Main Menu, Back) without leaving the menu locked.</summary>
        void AbortTutorial()
        {
            if (!_inTutorial) return;

            StopAllCoroutines(); // the tutorial coroutine and any resolve it started
            _tutorialCoroutine = null;
            _inTutorial = false;
            _tutorialRequiredColumn = -1;
            _resolvingMatches = false;
            _playing = false;
            ClearTapBuffer();
            _view.ClearColumnHighlight();
            _view.HideTutorialDialog();
            _view.SetTutorialActive(false);
        }

        public void Retry()
        {
            BeginRun();
        }

        public void ShareStub()
        {
            var msg = $"Stack Surge — {_score.TotalScore} points!";
            GUIUtility.systemCopyBuffer = msg;
            Debug.Log("Share (stub): " + msg);
        }

        void UpdateHud()
        {
            float interval = _slowFillBuff > 0f ? 8f : DifficultyCurve.GetRowIntervalSeconds(_timeAlive);
            float nextRise = Mathf.Max(0f, interval - _riseAccumulator);
            float graceLeft = Mathf.Max(0f, _settings.InitialRiseGraceSeconds - _timeAlive);
            bool inGrace = graceLeft > 0f;
            float riseProgress = inGrace
                ? (_settings.InitialRiseGraceSeconds > 0f ? _timeAlive / _settings.InitialRiseGraceSeconds : 1f)
                : (interval > 0f ? _riseAccumulator / interval : 0f);

            _view.UpdateHud(_score.TotalScore, _timeAlive, inGrace ? graceLeft : nextRise, riseProgress, inGrace, _current, _next,
                slowSecondsLeft: Mathf.Max(0f, _slowFillBuff));
        }

        /// <summary>
        /// Chain counter above the board. Runs every frame, clears included: the 3 seconds to the next clear keep
        /// counting while tiles pop (the combo uses game time, not the paused-during-clears run clock).
        /// </summary>
        void UpdateChainMeter()
        {
            float window = Mathf.Max(0.01f, _score.ComboWindow);
            _view.UpdateChain(_score.CurrentChain, _score.ChainTimeLeft(Time.time) / window, TodaysChainTarget());
        }

        /// <summary>Target of today's chain challenge while it's still open, else 0 (the counter then shows no target).</summary>
        int TodaysChainTarget()
        {
            var defs = _provider?.ActiveChallenges;
            if (defs == null) return 0;
            for (int i = 0; i < defs.Length; i++)
                if (defs[i] != null && defs[i].Type == ChallengeType.ChainInRun && i < _provider.CompletionBits.Length && _provider.CompletionBits[i] == 0)
                    return defs[i].TargetValue;
            return 0;
        }

        ChallengeDisplayData[] GetChallengeDisplayData()
        {
            if (_provider == null) return Array.Empty<ChallengeDisplayData>();
            return _provider.GetDisplayData();
        }

        TimeSpan GetTimeUntilReset()
        {
            if (_provider == null) return TimeSpan.FromHours(24);
            return _provider.GetTimeUntilReset();
        }

        void ClaimReward(int index)
        {
            if (_provider != null)
            {
                _provider.ClaimReward(index);
                // Also trigger save immediately to persist the claim
                _ = _provider.SaveAsync();
            }
        }

        // ── In-app updates ───────────────────────────────────────────────────
        PlayAppUpdate _appUpdate;

        const string UpdatePromptDayKey = "update_prompt_day";

        /// <summary>
        /// Google Play's in-app updates. When one is available, Google's own prompt opens on the menu (see
        /// <see cref="TryPromptUpdate"/>); the banner at the bottom of the menu then shows the download and, once it's
        /// done, offers RESTART. An update left waiting installs when the player leaves the game (never mid-run).
        /// </summary>
        void SetUpAppUpdates()
        {
            _appUpdate = gameObject.AddComponent<PlayAppUpdate>();
            _appUpdate.Changed += OnAppUpdateChanged;
            _view.OnUpdateBannerButton = _appUpdate.Restart;
            if (_switchPlayerUI != null) _switchPlayerUI.Closed += () => TryPromptUpdate();
        }

        void OnAppUpdateChanged()
        {
            _view.ShowUpdateBanner(_appUpdate.Current, _appUpdate.Progress);
            if (_appUpdate.Current == PlayAppUpdate.Stage.Available) TryPromptUpdate();
        }

        /// <summary>
        /// Opens Google's update prompt when an update is available: on the menu with nothing else showing (consent,
        /// notifications, the Play Games switch), and at most once a day, so a player who says no isn't asked every launch.
        /// </summary>
        bool TryPromptUpdate()
        {
            if (_appUpdate == null || _appUpdate.Current != PlayAppUpdate.Stage.Available) return false;
            if (_playing || _inTutorial || _view == null) return false;
            if (_view.IsLoadingScreenActive || _view.IsGameOverShowing) return false;
            if (AnalyticsConsent.NeedsPrompt(_save) || PushPermissionPromptUI.IsOpen) return false;
            if (_switchPlayerUI != null && _switchPlayerUI.IsOpen) return false;
            string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (PlayerPrefs.GetString(UpdatePromptDayKey, "") == today) return false;
            PlayerPrefs.SetString(UpdatePromptDayKey, today);
            _appUpdate.StartUpdate();
            return true;
        }

        void OnLoadingScreenFinished()
        {
            _playing = false;
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            _view.ShowMainMenu(false, profileEntrance: true);
            _appUpdate?.Check();
            if (_analyticsConsentUI != null) _analyticsConsentUI.ShowIfNeeded();

            // A push or reminder tap that launched the app; wait if the consent prompt is up.
            if (!AnalyticsConsent.NeedsPrompt(_save) && !TryRoutePendingPush()) TryOfferPlayerSwitch();
        }

        public void GoToMainMenuFromGameOver()
        {
            AbortTutorial();
            _playing = false;
            _view.HideGameOver();
            _view.SetInstructionalTextActive(false);
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            _view.ShowMainMenu(true, profileEntrance: true);
            RefreshChallenges();

            // Back on the menu after a run: open anything tapped meanwhile, otherwise this is the moment for the
            // soft notification prompt (it checks permission, consent, ask count and spacing itself).
            if (!TryRoutePendingPush() && !TryOfferPlayerSwitch()
                && !PushPermissionPromptUI.ShowIfEligible(_save, onClosed: () => { if (!TryRoutePendingPush()) TryPromptUpdate(); }))
                TryPromptUpdate();
        }

        async void OnPlayerNameChangedFromMainMenu(string newName)
        {
            string name = newName.Trim();
            if (name.Length < 1 || name.Length > 20) return;
            _save.PlayerDisplayName = name;
            LocalProgress.Save(_save);

            RegisterLeaderboardCallbacks();

            if (_leaderboardService != null)
            {
                await _leaderboardService.SetPlayerNameAsync(name);
            }
            
            _view.RefreshMainMenuStats();
        }

        void OnMainMenuPlayGame()
        {
            if (!_save.TutorialCompleted)
            {
                _view.HideMainMenu();
                _view.ShowTutorialChoiceDialog(
                    "Welcome to Stack Surge!",
                    "Would you like to play the interactive tutorial to learn the basics?",
                    onPlay: () =>
                    {
                        StartTutorial();
                    },
                    onSkip: () =>
                    {
                        _save.TutorialCompleted = true;
                        LocalProgress.Save(_save);
                        BeginRun();
                    }
                );
            }
            else
            {
                BeginRun();
            }
        }

        void OnMainMenuPlayTutorial()
        {
            StartTutorial();
        }

        void OnMainMenuToggleLeaderboard()
        {
            _leaderboardManager.ToggleLeaderboard();
        }
    }
}

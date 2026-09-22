using System;
using System.Collections;
using System.Threading.Tasks;
using StackSurge.Core;
using StackSurge.Meta;
using StackSurge.Settings;
using StackSurge.UI;
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

        TileKind _current;
        TileKind _next;

        float _timeAlive;
        float _riseAccumulator;
        float _slowFillBuff;
        bool _playing;
        bool _resolvingMatches;
        int _tilesPlacedThisRun;

        [SerializeField] float _blinkTime = 0.25f;

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
            Time.timeScale = 1f; // Ensure clock runs — WaitForSeconds blocks if timeScale == 0
            _view.HideMainMenu();
            _view.HideGameOver();
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            _view.SetTutorialActive(true); // Lock all non-tutorial buttons
            _view.SetInstructionalTextActive(false);
            _tutorialCoroutine = StartCoroutine(_tutorialController.PlayTutorialCoroutine());
        }

        static ChallengeDefinition[] BuildDefaultChallenges()
        {
            ChallengeDefinition C(string title, string desc, ChallengeType ty, int target)
            {
                var c = ScriptableObject.CreateInstance<ChallengeDefinition>();
                c.Title = title;
                c.Description = desc;
                c.Type = ty;
                c.TargetValue = target;
                return c;
            }

            return new[]
            {
                C("Score 1000", "Reach 1000 points in one run.", ChallengeType.ScoreInRun, 1000),
                C("Survivalist", "Stay alive for 3 minutes.", ChallengeType.SurviveSeconds, 180),
                C("Combo hunter", "Reach x5 combo multiplier.", ChallengeType.MaxComboMultiplier, 5)
            };
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
                cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
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
            _view.OnReplayTutorialTriggered = StartTutorial;
            _view.OnNotificationSettingsTriggered = OpenNotificationSettings;

            // Game starts only after loading screen finishes fading
            _view.OnLoadingDone = OnLoadingScreenFinished;

            // UGS / Challenges async initialization
            _view.UpdateLoadingStatus("CONNECTING TO SERVICES...");
            var defs = _challengeAssets != null && _challengeAssets.Length > 0 ? _challengeAssets : BuildDefaultChallenges();
            await _provider.InitializeAsync(_save, defs);

            _friendsService = new FriendsService(_provider.IsOnline, _save);
            if (_friendsManagerUI != null)
            {
                _friendsManagerUI.Initialize(_friendsService);
            }

            // Ensure Friends SDK is initialized and publish Online presence
            _ = _friendsService.EnsureInitializedAsync();

            _leaderboardService = new LeaderboardService(_provider.IsOnline, _save, _friendsService);
            RegisterLeaderboardCallbacks();

            _view.UpdateLoadingStatus(_provider.IsOnline ? "ONLINE" : "NO INTERNET, PLAYING OFFLINE");
            _challenges = new ChallengeTracker(_provider);

            // Initialize Notification System & Programmatic Settings UI
            NotificationService.EnsureInstance(_save);
            if (NotificationService.Instance != null)
            {
                // Remote pushes honour the RECIPIENT's public preference (read from Cloud Save).
                NotificationService.Instance.RecipientPrefsResolver = id => _friendsService.GetSocialPrefsAsync(id);
                NotificationService.Instance.OnPushOpened -= OnPushOpened;
                NotificationService.Instance.OnPushOpened += OnPushOpened;
                NotificationService.Instance.RescheduleReminders();
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
                onSocialSettingsChanged: () => { _ = _friendsService?.PublishSocialPrefsAsync(); });

            _view.EnableStartButton();
        }

        private NotificationSettingsUI _notificationSettingsUI;

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

        /// <summary>Routes a tapped remote push to the matching screen.</summary>
        void OnPushOpened(PushKind kind)
        {
            if (_playing || _inTutorial) return; // don't yank the player out of a run

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
                        _leaderboardManager.SetFilterMode(LeaderboardFilterMode.FriendsOnly);
                        _leaderboardManager.ShowLeaderboard();
                    }
                    break;
            }
        }

        public void OpenNotificationSettings()
        {
            if (_notificationSettingsUI != null)
            {
                _notificationSettingsUI.Show();
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                NotificationService.Instance?.RescheduleReminders();
                if (_friendsService != null) _ = _friendsService.PublishPresenceAsync(PresenceStatus.Offline);
            }
            else
            {
                if (_friendsService != null) _ = _friendsService.PublishPresenceAsync(PresenceStatus.Online);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.nKey.wasPressedThisFrame) OpenNotificationSettings();
                if (keyboard.hKey.wasPressedThisFrame && _view != null) _view.ToggleHelp();
            }
#endif

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

            _timeAlive = 0f;
            _riseAccumulator = 0f;
            _slowFillBuff = 0f;
            _playing = true;
            _resolvingMatches = false;
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
            if (!_playing || _resolvingMatches) return;

            // If in tutorial mode, only allow dropping in the required column
            if (_inTutorial && col != _tutorialRequiredColumn)
            {
                return;
            }

            int row = _board.GetLowestEmptyRow(col);
            if (row < 0)
            {
                if (_inTutorial) return;

                EndRun();
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

            if (placed == TileKind.Bomb)
            {
                StartCoroutine(ExplodeBombAndResolve(col, row));
                UpdateHud();
                return;
            }

            _board.SetCell(col, row, placed);
            _view.RefreshGrid(_board.Cells);
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

                // Animate/blink tiles before clearing
                yield return _view.BlinkTilesCoroutine(matches, _board.Cells, _blinkTime);

                bool hasFullRow = HasEntireRowInMatch(matches);
                int cleared = matches.Count;

                foreach (var m in matches)
                {
                    _board.Cells[m.r, m.c] = TileKind.Empty;
                    _view.SetCellColor(m.r, m.c, TileKind.Empty);
                }

                yield return AnimateGravityCoroutine();

                bool perfect = _board.IsBoardEmpty();
                int prevCombo = _score.CurrentComboMultiplier;
                _score.RegisterClearWave(largestInWave, cleared, hasFullRow, perfect,
                    Time.time, out int pointsAdded, out int comboMult, out float carryMult);

                _view.TriggerClearShake(cleared);

                // ── Reward toasts ────────────────────────────────────────────
                if (perfect)
                    _view.ShowRewardToast("PERFECT CLEAR!", "+2000", RewardToastStyle.PerfectClear);
                else if (hasFullRow)
                    _view.ShowRewardToast("ROW CLEAR", "+200", RewardToastStyle.RowClear);

                if (comboMult >= 2)
                    _view.ShowRewardToast($"COMBO x{comboMult}", string.Empty, RewardToastStyle.Combo);

                if (largestInWave >= 4 && carryMult > 1f)
                    _view.ShowRewardToast($"CARRY x{carryMult:0.#}", string.Empty, RewardToastStyle.Carry);
                // ─────────────────────────────────────────────────────────────

                _view.RefreshGrid(_board.Cells);
                UpdateHud();

                yield return new WaitForSeconds(0.1f);

                if (perfect)
                {
                    emptyAfter = true;
                    break;
                }
            }

            if (emptyAfter)
            {
                _slowFillBuff = _settings.SlowFillBuffSeconds;
                _view.ShowSlowRiseBadge(_settings.SlowFillBuffSeconds);
            }

            _challenges.TickRun(_timeAlive, _score.TotalScore, _score.BestComboMultiplier);
            UpdateHud();
        }

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
            _resolvingMatches = false;
        }

        IEnumerator ExplodeBombAndResolve(int col, int row)
        {
            _resolvingMatches = true;
            _board.SetCell(col, row, TileKind.Bomb);
            _view.RefreshGrid(_board.Cells);

            yield return _view.BlinkBombAreaCoroutine(col, row, _board.Cells, 0.25f);

            _board.ExplodeBomb3x3(col, row);
            
            for (int r = 0; r < _settings.Rows; r++)
            for (int c = 0; c < _settings.Columns; c++)
                if (_board.Cells[r, c] == TileKind.Empty)
                    _view.SetCellColor(r, c, TileKind.Empty);

            yield return AnimateGravityCoroutine();
            _view.RefreshGrid(_board.Cells);

            yield return ResolveMatchesAnimCoroutineInner();
            _resolvingMatches = false;
        }

        IEnumerator RiseRowAndResolve()
        {
            _resolvingMatches = true;

            if (!_board.TryRiseRow(RollRisingCell))
            {
                EndRun();
                yield break;
            }

            _view.RefreshGrid(_board.Cells);

            yield return _view.SlideGridUpCoroutine(0.2f);

            yield return ResolveMatchesAnimCoroutineInner();
            _resolvingMatches = false;
        }

        void EndRun()
        {
            _playing = false;
            _challenges.TickRun(_timeAlive, _score.TotalScore, _score.BestComboMultiplier);
            int previousBest = _save.AllTimeHigh; // capture before RegisterRunEnd updates it
            LocalProgress.RegisterRunEnd(_score.TotalScore, _save);

            if (_provider != null)
            {
                _ = _provider.SaveAsync();
            }

            if (_leaderboardService != null)
                _ = _leaderboardService.SubmitScoreAsync(_score.TotalScore, previousBest);
            
            _view.ShowGameOver(_score.TotalScore, _save.DailyBest, _save.AllTimeHigh, _save.Streak);
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
            
            string riseLine = graceLeft > 0f
                ? $"Auto-rise starts in: {graceLeft:0.0}s"
                : $"Next auto-rise: {nextRise:0.0}s";

            string timeStr = FormatTime(_timeAlive);
            float wildChance = DifficultyCurve.GetWildChance(_timeAlive) * 100f;

            _view.UpdateHud(_score.TotalScore, timeStr, riseLine, wildChance, _current, _next);
        }

        static string FormatTime(float t)
        {
            int m = (int)(t / 60f);
            int s = (int)(t % 60f);
            return $"{m:00}:{s:00}";
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

        void OnLoadingScreenFinished()
        {
            _playing = false;
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            _view.ShowMainMenu(false);
        }

        public void GoToMainMenuFromGameOver()
        {
            _playing = false;
            _view.HideGameOver();
            _view.SetInstructionalTextActive(false);
            if (_leaderboardManager != null) _leaderboardManager.HideLeaderboard();
            _view.ShowMainMenu(true);
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using StackSurge.Core;
using StackSurge.Settings;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using DG.Tweening;

namespace StackSurge.UI
{
    public class GameView : MonoBehaviour
    {
        StackSurgeSettings _settings;
        Image[,] _cellImages;
        RectTransform _gridRoot;
        [SerializeField] TextMeshProUGUI _scoreText;
        [SerializeField] TextMeshProUGUI _hudText;
        [SerializeField] GameObject _instructionalText;

        // Visual Previews
        [SerializeField] Image _nextPreviewImg;
        [SerializeField] Image _queuedPreviewImg;
        [SerializeField] Image _nextInnerIcon;
        [SerializeField] Image _queuedInnerIcon;
        [SerializeField] Sprite _bombSprite;
        [SerializeField] Sprite _wildSprite;

        // Column buttons
        //[SerializeField] ColumnButtonView _columnButtonPrefab;
        //[SerializeField] Transform _columnButtonsRoot;

        [SerializeField] GameObject _helpPanel;
        [SerializeField] Button _closeHelpButton;
        [SerializeField] Canvas _mainCanvas;
        [SerializeField] GameObject _hudRoot;
        [SerializeField] GameObject _loadingRoot;
        [SerializeField] TextMeshProUGUI _loadingTitleText;
        [SerializeField] TextMeshProUGUI _loadingStatusText;
        [SerializeField] Image[] _loadingGlows;
        [SerializeField] GameObject _gameOverRoot;
        [SerializeField] TextMeshProUGUI _gameOverScore;
        [SerializeField] Button _retryButton;
        [SerializeField] Button _shareButton;

        [SerializeField] GameObject _challengesRoot;
        [SerializeField] GameObject _challengeRowPrefab;
        [SerializeField] TextMeshProUGUI _challengesBody;
        [SerializeField] Button _challengesButton;
        [SerializeField] Button _helpButton;
        [SerializeField] Button _leaderboardButton;

        [Header("Icons")]
        [SerializeField] Sprite completedIcon;
        [SerializeField] Sprite pendingIcon;

        // ── Main Menu Serialized Fields ──────────────────────────────────────
        [Header("Main Menu")]
        [SerializeField] GameObject _mainMenuRoot;

        [Header("Main Menu – Buttons")]
        [SerializeField] Button _mainMenuPlayButton;
        [SerializeField] Button _mainMenuChallengesButton;   // replaces Tutorial
        [SerializeField] Button _mainMenuLeaderboardButton;
        [SerializeField] Button _mainMenuHowToPlayButton;
        [SerializeField] Toggle _mainMenuVibrationToggle;    // placed wherever suits the UI

        [Header("Main Menu – Stats Card")]
        [SerializeField] TextMeshProUGUI _mainMenuAllTimeHighText;
        [SerializeField] TextMeshProUGUI _mainMenuDailyBestText;
        [SerializeField] TextMeshProUGUI _mainMenuStreakText;
        [SerializeField] TextMeshProUGUI _mainMenuPlayerNameText;
        [SerializeField] TMP_InputField _mainMenuNameInput;
        [SerializeField] Button _mainMenuSaveNameButton;

        [Header("Pause UI")]
        [SerializeField] Button _pauseButton;
        [SerializeField] GameObject _pauseMenuRoot;
        [SerializeField] Button _pauseMenuResumeButton;
        [SerializeField] Button _pauseMenuMainMenuButton;

        [Header("Slow Rise Badge")]
        [SerializeField] GameObject _slowRiseBadgePrefab;
        // ────────────────────────────────────────────────────────────────────


        ScreenShake _shake;

        Action<int> _onColumnClicked;
        Action _onRetry;
        Action _onMainMenu;
        Func<ChallengeDisplayData[]> _getChallengesData;
        Func<TimeSpan> _getTimeUntilReset;
        Action<int> _onClaimReward;
        Func<SaveData> _getSaveData;
        Action<string> _onPlayerNameChanged;
        Action _onPlayGame;
        Action _onPlayTutorial;
        Action _onToggleLeaderboard;

        [SerializeField] Transform _challengeRowContainer;
        TextMeshProUGUI _titleText;
        Coroutine _resetTimerCoroutine;
        bool _challengesOpenedFromMainMenu;

        private int _lastScore = 0;
        private int _scoreShown;
        private Tween _scoreCountTween;
        private bool _isPaused = false;
        private bool _helpFrozeClock;

        // HUD change-gating: TMP text is only rebuilt when what it displays actually changes.
        private int _hudTimeSeconds = -1;
        private int _hudRiseTenths = -1;
        private bool _hudInGrace;
        private int _nextPreviewKind = -1;
        private int _queuedPreviewKind = -1;

        /// <summary>True while a run (not the tutorial) is in progress. Set by the game; used to freeze the clock under Help.</summary>
        public Func<bool> IsRunActive;
        /// <summary>Score of the run in progress, or -1 when there is no run to save. Set by the game.</summary>
        public Func<int> GetActiveRunScore;
        /// <summary>Raised when the player confirms ending the run from the pause menu.</summary>
        public Action OnQuitRunConfirmed;

        public bool IsPaused => _isPaused;
        public bool IsHelpOpen => _helpPanel != null && _helpPanel.activeSelf;
        public bool IsChallengesOpen => _challengesRoot != null && _challengesRoot.activeSelf;
        public bool IsGameOverShowing => _gameOverRoot != null && _gameOverRoot.activeSelf;
        public bool IsLoadingScreenActive => _loadingRoot != null && _loadingRoot.activeSelf;

        private Color completedColor = new(0.29f, 0.87f, 0.50f);
        private Color pendingColor = new(0.29f, 0.33f, 0.39f);

        // ── Tutorial UI Elements ─────────────────────────────────────────────
        private GameObject _tutorialDialogGo;
        private TextMeshProUGUI _tutorialTitleText;
        private TextMeshProUGUI _tutorialBodyText;
        private Button _tutorialNextBtn;
        private bool _tutorialNextClicked;
        private Button[] _colOverlayBtns;   // full-height transparent click zones (interactable gate only)
        private ColumnTapZone[] _colTapZones;
        private int _tutorialHighlightedColumn = -1;
        private RectTransform _gridMaskRt;

        public Action OnReplayTutorialTriggered;
        public Action OnNotificationSettingsTriggered;

        void Awake()
        {
            if (_closeHelpButton != null) _closeHelpButton.onClick.AddListener(ToggleHelp);
        }

        public void Build(
            StackSurgeSettings settings,
            Action<int> onColumnClicked,
            Action onRetry,
            Action onMainMenu,
            Func<ChallengeDisplayData[]> getChallengesData,
            Func<TimeSpan> getTimeUntilReset,
            Action<int> onClaimReward,
            Func<SaveData> getSaveData,
            Action<string> onPlayerNameChanged,
            Action onPlayGame,
            Action onPlayTutorial,
            Action onToggleLeaderboard)
        {
            _settings = settings;
            _onColumnClicked = onColumnClicked;
            _onRetry = onRetry;
            _onMainMenu = onMainMenu;
            _getChallengesData = getChallengesData;
            _getTimeUntilReset = getTimeUntilReset;
            _onClaimReward = onClaimReward;
            _getSaveData = getSaveData;
            _onPlayerNameChanged = onPlayerNameChanged;
            _onPlayGame = onPlayGame;
            _onPlayTutorial = onPlayTutorial;
            _onToggleLeaderboard = onToggleLeaderboard;

            EnsureInputSystemUi();
            BuildUi();

            DOTween.SetTweensCapacity(800, 200); // particle bursts are one Sequence per chip
            _retryButton.onClick.AddListener(Retry);

            if (_shareButton != null)
            {
                _shareButton.onClick.RemoveAllListeners();
                _shareButton.onClick.AddListener(GoToMainMenu);
                var shareTxt = _shareButton.GetComponentInChildren<TextMeshProUGUI>();
                if (shareTxt != null) shareTxt.text = "MAIN MENU";
            }

            if (_challengesButton != null) _challengesButton.onClick.AddListener(ToggleChallenges);
            if (_helpButton != null) _helpButton.onClick.AddListener(ToggleHelp);
            if (_pauseButton != null)
            {
                _pauseButton.onClick.RemoveAllListeners();
                _pauseButton.onClick.AddListener(TogglePauseMenu);
            }

            if (_pauseMenuRoot != null)
            {
                _pauseMenuRoot.SetActive(false);
                if (_pauseMenuResumeButton != null)
                {
                    _pauseMenuResumeButton.onClick.RemoveAllListeners();
                    _pauseMenuResumeButton.onClick.AddListener(ResumeGame);
                }
                if (_pauseMenuMainMenuButton != null)
                {
                    _pauseMenuMainMenuButton.onClick.RemoveAllListeners();
                    _pauseMenuMainMenuButton.onClick.AddListener(OnPauseMainMenuPressed);
                }
            }

            if (_challengesBody != null)
                _challengesBody.gameObject.SetActive(false);

            if (_challengesRoot != null)
                _titleText = _challengesRoot.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();

            // Append Replay Tutorial Button to Help Panel
            if (_helpPanel != null)
            {
                var oldBtn = _helpPanel.transform.Find("ReplayTutorialBtn");
                if (oldBtn != null) Destroy(oldBtn.gameObject);

                var replayBtnGo = new GameObject("ReplayTutorialBtn");
                replayBtnGo.transform.SetParent(_helpPanel.transform, false);
                var rt = replayBtnGo.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 60f);
                rt.sizeDelta = new Vector2(340f, 80f);

                var img = replayBtnGo.AddComponent<Image>();
                img.color = new Color(0.95f, 0.75f, 0.2f, 0.95f);

                var btn = replayBtnGo.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    ToggleHelp();
                    OnReplayTutorialTriggered?.Invoke();
                });

                var txtGo = new GameObject("Text");
                txtGo.transform.SetParent(replayBtnGo.transform, false);
                var txtRt = txtGo.AddComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.sizeDelta = Vector2.zero;
                var txt = txtGo.AddComponent<TextMeshProUGUI>();
                txt.text = "REPLAY TUTORIAL";
                txt.fontSize = 30;
                txt.fontStyle = FontStyles.Bold;
                txt.color = Color.black;
                txt.alignment = TextAlignmentOptions.Center;
            }

            if (_helpPanel != null) BuildHelpLegend(_helpPanel.transform);

            // Append Notification Settings Button to Help Panel
            if (_helpPanel != null)
            {
                var oldNotifBtn = _helpPanel.transform.Find("NotificationSettingsBtn");
                if (oldNotifBtn != null) Destroy(oldNotifBtn.gameObject);

                var notifBtnGo = new GameObject("NotificationSettingsBtn");
                notifBtnGo.transform.SetParent(_helpPanel.transform, false);
                var rt = notifBtnGo.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 155f);
                rt.sizeDelta = new Vector2(340f, 80f);

                var img = notifBtnGo.AddComponent<Image>();
                img.color = new Color(0.2f, 0.45f, 0.85f, 0.95f);

                var btn = notifBtnGo.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    ToggleHelp();
                    OnNotificationSettingsTriggered?.Invoke();
                });

                var txtGo = new GameObject("Text");
                txtGo.transform.SetParent(notifBtnGo.transform, false);
                var txtRt = txtGo.AddComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.sizeDelta = Vector2.zero;
                var txt = txtGo.AddComponent<TextMeshProUGUI>();
                txt.text = "SETTINGS";
                txt.fontSize = 30;
                txt.fontStyle = FontStyles.Bold;
                txt.color = Color.white;
                txt.alignment = TextAlignmentOptions.Center;
            }

            // Wire up the main menu buttons now that delegates are assigned
            InitMainMenu();

            StartCoroutine(ApplySkinNextFrame());
        }

        /// <summary>Rounds the scene/prefab UI once the canvas scaler has sized the canvas (see UiSkin).</summary>
        IEnumerator ApplySkinNextFrame()
        {
            yield return null; // runs during the loading screen (timeScale 0 doesn't block a null yield)
            Canvas.ForceUpdateCanvases();
            UiSkin.Apply(_mainCanvas != null ? _mainCanvas.transform : transform);
        }

        // Main Menu Init
        /// <summary>
        /// Wires up all delegates and initial state for the main menu.
        /// All GameObjects are assigned in the Inspector; this method only
        /// adds listeners and sets starting visibility.
        /// </summary>
        private void InitMainMenu()
        {
            if (_mainMenuRoot == null)
            {
                Debug.LogError("[GameView] _mainMenuRoot is not assigned in the Inspector!");
                return;
            }

            // Start hidden – shown after the loading screen completes
            _mainMenuRoot.SetActive(false);
            FitStatsCard();

            // Ensure a CanvasGroup exists for fade animations
            if (_mainMenuRoot.GetComponent<CanvasGroup>() == null)
                _mainMenuRoot.AddComponent<CanvasGroup>();

            // PLAY GAME
            if (_mainMenuPlayButton != null)
            {
                _mainMenuPlayButton.onClick.RemoveAllListeners();
                _mainMenuPlayButton.onClick.AddListener(() =>
                {
                    HideMainMenu();
                    _onPlayGame?.Invoke();
                });
            }
            else Debug.LogWarning("[GameView] _mainMenuPlayButton not assigned.");

            // DAILY CHALLENGES
            if (_mainMenuChallengesButton != null)
            {
                _mainMenuChallengesButton.onClick.RemoveAllListeners();
                _mainMenuChallengesButton.onClick.AddListener(() =>
                {
                    // Open challenges ON TOP of the main menu (mirrors How to Play behaviour)
                    _challengesOpenedFromMainMenu = true;
                    ToggleChallenges();
                });
            }
            else Debug.LogWarning("[GameView] _mainMenuChallengesButton not assigned.");

            // LEADERBOARD
            if (_mainMenuLeaderboardButton != null)
            {
                _mainMenuLeaderboardButton.onClick.RemoveAllListeners();
                _mainMenuLeaderboardButton.onClick.AddListener(() => _onToggleLeaderboard?.Invoke());
            }
            else Debug.LogWarning("[GameView] _mainMenuLeaderboardButton not assigned.");

            // HOW TO PLAY
            if (_mainMenuHowToPlayButton != null)
            {
                _mainMenuHowToPlayButton.onClick.RemoveAllListeners();
                _mainMenuHowToPlayButton.onClick.AddListener(ToggleHelp);
            }
            else Debug.LogWarning("[GameView] _mainMenuHowToPlayButton not assigned.");

            // PROGRAMMATIC MAIN MENU BUTTONS (NOTIFICATIONS & HOW TO PLAY FALLBACK)
            if (_mainMenuRoot != null)
            {
                // 1. Programmatic NOTIFICATIONS Button
                var oldNotifBtn = _mainMenuRoot.transform.Find("ProgNotificationsBtn");
                if (oldNotifBtn != null) Destroy(oldNotifBtn.gameObject);

                var notifBtnGo = new GameObject("ProgNotificationsBtn");
                notifBtnGo.transform.SetParent(_mainMenuRoot.transform, false);

                var rtNotif = notifBtnGo.AddComponent<RectTransform>();
                rtNotif.anchorMin = new Vector2(1f, 1f);
                rtNotif.anchorMax = new Vector2(1f, 1f);
                rtNotif.pivot = new Vector2(1f, 1f);
                rtNotif.anchoredPosition = new Vector2(-40f, -40f);
                rtNotif.sizeDelta = new Vector2(250f, 75f);

                var imgNotif = notifBtnGo.AddComponent<Image>();
                imgNotif.color = new Color(0.18f, 0.40f, 0.88f, 0.95f);

                var btnNotif = notifBtnGo.AddComponent<Button>();
                btnNotif.onClick.AddListener(() => OnNotificationSettingsTriggered?.Invoke());

                var txtNotifGo = new GameObject("Text");
                txtNotifGo.transform.SetParent(notifBtnGo.transform, false);
                var txtRtNotif = txtNotifGo.AddComponent<RectTransform>();
                txtRtNotif.anchorMin = Vector2.zero;
                txtRtNotif.anchorMax = Vector2.one;
                txtRtNotif.sizeDelta = Vector2.zero;

                var txtNotif = txtNotifGo.AddComponent<TextMeshProUGUI>();
                txtNotif.text = "SETTINGS";
                txtNotif.fontSize = 26;
                txtNotif.fontStyle = FontStyles.Bold;
                txtNotif.color = Color.white;
                txtNotif.alignment = TextAlignmentOptions.Center;

                // 2. Programmatic HOW TO PLAY Button (if Inspector button unassigned)
                if (_mainMenuHowToPlayButton == null)
                {
                    var oldHelpBtn = _mainMenuRoot.transform.Find("ProgHelpBtn");
                    if (oldHelpBtn != null) Destroy(oldHelpBtn.gameObject);

                    var helpBtnGo = new GameObject("ProgHelpBtn");
                    helpBtnGo.transform.SetParent(_mainMenuRoot.transform, false);

                    var rtHelp = helpBtnGo.AddComponent<RectTransform>();
                    rtHelp.anchorMin = new Vector2(1f, 1f);
                    rtHelp.anchorMax = new Vector2(1f, 1f);
                    rtHelp.pivot = new Vector2(1f, 1f);
                    rtHelp.anchoredPosition = new Vector2(-40f, -125f);
                    rtHelp.sizeDelta = new Vector2(250f, 75f);

                    var imgHelp = helpBtnGo.AddComponent<Image>();
                    imgHelp.color = new Color(0.95f, 0.70f, 0.20f, 0.95f);

                    var btnHelp = helpBtnGo.AddComponent<Button>();
                    btnHelp.onClick.AddListener(ToggleHelp);

                    var txtHelpGo = new GameObject("Text");
                    txtHelpGo.transform.SetParent(helpBtnGo.transform, false);
                    var txtRtHelp = txtHelpGo.AddComponent<RectTransform>();
                    txtRtHelp.anchorMin = Vector2.zero;
                    txtRtHelp.anchorMax = Vector2.one;
                    txtRtHelp.sizeDelta = Vector2.zero;

                    var txtHelp = txtHelpGo.AddComponent<TextMeshProUGUI>();
                    txtHelp.text = "HOW TO PLAY";
                    txtHelp.fontSize = 26;
                    txtHelp.fontStyle = FontStyles.Bold;
                    txtHelp.color = Color.black;
                    txtHelp.alignment = TextAlignmentOptions.Center;
                }
            }

            // Vibration is in Settings (with Sound and Tile Shapes); hide the old main-menu toggle so it isn't in two places.
            if (_mainMenuVibrationToggle != null) _mainMenuVibrationToggle.gameObject.SetActive(false);
            GamePrefs.Changed -= OnGamePrefsChanged;
            GamePrefs.Changed += OnGamePrefsChanged;

            // SAVE NAME
            if (_mainMenuSaveNameButton != null)
            {
                _mainMenuSaveNameButton.onClick.RemoveAllListeners();
                _mainMenuSaveNameButton.onClick.AddListener(() =>
                {
                    if (_mainMenuNameInput != null)
                    {
                        _onPlayerNameChanged?.Invoke(_mainMenuNameInput.text);
                        _mainMenuNameInput.text = "";
                    }
                });
            }
            else Debug.LogWarning("[GameView] _mainMenuSaveNameButton not assigned.");

            // Name input – submit on keyboard confirm as well
            if (_mainMenuNameInput != null)
            {
                // The game rejects names over 20 characters; stop the field at 20 instead of silently ignoring the save.
                _mainMenuNameInput.characterLimit = 20;
                _mainMenuNameInput.onSubmit.RemoveAllListeners();
                _mainMenuNameInput.onSubmit.AddListener(val =>
                {
                    _onPlayerNameChanged?.Invoke(val);
                    _mainMenuNameInput.text = "";
                });
            }
        }



        /// <summary>
        /// Call with <c>true</c> when the interactive tutorial starts and <c>false</c> when it ends.
        /// Locks every HUD/menu button that should not be reachable mid-tutorial so only the
        /// tutorial-controlled column highlight can receive input.
        /// </summary>
        public void SetTutorialActive(bool tutorialActive)
        {
            bool canInteract = !tutorialActive;

            // HUD overlay buttons
            if (_challengesButton != null) _challengesButton.interactable = canInteract;
            if (_helpButton != null) _helpButton.interactable = canInteract;
            if (_leaderboardButton != null) _leaderboardButton.interactable = canInteract;

            // Main menu buttons (usually hidden during tutorial, but guard anyway)
            if (_mainMenuPlayButton != null) _mainMenuPlayButton.interactable = canInteract;
            if (_mainMenuChallengesButton != null) _mainMenuChallengesButton.interactable = canInteract;
            if (_mainMenuLeaderboardButton != null) _mainMenuLeaderboardButton.interactable = canInteract;
            if (_mainMenuHowToPlayButton != null) _mainMenuHowToPlayButton.interactable = canInteract;
            if (_mainMenuVibrationToggle != null) _mainMenuVibrationToggle.interactable = canInteract;
            if (_mainMenuSaveNameButton != null) _mainMenuSaveNameButton.interactable = canInteract;
        }

        public void SetInstructionalTextActive(bool active)
        {
            if (_instructionalText == null) return;

            if (active)
            {
                var cg = _instructionalText.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
                _instructionalText.SetActive(true);
            }
            else
            {
                var cg = _instructionalText.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.DOKill();
                    cg.DOFade(0f, 0.4f).SetUpdate(true).OnComplete(() => _instructionalText.SetActive(false));
                }
                else
                {
                    _instructionalText.SetActive(false);
                }
            }
        }
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called every frame during a run. Text and previews are only touched when the displayed value changes,
        /// so the HUD doesn't allocate strings or rebuild TMP meshes every frame.
        /// </summary>
        public void UpdateHud(int score, float timeAlive, float riseSeconds, float riseProgress01, bool inGrace, TileKind current, TileKind next)
        {
            UpdateRiseBar(riseProgress01, inGrace, riseSeconds);

            if (_scoreText == null) return;

            if (score != _lastScore)
            {
                bool counting = _scoreCountTween != null && _scoreCountTween.IsActive();
                _scoreCountTween?.Kill();
                // Real gains count up with a punch; the +10/s survival trickle and new runs just update, except that a
                // small gain during a count-up retargets it instead of snapping the number.
                bool bigGain = score - _lastScore >= 50;
                if (bigGain || (counting && score > _lastScore))
                {
                    _scoreCountTween = DOTween.To(() => _scoreShown, v =>
                        {
                            _scoreShown = v;
                            _scoreText.text = FormatScore(v);
                        }, score, 0.4f)
                        .SetEase(Ease.OutCubic).SetLink(_scoreText.gameObject);
                    if (bigGain)
                    {
                        _scoreText.transform.DOKill();
                        _scoreText.transform.localScale = Vector3.one;
                        _scoreText.transform.DOPunchScale(Vector3.one * 0.15f, 0.3f, 10, 1f);
                    }
                }
                else
                {
                    _scoreShown = score;
                    _scoreText.text = FormatScore(score);
                }
                _lastScore = score;
            }

            int timeSeconds = (int)timeAlive;
            int riseTenths = Mathf.RoundToInt(riseSeconds * 10f);
            if (_hudText != null && (timeSeconds != _hudTimeSeconds || riseTenths != _hudRiseTenths || inGrace != _hudInGrace))
            {
                _hudTimeSeconds = timeSeconds;
                _hudRiseTenths = riseTenths;
                _hudInGrace = inGrace;

                string riseLine = inGrace
                    ? $"Auto-rise starts in: {riseTenths / 10f:0.0}s"
                    : $"Next auto-rise: {riseTenths / 10f:0.0}s";
                _hudText.text =
                    $"Time: <color=#A0A0A0>{timeSeconds / 60:00}:{timeSeconds % 60:00}</color>\n" +
                    $"<color=#60A5FA>{riseLine}</color>";
            }

            UpdatePreviewSquare(_nextPreviewImg, _nextInnerIcon, current, ref _nextPreviewKind);
            UpdatePreviewSquare(_queuedPreviewImg, _queuedInnerIcon, next, ref _queuedPreviewKind);
        }

        private string FormatScore(int score)
        {
            return score.ToString("N0", CultureInfo.InvariantCulture);
        }

        public void TogglePauseMenu()
        {
            if (_pauseMenuRoot == null) return;
            SfxPlayer.Play(Sfx.UiTap);
            if (_isPaused) ResumeGame();
            else PauseGame();
        }

        /// <summary>Safe to call when already paused (e.g. focus loss right after the player paused).</summary>
        public void PauseGame()
        {
            if (_isPaused) return;
            if (_pauseMenuRoot != null)
            {
                _pauseMenuRoot.SetActive(true);
                var cg = _pauseMenuRoot.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.DOKill();
                    cg.blocksRaycasts = true;
                    cg.alpha = 0f;
                    cg.DOFade(1f, 0.2f).SetUpdate(true);
                }
            }
            _isPaused = true;
            Time.timeScale = 0f;
        }

        public void ResumeGame()
        {
            HideConfirmDialog();
            if (_pauseMenuRoot != null)
            {
                var cg = _pauseMenuRoot.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    // Stop catching taps as soon as the fade starts, so the first tap after Resume reaches the board.
                    cg.DOKill();
                    cg.blocksRaycasts = false;
                    cg.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() => _pauseMenuRoot.SetActive(false));
                }
                else
                {
                    _pauseMenuRoot.SetActive(false);
                }
            }
            _isPaused = false;
            Time.timeScale = 1f;
        }

        void OnPauseMainMenuPressed()
        {
            int score = GetActiveRunScore != null ? GetActiveRunScore() : -1;
            if (score < 0)
            {
                ResumeGame();
                _onMainMenu?.Invoke();
                return;
            }

            ShowConfirmDialog(
                "End this run?",
                $"Your score of {FormatScore(score)} will be saved.",
                "END RUN",
                "CANCEL",
                onConfirm: () =>
                {
                    ResumeGame();
                    OnQuitRunConfirmed?.Invoke();
                });
        }

        /// <param name="lastKind">Kind currently shown in this slot (-1 = nothing yet); only redraws on change.</param>
        private void UpdatePreviewSquare(Image img, Image innerIcon, TileKind k, ref int lastKind)
        {
            if (img == null || innerIcon == null) return;
            if (lastKind == (int)k) return;
            lastKind = (int)k;

            // Previews look exactly like the tile that will land: same body, colour and shape/icon.
            img.sprite = UiSprites.Tile;
            img.type = Image.Type.Simple;
            img.color = ColorFor(k);
            innerIcon.preserveAspect = true;
            ApplyGlyph(innerIcon, k);

            // Pop in when the tile changes; Wild then keeps a gentle pulse.
            img.transform.DOKill();
            img.transform.localScale = Vector3.one * 0.8f;
            var pop = img.transform.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetLink(img.gameObject);
            if (k == TileKind.Wild)
                pop.OnComplete(() => img.transform.DOScale(1.1f, 0.5f).SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine).SetLink(img.gameObject));
        }

        GameOverPanel _gameOverPanel;

        public void ShowGameOver(GameOverData data)
        {
            ClearTransientUi();
            _hudRoot.SetActive(false);

            _gameOverRoot.SetActive(true);
            // Built on first use, once the root is active and has its real size.
            if (_gameOverPanel == null)
                _gameOverPanel = new GameOverPanel(_gameOverRoot, _gameOverScore, _retryButton, _shareButton,
                    _onClaimReward, _getTimeUntilReset, () => _onToggleLeaderboard?.Invoke());
            _gameOverPanel.Show(data);

            var cg = _gameOverRoot.GetComponent<CanvasGroup>();
            cg.DOKill();
            cg.alpha = 0;
            cg.DOFade(1f, 0.4f).SetUpdate(true);
            // Taps already in flight when the board filled must not land on RETRY / MAIN MENU.
            cg.interactable = false;
            DOVirtual.DelayedCall(0.5f, () => cg.interactable = true, ignoreTimeScale: true).SetLink(_gameOverRoot);
            _gameOverRoot.transform.DOKill();
            _gameOverRoot.transform.localScale = Vector3.one * 0.9f;
            _gameOverRoot.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        /// <summary>Your All-time rank once this run's score is on the leaderboard (Game Over shows it on its link).</summary>
        public void SetGameOverRank(int? rank)
        {
            if (_gameOverPanel != null && IsGameOverShowing) _gameOverPanel.SetRank(rank);
        }

        public void HideGameOver()
        {
            if (_gameOverRoot != null) _gameOverRoot.SetActive(false);
            if (_hudRoot != null) _hudRoot.SetActive(true);
        }

        public void Retry() => _onRetry?.Invoke();
        public void GoToMainMenu() => _onMainMenu?.Invoke();

        public void ShowMainMenu(bool animate = true)
        {
            ClearTransientUi();
            HideConfirmDialog();
            if (_hudRoot != null) _hudRoot.SetActive(false);
            if (_gameOverRoot != null) _gameOverRoot.SetActive(false);
            if (_helpPanel != null) _helpPanel.SetActive(false);
            _helpFrozeClock = false;
            if (_challengesRoot != null) _challengesRoot.SetActive(false);

            if (_mainMenuRoot != null)
            {
                _mainMenuRoot.SetActive(true);
                RefreshMainMenuStats();

                var cg = _mainMenuRoot.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.DOKill();
                    cg.blocksRaycasts = true;
                }
                if (animate)
                {
                    if (cg != null)
                    {
                        cg.alpha = 0f;
                        cg.DOFade(1f, 0.4f).SetUpdate(true);
                    }
                    _mainMenuRoot.transform.localScale = Vector3.one * 0.9f;
                    _mainMenuRoot.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
                }
                else
                {
                    if (cg != null) cg.alpha = 1f;
                    _mainMenuRoot.transform.localScale = Vector3.one;
                }
            }
        }

        public void HideMainMenu()
        {
            if (_hudRoot != null) _hudRoot.SetActive(true);
            if (_mainMenuRoot != null)
            {
                var cg = _mainMenuRoot.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    // Let taps through to the board as soon as the fade starts.
                    cg.DOKill();
                    cg.blocksRaycasts = false;
                    cg.DOFade(0f, 0.3f).SetUpdate(true).OnComplete(() => _mainMenuRoot.SetActive(false));
                }
                else
                {
                    _mainMenuRoot.SetActive(false);
                }
            }
        }

        public void RefreshMainMenuStats()
        {
            if (_getSaveData == null) return;
            var save = _getSaveData();
            if (save == null) return;

            if (_mainMenuAllTimeHighText != null)
                _mainMenuAllTimeHighText.text = FormatScore(save.AllTimeHigh);

            if (_mainMenuDailyBestText != null)
                _mainMenuDailyBestText.text = FormatScore(save.DailyBest);

            if (_mainMenuStreakText != null)
                _mainMenuStreakText.text = $"{save.Streak} {(save.Streak == 1 ? "Day" : "Days")}";

            /*
            if (_mainMenuNameInput != null)
            {
                _mainMenuNameInput.text = save.PlayerDisplayName ?? "";
            }*/

            if (_mainMenuPlayerNameText != null)
            {
                _mainMenuPlayerNameText.text = save.PlayerDisplayName ?? "";
            }
        }

        /// <summary>
        /// Opening Help mid-run freezes the clock (same rule as the leaderboard and friends panels: only if it was
        /// running, and closing never unpauses a paused game).
        /// </summary>
        public void ToggleHelp()
        {
            if (_helpPanel == null) return;
            bool open = !_helpPanel.activeSelf;
            _helpPanel.SetActive(open);

            if (open)
            {
                _helpFrozeClock = IsRunActive != null && IsRunActive() && Time.timeScale > 0f;
                if (_helpFrozeClock) Time.timeScale = 0f;
            }
            else if (_helpFrozeClock)
            {
                _helpFrozeClock = false;
                if (!_isPaused) Time.timeScale = 1f;
            }
        }

        public void ToggleChallenges()
        {
            if (_challengesRoot == null) return;
            bool active = !_challengesRoot.activeSelf;
            _challengesRoot.SetActive(active);

            // Only touch timeScale when in-game (not when opened from the main menu)
            if (!_challengesOpenedFromMainMenu)
                Time.timeScale = active ? 0f : 1f;

            if (active)
            {
                var cg = _challengesRoot.GetComponent<CanvasGroup>();
                if (cg == null) cg = _challengesRoot.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.DOFade(1f, 0.25f).SetUpdate(true);
                _challengesRoot.transform.localScale = Vector3.one * 0.9f;
                _challengesRoot.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);

                PopulateChallenges();

                if (_resetTimerCoroutine != null) StopCoroutine(_resetTimerCoroutine);
                _resetTimerCoroutine = StartCoroutine(UpdateResetTimerCoroutine());

                var closeBtn = _challengesRoot.GetComponentInChildren<Button>();
                if (closeBtn != null && closeBtn != _challengesButton)
                {
                    closeBtn.onClick.RemoveAllListeners();
                    closeBtn.onClick.AddListener(ToggleChallenges);
                }
            }
            else
            {
                if (_resetTimerCoroutine != null)
                {
                    StopCoroutine(_resetTimerCoroutine);
                    _resetTimerCoroutine = null;
                }

                // If we came from the main menu, return to it instead of the game
                if (_challengesOpenedFromMainMenu)
                {
                    _challengesOpenedFromMainMenu = false;
                    ShowMainMenu(animate: false);
                }
            }
        }

        const float ChallengeRowHeight = 130f;

        /// <summary>
        /// The prefab row is 100 tall with an 18pt description sitting just above the progress bar. The row grows and
        /// the text column moves up so the description (22pt) and reward (22pt) fit with clear space around them.
        /// </summary>
        static void FitChallengeRow(RectTransform row, ChallengeRowView view)
        {
            row.sizeDelta = new Vector2(row.sizeDelta.x, ChallengeRowHeight);
            if (row.Find("TextColumn") is RectTransform textColumn)
                textColumn.anchoredPosition = new Vector2(textColumn.anchoredPosition.x, 22f);
            if (view == null) return;
            if (view.DescriptionText != null)
            {
                // One line, shrinking for longer descriptions (Remote Config can send any text) so it never runs into the bar.
                var description = view.DescriptionText;
                description.fontSize = 22f;
                description.enableAutoSizing = true;
                description.fontSizeMax = 22f;
                description.fontSizeMin = 16f;
                description.textWrappingMode = TextWrappingModes.NoWrap;
                description.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (view.RewardText != null)
            {
                view.RewardText.fontSize = 22f;
                var reward = view.RewardText.rectTransform;
                reward.sizeDelta = new Vector2(reward.sizeDelta.x, 30f);
            }
        }

        /// <summary>The profile card's stat captions and scores have boxes too short for readable text; give them room.</summary>
        void FitStatsCard()
        {
            var card = _mainMenuRoot != null ? _mainMenuRoot.transform.Find("StatsCard") : null;
            if (card == null) return;
            foreach (string box in new[] { "HighScores/AllTime", "HighScores/DailyBest" })
                CentreStatBox(card.Find(box) as RectTransform);
            // "STREAK:" and its badge sit edge to edge in the prefab (layout spacing 0); give them a gap.
            if (card.Find("Streak") is Transform streak && streak.TryGetComponent(out HorizontalLayoutGroup streakRow))
                streakRow.spacing = 14f;
        }

        const float StatCaptionSize = 22f, StatCaptionHeight = 28f; // a line box is 1.22 em, so 28 holds 22pt
        const float StatScoreSize = 30f, StatScoreHeight = 38f;
        const float StatLineGap = -4f;   // pulls caption and score together so they read as one pair
        const int StatSidePadding = 12;

        /// <summary>
        /// Centres a stat box's caption + score by their visible letters. The prefab pads 20 above and 15 below and
        /// top-aligns each line, which left the pair sitting low in the box once the text was enlarged.
        /// </summary>
        void CentreStatBox(RectTransform stat)
        {
            if (stat == null) return;
            var caption = stat.Find("Title") as RectTransform;
            var score = stat.Find("Score") as RectTransform;
            FitText(caption, StatCaptionHeight, StatCaptionSize);
            FitText(score, StatScoreHeight, StatScoreSize);
            if (!stat.TryGetComponent(out VerticalLayoutGroup layout)) return;

            // Capline alignment centres the capitals/digits themselves in each line box, so the empty space inside
            // a line is (height - cap height) / 2 above and below its letters.
            TMP_Text captionText = caption != null ? caption.GetComponent<TMP_Text>() : null;
            TMP_Text scoreText = score != null ? score.GetComponent<TMP_Text>() : null;
            if (captionText != null) captionText.verticalAlignment = VerticalAlignmentOptions.Capline;
            if (scoreText != null) scoreText.verticalAlignment = VerticalAlignmentOptions.Capline;
            float capRatio = CapRatio(scoreText != null ? scoreText : captionText);
            float captionInset = (StatCaptionHeight - StatCaptionSize * capRatio) * 0.5f;
            float scoreInset = (StatScoreHeight - StatScoreSize * capRatio) * 0.5f;

            // Top padding that leaves the same gap above the caption's letters as below the score's.
            float spare = stat.rect.height - StatCaptionHeight - StatScoreHeight - StatLineGap;
            float top = (spare + scoreInset - captionInset) * 0.5f;

            layout.padding = new RectOffset(StatSidePadding, StatSidePadding, Mathf.Max(0, Mathf.RoundToInt(top)), 0);
            layout.spacing = StatLineGap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandHeight = false;
            // Full box width (the prefab's 200-wide text boxes made the caption shrink to fit).
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
        }

        static float CapRatio(TMP_Text text)
        {
            var face = text != null && text.font != null ? text.font.faceInfo : default;
            return face.pointSize > 0f && face.capLine > 0f ? face.capLine / face.pointSize : 0.7f;
        }

        /// <summary>Sets a text box's height (also for a parent layout group) and lets the text auto-size up to maxSize.</summary>
        static void FitText(RectTransform rt, float height, float maxSize)
        {
            if (rt == null) return;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
            if (!rt.TryGetComponent(out LayoutElement element)) element = rt.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            if (rt.TryGetComponent(out TMP_Text text))
            {
                text.enableAutoSizing = true;
                text.fontSizeMax = maxSize;
            }
        }

        private IEnumerator UpdateResetTimerCoroutine()
        {
            while (true)
            {
                if (_titleText != null && _getTimeUntilReset != null)
                {
                    var timeRemaining = _getTimeUntilReset();
                    _titleText.text = $"DAILY CHALLENGES\n<size=50%><color=#A0A0A0>Resets in {timeRemaining.Hours:D2}h {timeRemaining.Minutes:D2}m {timeRemaining.Seconds:D2}s</color></size>";
                }
                yield return new WaitForSecondsRealtime(1.0f);
            }
        }

        void PopulateChallenges()
        {
            if (_challengeRowContainer == null || _getChallengesData == null) return;

            foreach (Transform child in _challengeRowContainer)
                Destroy(child.gameObject);

            var data = _getChallengesData();
            if (data == null) return;

            float rowHeight = 110f;
            float startY = (data.Length - 1) * rowHeight * 0.5f;

            for (int i = 0; i < data.Length; i++)
            {
                int index = i;
                var item = data[i];

                GameObject rowGo;
                RectTransform rowRt;

                if (_challengeRowPrefab != null)
                {
                    rowGo = Instantiate(_challengeRowPrefab, _challengeRowContainer, false);
                    rowRt = rowGo.GetComponent<RectTransform>();
                    if (rowRt != null && _challengeRowContainer.GetComponent<UnityEngine.UI.LayoutGroup>() == null)
                        rowRt.anchoredPosition = new Vector2(0, startY - i * rowHeight);

                    var rowView = rowGo.GetComponent<ChallengeRowView>();
                    if (rowRt != null) FitChallengeRow(rowRt, rowView);

                    // The prefab has a second "ProgressText" at the top right that nothing updates (it read "0 / 1000"
                    // on every challenge); the one inside the bar is the real progress.
                    var strayProgress = rowGo.transform.Find("ProgressText");
                    if (strayProgress != null && (rowView == null || rowView.ProgressText == null || strayProgress != rowView.ProgressText.transform))
                        strayProgress.gameObject.SetActive(false);
                    if (rowView != null)
                    {
                        if (rowView.TitleText != null)
                            rowView.TitleText.text = $"{item.Title}  <size=70%><color=#A3AEC6>{ChallengeDifficulty.Label(item.Difficulty)}</color></size>";
                        if (rowView.DescriptionText != null) rowView.DescriptionText.text = item.Description;
                        if (rowView.StatusIcon != null)
                        {
                            rowView.StatusIcon.sprite = item.Completed ? completedIcon : pendingIcon;
                            rowView.StatusIcon.color = item.Completed ? completedColor : pendingColor;
                        }
                        if (rowView.ProgressText != null)
                            rowView.ProgressText.text =
                                $"{GameOverPanel.FormatChallengeValue(item.Type, item.Progress)} / {GameOverPanel.FormatChallengeValue(item.Type, item.TargetValue)}";

                        float ratio = item.TargetValue > 0 ? (float)item.Progress / item.TargetValue : 0f;

                        if (rowView.ProgressBarFill != null)
                        {
                            // The prefab's fill is anchored at zero width; grow its right anchor to the progress.
                            var fillRt = rowView.ProgressBarFill.rectTransform;
                            fillRt.anchorMax = new Vector2(0f, fillRt.anchorMax.y);
                            fillRt.DOAnchorMax(new Vector2(Mathf.Clamp01(ratio), fillRt.anchorMax.y), 0.75f)
                                .SetEase(Ease.OutQuad).SetUpdate(true).SetLink(fillRt.gameObject);
                            rowView.ProgressBarFill.color = Color.Lerp(new Color(0.2f, 0.6f, 1.0f), new Color(0.3f, 0.8f, 0.4f), ratio);
                        }

                        if (item.Completed)
                        {
                            if (item.Claimed)
                            {
                                if (rowView.ClaimButton != null) rowView.ClaimButton.gameObject.SetActive(false);
                                if (rowView.RewardText != null)
                                {
                                    rowView.RewardText.gameObject.SetActive(true);
                                    rowView.RewardText.text = "CLAIMED";
                                    rowView.RewardText.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                                }
                            }
                            else
                            {
                                if (rowView.ClaimButton != null)
                                {
                                    rowView.ClaimButton.gameObject.SetActive(true);
                                    rowView.ClaimButton.onClick.RemoveAllListeners();
                                    rowView.ClaimButton.onClick.AddListener(() =>
                                    {
                                        _onClaimReward?.Invoke(index);
                                        PopulateChallenges();
                                    });
                                    rowView.ClaimButton.transform.DOScale(1.08f, 0.6f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(rowView.ClaimButton.gameObject);
                                }
                                if (rowView.RewardText != null) rowView.RewardText.gameObject.SetActive(false);
                            }
                        }
                        else
                        {
                            if (rowView.ClaimButton != null) rowView.ClaimButton.gameObject.SetActive(false);
                            if (rowView.RewardText != null)
                            {
                                rowView.RewardText.gameObject.SetActive(true);
                                rowView.RewardText.text = $"+{item.RewardPoints.ToString("N0", CultureInfo.InvariantCulture)} pts";
                                rowView.RewardText.color = new Color(0.95f, 0.75f, 0.2f, 0.8f);
                            }
                        }

                        if (rowView.BackgroundImage != null)
                        {
                            rowView.BackgroundImage.color = item.Completed
                                ? new Color(1f, 0.85f, 0.4f, 0.15f)
                                : new Color(0.1f, 0.1f, 0.12f, 0.6f);
                        }
                    }
                }
                else
                {
                    rowGo = new GameObject($"ChallengeRow_{i}");
                    rowGo.transform.SetParent(_challengeRowContainer, false);
                    rowRt = rowGo.AddComponent<RectTransform>();
                    rowRt.anchorMin = new Vector2(0, 0.5f);
                    rowRt.anchorMax = new Vector2(1, 0.5f);
                    rowRt.pivot = new Vector2(0.5f, 0.5f);
                    rowRt.anchoredPosition = new Vector2(0, startY - i * rowHeight);
                    rowRt.sizeDelta = new Vector2(0, 100f);

                    var bgImg = rowGo.AddComponent<Image>();
                    bgImg.color = item.Completed
                        ? new Color(1f, 0.85f, 0.4f, 0.15f)
                        : new Color(0.1f, 0.1f, 0.12f, 0.6f);

                    var statusGo = new GameObject("StatusIcon");
                    statusGo.transform.SetParent(rowGo.transform, false);
                    var statusRt = statusGo.AddComponent<RectTransform>();
                    statusRt.anchorMin = new Vector2(0, 0.5f);
                    statusRt.anchorMax = new Vector2(0, 0.5f);
                    statusRt.pivot = new Vector2(0, 0.5f);
                    statusRt.anchoredPosition = new Vector2(32, 0);
                    statusRt.sizeDelta = new Vector2(28, 28);
                    // The TMP fonts have no ✓/○ glyphs (they render as boxes), so use the status sprites or a plain chip.
                    var statusImg = statusGo.AddComponent<Image>();
                    statusImg.sprite = item.Completed ? completedIcon : pendingIcon;
                    statusImg.preserveAspect = true;
                    statusImg.color = item.Completed ? completedColor : pendingColor;
                    statusImg.raycastTarget = false;

                    var textColGo = new GameObject("TextColumn");
                    textColGo.transform.SetParent(rowGo.transform, false);
                    var textColRt = textColGo.AddComponent<RectTransform>();
                    textColRt.anchorMin = new Vector2(0, 0.5f);
                    textColRt.anchorMax = new Vector2(1, 0.5f);
                    textColRt.pivot = new Vector2(0, 0.5f);
                    float rightOffset = (item.Completed && !item.Claimed) ? -180f : -150f;
                    textColRt.anchoredPosition = new Vector2(80, 10);
                    textColRt.sizeDelta = new Vector2(rightOffset - 80f, 80);

                    var titleGo2 = new GameObject("Title");
                    titleGo2.transform.SetParent(textColGo.transform, false);
                    var titleRt2 = titleGo2.AddComponent<RectTransform>();
                    titleRt2.anchorMin = new Vector2(0, 1);
                    titleRt2.anchorMax = new Vector2(1, 1);
                    titleRt2.pivot = new Vector2(0, 1);
                    titleRt2.anchoredPosition = new Vector2(0, 0);
                    titleRt2.sizeDelta = new Vector2(0, 35);
                    var titleText2 = titleGo2.AddComponent<TextMeshProUGUI>();
                    titleText2.text = item.Title;
                    titleText2.fontSize = 24;
                    titleText2.fontStyle = FontStyles.Bold;
                    titleText2.color = Color.white;

                    var descGo = new GameObject("Description");
                    descGo.transform.SetParent(textColGo.transform, false);
                    var descRt = descGo.AddComponent<RectTransform>();
                    descRt.anchorMin = new Vector2(0, 0);
                    descRt.anchorMax = new Vector2(1, 0);
                    descRt.pivot = new Vector2(0, 0);
                    descRt.anchoredPosition = new Vector2(0, 0);
                    descRt.sizeDelta = new Vector2(0, 30);
                    var descText = descGo.AddComponent<TextMeshProUGUI>();
                    descText.text = item.Description;
                    descText.fontSize = 18;
                    descText.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);

                    var progTextGo = new GameObject("ProgressText");
                    progTextGo.transform.SetParent(rowGo.transform, false);
                    var progTextRt = progTextGo.AddComponent<RectTransform>();
                    progTextRt.anchorMin = new Vector2(1, 0.5f);
                    progTextRt.anchorMax = new Vector2(1, 0.5f);
                    progTextRt.pivot = new Vector2(1, 0.5f);
                    float progTextRightOffset = (item.Completed && !item.Claimed) ? -180f : -20f;
                    progTextRt.anchoredPosition = new Vector2(progTextRightOffset, 15);
                    progTextRt.sizeDelta = new Vector2(150, 40);
                    var progText = progTextGo.AddComponent<TextMeshProUGUI>();
                    progText.text = $"{item.Progress} / {item.TargetValue}";
                    progText.fontSize = 20;
                    progText.alignment = TextAlignmentOptions.Right;
                    progText.color = new Color(0.9f, 0.9f, 0.9f);

                    var barBgGo = new GameObject("ProgressBarBg");
                    barBgGo.transform.SetParent(rowGo.transform, false);
                    var barBgRt = barBgGo.AddComponent<RectTransform>();
                    barBgRt.anchorMin = new Vector2(0, 0);
                    barBgRt.anchorMax = new Vector2(1, 0);
                    barBgRt.pivot = new Vector2(0.5f, 0);
                    float barBgRightOffset = (item.Completed && !item.Claimed) ? -180f : -20f;
                    barBgRt.anchoredPosition = new Vector2(80, 8);
                    barBgRt.sizeDelta = new Vector2(barBgRightOffset - 80f, 10);
                    var barBgImg = barBgGo.AddComponent<Image>();
                    barBgImg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

                    var barFillGo = new GameObject("ProgressBarFill");
                    barFillGo.transform.SetParent(barBgGo.transform, false);
                    var barFillRt = barFillGo.AddComponent<RectTransform>();
                    barFillRt.anchorMin = new Vector2(0, 0);
                    barFillRt.anchorMax = new Vector2(0, 1);
                    barFillRt.pivot = new Vector2(0, 0.5f);
                    barFillRt.anchoredPosition = Vector2.zero;
                    barFillRt.sizeDelta = new Vector2(0, 0);
                    var barFillImg = barFillGo.AddComponent<Image>();

                    float ratio = item.TargetValue > 0 ? (float)item.Progress / item.TargetValue : 0f;
                    barFillImg.color = Color.Lerp(new Color(0.2f, 0.6f, 1.0f), new Color(0.3f, 0.8f, 0.4f), ratio);

                    float targetWidth = ratio * barBgRt.sizeDelta.x;
                    barFillRt.sizeDelta = new Vector2(0, 0);
                    barFillRt.DOSizeDelta(new Vector2(targetWidth, 0), 0.75f).SetEase(Ease.OutQuad).SetUpdate(true);

                    if (item.Completed)
                    {
                        if (item.Claimed)
                        {
                            var claimedGo = new GameObject("ClaimedLabel");
                            claimedGo.transform.SetParent(rowGo.transform, false);
                            var claimedRt = claimedGo.AddComponent<RectTransform>();
                            claimedRt.anchorMin = new Vector2(1, 0.5f);
                            claimedRt.anchorMax = new Vector2(1, 0.5f);
                            claimedRt.pivot = new Vector2(1, 0.5f);
                            claimedRt.anchoredPosition = new Vector2(-20, 0);
                            claimedRt.sizeDelta = new Vector2(140, 60);
                            var claimedText = claimedGo.AddComponent<TextMeshProUGUI>();
                            claimedText.text = "CLAIMED";
                            claimedText.fontSize = 20;
                            claimedText.alignment = TextAlignmentOptions.Center;
                            claimedText.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                        }
                        else
                        {
                            var claimBtnGo = new GameObject("ClaimButton");
                            claimBtnGo.transform.SetParent(rowGo.transform, false);
                            var claimBtnRt = claimBtnGo.AddComponent<RectTransform>();
                            claimBtnRt.anchorMin = new Vector2(1, 0.5f);
                            claimBtnRt.anchorMax = new Vector2(1, 0.5f);
                            claimBtnRt.pivot = new Vector2(1, 0.5f);
                            claimBtnRt.anchoredPosition = new Vector2(-20, 0);
                            claimBtnRt.sizeDelta = new Vector2(140, 60);
                            var claimBtnImg = claimBtnGo.AddComponent<Image>();
                            claimBtnImg.color = new Color(0.95f, 0.75f, 0.2f);
                            var btn = claimBtnGo.AddComponent<Button>();
                            btn.onClick.AddListener(() =>
                            {
                                _onClaimReward?.Invoke(index);
                                PopulateChallenges();
                            });

                            var btnTextGo = new GameObject("Text");
                            btnTextGo.transform.SetParent(claimBtnGo.transform, false);
                            var btnTextRt = btnTextGo.AddComponent<RectTransform>();
                            btnTextRt.anchorMin = Vector2.zero;
                            btnTextRt.anchorMax = Vector2.one;
                            btnTextRt.sizeDelta = Vector2.zero;
                            var btnText = btnTextGo.AddComponent<TextMeshProUGUI>();
                            btnText.text = "CLAIM";
                            btnText.fontSize = 22;
                            btnText.fontStyle = FontStyles.Bold;
                            btnText.alignment = TextAlignmentOptions.Center;
                            btnText.color = Color.black;

                            claimBtnRt.DOScale(1.08f, 0.6f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(claimBtnGo);
                        }
                    }
                    else
                    {
                        var rewardGo = new GameObject("RewardLabel");
                        rewardGo.transform.SetParent(rowGo.transform, false);
                        var rewardRt = rewardGo.AddComponent<RectTransform>();
                        rewardRt.anchorMin = new Vector2(1, 0.5f);
                        rewardRt.anchorMax = new Vector2(1, 0.5f);
                        rewardRt.pivot = new Vector2(1, 0.5f);
                        rewardRt.anchoredPosition = new Vector2(-20, 0);
                        rewardRt.sizeDelta = new Vector2(140, 60);
                        var rewardText = rewardGo.AddComponent<TextMeshProUGUI>();
                        rewardText.text = $"+{item.RewardPoints.ToString("N0", CultureInfo.InvariantCulture)} pts";
                        rewardText.fontSize = 20;
                        rewardText.alignment = TextAlignmentOptions.Right;
                        rewardText.color = new Color(0.95f, 0.75f, 0.2f, 0.8f);
                    }
                }

                if (rowRt != null)
                {
                    rowRt.localScale = new Vector3(0.9f, 0.9f, 1f);
                    rowRt.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetDelay(i * 0.1f).SetUpdate(true);
                }
            }

            AddAllDoneNote(data, startY - data.Length * rowHeight + rowHeight * 0.25f);
            UiSkin.Apply(_challengeRowContainer);
        }

        /// <summary>Under the rows: what finishing all of today's challenges adds, or that it's done.</summary>
        void AddAllDoneNote(ChallengeDisplayData[] data, float y)
        {
            if (data.Length <= 1) return;
            bool allDone = true;
            foreach (var item in data) allDone &= item.Completed;

            var go = new GameObject("AllDoneNote", typeof(RectTransform));
            go.transform.SetParent(_challengeRowContainer, false);
            var rt = (RectTransform)go.transform;
            if (_challengeRowContainer.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
            {
                go.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 50f;
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta = new Vector2(0f, 50f);
            }
            var text = go.AddComponent<TextMeshProUGUI>();
            if (_titleText != null) text.font = _titleText.font;
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.text = allDone
                ? $"ALL {data.Length} DONE TODAY"
                : $"FINISH ALL {data.Length} FOR +{ChallengeProvider.AllDoneBonusPoints.ToString("N0", CultureInfo.InvariantCulture)}";
            text.color = allDone ? UiSkin.Teal : UiSkin.TextMuted;
        }

        /// <summary>
        /// Puts every tile back at rest: kills in-flight blink/fall/slide/shake tweens and transient overlays.
        /// Used when a run starts so animations from an interrupted run can't keep moving the new board.
        /// </summary>
        public void ResetBoardVisuals()
        {
            if (_shake != null) _shake.StopShake();
            if (_gridRoot != null)
            {
                _gridRoot.DOKill();
                _gridRoot.anchoredPosition = Vector2.zero;
            }
            if (_gridClip != null) _gridClip.enabled = _clipForTutorial; // a slide interrupted by quitting the run
            if (_cellImages != null)
            {
                foreach (var img in _cellImages)
                {
                    if (img == null) continue;
                    img.DOKill();
                    img.rectTransform.DOKill();
                }
            }
            ClearTransientUi();
        }

        void ResetDangerGlow()
        {
            if (_dangerGlow == null) return;
            _dangerGlow.DOKill();
            var col = _dangerGlow.color;
            col.a = 0f;
            _dangerGlow.color = col;
            _dangerShown = false;
        }

        public void RefreshGrid(TileKind[,] cells)
        {
            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    SetCellColor(r, c, cells[r, c]);
                    // A tile still dropping/landing/popping keeps its animation; everything else snaps to rest.
                    var rt = _cellImages[r, c].rectTransform;
                    if (DOTween.IsTweening(rt)) continue;
                    rt.localScale = Vector3.one;
                    rt.anchoredPosition = CellPos(r, c);
                }

            _lastCells = cells;
            UpdateDangerGlow(cells);
        }

        public void SetCellColor(int r, int c, TileKind k)
        {
            _cellImages[r, c].color = ColorFor(k);
            ApplyGlyph(_cellGlyphs[r, c], k);
        }

        // ── Board animation ──────────────────────────────────────────────────
        // Drop: the placed tile falls from a few rows above its landing spot, stretched, then squashes on landing.
        // Kept short and near-constant: a match waits for the landing, so this is added to the board's busy time.
        const int DropMaxRows = 3;
        const float DropBaseSeconds = 0.04f;
        const float DropPerRowSeconds = 0.013f;
        const float DropMaxSeconds = 0.08f;

        RectTransform _fxLayer;
        readonly List<Image> _particles = new List<Image>();
        float _dropEndsAt;

        float CellW => 720f / _settings.Columns;
        float CellH => 920f / _settings.Rows;

        /// <summary>Centre of a cell in grid coordinates (tiles use a centre pivot).</summary>
        Vector2 CellPos(int r, int c) => new Vector2((c + 0.5f) * CellW, (r + 0.5f) * CellH);

        /// <summary>Finishes every tile animation now (before the rows shift during a rise).</summary>
        public void SettleTiles()
        {
            if (_cellImages == null) return;
            foreach (var img in _cellImages)
                if (img != null) img.rectTransform.DOKill(true);
        }

        /// <summary>Seconds until the last dropped tile lands (a match waits for it before popping).</summary>
        public float DropRemaining => Mathf.Max(0f, _dropEndsAt - Time.time);

        public void AnimateDrop(int col, int row)
        {
            var rt = _cellImages[row, col].rectTransform;
            Vector2 target = CellPos(row, col);
            int startRow = Mathf.Min(_settings.Rows - 1, row + DropMaxRows);
            float dur = Mathf.Min(DropBaseSeconds + DropPerRowSeconds * (startRow - row), DropMaxSeconds);

            rt.DOKill(true);
            rt.anchoredPosition = new Vector2(target.x, CellPos(startRow, col).y);
            rt.localScale = new Vector3(0.9f, 1.1f, 1f);
            DOTween.Sequence().SetTarget(rt).SetLink(rt.gameObject)
                .Append(rt.DOAnchorPosY(target.y, dur).SetEase(Ease.InQuad))
                .Append(rt.DOScale(new Vector3(1.14f, 0.84f, 1f), 0.05f).SetEase(Ease.OutQuad))
                .Append(rt.DOScale(Vector3.one, 0.16f).SetEase(Ease.OutBack));
            _dropEndsAt = Time.time + dur;
        }

        /// <summary>Small rounded chips that fly out from a point, spin, shrink and fade (pooled).</summary>
        void SpawnBurst(Vector2 pos, Color color, int count, float distance)
        {
            if (_fxLayer == null) return;
            for (int i = 0; i < count; i++)
            {
                var chip = TakeParticle();
                var rt = chip.rectTransform;
                chip.color = color;
                rt.anchoredPosition = pos;
                rt.localScale = Vector3.one;
                rt.localEulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 90f));
                float size = UnityEngine.Random.Range(16f, 26f);
                rt.sizeDelta = new Vector2(size, size);

                float angle = (i + UnityEngine.Random.value * 0.8f) * Mathf.PI * 2f / count;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float dist = distance * UnityEngine.Random.Range(0.6f, 1f);
                float dur = UnityEngine.Random.Range(0.32f, 0.45f);
                chip.gameObject.SetActive(true);
                DOTween.Sequence().SetTarget(rt).SetLink(chip.gameObject)
                    .Append(rt.DOAnchorPos(pos + dir * dist + Vector2.down * 25f, dur).SetEase(Ease.OutCubic))
                    .Join(rt.DOScale(0.2f, dur).SetEase(Ease.InQuad))
                    .Join(rt.DOLocalRotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-240f, 240f)), dur, RotateMode.LocalAxisAdd))
                    .Join(chip.DOFade(0f, dur).SetEase(Ease.InQuad))
                    .OnComplete(() => chip.gameObject.SetActive(false));
            }
        }

        Image TakeParticle()
        {
            foreach (var p in _particles)
                if (!p.gameObject.activeSelf) return p;
            var go = new GameObject("Chip");
            go.transform.SetParent(_fxLayer, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            UiSprites.ApplyRounded(img, 5f);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
            go.SetActive(false);
            _particles.Add(img);
            return img;
        }

        void BombShockwave(Vector2 pos)
        {
            if (_fxLayer == null) return;
            var go = new GameObject("Shockwave");
            go.transform.SetParent(_fxLayer, false);
            var ring = go.AddComponent<Image>();
            ring.sprite = UiSprites.Ring;
            ring.raycastTarget = false;
            ring.color = new Color(1f, 0.62f, 0.25f, 0.95f);
            var rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(120f, 120f);
            rt.localScale = Vector3.one * 0.3f;
            DOTween.Sequence().SetLink(go)
                .Append(rt.DOScale(3.2f, 0.38f).SetEase(Ease.OutCubic))
                .Join(ring.DOFade(0f, 0.38f).SetEase(Ease.InQuad))
                .OnComplete(() => Destroy(go));
        }

        Material _pointsPopupMaterial;

        Material PointsPopupMaterial(TMP_Text template)
        {
            if (_pointsPopupMaterial != null) return _pointsPopupMaterial;
            _pointsPopupMaterial = new Material(template.fontSharedMaterial) { name = "PointsPopup (shared outline)" };
            _pointsPopupMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _pointsPopupMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            _pointsPopupMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(10, 14, 24, 200));
            return _pointsPopupMaterial;
        }

        /// <summary>"+250" that pops up where a wave cleared and floats away (combos tinted like the combo toast).</summary>
        public void ShowPointsPopup(float row, float col, int points, int comboMultiplier)
        {
            if (_fxLayer == null || points <= 0) return;
            var text = CreateTmp(_fxLayer, "Points", 46, FontStyles.Bold, TextAlignmentOptions.Center);
            text.text = "+" + FormatScore(points);
            text.color = comboMultiplier >= 2 ? new Color(1f, 0.62f, 0.25f) : Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            // Shared outline material: setting outlineWidth per text would create (and leak) a material per popup.
            text.fontSharedMaterial = PointsPopupMaterial(text);
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 60f);
            rt.anchoredPosition = new Vector2((col + 0.5f) * CellW, (row + 0.5f) * CellH);
            rt.localScale = Vector3.one * 0.6f;
            DOTween.Sequence().SetLink(text.gameObject)
                .Append(rt.DOScale(1.1f, 0.12f).SetEase(Ease.OutQuad))
                .Append(rt.DOScale(1f, 0.08f))
                .Insert(0f, rt.DOAnchorPosY(rt.anchoredPosition.y + 90f, 0.75f).SetEase(Ease.OutCubic))
                .Insert(0.45f, text.DOFade(0f, 0.3f))
                .OnComplete(() => Destroy(text.gameObject));
        }

        // ── Tile look: shapes, danger glow, rise bar ─────────────────────────
        const float BoardCornerRadius = 26f;
        const float GlyphScale = 0.5f;
        const float RiseBarHeight = 12f;
        const float RiseBarGap = 22f;
        const float DangerGlowInset = 8f; // >= BoardCornerRadius * (1 - 1/sqrt 2)

        Image[,] _cellGlyphs;
        TileKind[,] _lastCells;
        bool _showTileShapes = true;

        Image _dangerGlow;
        bool _dangerShown;

        RectMask2D _gridClip;
        bool _clipForTutorial; // the tutorial's pulsing column highlight would otherwise overhang the board
        RectTransform _riseFill;
        Image _riseFillImg;
        float _riseShown = -1f;
        int _riseColorState = -1;

        /// <summary>Shape (colour tiles, if Tile shapes is on) or icon (Wild, Bomb) drawn on a tile; null = none.</summary>
        Sprite GlyphSpriteFor(TileKind k) => k switch
        {
            TileKind.Empty => null,
            TileKind.Wild => _wildSprite,
            TileKind.Bomb => _bombSprite,
            _ => _showTileShapes ? UiSprites.GlyphFor(k) : null
        };

        Color GlyphColorFor(TileKind k) => k switch
        {
            TileKind.Wild => new Color(0.16f, 0.20f, 0.30f, 0.9f),
            TileKind.Bomb => new Color(1f, 1f, 1f, 0.95f),
            _ => Color.Lerp(ColorFor(k), Color.black, 0.45f) // a darker shade of the tile reads on every colour
        };

        void ApplyGlyph(Image glyph, TileKind k)
        {
            if (glyph == null) return;
            Sprite sprite = GlyphSpriteFor(k);
            if (sprite == null)
            {
                glyph.enabled = false;
                return;
            }
            glyph.sprite = sprite;
            glyph.type = Image.Type.Simple;
            glyph.preserveAspect = true;
            glyph.color = GlyphColorFor(k);
            glyph.enabled = true;
        }

        bool IsEmptyCell(int r, int c) => _lastCells != null && _lastCells[r, c] == TileKind.Empty;

        /// <summary>Dims a tile's shape/icon along with the tile (tutorial column focus). 1 = normal.</summary>
        void SetGlyphAlpha(int r, int c, float alpha)
        {
            var glyph = _cellGlyphs != null ? _cellGlyphs[r, c] : null;
            if (glyph == null) return;
            var col = glyph.color;
            col.a = alpha;
            glyph.color = col;
        }

        void OnGamePrefsChanged()
        {
            bool shapes = GamePrefs.TileShapes;
            if (shapes == _showTileShapes) return;
            _showTileShapes = shapes;
            if (_lastCells != null)
                for (int r = 0; r < _settings.Rows; r++)
                    for (int c = 0; c < _settings.Columns; c++)
                        ApplyGlyph(_cellGlyphs[r, c], _lastCells[r, c]);
            _nextPreviewKind = _queuedPreviewKind = -1; // previews redraw on the next HUD update
        }

        void OnDestroy() => GamePrefs.Changed -= OnGamePrefsChanged;

        /// <summary>Red veil over the top rows while any column is two tiles from the top (a rise could end the run).</summary>
        void BuildDangerGlow(Transform board)
        {
            var go = new GameObject("DangerGlow");
            go.transform.SetParent(board, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // Inset so its square corners stay inside the board's rounded ones (RectMask2D clips rectangles only).
            rt.sizeDelta = new Vector2(-2f * DangerGlowInset, 920f / _settings.Rows * 2.4f);
            rt.anchoredPosition = new Vector2(0f, -DangerGlowInset);
            _dangerGlow = go.AddComponent<Image>();
            _dangerGlow.sprite = UiSprites.FadeDown;
            _dangerGlow.color = new Color(UiSkin.Danger.r, UiSkin.Danger.g, UiSkin.Danger.b, 0f);
            _dangerGlow.raycastTarget = false;
        }

        void UpdateDangerGlow(TileKind[,] cells)
        {
            if (_dangerGlow == null) return;
            int top = _settings.Rows - 1;
            bool danger = false;
            for (int c = 0; c < _settings.Columns && !danger; c++)
                danger = cells[top, c] != TileKind.Empty || cells[top - 1, c] != TileKind.Empty;
            if (danger == _dangerShown) return;
            _dangerShown = danger;

            _dangerGlow.DOKill();
            if (danger)
            {
                var col = _dangerGlow.color;
                col.a = 0.2f;
                _dangerGlow.color = col;
                _dangerGlow.DOFade(0.45f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(_dangerGlow.gameObject);
            }
            else
            {
                _dangerGlow.DOFade(0f, 0.25f).SetLink(_dangerGlow.gameObject);
            }
        }

        /// <summary>Thin bar above the board that fills up to the next automatic row rise.</summary>
        void BuildRiseBar(RectTransform board)
        {
            var trackGo = new GameObject("RiseBar");
            trackGo.transform.SetParent(board.parent, false);
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackGo.AddComponent<Canvas>(); // fills most frames; isolate its rebuilds like the board's (no raycasts)
            trackRt.anchorMin = trackRt.anchorMax = board.anchorMin;
            trackRt.sizeDelta = new Vector2(board.sizeDelta.x, RiseBarHeight);
            trackRt.anchoredPosition = board.anchoredPosition + new Vector2(0f, board.sizeDelta.y * 0.5f + RiseBarGap);
            var track = trackGo.AddComponent<Image>();
            track.color = UiSkin.Slot;
            track.raycastTarget = false;
            UiSprites.ApplyRounded(track, RiseBarHeight * 0.5f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            _riseFill = fillGo.AddComponent<RectTransform>();
            _riseFill.anchorMin = Vector2.zero;
            _riseFill.anchorMax = new Vector2(0f, 1f);
            _riseFill.offsetMin = _riseFill.offsetMax = Vector2.zero;
            _riseFillImg = fillGo.AddComponent<Image>();
            _riseFillImg.color = UiSkin.Primary;
            _riseFillImg.raycastTarget = false;
            UiSprites.ApplyRounded(_riseFillImg, RiseBarHeight * 0.5f);
        }

        void UpdateRiseBar(float progress01, bool inGrace, float secondsLeft)
        {
            if (_riseFill == null) return;
            float p = Mathf.Clamp01(progress01);
            if (Mathf.Abs(p - _riseShown) > 0.004f || (p == 0f) != (_riseShown == 0f))
            {
                _riseFill.anchorMax = new Vector2(p, 1f);
                _riseShown = p;
            }

            int state = inGrace ? 0 : secondsLeft <= 1.0f ? 2 : 1;
            if (state == _riseColorState) return;
            _riseColorState = state;
            _riseFillImg.color = state == 0 ? UiSkin.TextMuted : state == 2 ? UiSkin.Bolt : UiSkin.Primary;
        }

        public void TriggerClearShake(int tileCount)
        {
            if (tileCount >= 5) Haptics.Medium();
            else Haptics.Light();

            float shake = _settings.ShakeBase + Mathf.Max(0, tileCount - 4) * _settings.ShakePerExtraTile;
            if (_shake != null) _shake.AddShake(shake, 0.12f);
        }

        /// <summary>
        /// Feedback for a refused column tap (full column, or the wrong column during the tutorial):
        /// red flash, a sideways nudge of that column's tiles and a light haptic tick.
        /// </summary>
        /// <param name="nudgeTiles">False in the tutorial, where the column can be mid-clear: completing its tile
        /// tweens early would make matched tiles vanish before the blink ends.</param>
        public void RejectColumnTap(int col, bool nudgeTiles = true)
        {
            if (_colTapZones == null || col < 0 || col >= _colTapZones.Length) return;
            Haptics.Light();
            SfxPlayer.Play(Sfx.Reject);
            if (!nudgeTiles) return;

            for (int r = 0; r < _settings.Rows; r++)
            {
                var rt = _cellImages[r, col].rectTransform;
                rt.DOKill(true);
                rt.DOPunchAnchorPos(new Vector2(10f, 0f), 0.25f, 12, 0.5f).SetUpdate(true);
            }
        }

        const string TutorialArrowPunchId = "TutorialArrowPunch";

        void OnColumnTapRejected(int col)
        {
            // Columns are only locked during the tutorial: point at the column the tutorial wants.
            if (_tutorialHighlightedColumn < 0 || col == _tutorialHighlightedColumn) return;
            RejectColumnTap(col, nudgeTiles: false);

            var arrow = _colOverlayBtns[_tutorialHighlightedColumn].transform.Find("TutorialArrow");
            if (arrow != null)
            {
                // Only kill the previous punch (by id), not the arrow's looping bob on the same transform.
                DOTween.Kill(TutorialArrowPunchId);
                arrow.localScale = Vector3.one;
                arrow.DOPunchScale(Vector3.one * 0.4f, 0.35f, 6, 0.5f).SetUpdate(true).SetId(TutorialArrowPunchId).SetLink(arrow.gameObject);
            }
        }

        // ── Reward Toast / Badge ──────────────────────────────────────────────
        RectTransform _toastParent;  // lazy – created on first use

        RectTransform GetToastParent()
        {
            if (_toastParent != null) return _toastParent;
            var go = new GameObject("ToastLayer");
            // Directly above the HUD (inside the safe area) so the pause menu, Help and game over draw over toasts.
            var parent = _hudRoot != null && _hudRoot.transform.parent != null
                ? _hudRoot.transform.parent
                : (_mainCanvas != null ? _mainCanvas.transform : transform);
            go.transform.SetParent(parent, false);
            if (_hudRoot != null && parent == _hudRoot.transform.parent)
                go.transform.SetSiblingIndex(_hudRoot.transform.GetSiblingIndex() + 1);
            _toastParent = go.AddComponent<RectTransform>();
            _toastParent.anchorMin = Vector2.zero;
            _toastParent.anchorMax = Vector2.one;
            _toastParent.offsetMin = Vector2.zero;
            _toastParent.offsetMax = Vector2.zero;

            // Toasts and badges float over the top rows of the board; they must never swallow column taps.
            var layerGroup = go.AddComponent<CanvasGroup>();
            layerGroup.blocksRaycasts = false;
            layerGroup.interactable = false;
            return _toastParent;
        }

        /// <summary>Removes reward toasts and the slow-rise badge (run ended, menu shown, new run).</summary>
        void ClearTransientUi()
        {
            ResetDangerGlow(); // the run almost always ends in danger; don't keep the pulse looping behind menus
            if (_slowRiseExpireCoroutine != null) { StopCoroutine(_slowRiseExpireCoroutine); _slowRiseExpireCoroutine = null; }
            if (_slowRiseBadgeGo != null) { Destroy(_slowRiseBadgeGo); _slowRiseBadgeGo = null; }
            if (_toastParent == null) return;
            for (int i = _toastParent.childCount - 1; i >= 0; i--)
                Destroy(_toastParent.GetChild(i).gameObject);
        }

        /// <summary>
        /// Spawns a floating animated reward label in the centre of the screen.
        /// Multiple toasts stack vertically so they don't overlap.
        /// </summary>
        public void ShowRewardToast(string label, string sub, RewardToastStyle style)
        {
            var parent = GetToastParent();

            // Count existing toasts to offset vertically
            int existingCount = 0;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == "RewardToast") existingCount++;

            // ── Colours ───────────────────────────────────────────────────────
            Color accent = style switch
            {
                RewardToastStyle.PerfectClear => new Color(1f, 0.84f, 0f),      // gold
                RewardToastStyle.RowClear => new Color(0.4f, 0.9f, 1f),     // cyan
                RewardToastStyle.Combo => new Color(1f, 0.45f, 0.15f),   // orange
                RewardToastStyle.Carry => new Color(0.6f, 0.9f, 0.4f),   // green
                _ => Color.white
            };

            // ── Container ─────────────────────────────────────────────────────
            var go = new GameObject("RewardToast");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            // Over the middle of the board: clear of the slow-rise badge above it as they float up.
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(400f, 80f);
            rt.anchoredPosition = new Vector2(0f, existingCount * 100f);

            // ── Main label ────────────────────────────────────────────────────
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            // Points ride in the same pill ("ROW CLEAR +200") instead of hanging below it into the next toast.
            txt.text = string.IsNullOrEmpty(sub) ? label : $"{label} <size=62%><alpha=#D9>{sub}</size>";
            txt.fontSize = style == RewardToastStyle.PerfectClear ? 52 :
                           style == RewardToastStyle.Combo ? 58 : 46;
            txt.fontStyle = FontStyles.Bold;
            txt.color = accent;
            txt.alignment = TextAlignmentOptions.Center;
            txt.enableAutoSizing = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap; // one line; the pill is sized to it
            txt.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            // Dark pill behind the label so it stays readable over bright tiles.
            var pillGo = new GameObject("Pill");
            pillGo.transform.SetParent(go.transform, false);
            pillGo.transform.SetAsFirstSibling();
            var pillRt = pillGo.AddComponent<RectTransform>();
            pillRt.anchorMin = pillRt.anchorMax = new Vector2(0.5f, 0.5f);
            pillRt.sizeDelta = new Vector2(Mathf.Max(200f, txt.GetPreferredValues(txt.text).x + 64f), 84f);
            var pill = pillGo.AddComponent<Image>();
            pill.color = new Color(0.04f, 0.06f, 0.11f, 0.6f);
            pill.raycastTarget = false;
            UiSprites.ApplyRounded(pill, 42f);

            // ── Animation ────────────────────────────────────────────────────
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            float totalDuration = style == RewardToastStyle.PerfectClear ? 2.2f : 1.6f;

            // Fade in fast, hold, fade out; float upward over the whole sequence so nothing outlives the
            // Destroy (a separate float tween used to tick on a destroyed object and log a safe-mode warning).
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(go);
            seq.Append(cg.DOFade(1f, 0.18f));
            seq.AppendInterval(totalDuration - 0.55f);
            seq.Append(cg.DOFade(0f, 0.35f));
            seq.Insert(0f, rt.DOAnchorPosY(rt.anchoredPosition.y + 120f, totalDuration - 0.02f).SetEase(Ease.OutCubic));
            seq.OnComplete(() => { if (go != null) Destroy(go); });

            // Scale punch on appear
            go.transform.localScale = Vector3.one * 0.5f;
            go.transform.DOScale(1f, 0.22f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(go);

            // Combo gets a continuous pulse
            if (style == RewardToastStyle.Combo)
                txt.transform.DOScale(1.06f, 0.25f).SetLoops(4, LoopType.Yoyo)
                   .SetEase(Ease.InOutSine).SetUpdate(true).SetLink(go);

            // Perfect clear gets a shimmer outline
            if (style == RewardToastStyle.PerfectClear)
            {
                txt.fontStyle |= FontStyles.Underline;
                txt.outlineWidth = 0.15f;
                txt.outlineColor = new Color32(255, 255, 200, 200);
            }
        }

        // Persistent badge – e.g. "SLOW RISE" while SlowFillBuff is active
        GameObject _slowRiseBadgeGo;
        Coroutine _slowRiseExpireCoroutine;

        public void ShowSlowRiseBadge(float durationSeconds)
        {
            // Kill any existing badge
            if (_slowRiseBadgeGo != null) { Destroy(_slowRiseBadgeGo); _slowRiseBadgeGo = null; }
            if (_slowRiseExpireCoroutine != null) { StopCoroutine(_slowRiseExpireCoroutine); _slowRiseExpireCoroutine = null; }

            if (_slowRiseBadgePrefab == null)
            {
                Debug.LogWarning("[GameView] Slow rise badge prefab is not assigned.");
                return;
            }

            var parent = GetToastParent();
            var go = Instantiate(_slowRiseBadgePrefab, parent, false);
            go.name = "SlowRiseBadge";
            _slowRiseBadgeGo = go;
            if (_gridMaskRt != null && go.transform is RectTransform badgeRt)
            {
                badgeRt.anchorMin = badgeRt.anchorMax = badgeRt.pivot = new Vector2(0.5f, 0.5f);
                badgeRt.anchoredPosition = _gridMaskRt.anchoredPosition
                    + new Vector2(0f, _gridMaskRt.sizeDelta.y * 0.5f + RiseBarGap + RiseBarHeight + 46f);
            }

            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.DOFade(1f, 0.3f).SetUpdate(true).SetLink(go);
            go.transform.localScale = Vector3.one * 0.8f;

            // Pop in, then a gentle continuous pulse to stay visible. Chained so the two scale tweens don't fight
            // (infinite loops can't live inside a Sequence, so the pulse starts from OnComplete).
            go.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(go)
                .OnComplete(() => go.transform.DOScale(1.04f, 0.7f).SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine).SetUpdate(true).SetLink(go));

            // Auto-expire
            _slowRiseExpireCoroutine = StartCoroutine(ExpireSlowRiseBadge(go, cg, durationSeconds));
        }

        IEnumerator ExpireSlowRiseBadge(GameObject badge, CanvasGroup cg, float delay)
        {
            yield return new WaitForSeconds(delay - 0.5f);
            _slowRiseExpireCoroutine = null;
            if (badge == null) yield break;
            cg.DOFade(0f, 0.5f).SetUpdate(true).SetLink(badge).OnComplete(() =>
            {
                if (badge != null) Destroy(badge);
                if (_slowRiseBadgeGo == badge) _slowRiseBadgeGo = null;
            });
        }
        // ─────────────────────────────────────────────────────────────────────

        public IEnumerator BlinkTilesCoroutine(IEnumerable<(int r, int c)> tiles, TileKind[,] boardCells, float blinkTime)
        {
            // Pop: swell and brighten, then shrink away with a burst of particles in the tile's colour.
            float swell = blinkTime * 0.35f;
            float vanish = blinkTime - swell;
            foreach (var m in tiles)
            {
                var img = _cellImages[m.r, m.c];
                var rt = img.rectTransform;
                rt.DOKill(true); // land a tile that's still dropping first
                Color baseColor = img.color;
                Vector2 pos = CellPos(m.r, m.c);
                DOTween.Sequence().SetTarget(rt).SetLink(img.gameObject)
                    .Append(rt.DOScale(1.18f, swell).SetEase(Ease.OutQuad))
                    .Join(img.DOColor(Color.Lerp(baseColor, Color.white, 0.55f), swell))
                    .Append(rt.DOScale(0f, vanish).SetEase(Ease.InBack))
                    .InsertCallback(swell + vanish * 0.4f, () => SpawnBurst(pos, baseColor, 4, 95f));
            }
            yield return new WaitForSeconds(blinkTime);
            // Finish the pops exactly now (scale 0), so the board refresh after clearing isn't skipped as "still animating".
            foreach (var m in tiles) _cellImages[m.r, m.c].rectTransform.DOKill(true);
        }

        public IEnumerator BlinkBombAreaCoroutine(int startCol, int startRow, TileKind[,] boardCells, float blinkTime)
        {
            int W = boardCells.GetLength(1);
            int H = boardCells.GetLength(0);

            for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                {
                    int cc = startCol + dc;
                    int rr = startRow + dr;
                    if (cc < 0 || cc >= W || rr < 0 || rr >= H) continue;
                    if (boardCells[rr, cc] == TileKind.Empty) continue;
                    // Even loop count so a Yoyo ends on the tile colour, not white.
                    int loops = Mathf.Max(2, Mathf.RoundToInt(blinkTime / 0.05f) & ~1);
                    _cellImages[rr, cc].DOColor(Color.white, blinkTime / loops).SetLoops(loops, LoopType.Yoyo);
                }
            yield return new WaitForSeconds(blinkTime);

            // Shockwave from the bomb and debris from every tile it takes out.
            BombShockwave(CellPos(startRow, startCol));
            for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                {
                    int cc = startCol + dc, rr = startRow + dr;
                    if (cc < 0 || cc >= W || rr < 0 || rr >= H || boardCells[rr, cc] == TileKind.Empty) continue;
                    SpawnBurst(CellPos(rr, cc), ColorFor(boardCells[rr, cc]), 5, 150f);
                }
        }

        // Falls scale with distance but stay short: every frame spent here is a frame the board ignores taps.
        const float FallBaseSeconds = 0.08f;
        const float FallPerCellSeconds = 0.035f;
        const float FallMaxSeconds = 0.22f;

        public IEnumerator AnimateGravityCoroutine(int[,] fallDistances)
        {
            float longestFall = 0f;

            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    int dist = fallDistances[r, c];
                    if (dist > 0)
                    {
                        float dur = Mathf.Min(FallBaseSeconds + FallPerCellSeconds * dist, FallMaxSeconds);
                        longestFall = Mathf.Max(longestFall, dur);
                        Vector2 start = CellPos(r, c);
                        float endY = CellPos(r - dist, c).y;
                        var rt = _cellImages[r, c].rectTransform;
                        rt.DOKill(true); // finish any reject nudge first so it can't fight the fall
                        rt.anchoredPosition = start;
                        rt.DOAnchorPosY(endY, dur).SetEase(Ease.InQuad);
                    }
                }

            // Wait exactly as long as the longest fall, so the snap below never cuts a running tween short.
            if (longestFall > 0f) yield return new WaitForSeconds(longestFall);

            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    var rt = _cellImages[r, c].rectTransform;
                    // A fall can still have one tick left; kill it so it can't move the tile after the snap.
                    if (fallDistances[r, c] > 0) rt.DOKill();
                    rt.anchoredPosition = CellPos(r, c);
                }
        }

        public IEnumerator SlideGridUpCoroutine(float slideTime)
        {
            float cellH = 920f / _settings.Rows;
            if (_gridClip != null) _gridClip.enabled = true; // the new bottom row starts below the board
            _gridRoot.anchoredPosition = new Vector2(0, -cellH);
            _gridRoot.DOAnchorPosY(0f, slideTime).SetEase(Ease.OutBack);
            yield return new WaitForSeconds(slideTime);
            _gridRoot.DOKill();
            _gridRoot.anchoredPosition = Vector2.zero;
            if (_gridClip != null) _gridClip.enabled = _clipForTutorial;
        }

        // Brand palette (app icon). Hues are kept apart, and each colour also has its own shape (UiSprites.GlyphFor).
        static readonly Color TileRed = new Color32(0xEF, 0x5B, 0x4B, 0xFF);
        static readonly Color TileBlue = new Color32(0x3E, 0x8E, 0xDE, 0xFF);
        static readonly Color TileGreen = new Color32(0x2B, 0xC4, 0xA7, 0xFF);
        static readonly Color TileYellow = new Color32(0xF5, 0xC0, 0x4A, 0xFF);
        static readonly Color TilePurple = new Color32(0x9B, 0x6B, 0xEF, 0xFF);
        static readonly Color TileWild = new Color32(0xF2, 0xF4, 0xFA, 0xFF);
        static readonly Color TileBomb = new Color32(0x2E, 0x36, 0x4C, 0xFF);
        // Empty cells are transparent: the static slot layer underneath shows through.
        static readonly Color EmptyCell = new Color(UiSkin.Slot.r, UiSkin.Slot.g, UiSkin.Slot.b, 0f);

        Color ColorFor(TileKind k) => k switch
        {
            TileKind.Empty => EmptyCell,
            TileKind.Red => TileRed,
            TileKind.Blue => TileBlue,
            TileKind.Green => TileGreen,
            TileKind.Yellow => TileYellow,
            TileKind.Purple => TilePurple,
            TileKind.Wild => TileWild,
            TileKind.Bomb => TileBomb,
            _ => Color.gray
        };

        void EnsureInputSystemUi()
        {
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
                go.AddComponent<InputSystemUIInputModule>();
                ApplyDragThreshold(es);
                return;
            }
            ApplyDragThreshold(es);

            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                standalone.enabled = false;
                UnityEngine.Object.Destroy(standalone);
            }

            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// A fixed 10 px drag threshold is under 1 mm on a 450 dpi phone, so a slightly moving finger turns a tap on a
        /// list button into a scroll. Scale it to ~1.5 mm of physical distance.
        /// </summary>
        static void ApplyDragThreshold(EventSystem es)
        {
            float dpi = Screen.dpi;
            if (dpi <= 0f) return; // unknown: keep the scene value
            es.pixelDragThreshold = Mathf.Clamp(Mathf.RoundToInt(dpi * 0.06f), 10, 60);
        }

        void BuildUi()
        {
            if (_mainCanvas != null)
            {
                _shake = _mainCanvas.gameObject.GetComponent<ScreenShake>();
                if (_shake == null) _shake = _mainCanvas.gameObject.AddComponent<ScreenShake>();
                // Target is set to the board (GridMask) below; shaking the root canvas does nothing.
            }

            if (_hudRoot != null) _hudRoot.SetActive(false);
            if (_helpPanel != null) _helpPanel.SetActive(false);

            var gridMaskGo = new GameObject("GridMask");
            gridMaskGo.transform.SetParent(_hudRoot.transform, false);
            var maskRt = gridMaskGo.AddComponent<RectTransform>();
            maskRt.anchorMin = new Vector2(0.5f, 0.5f);
            maskRt.anchorMax = new Vector2(0.5f, 0.5f);
            maskRt.sizeDelta = new Vector2(740, 940);
            maskRt.anchoredPosition = new Vector2(0, -120);
            var maskImg = gridMaskGo.AddComponent<Image>();
            maskImg.color = UiSkin.Board;
            maskImg.raycastTarget = false; // only the column zones need to receive touches
            UiSprites.ApplyRounded(maskImg, BoardCornerRadius);
            // The board animates almost every frame (blinks, falls, shake, glow); its own canvas keeps that from
            // re-batching the whole UI. A nested canvas needs its own raycaster or the column taps stop working.
            gridMaskGo.AddComponent<Canvas>();
            gridMaskGo.AddComponent<GraphicRaycaster>();
            // Clip only matters while the grid slides up from below; RectMask2D culls its children every frame it
            // is enabled, so it is switched on just for the slide (SlideGridUpCoroutine).
            _gridClip = gridMaskGo.AddComponent<RectMask2D>();
            _gridClip.enabled = false;
            _gridMaskRt = maskRt;
            if (_shake != null) _shake.Target = maskRt;

            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(gridMaskGo.transform, false);
            _gridRoot = gridGo.AddComponent<RectTransform>();
            _gridRoot.anchorMin = Vector2.zero;
            _gridRoot.anchorMax = Vector2.one;
            _gridRoot.offsetMin = new Vector2(10, 10);
            _gridRoot.offsetMax = new Vector2(-10, -10);

            _showTileShapes = GamePrefs.TileShapes; // saved choice; GamePrefs.Changed keeps it current afterwards

            // Empty slots are a static layer under every tile, so a tile falling past them (drop, gravity) is never
            // hidden by a later-drawn empty cell; empty cells themselves are transparent.
            var slotsGo = new GameObject("Slots");
            slotsGo.transform.SetParent(_gridRoot, false);
            var slotsRt = slotsGo.AddComponent<RectTransform>();
            slotsRt.anchorMin = Vector2.zero;
            slotsRt.anchorMax = Vector2.one;
            slotsRt.offsetMin = slotsRt.offsetMax = Vector2.zero;
            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    var slot = new GameObject($"slot_{r}_{c}").AddComponent<Image>();
                    slot.transform.SetParent(slotsRt, false);
                    slot.sprite = UiSprites.Tile;
                    slot.color = UiSkin.Slot;
                    slot.raycastTarget = false;
                    var srt = slot.rectTransform;
                    srt.anchorMin = srt.anchorMax = Vector2.zero;
                    srt.sizeDelta = new Vector2(720f / _settings.Columns - 8f, 920f / _settings.Rows - 8f);
                    srt.anchoredPosition = new Vector2((c + 0.5f) * 720f / _settings.Columns, (r + 0.5f) * 920f / _settings.Rows);
                }

            _cellImages = new Image[_settings.Rows, _settings.Columns];
            _cellGlyphs = new Image[_settings.Rows, _settings.Columns];
            float cw = 720f / _settings.Columns;
            float cellH = 920f / _settings.Rows;
            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    var cell = new GameObject($"c_{r}_{c}");
                    cell.transform.SetParent(_gridRoot, false);
                    var rt = cell.AddComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(cw - 8f, cellH - 8f);
                    rt.anchorMin = rt.anchorMax = Vector2.zero;
                    rt.pivot = new Vector2(0.5f, 0.5f); // pops, squashes and shrinks happen around the tile's centre
                    rt.anchoredPosition = CellPos(r, c);
                    var img = cell.AddComponent<Image>();
                    img.sprite = UiSprites.Tile;
                    img.color = ColorFor(TileKind.Empty);
                    img.raycastTarget = false; // 63 tiles under the column zones; skip them in every raycast
                    _cellImages[r, c] = img;

                    // Shape / icon on the tile: colour-blind shapes for colours, asterisk for Wild, bomb for Bomb.
                    var glyphGo = new GameObject("Glyph");
                    glyphGo.transform.SetParent(cell.transform, false);
                    var glyphRt = glyphGo.AddComponent<RectTransform>();
                    glyphRt.anchorMin = glyphRt.anchorMax = new Vector2(0.5f, 0.5f);
                    glyphRt.sizeDelta = new Vector2(cw - 8f, cellH - 8f) * GlyphScale;
                    var glyph = glyphGo.AddComponent<Image>();
                    glyph.raycastTarget = false;
                    glyph.preserveAspect = true;
                    glyph.enabled = false;
                    _cellGlyphs[r, c] = glyph;
                }

            BuildDangerGlow(gridMaskGo.transform);
            BuildRiseBar(maskRt);

            _colOverlayBtns = new Button[_settings.Columns];
            _colTapZones = new ColumnTapZone[_settings.Columns];
            for (int c = 0; c < _settings.Columns; c++)
            {
                var colOverlay = new GameObject($"ColOverlay_{c}");
                colOverlay.transform.SetParent(_gridRoot, false);
                var rt = colOverlay.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(c * cw + cw * 0.5f, 0);
                rt.sizeDelta = new Vector2(cw, 0);

                var img = colOverlay.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0f);

                // The Button is only an interactable gate (tutorial locking). Taps are delivered on pointer DOWN by
                // ColumnTapZone; there is deliberately no onClick listener, so each tap fires exactly once.
                var btn = colOverlay.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = img;
                var nav = btn.navigation;
                nav.mode = Navigation.Mode.None;
                btn.navigation = nav;

                var zone = colOverlay.AddComponent<ColumnTapZone>();
                zone.Column = c;
                zone.Gate = btn;
                zone.OnTap = col => _onColumnClicked?.Invoke(col);
                zone.OnRejected = OnColumnTapRejected;

                _colOverlayBtns[c] = btn;   // store so tutorial can lock it
                _colTapZones[c] = zone;
            }

            // Effects (particles, points) sit above the tiles in the grid's own coordinates (same as CellPos).
            var fxGo = new GameObject("Fx");
            fxGo.transform.SetParent(_gridRoot, false);
            _fxLayer = fxGo.AddComponent<RectTransform>();
            _fxLayer.anchorMin = Vector2.zero;
            _fxLayer.anchorMax = Vector2.one;
            _fxLayer.offsetMin = _fxLayer.offsetMax = Vector2.zero;

            /*
            _colButtonGos = new GameObject[_settings.Columns];
            for (int c = 0; c < _settings.Columns; c++)
            {
                int col = c;
                var view = Instantiate(_columnButtonPrefab, _columnButtonsRoot);
                var btnGo = view.gameObject;
                btnGo.name = $"Col{c}";
                _colButtonGos[c] = btnGo;
                view.Button.onClick.AddListener(() => _onColumnClicked?.Invoke(col));
                view.Label.text = (c + 1).ToString();
            } */

            if (_gameOverRoot != null)
            {
                _gameOverRoot.SetActive(false);
                if (_gameOverRoot.GetComponent<CanvasGroup>() == null)
                    _gameOverRoot.AddComponent<CanvasGroup>();
            }

            if (_mainCanvas != null)
                BuildLoadingScreen(_mainCanvas.transform);
            else
                BuildLoadingScreen(transform);

            // Main menu is now fully in the hierarchy – InitMainMenu() is called
            // at the end of Build() once delegates are ready.
        }

        private void CreateGlow(Transform parent, Color color, float size, float duration)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.DOFade(color.a * 0.5f, duration).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
            rt.DOScale(1.15f, duration * 1.2f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
        }

        private Image CreatePreviewSlot(Transform parent, string label, Vector2 pos, out TextMeshProUGUI innerLabel)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(160, 160);
            rt.pivot = new Vector2(0, 1);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 1);
            lrt.anchorMax = new Vector2(1, 1);
            lrt.anchoredPosition = new Vector2(0, 30);
            lrt.sizeDelta = new Vector2(0, 40);
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = label;
            lt.fontSize = 24;
            lt.fontStyle = FontStyles.Bold;
            lt.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            lt.alignment = TextAlignmentOptions.Left;

            var box = new GameObject("Box");
            box.transform.SetParent(go.transform, false);
            var brt = box.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.sizeDelta = Vector2.zero;
            var img = box.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0.1f);

            var txt = new GameObject("Text");
            txt.transform.SetParent(box.transform, false);
            var trt = txt.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            innerLabel = txt.AddComponent<TextMeshProUGUI>();
            innerLabel.fontSize = 32;
            innerLabel.fontStyle = FontStyles.Bold;
            innerLabel.alignment = TextAlignmentOptions.Center;
            innerLabel.color = Color.white;
            innerLabel.raycastTarget = false;

            return img;
        }

        GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f, 0.8f);
            return go;
        }

        TextMeshProUGUI CreateTmp(Transform parent, string name, float size, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 200);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        Button CreateButton(Transform parent, string label, Vector2 pos, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(400, 100);
            rt.anchoredPosition = pos;
            go.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.85f, 1f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = txtGo.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = 38;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;

            return btn;
        }

        public void UpdateLoadingStatus(string status)
        {
            if (_loadingStatusText != null)
                _loadingStatusText.text = status;
        }

        public void EnableStartButton()
        {
            if (_loadingRoot == null) return;

            // Auto-dismiss after a brief pause so the player can see the status
            if (_loadingStatusText != null)
            {
                _loadingStatusText.transform.DOKill();
                _loadingStatusText.transform.localScale = Vector3.one;
            }

            // Automatically proceed to the main menu — no tap required
            Time.timeScale = 1f;
            HideLoadingScreen(1.0f);
        }

        private void BuildLoadingScreen(Transform parent)
        {
            Time.timeScale = 0f;

            if (_loadingRoot == null)
            {
                Debug.LogError("Loading Root is not assigned in the Inspector!");
                return;
            }

            _loadingRoot.SetActive(true);
            _loadingRoot.transform.SetParent(parent, false);
            _loadingRoot.transform.SetAsLastSibling();

            var cg = _loadingRoot.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;

            // Same navy as the rest of the app from the first frame (the skin pass would recolour it a frame later).
            if (_loadingRoot.transform.Find("Background") is Transform loadingBg && loadingBg.TryGetComponent(out Image loadingBgImg))
                loadingBgImg.color = UiSkin.AppBackground;

#if !UNITY_EDITOR
            // Device only: in the Editor, glyphs added to a dynamic font are saved into the font asset.
            StartCoroutine(WarmUpFontsCoroutine());
#endif

            var startBtn = _loadingRoot.GetComponent<Button>();
            if (startBtn != null)
            {
                startBtn.interactable = false;
                startBtn.onClick.RemoveAllListeners();
                startBtn.onClick.AddListener(() =>
                {
                    startBtn.interactable = false;
                    Time.timeScale = 1f;
                    HideLoadingScreen(1.2f);
                });
            }

            if (_loadingTitleText != null)
            {
                _loadingTitleText.transform.DOKill();
                _loadingTitleText.transform.localScale = Vector3.one;
                _loadingTitleText.transform.DOScale(1.04f, 3f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
            }

            if (_loadingGlows != null && _loadingGlows.Length > 0)
            {
                foreach (var glow in _loadingGlows)
                {
                    if (glow == null) continue;
                    glow.transform.DOKill();
                    float baseAlpha = glow.color.a;
                    glow.DOFade(baseAlpha * 0.5f, 3f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
                    glow.rectTransform.DOScale(1.15f, 3.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
                }
            }
        }

        public Action OnLoadingDone;

        public void HideLoadingScreen(float duration = 1.2f)
        {
            if (_loadingRoot == null) return;
            var cg = _loadingRoot.GetComponent<CanvasGroup>();
            cg.DOFade(0, duration).SetEase(Ease.InOutQuad).SetUpdate(true).OnComplete(() =>
            {
                StopLoadingScreenTweens();
                _loadingRoot.SetActive(false);
                OnLoadingDone?.Invoke();
            });
        }

        /// <summary>The loading screen's infinite pulse/glow loops would otherwise keep running for the whole session.</summary>
        void StopLoadingScreenTweens()
        {
            if (_loadingTitleText != null) _loadingTitleText.transform.DOKill();
            if (_loadingGlows == null) return;
            foreach (var glow in _loadingGlows)
            {
                if (glow == null) continue;
                glow.DOKill();                 // alpha loop (targets the Image)
                glow.rectTransform.DOKill();   // scale loop (targets the transform)
            }
        }

        // Printable ASCII plus the few symbols the UI uses. Both TMP fonts are dynamic and start empty in builds,
        // so without this every first use of a glyph renders it into the atlas mid-game (a visible hitch).
        const string WarmupCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~—–…×▼•·";

        IEnumerator WarmUpFontsCoroutine()
        {
            yield return null; // let the loading screen draw first

            var fonts = new HashSet<TMP_FontAsset>();
            if (TMP_Settings.defaultFontAsset != null) fonts.Add(TMP_Settings.defaultFontAsset);
            var root = _mainCanvas != null ? _mainCanvas.transform : transform;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.font != null) fonts.Add(text.font);

            foreach (var font in fonts)
            {
                if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
                font.TryAddCharacters(WarmupCharacters);
                yield return null; // spread the work over frames
            }
        }

        // ── Tutorial Controller Extensions ───────────────────────────────────

        public IEnumerator ShowTutorialDialogCoroutine(string title, string body, bool showNextButton)
        {
            _tutorialNextClicked = false;
            ShowTutorialDialog(title, body, showNextButton);
            if (showNextButton)
                yield return new WaitUntil(() => _tutorialNextClicked);
        }

        public void ShowTutorialDialog(string title, string body, bool showNextButton)
        {
            if (_tutorialDialogGo == null)
            {
                _tutorialDialogGo = new GameObject("TutorialDialogPanel");
                _tutorialDialogGo.transform.SetParent(_mainCanvas != null ? _mainCanvas.transform : transform, false);
                var rt = _tutorialDialogGo.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -140f);
                rt.sizeDelta = new Vector2(TutorialDialogWidth, 320f); // height is fitted to the text in LayoutTutorialDialog

                _tutorialDialogGo.AddComponent<Image>().color = UiSkin.Surface;

                var titleGo = new GameObject("Title");
                titleGo.transform.SetParent(_tutorialDialogGo.transform, false);
                var titleRt = titleGo.AddComponent<RectTransform>();
                titleRt.anchorMin = new Vector2(0f, 1f);
                titleRt.anchorMax = new Vector2(1f, 1f);
                titleRt.pivot = new Vector2(0.5f, 1f);
                titleRt.anchoredPosition = new Vector2(0f, -TutorialPadding);
                titleRt.sizeDelta = new Vector2(-2f * TutorialPadding, TutorialTitleHeight);
                _tutorialTitleText = titleGo.AddComponent<TextMeshProUGUI>();
                _tutorialTitleText.fontSize = 44;
                _tutorialTitleText.textWrappingMode = TextWrappingModes.NoWrap;
                _tutorialTitleText.enableAutoSizing = true; // long titles shrink to fit one line instead of overflowing
                _tutorialTitleText.fontSizeMin = 32;
                _tutorialTitleText.fontSizeMax = 44;
                _tutorialTitleText.fontStyle = FontStyles.Bold;
                _tutorialTitleText.color = new Color(0.2f, 0.6f, 1.0f);
                _tutorialTitleText.alignment = TextAlignmentOptions.Left;

                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(_tutorialDialogGo.transform, false);
                var bodyRt = bodyGo.AddComponent<RectTransform>();
                bodyRt.anchorMin = new Vector2(0f, 1f);
                bodyRt.anchorMax = new Vector2(1f, 1f);
                bodyRt.pivot = new Vector2(0.5f, 1f);
                bodyRt.anchoredPosition = new Vector2(0f, -(TutorialPadding + TutorialTitleHeight + TutorialGap));
                bodyRt.sizeDelta = new Vector2(-2f * TutorialPadding, 150f);
                _tutorialBodyText = bodyGo.AddComponent<TextMeshProUGUI>();
                _tutorialBodyText.fontSize = 32;
                _tutorialBodyText.color = new Color(0.9f, 0.9f, 0.95f);
                _tutorialBodyText.alignment = TextAlignmentOptions.TopLeft;
                _tutorialBodyText.textWrappingMode = TextWrappingModes.Normal;
                _tutorialBodyText.lineSpacing = 4f;

                var btnGo = new GameObject("NextBtn");
                btnGo.transform.SetParent(_tutorialDialogGo.transform, false);
                var btnRt = btnGo.AddComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(1f, 0f);
                btnRt.anchorMax = new Vector2(1f, 0f);
                btnRt.pivot = new Vector2(1f, 0f);
                btnRt.anchoredPosition = new Vector2(-TutorialPadding, TutorialPadding);
                btnRt.sizeDelta = new Vector2(230f, TutorialButtonHeight);
                btnGo.AddComponent<Image>().color = UiSkin.Primary;
                _tutorialNextBtn = btnGo.AddComponent<Button>();
                _tutorialNextBtn.onClick.AddListener(() => _tutorialNextClicked = true);

                var btnTxtGo = new GameObject("Text");
                btnTxtGo.transform.SetParent(btnGo.transform, false);
                var btnTxtRt = btnTxtGo.AddComponent<RectTransform>();
                btnTxtRt.anchorMin = Vector2.zero;
                btnTxtRt.anchorMax = Vector2.one;
                btnTxtRt.sizeDelta = Vector2.zero;
                var btnTxt = btnTxtGo.AddComponent<TextMeshProUGUI>();
                btnTxt.text = "NEXT";
                btnTxt.fontSize = 32;
                btnTxt.fontStyle = FontStyles.Bold;
                btnTxt.color = Color.white;
                btnTxt.alignment = TextAlignmentOptions.Center;
            }

            UiSkin.Apply(_tutorialDialogGo.transform);
            _tutorialDialogGo.SetActive(true);
            _tutorialDialogGo.transform.SetAsLastSibling();
            _tutorialTitleText.text = title;
            _tutorialBodyText.text = body;
            _tutorialNextBtn.gameObject.SetActive(showNextButton);
            LayoutTutorialDialog(showNextButton);

            _tutorialDialogGo.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            _tutorialDialogGo.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        const float TutorialDialogWidth = 880f;
        const float TutorialPadding = 26f;
        const float TutorialTitleHeight = 60f;
        const float TutorialGap = 14f;
        const float TutorialButtonHeight = 84f;

        /// <summary>Fits the dialog to its text (width capped to the screen), leaving a row for buttons if needed.</summary>
        void LayoutTutorialDialog(bool hasButtons)
        {
            var dialogRt = (RectTransform)_tutorialDialogGo.transform;
            float canvasWidth = _mainCanvas != null ? ((RectTransform)_mainCanvas.transform).rect.width : TutorialDialogWidth;
            float width = canvasWidth > 0f ? Mathf.Min(TutorialDialogWidth, canvasWidth - 40f) : TutorialDialogWidth;

            float textWidth = width - 2f * TutorialPadding;
            float bodyHeight = Mathf.Ceil(_tutorialBodyText.GetPreferredValues(_tutorialBodyText.text, textWidth, 0f).y) + 4f;
            var bodyRt = _tutorialBodyText.rectTransform;
            bodyRt.sizeDelta = new Vector2(-2f * TutorialPadding, bodyHeight);

            float height = TutorialPadding + TutorialTitleHeight + TutorialGap + bodyHeight + TutorialPadding;
            if (hasButtons) height += TutorialButtonHeight + TutorialGap;
            dialogRt.sizeDelta = new Vector2(width, height);
        }

        public void ShowTutorialChoiceDialog(string title, string body, Action onPlay, Action onSkip)
        {
            ShowTutorialDialog(title, body, false);
            LayoutTutorialDialog(hasButtons: true);

            var playBtnGo = new GameObject("PlayTutorialChoiceBtn");
            playBtnGo.transform.SetParent(_tutorialDialogGo.transform, false);
            var rtPlay = playBtnGo.AddComponent<RectTransform>();
            rtPlay.anchorMin = new Vector2(0.5f, 0f);
            rtPlay.anchorMax = new Vector2(0.5f, 0f);
            rtPlay.pivot = new Vector2(1f, 0f);
            rtPlay.anchoredPosition = new Vector2(-12f, TutorialPadding);
            rtPlay.sizeDelta = new Vector2(280f, TutorialButtonHeight);
            playBtnGo.AddComponent<Image>().color = UiSkin.Primary;
            var btnPlay = playBtnGo.AddComponent<Button>();
            btnPlay.onClick.AddListener(() =>
            {
                Destroy(playBtnGo);
                var skipBtn = _tutorialDialogGo.transform.Find("SkipTutorialChoiceBtn");
                if (skipBtn != null) Destroy(skipBtn.gameObject);
                onPlay?.Invoke();
            });
            var txtPlayGo = new GameObject("Text");
            txtPlayGo.transform.SetParent(playBtnGo.transform, false);
            var txtPlayRt = txtPlayGo.AddComponent<RectTransform>();
            txtPlayRt.anchorMin = Vector2.zero;
            txtPlayRt.anchorMax = Vector2.one;
            txtPlayRt.sizeDelta = Vector2.zero;
            var txtPlay = txtPlayGo.AddComponent<TextMeshProUGUI>();
            txtPlay.text = "TUTORIAL";
            txtPlay.fontSize = 32;
            txtPlay.fontStyle = FontStyles.Bold;
            txtPlay.color = Color.white;
            txtPlay.alignment = TextAlignmentOptions.Center;

            var skipBtnGo = new GameObject("SkipTutorialChoiceBtn");
            skipBtnGo.transform.SetParent(_tutorialDialogGo.transform, false);
            var rtSkip = skipBtnGo.AddComponent<RectTransform>();
            rtSkip.anchorMin = new Vector2(0.5f, 0f);
            rtSkip.anchorMax = new Vector2(0.5f, 0f);
            rtSkip.pivot = new Vector2(0f, 0f);
            rtSkip.anchoredPosition = new Vector2(12f, TutorialPadding);
            rtSkip.sizeDelta = new Vector2(280f, TutorialButtonHeight);
            skipBtnGo.AddComponent<Image>().color = UiSkin.SurfaceRaised;
            var btnSkip = skipBtnGo.AddComponent<Button>();
            btnSkip.onClick.AddListener(() =>
            {
                Destroy(skipBtnGo);
                var playBtn = _tutorialDialogGo.transform.Find("PlayTutorialChoiceBtn");
                if (playBtn != null) Destroy(playBtn.gameObject);
                HideTutorialDialog();
                onSkip?.Invoke();
            });
            var txtSkipGo = new GameObject("Text");
            txtSkipGo.transform.SetParent(skipBtnGo.transform, false);
            var txtSkipRt = txtSkipGo.AddComponent<RectTransform>();
            txtSkipRt.anchorMin = Vector2.zero;
            txtSkipRt.anchorMax = Vector2.one;
            txtSkipRt.sizeDelta = Vector2.zero;
            var txtSkip = txtSkipGo.AddComponent<TextMeshProUGUI>();
            txtSkip.text = "SKIP";
            txtSkip.fontSize = 32;
            txtSkip.fontStyle = FontStyles.Bold;
            txtSkip.color = Color.white;
            txtSkip.alignment = TextAlignmentOptions.Center;

            UiSkin.Apply(_tutorialDialogGo.transform);
        }

        public void HideTutorialDialog()
        {
            if (_tutorialDialogGo != null) _tutorialDialogGo.SetActive(false);
        }

        // ── How to play legend (Help panel) ─────────────────────────────────
        /// <summary>Visual rules summary under the Help text: tiles drawn exactly as they look on the board.</summary>
        void BuildHelpLegend(Transform helpPanel)
        {
            var old = helpPanel.Find("HowToPlayLegend");
            if (old != null) Destroy(old.gameObject);

            var legendGo = new GameObject("HowToPlayLegend");
            legendGo.transform.SetParent(helpPanel, false);
            var legendRt = legendGo.AddComponent<RectTransform>();
            legendRt.anchorMin = legendRt.anchorMax = legendRt.pivot = new Vector2(0.5f, 1f);
            legendRt.sizeDelta = new Vector2(680f, 0f);
            legendGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            legendRt.anchoredPosition = new Vector2(0f, -280f);

            // The scene's one-line help text moves to the top of the panel, with the legend right under it.
            if (helpPanel.Find("HelpOverlay") is RectTransform intro)
            {
                intro.anchorMin = new Vector2(0f, 1f);
                intro.anchorMax = new Vector2(1f, 1f);
                intro.pivot = new Vector2(0.5f, 1f);
                intro.sizeDelta = new Vector2(-220f, 150f);
                intro.anchoredPosition = new Vector2(0f, -100f);
            }
            var layout = legendGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            AddLegendRow(legendGo.transform, LegendTile(TileKind.Red), "Match 3 or more",
                "Same colour in a row, column or L. Each colour has its own shape.");
            AddLegendRow(legendGo.transform, LegendTile(TileKind.Wild), "Wild", "Matches any colour.");
            AddLegendRow(legendGo.transform, LegendTile(TileKind.Bomb), "Bomb", "Clears the 3x3 area where it lands.");
            AddLegendRow(legendGo.transform, LegendRiseBar(), "Rising rows",
                "When the bar fills, a new row pushes up. Keep every column off the top.");
            AddLegendRow(legendGo.transform, LegendCombo(), "Combos",
                "Clear again within 3 seconds to multiply your points.");
        }

        GameObject LegendTile(TileKind kind)
        {
            var go = new GameObject("Icon");
            var img = go.AddComponent<Image>();
            img.sprite = UiSprites.Tile;
            img.color = ColorFor(kind);
            img.raycastTarget = false;

            var glyphGo = new GameObject("Glyph");
            glyphGo.transform.SetParent(go.transform, false);
            var glyphRt = glyphGo.AddComponent<RectTransform>();
            glyphRt.anchorMin = glyphRt.anchorMax = new Vector2(0.5f, 0.5f);
            glyphRt.sizeDelta = Vector2.one * 84f * GlyphScale;
            var glyph = glyphGo.AddComponent<Image>();
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            Sprite sprite = kind == TileKind.Wild ? _wildSprite : kind == TileKind.Bomb ? _bombSprite : UiSprites.GlyphFor(kind);
            glyph.sprite = sprite;
            glyph.color = GlyphColorFor(kind);
            glyph.enabled = sprite != null;
            return go;
        }

        GameObject LegendRiseBar()
        {
            var go = new GameObject("Icon");
            go.AddComponent<RectTransform>();
            var trackGo = new GameObject("Track");
            trackGo.transform.SetParent(go.transform, false);
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(80f, RiseBarHeight);
            var track = trackGo.AddComponent<Image>();
            track.color = UiSkin.SurfaceRaised;
            track.raycastTarget = false;
            UiSprites.ApplyRounded(track, RiseBarHeight * 0.5f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0.65f, 1f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.color = UiSkin.Primary;
            fill.raycastTarget = false;
            UiSprites.ApplyRounded(fill, RiseBarHeight * 0.5f);
            return go;
        }

        GameObject LegendCombo()
        {
            var go = new GameObject("Icon");
            var pill = go.AddComponent<Image>();
            pill.color = new Color(0.04f, 0.06f, 0.11f, 0.8f);
            pill.raycastTarget = false;
            UiSprites.ApplyRounded(pill, 24f);
            var label = CreateTmp(go.transform, "Text", 34, FontStyles.Bold, TextAlignmentOptions.Center);
            label.text = "x3";
            label.color = new Color(1f, 0.45f, 0.15f);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            return go;
        }

        void AddLegendRow(Transform parent, GameObject icon, string title, string body)
        {
            var row = new GameObject("Row_" + title);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 24f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            row.AddComponent<LayoutElement>().minHeight = 100f; // grows if the description wraps to more lines

            icon.transform.SetParent(row.transform, false);
            var iconElement = icon.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = 84f;
            iconElement.minHeight = iconElement.preferredHeight = 84f;

            var textCol = new GameObject("Text");
            textCol.transform.SetParent(row.transform, false);
            textCol.AddComponent<RectTransform>();
            textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var textLayout = textCol.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = 2f;
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandHeight = false;

            var titleText = CreateTmp(textCol.transform, "Title", 36, FontStyles.Bold, TextAlignmentOptions.Left);
            titleText.text = title;
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            var bodyText = CreateTmp(textCol.transform, "Body", 28, FontStyles.Normal, TextAlignmentOptions.Left);
            bodyText.text = body;
            bodyText.color = UiSkin.TextMuted;
            bodyText.textWrappingMode = TextWrappingModes.Normal; // the project's TMP default doesn't wrap
        }

        // ── Confirm Dialog ───────────────────────────────────────────────────
        GameObject _confirmDialogGo;
        TextMeshProUGUI _confirmTitleText;
        TextMeshProUGUI _confirmBodyText;
        TextMeshProUGUI _confirmYesText;
        TextMeshProUGUI _confirmNoText;
        Action _confirmOnYes;

        public bool IsConfirmDialogOpen => _confirmDialogGo != null && _confirmDialogGo.activeSelf;

        /// <summary>Modal yes/no dialog over everything else (works while paused). Back / "No" just closes it.</summary>
        public void ShowConfirmDialog(string title, string body, string confirmLabel, string cancelLabel, Action onConfirm)
        {
            if (_confirmDialogGo == null) BuildConfirmDialog();

            _confirmTitleText.text = title;
            _confirmBodyText.text = body;
            _confirmYesText.text = confirmLabel;
            _confirmNoText.text = cancelLabel;
            _confirmOnYes = onConfirm;

            _confirmDialogGo.SetActive(true);
            _confirmDialogGo.transform.SetAsLastSibling();
            var panel = _confirmDialogGo.transform.GetChild(0);
            panel.DOKill();
            panel.localScale = Vector3.one * 0.9f;
            panel.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public void HideConfirmDialog()
        {
            _confirmOnYes = null;
            if (_confirmDialogGo != null) _confirmDialogGo.SetActive(false);
        }

        void BuildConfirmDialog()
        {
            _confirmDialogGo = new GameObject("ConfirmDialog");
            _confirmDialogGo.transform.SetParent(_mainCanvas != null ? _mainCanvas.transform : transform, false);
            var rootRt = _confirmDialogGo.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            _confirmDialogGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f); // dim + blocks taps behind

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(_confirmDialogGo.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(760f, 400f);
            panelGo.AddComponent<Image>().color = UiSkin.Surface;

            _confirmTitleText = CreateTmp(panelGo.transform, "Title", 42, FontStyles.Bold, TextAlignmentOptions.Center);
            var titleRt = _confirmTitleText.rectTransform;
            titleRt.anchoredPosition = new Vector2(0f, -40f);
            titleRt.sizeDelta = new Vector2(-60f, 60f);

            _confirmBodyText = CreateTmp(panelGo.transform, "Body", 30, FontStyles.Normal, TextAlignmentOptions.Center);
            _confirmBodyText.color = new Color(0.85f, 0.87f, 0.92f);
            var bodyRt = _confirmBodyText.rectTransform;
            bodyRt.anchoredPosition = new Vector2(0f, -120f);
            bodyRt.sizeDelta = new Vector2(-60f, 110f);

            _confirmNoText = CreateDialogButton(panelGo.transform, "CancelBtn", new Vector2(-170f, 40f),
                UiSkin.SurfaceRaised, () =>
                {
                    SfxPlayer.Play(Sfx.UiTap);
                    HideConfirmDialog();
                });
            _confirmYesText = CreateDialogButton(panelGo.transform, "ConfirmBtn", new Vector2(170f, 40f),
                UiSkin.Danger, () =>
                {
                    SfxPlayer.Play(Sfx.UiTap);
                    var onYes = _confirmOnYes;
                    HideConfirmDialog();
                    onYes?.Invoke();
                });

            UiSkin.Apply(_confirmDialogGo.transform);
            _confirmDialogGo.SetActive(false);
        }

        TextMeshProUGUI CreateDialogButton(Transform parent, string name, Vector2 pos, Color color, Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(300f, 96f);
            go.AddComponent<Image>().color = color;
            go.AddComponent<Button>().onClick.AddListener(() => onClick());

            var label = CreateTmp(go.transform, "Text", 30, FontStyles.Bold, TextAlignmentOptions.Center);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            return label;
        }

        public void HighlightColumn(int colIndex)
        {
            if (_colOverlayBtns == null) return;
            _tutorialHighlightedColumn = colIndex;
            _clipForTutorial = true;
            if (_gridClip != null) _gridClip.enabled = true;

            float cellH = 920f / _settings.Rows;
            float halfHeight = (_settings.Rows * cellH) * 0.5f;

            for (int c = 0; c < _colOverlayBtns.Length; c++)
            {
                var btn = _colOverlayBtns[c];
                if (btn == null) continue;
                var btnGo = btn.gameObject;

                if (c == colIndex)
                {
                    var img = btnGo.GetComponent<Image>();
                    if (img != null) img.color = new Color(1f, 1f, 1f, 0.05f);
                    if (btn != null) btn.interactable = true;

                    var overlayGo = btnGo.transform.Find("TutorialHighlightOverlay")?.gameObject;
                    if (overlayGo == null)
                    {
                        overlayGo = new GameObject("TutorialHighlightOverlay");
                        overlayGo.transform.SetParent(btnGo.transform, false);
                        var ort = overlayGo.AddComponent<RectTransform>();
                        ort.anchorMin = Vector2.zero;
                        ort.anchorMax = Vector2.one;
                        ort.sizeDelta = Vector2.zero;
                        var oimg = overlayGo.AddComponent<Image>();
                        oimg.color = new Color(0.95f, 0.75f, 0.2f, 0.2f);
                        UiSprites.ApplyRounded(oimg, 14f);
                        oimg.raycastTarget = false;
                        overlayGo.transform.DOScale(1.08f, 0.6f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(overlayGo);
                    }

                    var arrowGo = btnGo.transform.Find("TutorialArrow")?.gameObject;
                    if (arrowGo == null)
                    {
                        arrowGo = new GameObject("TutorialArrow");
                        arrowGo.transform.SetParent(btnGo.transform, false);
                        var art = arrowGo.AddComponent<RectTransform>();
                        art.anchoredPosition = new Vector2(0f, halfHeight - 60f);
                        art.sizeDelta = new Vector2(60f, 60f);
                        var txt = arrowGo.AddComponent<TextMeshProUGUI>();
                        txt.text = "▼";
                        txt.fontSize = 44;
                        txt.color = new Color(0.95f, 0.75f, 0.2f, 1f);
                        txt.fontStyle = FontStyles.Bold;
                        txt.alignment = TextAlignmentOptions.Center;
                        txt.raycastTarget = false;
                        art.DOAnchorPosY(halfHeight - 40f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(arrowGo);
                    }

                    for (int r = 0; r < _settings.Rows; r++)
                    {
                        var cellImg = _cellImages[r, c];

                        if (cellImg != null && !IsEmptyCell(r, c)) // empty cells stay transparent over the slot layer
                        {
                            var col = cellImg.color;
                            col.a = 1.0f;
                            cellImg.color = col;
                        }
                        SetGlyphAlpha(r, c, 1f);
                    }
                }
                else
                {
                    var img = btnGo.GetComponent<Image>();
                    if (img != null) img.color = new Color(1f, 1f, 1f, 0f);
                    if (btn != null) btn.interactable = false;

                    for (int r = 0; r < _settings.Rows; r++)
                    {
                        var cellImg = _cellImages[r, c];

                        if (cellImg != null && !IsEmptyCell(r, c)) // empty cells stay transparent over the slot layer
                        {
                            var col = cellImg.color;
                            col.a = 0.15f;
                            cellImg.color = col;
                        }
                        SetGlyphAlpha(r, c, 0.15f);
                    }
                }
            }

            // Ensure the target column's overlay is enabled
            if (_colOverlayBtns != null && colIndex >= 0 && colIndex < _colOverlayBtns.Length && _colOverlayBtns[colIndex] != null)
                _colOverlayBtns[colIndex].interactable = true;
        }

        public void ClearColumnHighlight()
        {
            _tutorialHighlightedColumn = -1;
            _clipForTutorial = false;
            if (_gridClip != null) _gridClip.enabled = false;
            if (_colOverlayBtns == null) return;

            for (int c = 0; c < _colOverlayBtns.Length; c++)
            {
                var btn = _colOverlayBtns[c];
                if (btn == null) continue;
                var btnGo = btn.gameObject;

                if (btn != null) btn.interactable = true;

                var img = btnGo.GetComponent<Image>();
                if (img != null) img.color = new Color(1f, 1f, 1f, 0f);

                var overlay = btnGo.transform.Find("TutorialHighlightOverlay");
                if (overlay != null) Destroy(overlay.gameObject);

                var arrow = btnGo.transform.Find("TutorialArrow");
                if (arrow != null) Destroy(arrow.gameObject);

                for (int r = 0; r < _settings.Rows; r++)
                {
                    var cellImg = _cellImages[r, c];

                    if (cellImg != null && !IsEmptyCell(r, c)) // empty cells stay transparent over the slot layer
                    {
                        var col = cellImg.color;
                        col.a = 1.0f;
                        cellImg.color = col;
                    }
                    SetGlyphAlpha(r, c, 1f);
                }
            }
        }
    }

    /// <summary>Style hint for ShowRewardToast so it can pick the right colour and emphasis.</summary>
    public enum RewardToastStyle { Default, Combo, RowClear, PerfectClear, Carry }
}
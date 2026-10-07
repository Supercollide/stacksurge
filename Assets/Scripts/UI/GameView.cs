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

        // Vibration runtime state (persisted via PlayerPrefs)
        private bool _vibrationEnabled = true;

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

            DOTween.SetTweensCapacity(500, 50);
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

                var outline = replayBtnGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.3f);
                outline.effectDistance = new Vector2(2f, 2f);

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
                txt.fontSize = 24;
                txt.fontStyle = FontStyles.Bold;
                txt.color = Color.black;
                txt.alignment = TextAlignmentOptions.Center;
            }

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

                var outline = notifBtnGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.3f);
                outline.effectDistance = new Vector2(2f, 2f);

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
                txt.fontSize = 24;
                txt.fontStyle = FontStyles.Bold;
                txt.color = Color.white;
                txt.alignment = TextAlignmentOptions.Center;
            }

            // Wire up the main menu buttons now that delegates are assigned
            InitMainMenu();
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

                var outlineNotif = notifBtnGo.AddComponent<Outline>();
                outlineNotif.effectColor = new Color(0f, 0f, 0f, 0.4f);
                outlineNotif.effectDistance = new Vector2(2f, -2f);

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
                txtNotif.fontSize = 20;
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

                    var outlineHelp = helpBtnGo.AddComponent<Outline>();
                    outlineHelp.effectColor = new Color(0f, 0f, 0f, 0.4f);
                    outlineHelp.effectDistance = new Vector2(2f, -2f);

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
                    txtHelp.fontSize = 20;
                    txtHelp.fontStyle = FontStyles.Bold;
                    txtHelp.color = Color.black;
                    txtHelp.alignment = TextAlignmentOptions.Center;
                }
            }

            // VIBRATION TOGGLE  — assign _mainMenuVibrationToggle in the Inspector
            _vibrationEnabled = PlayerPrefs.GetInt("VibrationEnabled", 1) == 1;
            Haptics.Enabled = _vibrationEnabled;
            if (_mainMenuVibrationToggle != null)
            {
                // Set without firing the callback first
                _mainMenuVibrationToggle.onValueChanged.RemoveAllListeners();
                _mainMenuVibrationToggle.isOn = _vibrationEnabled;
                _mainMenuVibrationToggle.onValueChanged.AddListener(isOn =>
                {
                    _vibrationEnabled = isOn;
                    Haptics.Enabled = isOn;
                    PlayerPrefs.SetInt("VibrationEnabled", _vibrationEnabled ? 1 : 0);
                    PlayerPrefs.Save();
                });
            }

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
        public void UpdateHud(int score, float timeAlive, float riseSeconds, bool inGrace, TileKind current, TileKind next)
        {
            if (_scoreText == null) return;

            if (score != _lastScore)
            {
                _scoreText.text = FormatScore(score);
                // Punch only for real gains; the +10/s survival trickle shouldn't wobble the score every second.
                if (score - _lastScore >= 50)
                {
                    _scoreText.transform.DOKill();
                    _scoreText.transform.localScale = Vector3.one;
                    _scoreText.transform.DOPunchScale(Vector3.one * 0.15f, 0.3f, 10, 1f);
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

            img.color = ColorFor(k);

            if (k == TileKind.Wild)
            {
                innerIcon.sprite = _wildSprite;
                //innerIcon.color = new Color(0.36f, 0.2f, 0.55f); // deep violet, reads clearly on white
                innerIcon.color = new Color(0.0f, 0.0f, 0.0f);
                innerIcon.enabled = true;
                img.transform.DOKill();
                img.transform.localScale = Vector3.one;
                img.transform.DOScale(1.1f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(img.gameObject);
            }
            else if (k == TileKind.Bomb)
            {
                innerIcon.sprite = _bombSprite;
                innerIcon.color = Color.white;
                innerIcon.enabled = true;
                img.transform.DOKill();
                img.transform.localScale = Vector3.one;
            }
            else
            {
                innerIcon.enabled = false;
                img.transform.DOKill();
                img.transform.localScale = Vector3.one;
            }
        }

        public void ShowGameOver(int score, int dailyBest, int allTimeHigh, int streak, bool isNewBest = false)
        {
            ClearTransientUi();
            _hudRoot.SetActive(false);

            string header = isNewBest ? "<color=#FACC15><b>NEW BEST!</b></color>\n" : string.Empty;
            _gameOverScore.text = header +
                                  $"Score: <size=120%>{FormatScore(score)}</size>\n\n" +
                                  $"Daily Best: {FormatScore(dailyBest)}\n" +
                                  $"All-time: {FormatScore(allTimeHigh)}\n" +
                                  $"Streak: {streak} {(streak == 1 ? "day" : "days")}";

            _gameOverRoot.SetActive(true);
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
                    if (rowView != null)
                    {
                        if (rowView.TitleText != null) rowView.TitleText.text = item.Title;
                        if (rowView.DescriptionText != null) rowView.DescriptionText.text = item.Description;
                        if (rowView.StatusIcon != null)
                        {
                            rowView.StatusIcon.sprite = item.Completed ? completedIcon : pendingIcon;
                            rowView.StatusIcon.color = item.Completed ? completedColor : pendingColor;
                        }
                        if (rowView.ProgressText != null)
                            rowView.ProgressText.text = $"{item.Progress} / {item.TargetValue}";

                        float ratio = item.TargetValue > 0 ? (float)item.Progress / item.TargetValue : 0f;

                        if (rowView.ProgressBarFill != null)
                        {
                            rowView.ProgressBarFill.fillAmount = 0f;
                            rowView.ProgressBarFill.DOFillAmount(ratio, 0.75f).SetEase(Ease.OutQuad).SetUpdate(true);
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
                                rowView.RewardText.text = $"+{item.RewardPoints} pts";
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
                        rewardText.text = $"+{item.RewardPoints} pts";
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

        public void RefreshGrid(TileKind[,] cells)
        {
            float cw = 720f / _settings.Columns;
            float cellH = 920f / _settings.Rows;

            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    SetCellColor(r, c, cells[r, c]);
                    _cellImages[r, c].rectTransform.localScale = Vector3.one;
                    _cellImages[r, c].rectTransform.anchoredPosition = new Vector2(c * cw + 4f, r * cellH + 4f);
                }
        }

        public void SetCellColor(int r, int c, TileKind k) => _cellImages[r, c].color = ColorFor(k);

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
            if (_colTapZones[col] != null) _colTapZones[col].FlashReject();
            Haptics.Light();
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
            var parent = (_mainCanvas != null ? _mainCanvas.transform : transform);
            go.transform.SetParent(parent, false);
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
            rt.anchorMin = new Vector2(0.5f, 0.6f);
            rt.anchorMax = new Vector2(0.5f, 0.6f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(400f, 80f);
            rt.anchoredPosition = new Vector2(0f, existingCount * 90f);

            // ── Main label ────────────────────────────────────────────────────
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = style == RewardToastStyle.PerfectClear ? 52 :
                           style == RewardToastStyle.Combo ? 58 : 46;
            txt.fontStyle = FontStyles.Bold;
            txt.color = accent;
            txt.alignment = TextAlignmentOptions.Center;
            txt.enableAutoSizing = false;
            txt.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            // ── Sub-label (points) ────────────────────────────────────────────
            if (!string.IsNullOrEmpty(sub))
            {
                var subGo = new GameObject("Sub");
                subGo.transform.SetParent(go.transform, false);
                var subTxt = subGo.AddComponent<TextMeshProUGUI>();
                subTxt.text = sub;
                subTxt.fontSize = 30;
                subTxt.color = new Color(accent.r, accent.g, accent.b, 0.85f);
                subTxt.alignment = TextAlignmentOptions.Center;
                subTxt.raycastTarget = false;
                var srt = subGo.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, -0.6f);
                srt.anchorMax = new Vector2(1f, -0.6f);
                srt.sizeDelta = new Vector2(0f, 40f);
            }

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
            foreach (var m in tiles)
            {
                var img = _cellImages[m.r, m.c];
                img.DOColor(Color.white, blinkTime * 0.5f).SetLoops(2, LoopType.Yoyo);
                // InQuad (not InExpo) so the tiles visibly shrink for the whole blink instead of holding still.
                img.rectTransform.DOScale(0f, blinkTime).SetEase(Ease.InQuad);
            }
            yield return new WaitForSeconds(blinkTime);
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
        }

        // Falls scale with distance but stay short: every frame spent here is a frame the board ignores taps.
        const float FallBaseSeconds = 0.08f;
        const float FallPerCellSeconds = 0.035f;
        const float FallMaxSeconds = 0.22f;

        public IEnumerator AnimateGravityCoroutine(int[,] fallDistances)
        {
            float longestFall = 0f;
            float cw = 720f / _settings.Columns;
            float cellH = 920f / _settings.Rows;

            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    int dist = fallDistances[r, c];
                    if (dist > 0)
                    {
                        float dur = Mathf.Min(FallBaseSeconds + FallPerCellSeconds * dist, FallMaxSeconds);
                        longestFall = Mathf.Max(longestFall, dur);
                        float startY = r * cellH + 4f;
                        float endY = (r - dist) * cellH + 4f;
                        var rt = _cellImages[r, c].rectTransform;
                        rt.DOKill(true); // finish any reject nudge first so it can't fight the fall
                        rt.anchoredPosition = new Vector2(c * cw + 4f, startY);
                        rt.DOAnchorPosY(endY, dur).SetEase(Ease.OutQuad);
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
                    rt.anchoredPosition = new Vector2(c * cw + 4f, r * cellH + 4f);
                }
        }

        public IEnumerator SlideGridUpCoroutine(float slideTime)
        {
            float cellH = 920f / _settings.Rows;
            _gridRoot.anchoredPosition = new Vector2(0, -cellH);
            _gridRoot.DOAnchorPosY(0f, slideTime).SetEase(Ease.OutBack);
            yield return new WaitForSeconds(slideTime);
            _gridRoot.anchoredPosition = Vector2.zero;
        }

        Color ColorFor(TileKind k) => k switch
        {
            TileKind.Empty => new Color(0.12f, 0.12f, 0.14f, 1f),
            TileKind.Red => new Color(0.95f, 0.35f, 0.35f),
            TileKind.Blue => new Color(0.35f, 0.55f, 0.95f),
            TileKind.Green => new Color(0.45f, 0.85f, 0.45f),
            TileKind.Yellow => new Color(1.0f, 0.9f, 0.3f),
            TileKind.Purple => new Color(0.75f, 0.45f, 1.0f),
            TileKind.Wild => Color.white,
            TileKind.Bomb => new Color(0.25f, 0.25f, 0.25f),
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
            maskImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            maskImg.raycastTarget = false; // only the column zones need to receive touches
            gridMaskGo.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = true;
            _gridMaskRt = maskRt;
            if (_shake != null) _shake.Target = maskRt;

            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(gridMaskGo.transform, false);
            _gridRoot = gridGo.AddComponent<RectTransform>();
            _gridRoot.anchorMin = Vector2.zero;
            _gridRoot.anchorMax = Vector2.one;
            _gridRoot.offsetMin = new Vector2(10, 10);
            _gridRoot.offsetMax = new Vector2(-10, -10);

            _cellImages = new Image[_settings.Rows, _settings.Columns];
            float cw = 720f / _settings.Columns;
            float cellH = 920f / _settings.Rows;
            for (int r = 0; r < _settings.Rows; r++)
                for (int c = 0; c < _settings.Columns; c++)
                {
                    var cell = new GameObject($"c_{r}_{c}");
                    cell.transform.SetParent(_gridRoot, false);
                    var rt = cell.AddComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(cw - 8f, cellH - 8f);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0);
                    rt.anchoredPosition = new Vector2(c * cw + 4f, r * cellH + 4f);
                    var img = cell.AddComponent<Image>();
                    img.color = ColorFor(TileKind.Empty);
                    img.raycastTarget = false; // 63 tiles under the column zones; skip them in every raycast
                    _cellImages[r, c] = img;
                }

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

                // Press flash: visible feedback the moment the finger lands.
                var flashGo = new GameObject("PressFlash");
                flashGo.transform.SetParent(colOverlay.transform, false);
                var flashRt = flashGo.AddComponent<RectTransform>();
                flashRt.anchorMin = Vector2.zero;
                flashRt.anchorMax = Vector2.one;
                flashRt.offsetMin = flashRt.offsetMax = Vector2.zero;
                var flashImg = flashGo.AddComponent<Image>();
                flashImg.color = new Color(1f, 1f, 1f, 0f);
                flashImg.raycastTarget = false;

                var zone = colOverlay.AddComponent<ColumnTapZone>();
                zone.Column = c;
                zone.Gate = btn;
                zone.Flash = flashImg;
                zone.OnTap = col => _onColumnClicked?.Invoke(col);
                zone.OnRejected = OnColumnTapRejected;

                _colOverlayBtns[c] = btn;   // store so tutorial can lock it
                _colTapZones[c] = zone;
            }

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

            StartCoroutine(WarmUpFontsCoroutine());

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
                rt.sizeDelta = new Vector2(680f, 320f);

                _tutorialDialogGo.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.98f);

                var outline = _tutorialDialogGo.AddComponent<Outline>();
                outline.effectColor = new Color(0.2f, 0.6f, 1.0f, 0.5f);
                outline.effectDistance = new Vector2(3f, 3f);

                var titleGo = new GameObject("Title");
                titleGo.transform.SetParent(_tutorialDialogGo.transform, false);
                var titleRt = titleGo.AddComponent<RectTransform>();
                titleRt.anchorMin = new Vector2(0f, 1f);
                titleRt.anchorMax = new Vector2(1f, 1f);
                titleRt.pivot = new Vector2(0.5f, 1f);
                titleRt.anchoredPosition = new Vector2(0f, -15f);
                titleRt.sizeDelta = new Vector2(-40f, 55f);
                _tutorialTitleText = titleGo.AddComponent<TextMeshProUGUI>();
                _tutorialTitleText.fontSize = 36;
                _tutorialTitleText.fontStyle = FontStyles.Bold;
                _tutorialTitleText.color = new Color(0.2f, 0.6f, 1.0f);
                _tutorialTitleText.alignment = TextAlignmentOptions.Left;

                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(_tutorialDialogGo.transform, false);
                var bodyRt = bodyGo.AddComponent<RectTransform>();
                bodyRt.anchorMin = new Vector2(0f, 1f);
                bodyRt.anchorMax = new Vector2(1f, 1f);
                bodyRt.pivot = new Vector2(0.5f, 1f);
                bodyRt.anchoredPosition = new Vector2(0f, -80f);
                bodyRt.sizeDelta = new Vector2(-40f, 150f);
                _tutorialBodyText = bodyGo.AddComponent<TextMeshProUGUI>();
                _tutorialBodyText.fontSize = 25;
                _tutorialBodyText.color = new Color(0.9f, 0.9f, 0.95f);
                _tutorialBodyText.alignment = TextAlignmentOptions.Left;
                _tutorialBodyText.overflowMode = TextOverflowModes.Ellipsis;

                var btnGo = new GameObject("NextBtn");
                btnGo.transform.SetParent(_tutorialDialogGo.transform, false);
                var btnRt = btnGo.AddComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(1f, 0f);
                btnRt.anchorMax = new Vector2(1f, 0f);
                btnRt.pivot = new Vector2(1f, 0f);
                btnRt.anchoredPosition = new Vector2(-20f, 20f);
                btnRt.sizeDelta = new Vector2(180f, 60f);
                btnGo.AddComponent<Image>().color = new Color(0.2f, 0.6f, 1.0f, 1.0f);
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
                btnTxt.fontSize = 26;
                btnTxt.fontStyle = FontStyles.Bold;
                btnTxt.color = Color.white;
                btnTxt.alignment = TextAlignmentOptions.Center;
            }

            _tutorialDialogGo.SetActive(true);
            _tutorialDialogGo.transform.SetAsLastSibling();
            _tutorialTitleText.text = title;
            _tutorialBodyText.text = body;
            _tutorialNextBtn.gameObject.SetActive(showNextButton);

            var bRt = _tutorialBodyText.GetComponent<RectTransform>();
            bRt.sizeDelta = showNextButton ? new Vector2(-40f, 150f) : new Vector2(-40f, 220f);

            _tutorialDialogGo.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            _tutorialDialogGo.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public void ShowTutorialChoiceDialog(string title, string body, Action onPlay, Action onSkip)
        {
            ShowTutorialDialog(title, body, false);

            var playBtnGo = new GameObject("PlayTutorialChoiceBtn");
            playBtnGo.transform.SetParent(_tutorialDialogGo.transform, false);
            var rtPlay = playBtnGo.AddComponent<RectTransform>();
            rtPlay.anchorMin = new Vector2(0.5f, 0f);
            rtPlay.anchorMax = new Vector2(0.5f, 0f);
            rtPlay.pivot = new Vector2(1f, 0f);
            rtPlay.anchoredPosition = new Vector2(-20f, 20f);
            rtPlay.sizeDelta = new Vector2(240f, 60f);
            playBtnGo.AddComponent<Image>().color = new Color(0.2f, 0.6f, 1.0f, 1.0f);
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
            txtPlay.fontSize = 24;
            txtPlay.fontStyle = FontStyles.Bold;
            txtPlay.color = Color.white;
            txtPlay.alignment = TextAlignmentOptions.Center;

            var skipBtnGo = new GameObject("SkipTutorialChoiceBtn");
            skipBtnGo.transform.SetParent(_tutorialDialogGo.transform, false);
            var rtSkip = skipBtnGo.AddComponent<RectTransform>();
            rtSkip.anchorMin = new Vector2(0.5f, 0f);
            rtSkip.anchorMax = new Vector2(0.5f, 0f);
            rtSkip.pivot = new Vector2(0f, 0f);
            rtSkip.anchoredPosition = new Vector2(20f, 20f);
            rtSkip.sizeDelta = new Vector2(240f, 60f);
            skipBtnGo.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.28f, 1.0f);
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
            txtSkip.fontSize = 24;
            txtSkip.fontStyle = FontStyles.Bold;
            txtSkip.color = Color.white;
            txtSkip.alignment = TextAlignmentOptions.Center;
        }

        public void HideTutorialDialog()
        {
            if (_tutorialDialogGo != null) _tutorialDialogGo.SetActive(false);
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
            panelGo.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.98f);

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
                new Color(0.25f, 0.25f, 0.28f, 1f), HideConfirmDialog);
            _confirmYesText = CreateDialogButton(panelGo.transform, "ConfirmBtn", new Vector2(170f, 40f),
                new Color(0.86f, 0.27f, 0.27f, 1f), () =>
                {
                    var onYes = _confirmOnYes;
                    HideConfirmDialog();
                    onYes?.Invoke();
                });

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
                        if (cellImg != null)
                        {
                            var col = cellImg.color;
                            col.a = 1.0f;
                            cellImg.color = col;
                        }
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
                        if (cellImg != null)
                        {
                            var col = cellImg.color;
                            col.a = 0.15f;
                            cellImg.color = col;
                        }
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
                    if (cellImg != null)
                    {
                        var col = cellImg.color;
                        col.a = 1.0f;
                        cellImg.color = col;
                    }
                }
            }
        }
    }

    /// <summary>Style hint for ShowRewardToast so it can pick the right colour and emphasis.</summary>
    public enum RewardToastStyle { Default, Combo, RowClear, PerfectClear, Carry }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    public enum NotificationType
    {
        Info,
        LeaderboardDrop,
        FriendActivity,
        Reminder
    }

    /// <summary>
    /// Programmatic in-app toast. Toasts are queued and shown one after another so a burst of
    /// events (e.g. friend accepted + rank changed) does not overwrite each other.
    /// While IsBusy returns true (e.g. mid-run) queued toasts wait. A toast with an onTap action
    /// is tappable; any other toast never blocks input.
    /// Must be called from the main thread.
    /// </summary>
    public class InAppNotificationView : MonoBehaviour
    {
        private struct Toast
        {
            public string Title;
            public string Message;
            public NotificationType Type;
            public Action OnTap;
        }

        private const float DisplaySeconds = 3.5f;
        private const float GapSeconds = 0.35f;
        private const float BusyPollSeconds = 0.25f;
        private const int MaxQueued = 5;

        // Layout, in reference units of the 1080x1920 canvas
        private const float MaxPanelWidth = 960f;
        private const float SideMargin = 24f;
        private const float PanelHeight = 172f;
        private const float ShownY = -24f;   // below the top of the safe area
        private const float HiddenY = 200f;  // above the top of the safe area
        private const float TitleFontSize = 34f;
        private const float BodyFontSize = 28f;
        private const float TitleHeight = 44f;
        private const float BodyHeight = 76f; // two lines of body text

        private static InAppNotificationView _instance;
        public static InAppNotificationView Instance => _instance;

        /// <summary>
        /// While this returns true, queued toasts are held back and shown once it returns false.
        /// A toast already on screen when it turns true is allowed to finish. Polled cheaply in unscaled time.
        /// </summary>
        public static Func<bool> IsBusy;

        private Canvas _toastCanvas;
        private RectTransform _safeRoot;
        private RectTransform _toastPanel;
        private Image _bgImage;
        private Image _iconImage;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _bodyText;
        private CanvasGroup _canvasGroup;

        private readonly List<Toast> _queue = new List<Toast>();
        private Coroutine _pumpCoroutine;
        private bool _skipCurrent;
        private bool _hasCurrent;
        private Toast _current;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUIProgrammatically();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Creates and builds the toast UI now (call during loading so the first toast doesn't hitch).</summary>
        public static void EnsureInstance()
        {
            if (_instance != null) return;
            // Awake registers the instance, marks it DontDestroyOnLoad and builds the UI.
            new GameObject("[InAppNotificationView]").AddComponent<InAppNotificationView>();
        }

        private void BuildUIProgrammatically()
        {
            GameObject canvasObj = new GameObject("InAppNotificationCanvas");
            canvasObj.transform.SetParent(transform);
            _toastCanvas = canvasObj.AddComponent<Canvas>();
            _toastCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _toastCanvas.sortingOrder = 999;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Safe-area root: anchors are recomputed from Screen.safeArea before every toast (notch / punch-hole).
            GameObject safeObj = new GameObject("SafeRoot", typeof(RectTransform));
            safeObj.transform.SetParent(canvasObj.transform, false);
            _safeRoot = (RectTransform)safeObj.transform;
            _safeRoot.anchorMin = Vector2.zero;
            _safeRoot.anchorMax = Vector2.one;
            _safeRoot.offsetMin = Vector2.zero;
            _safeRoot.offsetMax = Vector2.zero;

            GameObject panelObj = new GameObject("ToastPanel");
            panelObj.transform.SetParent(_safeRoot, false);

            _toastPanel = panelObj.AddComponent<RectTransform>();
            _toastPanel.anchorMin = new Vector2(0.5f, 1f);
            _toastPanel.anchorMax = new Vector2(0.5f, 1f);
            _toastPanel.pivot = new Vector2(0.5f, 1f);
            _toastPanel.sizeDelta = new Vector2(MaxPanelWidth, PanelHeight);
            _toastPanel.anchoredPosition = new Vector2(0, HiddenY);

            _canvasGroup = panelObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            _bgImage = panelObj.AddComponent<Image>();
            _bgImage.color = new Color(0.08f, 0.10f, 0.16f, 0.95f);
            UiSprites.ApplyRounded(_bgImage, 26f);

            HorizontalLayoutGroup layout = panelObj.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 18, 18);
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            GameObject iconObj = new GameObject("ToastIcon");
            iconObj.transform.SetParent(panelObj.transform, false);
            iconObj.AddComponent<RectTransform>();
            LayoutElement iconLayout = iconObj.AddComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = 70;
            iconLayout.minHeight = iconLayout.preferredHeight = 70;
            iconLayout.flexibleWidth = 0;
            _iconImage = iconObj.AddComponent<Image>();
            _iconImage.sprite = UiSprites.Circle;
            _iconImage.preserveAspect = true;
            _iconImage.color = new Color(0.3f, 0.6f, 1f, 1f);
            _iconImage.raycastTarget = false;

            GameObject textContainerObj = new GameObject("TextContainer");
            textContainerObj.transform.SetParent(panelObj.transform, false);
            textContainerObj.AddComponent<RectTransform>();
            LayoutElement textContainerLayout = textContainerObj.AddComponent<LayoutElement>();
            textContainerLayout.flexibleWidth = 1;
            textContainerLayout.minHeight = textContainerLayout.preferredHeight = TitleHeight + 4 + BodyHeight;

            VerticalLayoutGroup textLayout = textContainerObj.AddComponent<VerticalLayoutGroup>();
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.spacing = 4;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(textContainerObj.transform, false);
            _titleText = titleObj.AddComponent<TextMeshProUGUI>();
            _titleText.fontSize = TitleFontSize;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.color = Color.white;
            _titleText.textWrappingMode = TextWrappingModes.NoWrap;
            _titleText.overflowMode = TextOverflowModes.Ellipsis;
            _titleText.raycastTarget = false;
            LayoutElement titleLayout = titleObj.AddComponent<LayoutElement>();
            titleLayout.minHeight = titleLayout.preferredHeight = TitleHeight;

            GameObject bodyObj = new GameObject("BodyText");
            bodyObj.transform.SetParent(textContainerObj.transform, false);
            _bodyText = bodyObj.AddComponent<TextMeshProUGUI>();
            _bodyText.fontSize = BodyFontSize;
            _bodyText.color = new Color(0.85f, 0.88f, 0.95f, 1f);
            _bodyText.textWrappingMode = TextWrappingModes.Normal;
            _bodyText.overflowMode = TextOverflowModes.Ellipsis;
            _bodyText.enableAutoSizing = false;
            _bodyText.alignment = TextAlignmentOptions.TopLeft;
            _bodyText.raycastTarget = false;
            LayoutElement bodyLayout = bodyObj.AddComponent<LayoutElement>();
            bodyLayout.minHeight = bodyLayout.preferredHeight = BodyHeight;

            // Only reachable while the current toast has an action (blocksRaycasts follows OnTap).
            Button tapButton = panelObj.AddComponent<Button>();
            tapButton.targetGraphic = _bgImage;
            tapButton.transition = Selectable.Transition.None;
            tapButton.onClick.AddListener(OnToastTapped);
        }

        public static void Show(string title, string message, NotificationType type = NotificationType.Info, Action onTap = null)
        {
            EnsureInstance();

            // TMP has no emoji font: strip what would render as boxes.
            string cleanTitle = StripUnsupportedGlyphs(title);
            string cleanMessage = StripUnsupportedGlyphs(message);
            if (cleanTitle.Length == 0 && cleanMessage.Length == 0) return;

            _instance.Enqueue(new Toast { Title = cleanTitle, Message = cleanMessage, Type = type, OnTap = onTap });
        }

        private void Enqueue(Toast toast)
        {
            // Collapse duplicates: the same banner on screen or already waiting is not queued again.
            if (_hasCurrent && SameText(_current, toast)) return;
            for (int i = 0; i < _queue.Count; i++)
            {
                if (!SameText(_queue[i], toast)) continue;
                if (_queue[i].OnTap == null && toast.OnTap != null)
                {
                    Toast merged = _queue[i];
                    merged.OnTap = toast.OnTap;
                    _queue[i] = merged;
                }
                return;
            }

            if (_queue.Count >= MaxQueued)
            {
                Debug.Log("[InAppNotificationView] Toast queue full, dropping oldest.");
                _queue.RemoveAt(0);
            }
            _queue.Add(toast);

            if (_pumpCoroutine == null)
                _pumpCoroutine = StartCoroutine(PumpQueue());
        }

        private static bool SameText(Toast a, Toast b) => a.Title == b.Title && a.Message == b.Message;

        private static bool IsBusyNow()
        {
            if (IsBusy == null) return false;
            try { return IsBusy(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[InAppNotificationView] IsBusy threw: " + ex.Message);
                return false;
            }
        }

        private IEnumerator PumpQueue()
        {
            var busyPoll = new WaitForSecondsRealtime(BusyPollSeconds);

            while (_queue.Count > 0)
            {
                // Hold the queue while the game is busy (e.g. mid-run); flush once it is not.
                while (IsBusyNow()) yield return busyPoll;
                if (_queue.Count == 0) break;

                Toast toast = _queue[0];
                _queue.RemoveAt(0);
                _current = toast;
                _hasCurrent = true;

                ApplySafeAreaLayout();
                ApplyStyle(toast);

                _skipCurrent = false;
                // Toasts without an action never block input underneath them.
                _canvasGroup.blocksRaycasts = toast.OnTap != null;

                _toastPanel.DOKill();
                _canvasGroup.DOKill();
                _toastPanel.anchoredPosition = new Vector2(0, HiddenY);
                _canvasGroup.alpha = 0f;
                _toastPanel.DOAnchorPosY(ShownY, 0.45f).SetEase(Ease.OutBack).SetUpdate(true);
                _canvasGroup.DOFade(1f, 0.35f).SetUpdate(true);

                float elapsed = 0f;
                while (elapsed < DisplaySeconds && !_skipCurrent)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                _hasCurrent = false;
                _current = default;
                _canvasGroup.blocksRaycasts = false;
                _toastPanel.DOKill();
                _canvasGroup.DOKill();
                _toastPanel.DOAnchorPosY(HiddenY, 0.35f).SetEase(Ease.InBack).SetUpdate(true);
                _canvasGroup.DOFade(0f, 0.30f).SetUpdate(true);

                yield return new WaitForSecondsRealtime(GapSeconds);
            }

            _pumpCoroutine = null;
        }

        /// <summary>Fits the toast inside Screen.safeArea (re-read per toast: rotation, split screen, cutouts).</summary>
        private void ApplySafeAreaLayout()
        {
            int screenW = Screen.width;
            int screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0) return;

            Rect safe = Screen.safeArea;
            Vector2 min = new Vector2(safe.xMin / screenW, safe.yMin / screenH);
            Vector2 max = new Vector2(safe.xMax / screenW, safe.yMax / screenH);
            _safeRoot.anchorMin = min;
            _safeRoot.anchorMax = max;
            _safeRoot.offsetMin = Vector2.zero;
            _safeRoot.offsetMax = Vector2.zero;

            float canvasWidth = ((RectTransform)_toastCanvas.transform).rect.width;
            float safeWidth = canvasWidth > 0f ? canvasWidth * (max.x - min.x) : MaxPanelWidth + 2 * SideMargin;
            float panelWidth = Mathf.Clamp(safeWidth - 2 * SideMargin, 300f, MaxPanelWidth);
            _toastPanel.sizeDelta = new Vector2(panelWidth, PanelHeight);
        }

        private void ApplyStyle(Toast toast)
        {
            _titleText.text = toast.Title;
            _bodyText.text = toast.Message;

            switch (toast.Type)
            {
                case NotificationType.LeaderboardDrop:
                    _bgImage.color = new Color(0.20f, 0.08f, 0.12f, 0.95f);
                    _iconImage.color = new Color(1.0f, 0.35f, 0.35f, 1f);
                    break;
                case NotificationType.FriendActivity:
                    _bgImage.color = new Color(0.08f, 0.18f, 0.12f, 0.95f);
                    _iconImage.color = new Color(0.35f, 0.95f, 0.55f, 1f);
                    break;
                case NotificationType.Reminder:
                    _bgImage.color = new Color(0.16f, 0.12f, 0.22f, 0.95f);
                    _iconImage.color = new Color(0.80f, 0.45f, 1.00f, 1f);
                    break;
                default:
                    _bgImage.color = new Color(0.08f, 0.10f, 0.16f, 0.95f);
                    _iconImage.color = new Color(0.35f, 0.65f, 1.00f, 1f);
                    break;
            }
        }

        private void OnToastTapped()
        {
            if (!_hasCurrent) return;

            Action onTap = _current.OnTap;
            _current.OnTap = null; // one tap, one action
            _skipCurrent = true;

            if (onTap == null) return;
            try { onTap(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>
        /// Removes characters the TMP fonts can't draw (no emoji fallback): surrogate pairs (emoji and other
        /// astral symbols), variation selectors, zero-width joiners, the keycap mark and the misc-symbol /
        /// dingbat blocks. Then trims and collapses the spaces they leave behind.
        /// </summary>
        public static string StripUnsupportedGlyphs(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder(text.Length);
            bool lastWasSpace = false;
            foreach (char c in text)
            {
                if (char.IsSurrogate(c)) continue;
                if (c == '\uFE0F' || c == '\uFE0E' || c == '\u200D' || c == '\u20E3') continue;
                if (c >= '\u2600' && c <= '\u27BF') continue; // misc symbols + dingbats (sun, check mark, heart, zap...)
                if (c >= '\u2B00' && c <= '\u2BFF') continue; // misc symbols and arrows (star, heavy circle...)

                bool isSpace = c == ' ';
                if (isSpace && (lastWasSpace || sb.Length == 0)) continue;
                sb.Append(c);
                lastWasSpace = isSpace;
            }
            return sb.ToString().Trim();
        }
    }
}

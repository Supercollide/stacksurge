using System.Collections;
using System.Collections.Generic;
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
    /// Must be called from the main thread.
    /// </summary>
    public class InAppNotificationView : MonoBehaviour
    {
        private struct Toast
        {
            public string Title;
            public string Message;
            public NotificationType Type;
        }

        private const float DisplaySeconds = 4.0f;
        private const float GapSeconds = 0.35f;
        private const int MaxQueued = 5;

        private static InAppNotificationView _instance;
        public static InAppNotificationView Instance => _instance;

        private Canvas _toastCanvas;
        private RectTransform _toastPanel;
        private Image _bgImage;
        private Image _iconImage;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _bodyText;
        private CanvasGroup _canvasGroup;

        private readonly Queue<Toast> _queue = new Queue<Toast>();
        private Coroutine _pumpCoroutine;
        private bool _skipCurrent;

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

            GameObject panelObj = new GameObject("ToastPanel");
            panelObj.transform.SetParent(canvasObj.transform, false);

            _toastPanel = panelObj.AddComponent<RectTransform>();
            _toastPanel.anchorMin = new Vector2(0.5f, 1f);
            _toastPanel.anchorMax = new Vector2(0.5f, 1f);
            _toastPanel.pivot = new Vector2(0.5f, 1f);
            _toastPanel.sizeDelta = new Vector2(960, 175);
            _toastPanel.anchoredPosition = new Vector2(0, 200);

            _canvasGroup = panelObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            _bgImage = panelObj.AddComponent<Image>();
            _bgImage.color = new Color(0.08f, 0.10f, 0.16f, 0.95f);

            Outline outline = panelObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.45f, 0.95f, 0.8f);
            outline.effectDistance = new Vector2(2, -2);

            HorizontalLayoutGroup layout = panelObj.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 16, 16);
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            GameObject iconObj = new GameObject("ToastIcon");
            iconObj.transform.SetParent(panelObj.transform, false);
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(70, 70);
            _iconImage = iconObj.AddComponent<Image>();
            _iconImage.color = new Color(0.3f, 0.6f, 1f, 1f);

            GameObject textContainerObj = new GameObject("TextContainer");
            textContainerObj.transform.SetParent(panelObj.transform, false);
            RectTransform textContainerRect = textContainerObj.AddComponent<RectTransform>();
            textContainerRect.sizeDelta = new Vector2(790, 140);

            VerticalLayoutGroup textLayout = textContainerObj.AddComponent<VerticalLayoutGroup>();
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.spacing = 2;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandHeight = false;

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(textContainerObj.transform, false);
            _titleText = titleObj.AddComponent<TextMeshProUGUI>();
            _titleText.fontSize = 28;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.color = Color.white;

            GameObject bodyObj = new GameObject("BodyText");
            bodyObj.transform.SetParent(textContainerObj.transform, false);
            _bodyText = bodyObj.AddComponent<TextMeshProUGUI>();
            _bodyText.fontSize = 21;
            _bodyText.color = new Color(0.85f, 0.88f, 0.95f, 1f);
            _bodyText.textWrappingMode = TextWrappingModes.Normal;
            _bodyText.overflowMode = TextOverflowModes.Truncate;
            _bodyText.enableAutoSizing = false;
            RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
            if (bodyRect != null) bodyRect.sizeDelta = new Vector2(790, 90);

            Button dismissBtn = panelObj.AddComponent<Button>();
            dismissBtn.onClick.AddListener(SkipCurrent);
        }

        public static void Show(string title, string message, NotificationType type = NotificationType.Info)
        {
            if (_instance == null)
            {
                GameObject managerObj = new GameObject("[InAppNotificationView]");
                _instance = managerObj.AddComponent<InAppNotificationView>();
            }

            _instance.Enqueue(new Toast { Title = title, Message = message, Type = type });
        }

        private void Enqueue(Toast toast)
        {
            if (_queue.Count >= MaxQueued)
            {
                Debug.Log("[InAppNotificationView] Toast queue full, dropping oldest.");
                _queue.Dequeue();
            }
            _queue.Enqueue(toast);

            if (_pumpCoroutine == null)
                _pumpCoroutine = StartCoroutine(PumpQueue());
        }

        private IEnumerator PumpQueue()
        {
            while (_queue.Count > 0)
            {
                Toast toast = _queue.Dequeue();
                ApplyStyle(toast);

                _skipCurrent = false;
                _canvasGroup.blocksRaycasts = true;

                _toastPanel.DOKill();
                _canvasGroup.DOKill();
                _toastPanel.anchoredPosition = new Vector2(0, 180);
                _canvasGroup.alpha = 0f;
                _toastPanel.DOAnchorPosY(-40, 0.45f).SetEase(Ease.OutBack).SetUpdate(true);
                _canvasGroup.DOFade(1f, 0.35f).SetUpdate(true);

                float elapsed = 0f;
                while (elapsed < DisplaySeconds && !_skipCurrent)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                _canvasGroup.blocksRaycasts = false;
                _toastPanel.DOKill();
                _canvasGroup.DOKill();
                _toastPanel.DOAnchorPosY(200, 0.35f).SetEase(Ease.InBack).SetUpdate(true);
                _canvasGroup.DOFade(0f, 0.30f).SetUpdate(true);

                yield return new WaitForSecondsRealtime(GapSeconds);
            }

            _pumpCoroutine = null;
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

        private void SkipCurrent()
        {
            _skipCurrent = true;
        }
    }
}

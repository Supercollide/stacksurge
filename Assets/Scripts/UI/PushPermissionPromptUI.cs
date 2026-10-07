using System;
using System.Globalization;
using System.Threading.Tasks;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// In-game "turn on notifications" prompt, shown at a calm moment after the player has finished a run.
    /// Only "Turn on" triggers the OS permission dialog, so "Not now" never uses up the system prompt.
    /// Asked at most twice, at least 3 days apart, and never on top of the analytics consent prompt.
    /// Built in code, in the same style as NotificationSettingsUI.
    /// </summary>
    public class PushPermissionPromptUI : MonoBehaviour
    {
        public const int MaxAsks = 2;
        public static readonly TimeSpan MinTimeBetweenAsks = TimeSpan.FromDays(3);

        /// <summary>
        /// The Editor can't read push permission, so ShowIfEligible never shows the prompt there.
        /// Set this to true to preview it in the Editor (treated as "permission off"). Ignored on devices.
        /// </summary>
        public static bool EditorPreview = false;

        private static PushPermissionPromptUI _current;

        /// <summary>True while the prompt is on screen (false again as soon as it starts closing).</summary>
        public static bool IsOpen => _current != null && _current._isOpen;

        private Action _onClosed;
        private bool _isOpen;
        private bool _closing;

        private GameObject _overlayObj;
        private RectTransform _dialogPanel;
        private CanvasGroup _canvasGroup;

        // ── Public API ──────────────────────────────────────────────────────
        /// <summary>
        /// Shows the prompt if the player is eligible and records the ask. Returns true if shown; onClosed is
        /// then invoked once when it closes. Returns false (and never invokes onClosed) otherwise.
        /// </summary>
        public static bool ShowIfEligible(SaveData save, Action onClosed)
        {
            if (!IsEligible(save)) return false;

            save.PushAskCount++;
            save.LastPushAskUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            LocalProgress.Save(save);

            var go = new GameObject("[PushPermissionPromptUI]");
            _current = go.AddComponent<PushPermissionPromptUI>();
            _current.Open(onClosed);
            return true;
        }

        /// <summary>Closes the prompt as if the player tapped "Not now" (e.g. Android Back).</summary>
        public static void Dismiss()
        {
            if (_current != null) _current.Close();
        }

        /// <summary>
        /// Push permission is off on this device, the analytics prompt is not pending, fewer than 2 asks so far,
        /// the last one at least 3 days ago, and the player has completed a run.
        /// </summary>
        public static bool IsEligible(SaveData save)
        {
            if (save == null || _current != null) return false;
            if (AnalyticsConsent.NeedsPrompt(save)) return false;
            if (save.PushAskCount >= MaxAsks) return false;
            if (LocalProgress.RecordedRunCount(save) <= 0 && save.AllTimeHigh <= 0) return false;
            if (WasAskedRecently(save)) return false;
            return IsPushPermissionOff();
        }

        private static bool WasAskedRecently(SaveData save)
        {
            if (string.IsNullOrEmpty(save.LastPushAskUtc)) return false;
            if (!DateTime.TryParse(save.LastPushAskUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime lastAsk))
                return false;
            return DateTime.UtcNow - lastAsk.ToUniversalTime() < MinTimeBetweenAsks;
        }

        private static bool IsPushPermissionOff()
        {
#if UNITY_EDITOR
            return EditorPreview;
#else
            var service = NotificationService.Instance;
            return service != null && service.TryGetPushPermission(out bool granted) && !granted;
#endif
        }

        // ── Build ───────────────────────────────────────────────────────────
        private void Open(Action onClosed)
        {
            _onClosed = onClosed;
            BuildUIProgrammatically();

            _isOpen = true;
            _overlayObj.SetActive(true);
            _canvasGroup.alpha = 0f;
            _dialogPanel.localScale = Vector3.one * 0.8f;
            _canvasGroup.DOFade(1f, 0.25f).SetUpdate(true);
            _dialogPanel.DOScale(1f, 0.30f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        private void BuildUIProgrammatically()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 970; // above settings (950) and the consent prompt (960), below toasts (999)
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Dark overlay that blocks the game until the player chooses
            _overlayObj = new GameObject("PushPermissionOverlay");
            _overlayObj.transform.SetParent(transform, false);
            RectTransform overlayRect = _overlayObj.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            Image overlayBg = _overlayObj.AddComponent<Image>();
            overlayBg.color = new Color(0f, 0f, 0f, 0.75f);
            _canvasGroup = _overlayObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            // Dialog
            GameObject dialogObj = new GameObject("DialogPanel");
            dialogObj.transform.SetParent(_overlayObj.transform, false);
            _dialogPanel = dialogObj.AddComponent<RectTransform>();
            _dialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _dialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _dialogPanel.sizeDelta = new Vector2(850, 600);
            _dialogPanel.localScale = Vector3.one * 0.8f;
            Image dialogBg = dialogObj.AddComponent<Image>();
            dialogBg.color = new Color(0.10f, 0.12f, 0.18f, 0.98f);
            Outline dialogBorder = dialogObj.AddComponent<Outline>();
            dialogBorder.effectColor = new Color(0.25f, 0.40f, 0.85f, 0.6f);
            dialogBorder.effectDistance = new Vector2(3, -3);

            GameObject layoutObj = new GameObject("ContentLayout");
            layoutObj.transform.SetParent(dialogObj.transform, false);
            RectTransform layoutRect = layoutObj.AddComponent<RectTransform>();
            layoutRect.anchorMin = Vector2.zero;
            layoutRect.anchorMax = Vector2.one;
            layoutRect.sizeDelta = new Vector2(-60, -60);
            VerticalLayoutGroup vlayout = layoutObj.AddComponent<VerticalLayoutGroup>();
            vlayout.padding = new RectOffset(20, 20, 30, 20);
            vlayout.spacing = 28;
            vlayout.childAlignment = TextAnchor.UpperCenter;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = false;
            vlayout.childForceExpandHeight = false;

            AddText(layoutObj.transform, "Title", "Don't lose your streak", 40, FontStyles.Bold, Color.white, 60);
            AddText(layoutObj.transform, "Body",
                "We'll remind you before it resets and tell you when a friend beats your score.",
                26, FontStyles.Normal, new Color(0.80f, 0.84f, 0.92f), 110);

            AddButton(layoutObj.transform, "TurnOnButton", "Turn on", new Color(0.20f, 0.45f, 0.90f, 1f), OnTurnOn);
            AddButton(layoutObj.transform, "NotNowButton", "Not now", new Color(0.22f, 0.26f, 0.36f, 1f), Close);

            _overlayObj.SetActive(false);
        }

        private static void AddText(Transform parent, string name, string text, float size, FontStyles style, Color color, float height)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, height);
            TextMeshProUGUI t = obj.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
        }

        private static void AddButton(Transform parent, string name, string label, Color color, Action onClick)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(0, 90);
            Image bg = btnObj.AddComponent<Image>();
            bg.color = color;
            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform txtRect = txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.sizeDelta = Vector2.zero;
            TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 30;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
        }

        // ── Actions ─────────────────────────────────────────────────────────
        private void OnTurnOn()
        {
            if (_closing) return;

            var service = NotificationService.Instance;
            // Normally just the OS dialog (no OneSignal "go to Settings" fallback). If the OS will no longer
            // show it (earlier denials), the explicit tap lets OneSignal offer the app's settings instead.
            bool fallbackToSettings = service != null && !service.CanRequestPushPermission();

            // Close first so the system dialog appears over the game, not over this prompt.
            Close();
            if (service != null) _ = RequestPermissionAsync(service, fallbackToSettings);
        }

        private static async Task RequestPermissionAsync(NotificationService service, bool fallbackToSettings)
        {
            try
            {
                await service.RequestPushPermissionAsync(fallbackToSettings);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PushPermissionPromptUI] Permission request failed: " + ex.Message);
            }
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            _isOpen = false;

            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.DOKill();
            _dialogPanel.DOKill();
            _dialogPanel.DOScale(0.85f, 0.20f).SetUpdate(true);
            _canvasGroup.DOFade(0f, 0.20f).SetUpdate(true).OnComplete(Finish);
        }

        private void Finish()
        {
            Action onClosed = _onClosed;
            _onClosed = null;
            if (_current == this) _current = null;
            Destroy(gameObject);

            try { onClosed?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
            if (_canvasGroup != null) _canvasGroup.DOKill();
            if (_dialogPanel != null) _dialogPanel.DOKill();
        }
    }
}

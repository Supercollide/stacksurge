using System;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// First-launch prompt asking whether Stack Surge may collect usage data with Unity Analytics.
    /// Built in code, in the same style as NotificationSettingsUI.
    /// </summary>
    public class AnalyticsConsentUI : MonoBehaviour
    {
        private SaveData _save;
        private Action _onDecided;

        private GameObject _overlayObj;
        private RectTransform _dialogPanel;
        private CanvasGroup _canvasGroup;

        /// <param name="onDecided">Called after the player taps Allow or No thanks.</param>
        public void Initialize(SaveData save, Action onDecided = null)
        {
            _save = save;
            _onDecided = onDecided;
            BuildUIProgrammatically();
        }

        /// <summary>Shows the prompt if the player hasn't answered it yet.</summary>
        public void ShowIfNeeded()
        {
            if (AnalyticsConsent.NeedsPrompt(_save)) Show();
        }

        private void BuildUIProgrammatically()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 960; // above the settings dialog (950)
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Dark overlay that blocks the game until the player chooses
            _overlayObj = new GameObject("AnalyticsConsentOverlay");
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
            _dialogPanel.sizeDelta = new Vector2(850, 760);
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

            AddText(layoutObj.transform, "Title", "Help us improve Stack Surge?", 38, FontStyles.Bold, Color.white, 60);
            AddText(layoutObj.transform, "Body",
                "Can we collect usage data, like how long you play, your device model and your country, " +
                "to fix bugs and make the game better? We never use it for ads.\n\n" +
                "You can change this anytime in Settings.",
                24, FontStyles.Normal, new Color(0.80f, 0.84f, 0.92f), 260);

            AddButton(layoutObj.transform, "AllowButton", "Allow", new Color(0.20f, 0.45f, 0.90f, 1f), () => Decide(true));
            AddButton(layoutObj.transform, "DeclineButton", "No thanks", new Color(0.22f, 0.26f, 0.36f, 1f), () => Decide(false));

            // Privacy policy link
            GameObject linkObj = new GameObject("PrivacyLink");
            linkObj.transform.SetParent(layoutObj.transform, false);
            RectTransform linkRect = linkObj.AddComponent<RectTransform>();
            linkRect.sizeDelta = new Vector2(0, 50);
            Button linkBtn = linkObj.AddComponent<Button>();
            linkBtn.onClick.AddListener(() => Application.OpenURL(AnalyticsConsent.PrivacyPolicyUrl));
            TextMeshProUGUI linkText = linkObj.AddComponent<TextMeshProUGUI>();
            linkText.text = "<u>Privacy policy</u>";
            linkText.fontSize = 22;
            linkText.alignment = TextAlignmentOptions.Center;
            linkText.color = new Color(0.55f, 0.70f, 1f);
            linkBtn.targetGraphic = linkText;

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
        }

        private static void AddButton(Transform parent, string name, string label, Color color, Action onClick)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(0, 80);
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
            txt.fontSize = 28;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
        }

        private void Decide(bool allowed)
        {
            AnalyticsConsent.SetConsent(_save, allowed);
            Hide();
            _onDecided?.Invoke();
        }

        public void Show()
        {
            _overlayObj.SetActive(true);
            _canvasGroup.DOKill();
            _dialogPanel.DOKill();
            _canvasGroup.alpha = 0f;
            _dialogPanel.localScale = Vector3.one * 0.8f;
            _canvasGroup.DOFade(1f, 0.25f);
            _dialogPanel.DOScale(1f, 0.30f).SetEase(Ease.OutBack);
        }

        private void Hide()
        {
            _canvasGroup.DOKill();
            _dialogPanel.DOKill();
            _dialogPanel.DOScale(0.85f, 0.20f);
            _canvasGroup.DOFade(0f, 0.20f).OnComplete(() => _overlayObj.SetActive(false));
        }
    }
}

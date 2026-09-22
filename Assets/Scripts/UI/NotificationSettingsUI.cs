using System;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    public class NotificationSettingsUI : MonoBehaviour
    {
        private SaveData _save;
        private Action _onSettingsChanged;
        private Action _onSocialSettingsChanged;

        private GameObject _overlayObj;
        private RectTransform _dialogPanel;
        private CanvasGroup _canvasGroup;

        private Toggle _leaderboardToggle;
        private Toggle _friendsToggle;
        private Toggle _remindersToggle;
        private Toggle _allowRequestsToggle;
        private Button _closeButton;

        /// <param name="onSettingsChanged">Called after any notification toggle changes (reschedule reminders, sync tags).</param>
        /// <param name="onSocialSettingsChanged">Called after a social toggle changes (publish public prefs).</param>
        public void Initialize(SaveData save, Action onSettingsChanged = null, Action onSocialSettingsChanged = null)
        {
            _save = save;
            _onSettingsChanged = onSettingsChanged;
            _onSocialSettingsChanged = onSocialSettingsChanged;

            BuildUIProgrammatically();
            UpdateToggleStates();
        }

        private void BuildUIProgrammatically()
        {
            // Canvas overlay
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                parentCanvas = gameObject.AddComponent<Canvas>();
                parentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                parentCanvas.sortingOrder = 950;
                gameObject.AddComponent<CanvasScaler>();
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // Overlay Dark Background
            _overlayObj = new GameObject("NotificationSettingsOverlay");
            _overlayObj.transform.SetParent(transform, false);
            RectTransform overlayRect = _overlayObj.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;

            Image overlayBg = _overlayObj.AddComponent<Image>();
            overlayBg.color = new Color(0f, 0f, 0f, 0.65f);

            _canvasGroup = _overlayObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            // Modal Dialog Box
            GameObject dialogObj = new GameObject("DialogPanel");
            dialogObj.transform.SetParent(_overlayObj.transform, false);
            _dialogPanel = dialogObj.AddComponent<RectTransform>();
            _dialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _dialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _dialogPanel.sizeDelta = new Vector2(850, 860);
            _dialogPanel.localScale = Vector3.one * 0.8f;

            Image dialogBg = dialogObj.AddComponent<Image>();
            dialogBg.color = new Color(0.10f, 0.12f, 0.18f, 0.98f);

            Outline dialogBorder = dialogObj.AddComponent<Outline>();
            dialogBorder.effectColor = new Color(0.25f, 0.40f, 0.85f, 0.6f);
            dialogBorder.effectDistance = new Vector2(3, -3);

            // Vertical Layout Container
            GameObject layoutObj = new GameObject("ContentLayout");
            layoutObj.transform.SetParent(dialogObj.transform, false);
            RectTransform layoutRect = layoutObj.AddComponent<RectTransform>();
            layoutRect.anchorMin = Vector2.zero;
            layoutRect.anchorMax = Vector2.one;
            layoutRect.sizeDelta = new Vector2(-60, -60);

            VerticalLayoutGroup vlayout = layoutObj.AddComponent<VerticalLayoutGroup>();
            vlayout.padding = new RectOffset(20, 20, 20, 20);
            vlayout.spacing = 25;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = false;

            // Header Title
            GameObject titleObj = new GameObject("HeaderTitle");
            titleObj.transform.SetParent(layoutObj.transform, false);
            TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.text = "Notification Settings";
            titleText.fontSize = 38;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;

            // Description
            GameObject descObj = new GameObject("Description");
            descObj.transform.SetParent(layoutObj.transform, false);
            TextMeshProUGUI descText = descObj.AddComponent<TextMeshProUGUI>();
            descText.text = "Customize which alerts you receive and who can add you:";
            descText.fontSize = 22;
            descText.alignment = TextAlignmentOptions.Center;
            descText.color = new Color(0.75f, 0.80f, 0.90f);

            // Toggles
            _leaderboardToggle = CreateToggleRow(layoutObj.transform, "Leaderboard Position Drops", "Alert when someone overtakes your high score rank");
            _friendsToggle = CreateToggleRow(layoutObj.transform, "Friend Activity", "Alert on new friend requests and accepted requests");
            _remindersToggle = CreateToggleRow(layoutObj.transform, "Streak & Game Reminders", "Daily streak protection and leaderboard reset alerts");
            _allowRequestsToggle = CreateToggleRow(layoutObj.transform, "Allow Friend Requests", "Let other players send you friend requests from the leaderboard");

            // Close Button
            GameObject btnObj = new GameObject("CloseButton");
            btnObj.transform.SetParent(layoutObj.transform, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(300, 70);
            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = new Color(0.20f, 0.45f, 0.90f, 1f);

            _closeButton = btnObj.AddComponent<Button>();
            _closeButton.onClick.AddListener(Hide);

            GameObject btnTextObj = new GameObject("Text");
            btnTextObj.transform.SetParent(btnObj.transform, false);
            RectTransform btnTextRect = btnTextObj.AddComponent<RectTransform>();
            btnTextRect.anchorMin = Vector2.zero;
            btnTextRect.anchorMax = Vector2.one;
            TextMeshProUGUI btnText = btnTextObj.AddComponent<TextMeshProUGUI>();
            btnText.text = "Close";
            btnText.fontSize = 26;
            btnText.fontStyle = FontStyles.Bold;
            btnText.alignment = TextAlignmentOptions.Center;
            btnText.color = Color.white;

            // Wire toggle events
            _leaderboardToggle.onValueChanged.AddListener(OnLeaderboardToggleChanged);
            _friendsToggle.onValueChanged.AddListener(OnFriendsToggleChanged);
            _remindersToggle.onValueChanged.AddListener(OnRemindersToggleChanged);
            _allowRequestsToggle.onValueChanged.AddListener(OnAllowRequestsToggleChanged);

            _overlayObj.SetActive(false);
        }

        private Toggle CreateToggleRow(Transform parent, string titleText, string subtitleText)
        {
            GameObject row = new GameObject($"ToggleRow_{titleText}");
            row.transform.SetParent(parent, false);

            HorizontalLayoutGroup hlayout = row.AddComponent<HorizontalLayoutGroup>();
            hlayout.spacing = 20;
            hlayout.childAlignment = TextAnchor.MiddleLeft;
            hlayout.childControlWidth = false;
            hlayout.childControlHeight = false;

            // Left Text Stack
            GameObject textCol = new GameObject("TextCol");
            textCol.transform.SetParent(row.transform, false);
            RectTransform textColRect = textCol.AddComponent<RectTransform>();
            textColRect.sizeDelta = new Vector2(580, 75);

            VerticalLayoutGroup vlayout = textCol.AddComponent<VerticalLayoutGroup>();
            vlayout.spacing = 4;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = false;

            GameObject tObj = new GameObject("Title");
            tObj.transform.SetParent(textCol.transform, false);
            TextMeshProUGUI t = tObj.AddComponent<TextMeshProUGUI>();
            t.text = titleText;
            t.fontSize = 24;
            t.fontStyle = FontStyles.Bold;
            t.color = Color.white;

            GameObject subObj = new GameObject("Subtitle");
            subObj.transform.SetParent(textCol.transform, false);
            TextMeshProUGUI sub = subObj.AddComponent<TextMeshProUGUI>();
            sub.text = subtitleText;
            sub.fontSize = 18;
            sub.color = new Color(0.70f, 0.75f, 0.85f);

            // Right Toggle graphic
            GameObject toggleObj = new GameObject("Toggle");
            toggleObj.transform.SetParent(row.transform, false);
            RectTransform toggleRect = toggleObj.AddComponent<RectTransform>();
            toggleRect.sizeDelta = new Vector2(100, 50);

            Image bg = toggleObj.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.25f, 0.35f, 1f);

            GameObject checkmarkObj = new GameObject("Checkmark");
            checkmarkObj.transform.SetParent(toggleObj.transform, false);
            RectTransform checkRect = checkmarkObj.AddComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(40, 40);

            Image checkImg = checkmarkObj.AddComponent<Image>();
            checkImg.color = new Color(0.3f, 0.85f, 0.45f, 1f);

            Toggle toggle = toggleObj.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = checkImg;

            return toggle;
        }

        private void UpdateToggleStates()
        {
            if (_save == null) return;
            _leaderboardToggle.SetIsOnWithoutNotify(_save.NotifyLeaderboardDrops);
            _friendsToggle.SetIsOnWithoutNotify(_save.NotifyFriendActivity);
            _remindersToggle.SetIsOnWithoutNotify(_save.NotifyReminders);
            _allowRequestsToggle.SetIsOnWithoutNotify(_save.AllowFriendRequests);
        }

        private void OnLeaderboardToggleChanged(bool val)
        {
            if (_save != null) _save.NotifyLeaderboardDrops = val;
            LocalProgress.Save(_save);
            _onSettingsChanged?.Invoke();
        }

        private void OnFriendsToggleChanged(bool val)
        {
            if (_save != null) _save.NotifyFriendActivity = val;
            LocalProgress.Save(_save);
            _onSettingsChanged?.Invoke();
            // Friend-activity opt-out is public so other clients skip pushing to us.
            _onSocialSettingsChanged?.Invoke();
        }

        private void OnAllowRequestsToggleChanged(bool val)
        {
            if (_save != null) _save.AllowFriendRequests = val;
            LocalProgress.Save(_save);
            _onSocialSettingsChanged?.Invoke();
        }

        private void OnRemindersToggleChanged(bool val)
        {
            if (_save != null) _save.NotifyReminders = val;
            LocalProgress.Save(_save);
            _onSettingsChanged?.Invoke();
        }

        public void Show()
        {
            UpdateToggleStates();
            _overlayObj.SetActive(true);
            _canvasGroup.DOKill();
            _dialogPanel.DOKill();

            _canvasGroup.alpha = 0f;
            _dialogPanel.localScale = Vector3.one * 0.8f;

            _canvasGroup.DOFade(1f, 0.25f);
            _dialogPanel.DOScale(1f, 0.30f).SetEase(Ease.OutBack);
        }

        public void Hide()
        {
            _canvasGroup.DOKill();
            _dialogPanel.DOKill();

            _dialogPanel.DOScale(0.85f, 0.20f);
            _canvasGroup.DOFade(0f, 0.20f).OnComplete(() =>
            {
                _overlayObj.SetActive(false);
            });
        }
    }
}

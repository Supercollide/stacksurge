using System;
using System.Collections.Generic;
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
        private RectTransform _overlayRect;
        private RectTransform _dialogPanel;
        private CanvasGroup _canvasGroup;

        private Slider _sfxVolumeSlider;
        private TextMeshProUGUI _sfxVolumeLabel;
        private Toggle _vibrationToggle;
        private Toggle _shapesToggle;
        private Toggle _leaderboardToggle;
        private Toggle _friendsToggle;
        private Toggle _remindersToggle;
        private Toggle _allowRequestsToggle;
        private Toggle _analyticsToggle;
        private Button _closeButton;

        private GameObject _permissionBanner;
        private Button _enableButton;
        private NotificationService _permissionSource;

        /// <summary>Visual parts of one ON/OFF pill switch.</summary>
        private class PillSwitch
        {
            public Toggle Toggle;
            public Image Track;
            public RectTransform Knob;
            public TextMeshProUGUI Label;
        }

        private readonly List<PillSwitch> _pills = new List<PillSwitch>();

        // Layout, in reference units of the 1080x1920 canvas
        private const float DialogWidth = 880f;
        private const float DialogPadding = 36f;
        private const float SectionSpacing = 20f;
        private const float HeaderHeight = 58f;
        private const float DescriptionHeight = 76f;
        private const float BannerHeight = 150f;
        private const float RowHeight = 124f;
        private const float RowSpacing = 12f;
        private const float CloseButtonHeight = 92f;
        private const float MinScrollHeight = 220f;
        private const float ScreenMargin = 40f;
        private const int RowCount = 8;

        private static readonly Color PillOnColor = new Color(0.20f, 0.72f, 0.42f, 1f);
        private static readonly Color PillOffColor = new Color(0.26f, 0.30f, 0.40f, 1f);

        /// <summary>True from Show() until Hide() starts closing the dialog.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Raised once each time the dialog finishes closing (close button or Hide()).</summary>
        public event Action Closed;

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
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // Overlay Dark Background
            _overlayObj = new GameObject("NotificationSettingsOverlay");
            _overlayObj.transform.SetParent(transform, false);
            _overlayRect = _overlayObj.AddComponent<RectTransform>();
            _overlayRect.anchorMin = Vector2.zero;
            _overlayRect.anchorMax = Vector2.one;
            _overlayRect.sizeDelta = Vector2.zero;

            Image overlayBg = _overlayObj.AddComponent<Image>();
            overlayBg.color = new Color(0f, 0f, 0f, 0.65f);

            _canvasGroup = _overlayObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            // Modal Dialog Box (height is fitted to the screen in ResizeDialog)
            GameObject dialogObj = new GameObject("DialogPanel");
            dialogObj.transform.SetParent(_overlayObj.transform, false);
            _dialogPanel = dialogObj.AddComponent<RectTransform>();
            _dialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _dialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _dialogPanel.sizeDelta = new Vector2(DialogWidth, 980);
            _dialogPanel.localScale = Vector3.one * 0.8f;

            Image dialogBg = dialogObj.AddComponent<Image>();
            dialogBg.color = UiSkin.Surface;
            UiSprites.ApplyRounded(dialogBg, 28f);

            // Vertical Layout Container
            GameObject layoutObj = new GameObject("ContentLayout");
            layoutObj.transform.SetParent(dialogObj.transform, false);
            RectTransform layoutRect = layoutObj.AddComponent<RectTransform>();
            layoutRect.anchorMin = Vector2.zero;
            layoutRect.anchorMax = Vector2.one;
            layoutRect.offsetMin = Vector2.zero;
            layoutRect.offsetMax = Vector2.zero;

            int pad = Mathf.RoundToInt(DialogPadding);
            VerticalLayoutGroup vlayout = layoutObj.AddComponent<VerticalLayoutGroup>();
            vlayout.padding = new RectOffset(pad, pad, pad, pad);
            vlayout.spacing = SectionSpacing;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = true;
            vlayout.childForceExpandWidth = true;
            vlayout.childForceExpandHeight = false;

            // Header Title
            GameObject titleObj = new GameObject("HeaderTitle");
            titleObj.transform.SetParent(layoutObj.transform, false);
            TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.text = "Settings";
            titleText.fontSize = 46;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            SetFixedHeight(titleObj, HeaderHeight);

            // Description
            GameObject descObj = new GameObject("Description");
            descObj.transform.SetParent(layoutObj.transform, false);
            TextMeshProUGUI descText = descObj.AddComponent<TextMeshProUGUI>();
            descText.text = "Sound, accessibility, alerts, who can add you and what data you share:";
            descText.fontSize = 28;
            descText.alignment = TextAlignmentOptions.Center;
            descText.color = new Color(0.75f, 0.80f, 0.90f);
            descText.textWrappingMode = TextWrappingModes.Normal;
            SetFixedHeight(descObj, DescriptionHeight);

            // "Notifications are off" banner (device only; refreshed on Show / focus / permission change)
            BuildPermissionBanner(layoutObj.transform);

            // Toggles live in a scroll view so the dialog still fits short screens.
            Transform rows = BuildScrollView(layoutObj.transform);
            _sfxVolumeSlider = CreateSliderRow(rows, "Sound Effects", "Volume of drops, clears, bombs and other game sounds");
            _vibrationToggle = CreateToggleRow(rows, "Vibration", "Short haptic taps on clears, bombs and blocked moves");
            _shapesToggle = CreateToggleRow(rows, "Tile Shapes", "A different shape on each tile colour (colour-blind friendly)");
            _leaderboardToggle = CreateToggleRow(rows, "Rank drop alerts (in-game)", "Show a banner in the leaderboard when someone overtakes your rank");
            _friendsToggle = CreateToggleRow(rows, "Friend Activity", "Friend requests, accepted requests and friends beating your score");
            _remindersToggle = CreateToggleRow(rows, "Streak & Game Reminders", "Streak protection, daily board reset and comeback reminders");
            _allowRequestsToggle = CreateToggleRow(rows, "Allow Friend Requests", "Let other players send you friend requests from the leaderboard");
            _analyticsToggle = CreateToggleRow(rows, "Share Usage Data", "Help improve the game with usage stats (Unity Analytics)");

            // Close Button
            GameObject btnObj = new GameObject("CloseButton");
            btnObj.transform.SetParent(layoutObj.transform, false);
            btnObj.AddComponent<RectTransform>();
            SetFixedHeight(btnObj, CloseButtonHeight);
            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = UiSkin.Primary;
            UiSprites.ApplyRounded(btnBg, 20f);

            _closeButton = btnObj.AddComponent<Button>();
            _closeButton.targetGraphic = btnBg;
            _closeButton.onClick.AddListener(Hide);
            AddStretchedLabel(btnObj.transform, "Close", 34);

            // Wire toggle events
            _sfxVolumeSlider.onValueChanged.AddListener(val =>
            {
                GamePrefs.SetSfxVolume(val, save: false);
                RefreshVolumeLabel();
            });
            _sfxVolumeSlider.gameObject.AddComponent<PointerUpRelay>().Released += () =>
            {
                GamePrefs.SetSfxVolume(_sfxVolumeSlider.value, save: true);
                SfxPlayer.Play(Sfx.UiTap); // preview the new volume (silent at 0)
            };
            _vibrationToggle.onValueChanged.AddListener(val =>
            {
                GamePrefs.Vibration = val;
                if (val) Haptics.Light();
            });
            _shapesToggle.onValueChanged.AddListener(val => GamePrefs.TileShapes = val);
            _leaderboardToggle.onValueChanged.AddListener(OnLeaderboardToggleChanged);
            _friendsToggle.onValueChanged.AddListener(OnFriendsToggleChanged);
            _remindersToggle.onValueChanged.AddListener(OnRemindersToggleChanged);
            _allowRequestsToggle.onValueChanged.AddListener(OnAllowRequestsToggleChanged);
            _analyticsToggle.onValueChanged.AddListener(OnAnalyticsToggleChanged);

            _overlayObj.SetActive(false);
        }

        private Transform BuildScrollView(Transform parent)
        {
            GameObject scrollObj = new GameObject("ToggleScroll");
            scrollObj.transform.SetParent(parent, false);
            scrollObj.AddComponent<RectTransform>();
            LayoutElement scrollLayout = scrollObj.AddComponent<LayoutElement>();
            scrollLayout.minHeight = MinScrollHeight;
            scrollLayout.preferredHeight = RowsHeight;
            scrollLayout.flexibleHeight = 1;

            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollObj.transform, false);
            RectTransform viewport = viewportObj.AddComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.pivot = new Vector2(0.5f, 1f);
            viewportObj.AddComponent<RectMask2D>();
            Image viewportHit = viewportObj.AddComponent<Image>(); // transparent, so drags between rows still scroll
            viewportHit.color = new Color(0f, 0f, 0f, 0f);

            GameObject contentObj = new GameObject("Rows");
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform content = contentObj.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup rowsLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = RowSpacing;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            return contentObj.transform;
        }

        private void BuildPermissionBanner(Transform parent)
        {
            _permissionBanner = new GameObject("PermissionBanner");
            _permissionBanner.transform.SetParent(parent, false);
            _permissionBanner.AddComponent<RectTransform>();
            SetFixedHeight(_permissionBanner, BannerHeight);

            Image bannerBg = _permissionBanner.AddComponent<Image>();
            bannerBg.color = new Color(0.30f, 0.18f, 0.10f, 1f);
            UiSprites.ApplyRounded(bannerBg, 18f);

            HorizontalLayoutGroup hlayout = _permissionBanner.AddComponent<HorizontalLayoutGroup>();
            hlayout.padding = new RectOffset(22, 22, 14, 14);
            hlayout.spacing = 18;
            hlayout.childAlignment = TextAnchor.MiddleLeft;
            hlayout.childControlWidth = true;
            hlayout.childControlHeight = true;
            hlayout.childForceExpandWidth = false;
            hlayout.childForceExpandHeight = false;

            GameObject textCol = new GameObject("TextCol");
            textCol.transform.SetParent(_permissionBanner.transform, false);
            textCol.AddComponent<RectTransform>();
            LayoutElement textColLayout = textCol.AddComponent<LayoutElement>();
            textColLayout.flexibleWidth = 1;
            VerticalLayoutGroup textLayout = textCol.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = 2;
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandHeight = false;

            AddText(textCol.transform, "Title", "Notifications are off", 30, FontStyles.Bold, new Color(1f, 0.82f, 0.45f));
            AddText(textCol.transform, "Subtitle", "Turn them on for streak reminders and friend alerts.", 25, FontStyles.Normal, new Color(0.92f, 0.86f, 0.80f));

            GameObject btnObj = new GameObject("EnableButton");
            btnObj.transform.SetParent(_permissionBanner.transform, false);
            btnObj.AddComponent<RectTransform>();
            LayoutElement btnLayout = btnObj.AddComponent<LayoutElement>();
            btnLayout.minWidth = btnLayout.preferredWidth = 170;
            btnLayout.minHeight = btnLayout.preferredHeight = 72;
            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = UiSkin.Primary;
            UiSprites.ApplyRounded(btnBg, 16f);
            _enableButton = btnObj.AddComponent<Button>();
            _enableButton.targetGraphic = btnBg;
            _enableButton.onClick.AddListener(OnEnableNotificationsClicked);
            AddStretchedLabel(btnObj.transform, "Enable", 30);

            _permissionBanner.SetActive(false);
        }

        private Toggle CreateToggleRow(Transform parent, string titleText, string subtitleText)
        {
            GameObject row = new GameObject($"ToggleRow_{titleText}");
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            SetFixedHeight(row, RowHeight);

            HorizontalLayoutGroup hlayout = row.AddComponent<HorizontalLayoutGroup>();
            hlayout.spacing = 20;
            hlayout.childAlignment = TextAnchor.MiddleLeft;
            hlayout.childControlWidth = true;
            hlayout.childControlHeight = true;
            hlayout.childForceExpandWidth = false;
            hlayout.childForceExpandHeight = false;

            // Left Text Stack
            GameObject textCol = new GameObject("TextCol");
            textCol.transform.SetParent(row.transform, false);
            textCol.AddComponent<RectTransform>();
            LayoutElement textColLayout = textCol.AddComponent<LayoutElement>();
            textColLayout.flexibleWidth = 1;

            VerticalLayoutGroup vlayout = textCol.AddComponent<VerticalLayoutGroup>();
            vlayout.spacing = 4;
            vlayout.childAlignment = TextAnchor.MiddleLeft;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = true;
            vlayout.childForceExpandHeight = false;

            AddText(textCol.transform, "Title", titleText, 32, FontStyles.Bold, Color.white);
            AddText(textCol.transform, "Subtitle", subtitleText, 25, FontStyles.Normal, new Color(0.70f, 0.75f, 0.85f));

            return CreatePillSwitch(row.transform);
        }

        private const float SliderWidth = 250f;
        private const float SliderTrackHeight = 14f;
        private const float SliderHandleSize = 46f;
        private const float VolumeLabelWidth = 84f;

        /// <summary>Row with a title/subtitle on the left and a 0–100% slider plus its percentage on the right.</summary>
        private Slider CreateSliderRow(Transform parent, string titleText, string subtitleText)
        {
            GameObject row = new GameObject($"SliderRow_{titleText}");
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            SetFixedHeight(row, RowHeight);

            HorizontalLayoutGroup hlayout = row.AddComponent<HorizontalLayoutGroup>();
            hlayout.spacing = 16;
            hlayout.childAlignment = TextAnchor.MiddleLeft;
            hlayout.childControlWidth = true;
            hlayout.childControlHeight = true;
            hlayout.childForceExpandWidth = false;
            hlayout.childForceExpandHeight = false;

            GameObject textCol = new GameObject("TextCol");
            textCol.transform.SetParent(row.transform, false);
            textCol.AddComponent<RectTransform>();
            textCol.AddComponent<LayoutElement>().flexibleWidth = 1;
            VerticalLayoutGroup vlayout = textCol.AddComponent<VerticalLayoutGroup>();
            vlayout.spacing = 4;
            vlayout.childAlignment = TextAnchor.MiddleLeft;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = true;
            vlayout.childForceExpandHeight = false;
            AddText(textCol.transform, "Title", titleText, 32, FontStyles.Bold, Color.white);
            AddText(textCol.transform, "Subtitle", subtitleText, 25, FontStyles.Normal, new Color(0.70f, 0.75f, 0.85f));

            // Slider: rounded track, blue fill, round white handle (kept inside the track's ends).
            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(row.transform, false);
            sliderObj.AddComponent<RectTransform>();
            LayoutElement sliderLayout = sliderObj.AddComponent<LayoutElement>();
            sliderLayout.minWidth = sliderLayout.preferredWidth = SliderWidth;
            sliderLayout.minHeight = sliderLayout.preferredHeight = SliderHandleSize;

            RectTransform track = NewChildRect(sliderObj.transform, "Track", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            track.sizeDelta = new Vector2(0f, SliderTrackHeight);
            Image trackImg = track.gameObject.AddComponent<Image>();
            trackImg.color = PillOffColor;
            UiSprites.ApplyRounded(trackImg, SliderTrackHeight * 0.5f);

            RectTransform fillArea = NewChildRect(sliderObj.transform, "FillArea", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillArea.sizeDelta = new Vector2(0f, SliderTrackHeight);
            RectTransform fill = NewChildRect(fillArea, "Fill", Vector2.zero, Vector2.one);
            fill.sizeDelta = Vector2.zero;
            Image fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = UiSkin.Primary;
            fillImg.raycastTarget = false;
            UiSprites.ApplyRounded(fillImg, SliderTrackHeight * 0.5f);

            RectTransform handleArea = NewChildRect(sliderObj.transform, "HandleArea", Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(SliderHandleSize * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-SliderHandleSize * 0.5f, 0f);
            // The Slider stretches the handle rect to the handle area's height, so the visible knob is a fixed-size
            // child: it stays a perfect circle whatever height the row's layout gives the slider.
            RectTransform handle = NewChildRect(handleArea, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f));
            handle.sizeDelta = new Vector2(SliderHandleSize, 0f);
            RectTransform knob = NewChildRect(handle, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            knob.sizeDelta = new Vector2(SliderHandleSize, SliderHandleSize);
            Image handleImg = knob.gameObject.AddComponent<Image>();
            handleImg.sprite = UiSprites.Circle;
            handleImg.preserveAspect = true;
            handleImg.color = Color.white;

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            var nav = slider.navigation;
            nav.mode = Navigation.Mode.None;
            slider.navigation = nav;

            GameObject labelObj = new GameObject("Value");
            labelObj.transform.SetParent(row.transform, false);
            labelObj.AddComponent<RectTransform>();
            LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
            labelLayout.minWidth = labelLayout.preferredWidth = VolumeLabelWidth;
            _sfxVolumeLabel = labelObj.AddComponent<TextMeshProUGUI>();
            _sfxVolumeLabel.fontSize = 26;
            _sfxVolumeLabel.fontStyle = FontStyles.Bold;
            _sfxVolumeLabel.color = Color.white;
            _sfxVolumeLabel.alignment = TextAlignmentOptions.MidlineRight;
            _sfxVolumeLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _sfxVolumeLabel.raycastTarget = false;

            return slider;
        }

        private void RefreshVolumeLabel()
        {
            if (_sfxVolumeLabel == null || _sfxVolumeSlider == null) return;
            int percent = Mathf.RoundToInt(_sfxVolumeSlider.value * 100f);
            _sfxVolumeLabel.text = percent == 0 ? "Off" : percent + "%";
        }

        private static RectTransform NewChildRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>ON/OFF switch: green track with the knob on the right when on, grey with the knob on the left when off.</summary>
        private Toggle CreatePillSwitch(Transform parent)
        {
            GameObject toggleObj = new GameObject("Toggle");
            toggleObj.transform.SetParent(parent, false);
            toggleObj.AddComponent<RectTransform>();
            LayoutElement toggleLayout = toggleObj.AddComponent<LayoutElement>();
            toggleLayout.minWidth = toggleLayout.preferredWidth = 132;
            toggleLayout.minHeight = toggleLayout.preferredHeight = 60;

            Image track = toggleObj.AddComponent<Image>();
            track.color = PillOffColor;
            UiSprites.ApplyRounded(track, 30f);

            GameObject knobObj = new GameObject("Knob");
            knobObj.transform.SetParent(toggleObj.transform, false);
            RectTransform knob = knobObj.AddComponent<RectTransform>();
            knob.sizeDelta = new Vector2(50, 50);
            Image knobImg = knobObj.AddComponent<Image>();
            knobImg.sprite = UiSprites.Circle;
            knobImg.color = Color.white;
            knobImg.raycastTarget = false;

            GameObject labelObj = new GameObject("State");
            labelObj.transform.SetParent(toggleObj.transform, false);
            RectTransform labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(14, 0);
            labelRect.offsetMax = new Vector2(-14, 0);
            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            label.fontSize = 22;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.raycastTarget = false;

            Toggle toggle = toggleObj.AddComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.graphic = null; // visuals are driven by RefreshPill

            var pill = new PillSwitch { Toggle = toggle, Track = track, Knob = knob, Label = label };
            _pills.Add(pill);
            toggle.onValueChanged.AddListener(_ => RefreshPill(pill));
            RefreshPill(pill);

            return toggle;
        }

        private static void RefreshPill(PillSwitch pill)
        {
            bool on = pill.Toggle.isOn;
            pill.Track.color = on ? PillOnColor : PillOffColor;

            float side = on ? 1f : 0f;
            pill.Knob.anchorMin = new Vector2(side, 0.5f);
            pill.Knob.anchorMax = new Vector2(side, 0.5f);
            pill.Knob.pivot = new Vector2(side, 0.5f);
            pill.Knob.anchoredPosition = new Vector2(on ? -5f : 5f, 0f);

            pill.Label.text = on ? "ON" : "OFF";
            pill.Label.alignment = on ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        }

        private static void AddText(Transform parent, string name, string text, float size, FontStyles style, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            TextMeshProUGUI t = obj.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
        }

        private static void AddStretchedLabel(Transform parent, string text, float size)
        {
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(parent, false);
            RectTransform txtRect = txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.fontSize = size;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
        }

        private static void SetFixedHeight(GameObject obj, float height)
        {
            if (!obj.TryGetComponent(out LayoutElement le)) le = obj.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0;
        }

        private static float RowsHeight => RowCount * RowHeight + (RowCount - 1) * RowSpacing;

        /// <summary>Sizes the dialog to its content, capped to the visible (safe-area) height; the toggle list scrolls if needed.</summary>
        private void ResizeDialog()
        {
            if (_dialogPanel == null) return;

            bool bannerShown = _permissionBanner != null && _permissionBanner.activeSelf;
            float desired = 2 * DialogPadding + HeaderHeight + DescriptionHeight + RowsHeight + CloseButtonHeight
                            + 3 * SectionSpacing
                            + (bannerShown ? BannerHeight + SectionSpacing : 0f);

            Rect area = _overlayRect != null ? _overlayRect.rect : new Rect(0, 0, 1080, 1920);
            float safeFraction = Screen.height > 0 ? Mathf.Clamp01(Screen.safeArea.height / Screen.height) : 1f;
            float maxHeight = area.height > 0f ? area.height * safeFraction - 2 * ScreenMargin : desired;
            float maxWidth = area.width > 0f ? area.width - 2 * ScreenMargin : DialogWidth;

            _dialogPanel.sizeDelta = new Vector2(Mathf.Min(DialogWidth, maxWidth), Mathf.Min(desired, maxHeight));
        }

        private void UpdateToggleStates()
        {
            if (_save == null) return;
            _sfxVolumeSlider.SetValueWithoutNotify(GamePrefs.SfxVolume);
            RefreshVolumeLabel();
            _vibrationToggle.SetIsOnWithoutNotify(GamePrefs.Vibration);
            _shapesToggle.SetIsOnWithoutNotify(GamePrefs.TileShapes);
            _leaderboardToggle.SetIsOnWithoutNotify(_save.NotifyLeaderboardDrops);
            _friendsToggle.SetIsOnWithoutNotify(_save.NotifyFriendActivity);
            _remindersToggle.SetIsOnWithoutNotify(_save.NotifyReminders);
            _allowRequestsToggle.SetIsOnWithoutNotify(_save.AllowFriendRequests);
            _analyticsToggle.SetIsOnWithoutNotify(AnalyticsConsent.IsCollectionAllowed(_save));

            // SetIsOnWithoutNotify skips onValueChanged, so redraw the switches here.
            foreach (var pill in _pills) RefreshPill(pill);
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

        private void OnAnalyticsToggleChanged(bool val)
        {
            // Saves the choice and starts collection, or stops it and requests deletion.
            AnalyticsConsent.SetConsent(_save, val);
        }

        private void OnRemindersToggleChanged(bool val)
        {
            if (_save != null) _save.NotifyReminders = val;
            LocalProgress.Save(_save);
            _onSettingsChanged?.Invoke();
        }

        // ── OS permission banner ────────────────────────────────────────────
        /// <summary>Shows the banner only on a device where push permission is known to be off (never in the Editor).</summary>
        private void RefreshPermissionBanner()
        {
            if (_permissionBanner == null) return;

            var service = NotificationService.Instance;
            bool show = service != null && service.TryGetPushPermission(out bool granted) && !granted;
            if (_permissionBanner.activeSelf != show) _permissionBanner.SetActive(show);
            ResizeDialog();
        }

        private void SubscribeToPermissionChanges()
        {
            var service = NotificationService.Instance;
            if (service == _permissionSource) return;
            if (_permissionSource != null) _permissionSource.PushPermissionChanged -= OnPushPermissionChanged;
            _permissionSource = service;
            if (_permissionSource != null) _permissionSource.PushPermissionChanged += OnPushPermissionChanged;
        }

        private void OnPushPermissionChanged(bool granted)
        {
            if (this != null && IsOpen) RefreshPermissionBanner();
        }

        private async void OnEnableNotificationsClicked()
        {
            var service = NotificationService.Instance;
            if (service == null) return;

            _enableButton.interactable = false;
            try
            {
                // fallbackToSettings: after repeated denials the OS no longer shows its dialog; OneSignal then offers Settings.
                await service.RequestPushPermissionAsync(true);
            }
            finally
            {
                if (this != null && _enableButton != null)
                {
                    _enableButton.interactable = true;
                    RefreshPermissionBanner();
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Back from the system settings screen: the permission may have changed there.
            if (hasFocus && IsOpen) RefreshPermissionBanner();
        }

        private void OnDestroy()
        {
            if (_permissionSource != null) _permissionSource.PushPermissionChanged -= OnPushPermissionChanged;
            _permissionSource = null;
        }

        // ── Show / Hide ─────────────────────────────────────────────────────
        public void Show()
        {
            if (_overlayObj == null) return;

            UpdateToggleStates();
            SubscribeToPermissionChanges();

            IsOpen = true;
            _overlayObj.SetActive(true);
            RefreshPermissionBanner(); // also sizes the dialog to the screen

            // Button/switch labels keep clear of their edges (needs laid-out sizes, so done once it is visible).
            Canvas.ForceUpdateCanvases();
            foreach (var selectable in _overlayObj.GetComponentsInChildren<Selectable>(true))
                UiSkin.PadLabels(selectable);

            _canvasGroup.DOKill();
            _dialogPanel.DOKill();

            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _dialogPanel.localScale = Vector3.one * 0.8f;

            _canvasGroup.DOFade(1f, 0.25f).SetUpdate(true);
            _dialogPanel.DOScale(1f, 0.30f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        /// <summary>
        /// Closes the dialog (close button, Android Back). Raises Closed right away, when input is released,
        /// not after the fade, so a run frozen under the dialog resumes the moment taps reach the board again.
        /// </summary>
        public void Hide()
        {
            if (!IsOpen || _overlayObj == null) return;
            IsOpen = false;

            _canvasGroup.DOKill();
            _dialogPanel.DOKill();

            // Let taps through immediately instead of swallowing them during the fade-out.
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            _dialogPanel.DOScale(0.85f, 0.20f).SetUpdate(true);
            _canvasGroup.DOFade(0f, 0.20f).SetUpdate(true).OnComplete(() => _overlayObj.SetActive(false));

            Closed?.Invoke();
        }
    }
}

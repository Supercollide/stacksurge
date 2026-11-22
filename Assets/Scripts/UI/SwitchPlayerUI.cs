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
    /// "Switch to your other player?": shown when the phone's Google Play Games account is already linked to another
    /// Stack Surge player. Compares that player with this phone's (badge, name, best score) and lets the player switch
    /// or keep this phone's player. Built in code, on its own overlay canvas above Settings.
    /// </summary>
    public class SwitchPlayerUI : MonoBehaviour
    {
        enum Phase { Loading, Ready, Unreachable, Switching, SwitchFailed }

        TMP_FontAsset _font;
        GameObject _overlayObj;
        RectTransform _overlayRect;
        RectTransform _panel;
        CanvasGroup _canvasGroup;
        TextMeshProUGUI _status;
        Card _otherCard;
        Card _thisCard;
        GameObject _note;
        Button _switchButton;
        Image _switchButtonBg;
        TextMeshProUGUI _switchLabel;
        Button _keepButton;
        TextMeshProUGUI _keepLabel;

        Phase _phase;
        int _showId; // a result that arrives after the dialog was closed and opened again is ignored
        PlayGamesAccount.PlayerSummary _other;
        Func<Task<bool>> _switchAsync;
        Action _keep;

        class Card
        {
            public GameObject Root;
            public Image Badge;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Score;
        }

        const float PanelWidth = 880f;
        const float ScreenMargin = 40f;
        const float ButtonHeight = 92f;
        const float BadgeSize = 112f;

        public bool IsOpen { get; private set; }
        public event Action Closed;

        public void Initialize(TMP_FontAsset font)
        {
            _font = font;
            Build();
        }

        // ── Build ───────────────────────────────────────────────────────
        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 960; // above Settings (950), where its Choose button opens it
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _overlayObj = new GameObject("SwitchPlayerOverlay", typeof(RectTransform));
            _overlayObj.transform.SetParent(transform, false);
            _overlayRect = (RectTransform)_overlayObj.transform;
            Stretch(_overlayRect);
            _overlayObj.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
            _canvasGroup = _overlayObj.AddComponent<CanvasGroup>();

            var panelObj = new GameObject("Panel", typeof(RectTransform));
            panelObj.transform.SetParent(_overlayObj.transform, false);
            _panel = (RectTransform)panelObj.transform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(PanelWidth, 900f);
            var bg = panelObj.AddComponent<Image>();
            bg.color = UiSkin.Surface;
            UiSprites.ApplyRounded(bg, 28f);
            var v = panelObj.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(40, 40, 40, 40);
            v.spacing = 22f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            panelObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddText(panelObj.transform, "Title", "Switch to your other player?", 42, FontStyles.Bold, Color.white);
            AddText(panelObj.transform, "Body",
                "This Google account is already linked to a Stack Surge player from another phone or an earlier install.",
                27, FontStyles.Normal, UiSkin.TextMuted);
            _status = AddText(panelObj.transform, "Status", "", 27, FontStyles.Normal, Color.white);
            _otherCard = BuildCard(panelObj.transform, "OtherPlayer", "On your Google account");
            _thisCard = BuildCard(panelObj.transform, "ThisPhone", "On this phone");
            _note = AddText(panelObj.transform, "Note",
                "Switching brings that player's name and friends to this phone. Your badges and best score from this phone come with you.",
                25, FontStyles.Normal, UiSkin.TextMuted).gameObject;

            _switchButton = AddButton(panelObj.transform, "SwitchButton", UiSkin.Primary, out _switchButtonBg, out _switchLabel);
            _switchButton.onClick.AddListener(OnSwitchClicked);
            _keepButton = AddButton(panelObj.transform, "KeepButton", UiSkin.SurfaceRaised, out _, out _keepLabel);
            _keepButton.onClick.AddListener(OnKeepClicked);

            _overlayObj.SetActive(false);
        }

        Card BuildCard(Transform parent, string name, string label)
        {
            var card = new Card();
            card.Root = new GameObject(name, typeof(RectTransform));
            card.Root.transform.SetParent(parent, false);
            var bg = card.Root.AddComponent<Image>();
            bg.color = UiSkin.Board;
            UiSprites.ApplyRounded(bg, 20f);
            var h = card.Root.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(24, 24, 18, 18);
            h.spacing = 22f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var badgeObj = new GameObject("Badge", typeof(RectTransform));
            badgeObj.transform.SetParent(card.Root.transform, false);
            card.Badge = badgeObj.AddComponent<Image>();
            card.Badge.preserveAspect = true;
            card.Badge.raycastTarget = false;
            var ble = badgeObj.AddComponent<LayoutElement>();
            ble.minWidth = ble.preferredWidth = BadgeSize;
            ble.minHeight = ble.preferredHeight = BadgeSize;

            var col = new GameObject("Text", typeof(RectTransform));
            col.transform.SetParent(card.Root.transform, false);
            col.AddComponent<LayoutElement>().flexibleWidth = 1;
            var cv = col.AddComponent<VerticalLayoutGroup>();
            cv.spacing = 2f;
            cv.childAlignment = TextAnchor.MiddleLeft;
            cv.childControlWidth = cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;

            var labelText = AddText(col.transform, "Label", label, 23, FontStyles.Normal, UiSkin.TextMuted);
            labelText.alignment = TextAlignmentOptions.Left;
            card.Name = AddText(col.transform, "Name", "", 32, FontStyles.Bold, Color.white);
            card.Name.alignment = TextAlignmentOptions.Left;
            card.Name.richText = false; // a player's chosen name
            card.Name.textWrappingMode = TextWrappingModes.NoWrap;
            card.Name.overflowMode = TextOverflowModes.Ellipsis;
            card.Score = AddText(col.transform, "Score", "", 26, FontStyles.Normal, UiSkin.Accent);
            card.Score.alignment = TextAlignmentOptions.Left;
            return card;
        }

        // ── Show / flow ─────────────────────────────────────────────────
        /// <param name="thisPhone">This phone's player, shown right away.</param>
        /// <param name="loadOther">Reads the other player (null when it can't be reached). Runs while the dialog says it's looking.</param>
        /// <param name="switchAsync">Switches to the other player; true when done.</param>
        /// <param name="keep">The player keeps this phone's player.</param>
        public void Show(PlayGamesAccount.PlayerSummary thisPhone, Func<Task<PlayGamesAccount.PlayerSummary?>> loadOther,
            Func<Task<bool>> switchAsync, Action keep)
        {
            if (_overlayObj == null || IsOpen) return;
            IsOpen = true;
            int id = ++_showId;
            _switchAsync = switchAsync;
            _keep = keep;
            FillCard(_thisCard, thisPhone);
            SetPhase(Phase.Loading);

            _overlayObj.SetActive(true);
            _panel.sizeDelta = new Vector2(Mathf.Min(PanelWidth, _overlayRect.rect.width - 2 * ScreenMargin), _panel.sizeDelta.y);
            _canvasGroup.DOKill();
            _panel.DOKill();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _panel.localScale = Vector3.one * 0.9f;
            _canvasGroup.DOFade(1f, 0.25f).SetUpdate(true);
            _panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetUpdate(true);

            _ = LoadOtherAsync(id, loadOther);
        }

        async Task LoadOtherAsync(int id, Func<Task<PlayGamesAccount.PlayerSummary?>> loadOther)
        {
            PlayGamesAccount.PlayerSummary? other = null;
            try { if (loadOther != null) other = await loadOther(); }
            catch (Exception e) { Debug.LogWarning("[SwitchPlayerUI] Looking up the other player failed: " + e.Message); }
            if (this == null || !IsOpen || id != _showId) return;

            if (other == null) { SetPhase(Phase.Unreachable); return; }
            _other = other.Value;
            FillCard(_otherCard, _other);
            SetPhase(Phase.Ready);
        }

        async void OnSwitchClicked()
        {
            if (_phase != Phase.Ready && _phase != Phase.SwitchFailed) return;
            SfxPlayer.Play(Sfx.UiTap);
            Haptics.Light();
            SetPhase(Phase.Switching);
            int id = _showId;
            bool done = false;
            try { if (_switchAsync != null) done = await _switchAsync(); }
            catch (Exception e) { Debug.LogWarning("[SwitchPlayerUI] Switch failed: " + e.Message); }
            if (this == null || !IsOpen || id != _showId) return;
            if (done) Hide();
            else SetPhase(Phase.SwitchFailed);
        }

        void OnKeepClicked()
        {
            if (_phase == Phase.Switching) return;
            SfxPlayer.Play(Sfx.UiTap);
            if (_phase != Phase.Unreachable) _keep?.Invoke(); // "Close" after a failed look decides nothing
            Hide();
        }

        /// <summary>The back button: closes without deciding (the choice stays open in Settings), unless a switch is running.</summary>
        public void Back()
        {
            if (_phase == Phase.Switching) return;
            Hide();
        }

        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _canvasGroup.DOKill();
            _panel.DOKill();
            _canvasGroup.blocksRaycasts = false;
            _panel.DOScale(0.92f, 0.2f).SetUpdate(true);
            _canvasGroup.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() => _overlayObj.SetActive(false));
            Closed?.Invoke();
        }

        void SetPhase(Phase phase)
        {
            _phase = phase;
            bool cards = phase != Phase.Loading && phase != Phase.Unreachable;
            _otherCard.Root.SetActive(cards);
            _thisCard.Root.SetActive(cards || phase == Phase.Loading);
            _note.SetActive(cards);

            _status.text = phase switch
            {
                Phase.Loading => "Looking up the player on your Google account",
                Phase.Unreachable => "Couldn't reach the player on your Google account. You can try again later from Settings.",
                Phase.SwitchFailed => "Couldn't switch. Check your connection and try again.",
                _ => "",
            };
            _status.gameObject.SetActive(_status.text.Length > 0);

            _switchButton.gameObject.SetActive(cards);
            _switchButton.interactable = phase != Phase.Switching;
            _switchButtonBg.color = phase == Phase.Switching ? UiSkin.SurfaceRaised : UiSkin.Primary;
            _switchLabel.text = phase == Phase.Switching ? "Switching" : "Switch to " + _other.Name;

            _keepButton.gameObject.SetActive(phase != Phase.Loading);
            _keepButton.interactable = phase != Phase.Switching;
            _keepLabel.text = phase == Phase.Unreachable ? "Close" : "Keep this phone's player";
        }

        static void FillCard(Card card, PlayGamesAccount.PlayerSummary player)
        {
            var sprite = BadgeArt.ForBadge(player.Badge);
            card.Badge.sprite = sprite;
            card.Badge.gameObject.SetActive(sprite != null);
            card.Name.text = string.IsNullOrEmpty(player.Name) ? "Unnamed player" : player.Name;
            card.Score.text = player.BestScore > 0
                ? "Best score " + player.BestScore.ToString("N0", CultureInfo.InvariantCulture)
                : "No score yet";
        }

        // ── Helpers ─────────────────────────────────────────────────────
        TextMeshProUGUI AddText(Transform parent, string name, string text, float size, FontStyles style, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var t = obj.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        Button AddButton(Transform parent, string name, Color color, out Image background, out TextMeshProUGUI label)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            background = obj.AddComponent<Image>();
            background.color = color;
            UiSprites.ApplyRounded(background, 20f);
            var le = obj.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = ButtonHeight;
            var button = obj.AddComponent<Button>();
            button.targetGraphic = background;
            label = AddText(obj.transform, "Text", "", 34, FontStyles.Bold, Color.white);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(18f, 0f);
            label.rectTransform.offsetMax = new Vector2(-18f, 0f);
            label.richText = false; // "Switch to" + a player's name
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMax = 34;
            label.fontSizeMin = 22;
            return button;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}

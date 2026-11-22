using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// The Achievements screen. Personal records (best score, longest chain, longest streak) sit on cards at the top,
    /// each with the record on its badge and the day it was set. The awards for all-time totals follow in a grid:
    /// the current badge with the goal that earned it, and how many of the six tiers are done. Tapping either opens
    /// the details: every tier, progress to the next one, and the button that shows the badge on the profile.
    /// Built in code, on its own overlay canvas above the main UI (below Settings and toasts).
    /// </summary>
    public class AchievementsUI : MonoBehaviour
    {
        AchievementService _service;
        TMP_FontAsset _font;
        TMP_FontAsset _numberFont;

        GameObject _overlayObj;
        RectTransform _overlayRect;
        RectTransform _dialogPanel;
        CanvasGroup _canvasGroup;
        TextMeshProUGUI _countText;
        ScrollRect _scroll;
        GridLayoutGroup _recordsGrid;
        GridLayoutGroup _awardsGrid;
        readonly List<Tile> _tiles = new List<Tile>();
        Material _numberMaterial;

        // Details
        GameObject _detailObj;
        CanvasGroup _detailGroup;
        RectTransform _detailPanel;
        AchievementDef _detailDef;
        BadgeView _detailBadge;
        TextMeshProUGUI _detailName;
        TextMeshProUGUI _detailTier;
        TextMeshProUGUI _detailDate;
        TextMeshProUGUI _detailNext;
        GameObject _detailBar;
        RectTransform _detailBarFill;
        TextMeshProUGUI _detailProgress;
        Image[] _stripEmblems;
        TextMeshProUGUI[] _stripGoals;
        Button _profileButton;
        Image _profileButtonBg;
        TextMeshProUGUI _profileButtonLabel;

        /// <summary>A badge sprite with a number across its lower edge.</summary>
        class BadgeView
        {
            public RectTransform Rect;
            public Image Emblem;
            public TextMeshProUGUI Number;
        }

        class Tile
        {
            public AchievementDef Def;
            public Image Background;
            public BadgeView Badge;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Sub;
            public TextMeshProUGUI Tag;
        }

        // Layout, in reference units of the 1080x1920 canvas
        const float DialogWidth = 960f;
        const float DialogPadding = 32f;
        const float SectionSpacing = 18f;
        const float HeaderHeight = 60f;
        const float HintHeight = 40f;
        const float CloseButtonHeight = 92f;
        const float ScreenMargin = 40f;
        const int Columns = 3;
        const float ColumnGap = 16f;
        const float ListSpacing = 16f;
        const float SectionTitleHeight = 50f;
        const float RecordCardHeight = 306f;
        const float RecordEmblemSize = 172f;
        const float AwardCellHeight = 328f; // badge, then one line each for the name, tier count and profile tag
        const float AwardRowGap = 16f;
        const float AwardEmblemSize = 208f;
        const float DetailWidth = 860f;
        const float DetailEmblemSize = 260f;
        const float StripEmblemSize = 96f;
        const float BarHeight = 16f;
        /// <summary>Where the number's middle sits, as a fraction of the badge's height from its bottom.
        /// The badges' lowest points run from 74% (tier 1) to 98% (tier 6) of the sprite, so it overlaps them all.</summary>
        const float NumberCenter = 0.16f;
        // The badge numbers' font: the UI font again with more room around each glyph, so the outline can be thick.
        const int NumberSamplingSize = 80;
        const int NumberAtlasPadding = 18;
        const string NumberCharacters = "0123456789,.KM";

        static readonly Color CardColor = UiSkin.Board;
        static readonly Color ShownColor = new Color32(0x33, 0x31, 0x2A, 0xFF); // the leaderboard's "you" row tint, solid
        static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        static readonly Color LockedTint = new Color32(0x3A, 0x44, 0x5E, 0xFF);   // a badge not earned yet: a dark shape
        static readonly Color LockedNumber = new Color32(0x6B, 0x76, 0x92, 0xFF);
        static readonly Color LockedTierTint = new Color32(0x3A, 0x44, 0x5E, 0x80); // half see-through, so the strip's bright tiers go dark too

        public bool IsOpen { get; private set; }
        public event Action Closed;

        public void Initialize(AchievementService service, TMP_FontAsset font = null)
        {
            _service = service;
            _font = font != null ? font : TMP_Settings.defaultFontAsset;
            _numberFont = CreateNumberFont(_font);
            Build();
            if (_service != null) _service.Changed += OnChanged;
        }

        void OnDestroy()
        {
            if (_service != null) _service.Changed -= OnChanged;
            if (_numberMaterial != null) Destroy(_numberMaterial);
            if (_numberFont != null)
            {
                foreach (var texture in _numberFont.atlasTextures) if (texture != null) Destroy(texture);
                if (_numberFont.material != null) Destroy(_numberFont.material);
                Destroy(_numberFont);
            }
        }

        void OnChanged()
        {
            if (this == null || !IsOpen) return;
            Refresh();
            if (_detailDef != null) RefreshDetail();
        }

        // ── Build ───────────────────────────────────────────────────────
        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 940;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _overlayObj = new GameObject("AchievementsOverlay", typeof(RectTransform));
            _overlayObj.transform.SetParent(transform, false);
            _overlayRect = (RectTransform)_overlayObj.transform;
            Stretch(_overlayRect);
            var dim = _overlayObj.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);
            _canvasGroup = _overlayObj.AddComponent<CanvasGroup>();

            var dialogObj = new GameObject("DialogPanel", typeof(RectTransform));
            dialogObj.transform.SetParent(_overlayObj.transform, false);
            _dialogPanel = (RectTransform)dialogObj.transform;
            _dialogPanel.anchorMin = _dialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _dialogPanel.sizeDelta = new Vector2(DialogWidth, 1600f);
            var dialogBg = dialogObj.AddComponent<Image>();
            dialogBg.color = UiSkin.Surface;
            UiSprites.ApplyRounded(dialogBg, 28f);

            var layoutObj = new GameObject("Content", typeof(RectTransform));
            layoutObj.transform.SetParent(dialogObj.transform, false);
            Stretch((RectTransform)layoutObj.transform);
            int pad = Mathf.RoundToInt(DialogPadding);
            var vlayout = layoutObj.AddComponent<VerticalLayoutGroup>();
            vlayout.padding = new RectOffset(pad, pad, pad, pad);
            vlayout.spacing = SectionSpacing;
            vlayout.childControlWidth = vlayout.childControlHeight = true;
            vlayout.childForceExpandWidth = true;
            vlayout.childForceExpandHeight = false;

            BuildHeader(layoutObj.transform);

            var hint = AddText(layoutObj.transform, "Hint",
                "Tap a badge to see its tiers or show it on your profile.", 26, FontStyles.Normal, UiSkin.TextMuted);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            hint.enableAutoSizing = true;
            hint.fontSizeMax = 26;
            hint.fontSizeMin = 20;
            SetFixedHeight(hint.gameObject, HintHeight);

            Transform list = BuildScrollView(layoutObj.transform);
            AddSectionTitle(list, "Personal records");
            _recordsGrid = AddGrid(list, "Records", RecordCardHeight);
            AddSectionTitle(list, "Awards");
            _awardsGrid = AddGrid(list, "Awards", AwardCellHeight);
            foreach (var def in AchievementCatalog.All)
                _tiles.Add(BuildTile(def.IsRecord ? _recordsGrid.transform : _awardsGrid.transform, def));

            var close = AddButton(layoutObj.transform, "CloseButton", "Close", UiSkin.Primary, out _, out _);
            SetFixedHeight(close.gameObject, CloseButtonHeight);
            close.onClick.AddListener(Hide);

            BuildDetail();
            _overlayObj.SetActive(false);
        }

        void BuildHeader(Transform parent)
        {
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(parent, false);
            SetFixedHeight(header, HeaderHeight);

            var title = AddText(header.transform, "Title", "ACHIEVEMENTS", 44, FontStyles.Bold, UiSkin.Accent);
            Stretch(title.rectTransform);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.textWrappingMode = TextWrappingModes.NoWrap;

            _countText = AddText(header.transform, "Count", "", 28, FontStyles.Bold, UiSkin.TextMuted);
            Stretch(_countText.rectTransform);
            _countText.alignment = TextAlignmentOptions.MidlineRight;
            _countText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Transform BuildScrollView(Transform parent)
        {
            var scrollObj = new GameObject("List", typeof(RectTransform));
            scrollObj.transform.SetParent(parent, false);
            var scrollLayout = scrollObj.AddComponent<LayoutElement>();
            scrollLayout.minHeight = RecordCardHeight;
            scrollLayout.flexibleHeight = 1;

            var viewportObj = new GameObject("Viewport", typeof(RectTransform));
            viewportObj.transform.SetParent(scrollObj.transform, false);
            var viewport = (RectTransform)viewportObj.transform;
            Stretch(viewport);
            viewport.pivot = new Vector2(0.5f, 1f);
            viewportObj.AddComponent<RectMask2D>();
            var viewportHit = viewportObj.AddComponent<Image>(); // transparent, so a drag between badges still scrolls
            viewportHit.color = Clear;

            var contentObj = new GameObject("Sections", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            var content = (RectTransform)contentObj.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = contentObj.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ListSpacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = scrollObj.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            return contentObj.transform;
        }

        void AddSectionTitle(Transform parent, string text)
        {
            var t = AddText(parent, "Section", text, 34, FontStyles.Bold, Color.white);
            t.alignment = TextAlignmentOptions.BottomLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            SetFixedHeight(t.gameObject, SectionTitleHeight);
        }

        static GridLayoutGroup AddGrid(Transform parent, string name, float cellHeight)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var grid = obj.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.spacing = new Vector2(ColumnGap, AwardRowGap);
            grid.cellSize = new Vector2(288f, cellHeight); // width set to fit in ResizeDialog
            return grid;
        }

        Tile BuildTile(Transform parent, AchievementDef def)
        {
            bool record = def.IsRecord;
            var tile = new Tile { Def = def };
            var obj = new GameObject((record ? "Record_" : "Award_") + def.Key, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            tile.Background = obj.AddComponent<Image>(); // clear behind an award, but still catches the tap
            UiSprites.ApplyRounded(tile.Background, 20f);
            var button = obj.AddComponent<Button>();
            button.targetGraphic = tile.Background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnTileTapped(tile));

            float size = record ? RecordEmblemSize : AwardEmblemSize;
            float top = record ? 14f : 4f;
            tile.Badge = BuildBadge(obj.transform, size, record ? 40f : 48f);
            var br = tile.Badge.Rect;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 1f);
            br.anchoredPosition = new Vector2(0f, -top);

            // Name, then the date or tier count, then the profile tag, stacked under the badge.
            var textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(obj.transform, false);
            var trt = (RectTransform)textObj.transform;
            Stretch(trt);
            trt.offsetMin = new Vector2(8f, 8f);
            trt.offsetMax = new Vector2(-8f, -(top + size + 6f));
            var v = textObj.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.UpperCenter;
            v.spacing = 2f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            float nameSize = record ? 28 : 30;
            tile.Name = AddText(textObj.transform, "Name", def.Name, nameSize, FontStyles.Bold, Color.white);
            tile.Name.alignment = TextAlignmentOptions.Top;
            tile.Name.textWrappingMode = TextWrappingModes.NoWrap; // shrinks to fit instead, so every row is the same height
            tile.Name.enableAutoSizing = true;
            tile.Name.fontSizeMax = nameSize;
            tile.Name.fontSizeMin = 20;
            tile.Sub = AddText(textObj.transform, "Sub", "", 24, FontStyles.Normal, UiSkin.TextMuted);
            tile.Sub.alignment = TextAlignmentOptions.Top;
            tile.Sub.textWrappingMode = TextWrappingModes.NoWrap;
            tile.Tag = AddText(textObj.transform, "Tag", "ON PROFILE", 22, FontStyles.Bold, UiSkin.Accent);
            tile.Tag.alignment = TextAlignmentOptions.Top;
            tile.Tag.textWrappingMode = TextWrappingModes.NoWrap;
            return tile;
        }

        BadgeView BuildBadge(Transform parent, float size, float numberSize)
        {
            var obj = new GameObject("Badge", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rt = (RectTransform)obj.transform;
            rt.sizeDelta = new Vector2(size, size);
            var emblem = obj.AddComponent<Image>();
            emblem.preserveAspect = true;
            emblem.raycastTarget = false;

            var number = AddText(obj.transform, "Number", "", numberSize,
                _numberFont != null ? FontStyles.Normal : FontStyles.Bold, Color.white, _numberFont);
            number.fontSharedMaterial = NumberMaterial(number.font);
            number.characterSpacing = 6f; // so one digit's outline doesn't cut into the next
            number.alignment = TextAlignmentOptions.Center;
            number.textWrappingMode = TextWrappingModes.NoWrap;
            number.enableAutoSizing = true;
            number.fontSizeMax = numberSize;
            number.fontSizeMin = numberSize * 0.55f;
            var nrt = number.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, NumberCenter);
            nrt.sizeDelta = new Vector2(size * 1.1f, numberSize * 1.3f);
            return new BadgeView { Rect = rt, Emblem = emblem, Number = number };
        }

        /// <summary>One outlined material for every badge number (setting outlineWidth per text would make a material each).</summary>
        Material NumberMaterial(TMP_FontAsset font)
        {
            if (_numberMaterial != null) return _numberMaterial;
            _numberMaterial = new Material(font.material) { name = "BadgeNumber (outline)" };
            _numberMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            if (_numberFont != null)
            {
                // About 7% of the type size of dark rim outside the glyphs, which also come out a little heavier than Bold.
                _numberMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.47f);
                _numberMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.34f);
            }
            else
            {
                _numberMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f); // as much as the UI font's atlas allows
            }
            _numberMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(10, 14, 24, 255));
            ShaderUtilities.UpdateShaderRatios(_numberMaterial);
            return _numberMaterial;
        }

        /// <summary>The UI font rendered again with a wider margin around each glyph. The UI font's own atlas leaves room
        /// for only a hairline outline at badge size. Null when the font file isn't in the build; the numbers then use
        /// the UI font with a thinner outline.</summary>
        static TMP_FontAsset CreateNumberFont(TMP_FontAsset uiFont)
        {
            var file = uiFont != null ? uiFont.sourceFontFile : null;
            if (file == null) return null;
            var font = TMP_FontAsset.CreateFontAsset(file, NumberSamplingSize, NumberAtlasPadding, GlyphRenderMode.SDFAA, 512, 512);
            if (font == null) return null;
            font.name = uiFont.name + " (badge numbers)";
            font.TryAddCharacters(NumberCharacters);
            return font;
        }

        // ── Details ─────────────────────────────────────────────────────
        void BuildDetail()
        {
            _detailObj = new GameObject("Details", typeof(RectTransform));
            _detailObj.transform.SetParent(_overlayObj.transform, false);
            Stretch((RectTransform)_detailObj.transform);
            _detailGroup = _detailObj.AddComponent<CanvasGroup>();

            // The dim is the panel's sibling, not its parent, so a tap on the panel doesn't reach the dim's button.
            var dimObj = new GameObject("Dim", typeof(RectTransform));
            dimObj.transform.SetParent(_detailObj.transform, false);
            Stretch((RectTransform)dimObj.transform);
            var dim = dimObj.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            var dimButton = dimObj.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(CloseDetail);

            var panelObj = new GameObject("Panel", typeof(RectTransform));
            panelObj.transform.SetParent(_detailObj.transform, false);
            _detailPanel = (RectTransform)panelObj.transform;
            _detailPanel.anchorMin = _detailPanel.anchorMax = _detailPanel.pivot = new Vector2(0.5f, 0.5f);
            _detailPanel.sizeDelta = new Vector2(DetailWidth, 900f);
            var bg = panelObj.AddComponent<Image>();
            bg.color = UiSkin.Surface;
            UiSprites.ApplyRounded(bg, 28f);
            var v = panelObj.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(40, 40, 36, 40);
            v.spacing = 10f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            panelObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var holder = new GameObject("BadgeHolder", typeof(RectTransform));
            holder.transform.SetParent(panelObj.transform, false);
            SetFixedHeight(holder, DetailEmblemSize);
            _detailBadge = BuildBadge(holder.transform, DetailEmblemSize, 64f);

            _detailName = AddCentered(panelObj.transform, "Name", 44, FontStyles.Bold, Color.white);
            _detailTier = AddCentered(panelObj.transform, "Tier", 28, FontStyles.Bold, UiSkin.Accent);
            _detailDate = AddCentered(panelObj.transform, "Date", 25, FontStyles.Normal, UiSkin.TextMuted);
            AddSpacer(panelObj.transform, 10f);
            _detailNext = AddCentered(panelObj.transform, "Next", 28, FontStyles.Normal, Color.white);

            _detailBar = new GameObject("Bar", typeof(RectTransform));
            _detailBar.transform.SetParent(panelObj.transform, false);
            SetFixedHeight(_detailBar, BarHeight);
            var track = _detailBar.AddComponent<Image>();
            track.color = UiSkin.SurfaceRaised;
            track.raycastTarget = false;
            UiSprites.ApplyRounded(track, BarHeight * 0.5f);
            var fillObj = new GameObject("Fill", typeof(RectTransform));
            fillObj.transform.SetParent(_detailBar.transform, false);
            _detailBarFill = (RectTransform)fillObj.transform;
            _detailBarFill.anchorMin = Vector2.zero;
            _detailBarFill.anchorMax = new Vector2(0f, 1f);
            _detailBarFill.pivot = new Vector2(0f, 0.5f);
            _detailBarFill.offsetMin = _detailBarFill.offsetMax = Vector2.zero;
            var fill = fillObj.AddComponent<Image>();
            fill.color = UiSkin.Teal;
            fill.raycastTarget = false;
            UiSprites.ApplyRounded(fill, BarHeight * 0.5f);

            _detailProgress = AddCentered(panelObj.transform, "Progress", 25, FontStyles.Normal, UiSkin.TextMuted);
            AddSpacer(panelObj.transform, 14f);
            BuildTierStrip(panelObj.transform);
            AddSpacer(panelObj.transform, 18f);

            var buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(panelObj.transform, false);
            SetFixedHeight(buttons, CloseButtonHeight);
            var h = buttons.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 20f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            var back = AddButton(buttons.transform, "BackButton", "Back", UiSkin.SurfaceRaised, out _, out _);
            back.onClick.AddListener(CloseDetail);
            _profileButton = AddButton(buttons.transform, "ProfileButton", "", UiSkin.Primary, out _profileButtonBg, out _profileButtonLabel);
            _profileButton.onClick.AddListener(OnProfileButton);

            _detailObj.SetActive(false);
        }

        /// <summary>All six badges of the achievement in a row, each with the goal that earns it underneath.</summary>
        void BuildTierStrip(Transform parent)
        {
            var strip = new GameObject("Tiers", typeof(RectTransform));
            strip.transform.SetParent(parent, false);
            SetFixedHeight(strip, StripEmblemSize + 34f);
            var h = strip.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.UpperCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            _stripEmblems = new Image[AchievementCatalog.Tiers];
            _stripGoals = new TextMeshProUGUI[AchievementCatalog.Tiers];
            for (int t = 0; t < AchievementCatalog.Tiers; t++)
            {
                var col = new GameObject("Tier" + (t + 1), typeof(RectTransform));
                col.transform.SetParent(strip.transform, false);
                var le = col.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = StripEmblemSize;
                le.minHeight = le.preferredHeight = StripEmblemSize + 34f;

                var e = new GameObject("Badge", typeof(RectTransform));
                e.transform.SetParent(col.transform, false);
                var ert = (RectTransform)e.transform;
                ert.anchorMin = ert.anchorMax = ert.pivot = new Vector2(0.5f, 1f);
                ert.sizeDelta = new Vector2(StripEmblemSize, StripEmblemSize);
                var img = e.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                _stripEmblems[t] = img;

                var goal = AddText(col.transform, "Goal", "", 22, FontStyles.Bold, UiSkin.TextMuted);
                var grt = goal.rectTransform;
                grt.anchorMin = new Vector2(0f, 0f);
                grt.anchorMax = new Vector2(1f, 0f);
                grt.pivot = new Vector2(0.5f, 0f);
                grt.sizeDelta = new Vector2(20f, 32f); // a little wider than the column, for "750K"
                goal.alignment = TextAlignmentOptions.Center;
                goal.textWrappingMode = TextWrappingModes.NoWrap;
                _stripGoals[t] = goal;
            }
        }

        // ── State ───────────────────────────────────────────────────────
        void Refresh()
        {
            var p = _service?.Progress;
            if (p == null) return;

            _countText.text = $"{AchievementCatalog.EarnedBadges(p)} / {AchievementCatalog.TotalBadges} badges";
            foreach (var tile in _tiles)
            {
                var def = tile.Def;
                int tier = AchievementCatalog.TierOf(p, def);
                int value = def.Value(p);
                bool onProfile = tier > 0 && p.ShownBadge == def.Key;

                tile.Background.color = onProfile ? ShownColor : def.IsRecord ? CardColor : Clear;
                tile.Tag.gameObject.SetActive(onProfile);
                if (def.IsRecord)
                {
                    SetBadge(tile.Badge, def, tier, value > 0 ? N(value) : "");
                    tile.Sub.text = value <= 0 ? "No record yet" : FormatDate(def.SetAtTicks(p)) ?? "All-time best";
                }
                else
                {
                    // Like the tier strip: the goal the badge was earned for, or the first goal on a badge still to earn.
                    SetBadge(tile.Badge, def, tier, AchievementCatalog.Compact(def.Goals[Mathf.Max(1, tier) - 1]));
                    tile.Sub.text = $"{tier} of {AchievementCatalog.Tiers}";
                }
            }
        }

        static void SetBadge(BadgeView view, AchievementDef def, int tier, string number)
        {
            bool earned = tier > 0;
            view.Emblem.sprite = BadgeArt.Emblem(def.Key, Mathf.Max(1, tier));
            view.Emblem.color = earned ? Color.white : LockedTint;
            view.Number.text = number;
            view.Number.color = earned ? Color.white : LockedNumber;
        }

        void RefreshDetail()
        {
            var def = _detailDef;
            var p = _service?.Progress;
            if (def == null || p == null) return;

            int tier = AchievementCatalog.TierOf(p, def);
            int value = def.Value(p);
            bool earned = tier > 0;
            bool allDone = tier >= AchievementCatalog.Tiers;

            SetBadge(_detailBadge, def, tier, def.IsRecord
                ? (value > 0 ? N(value) : "")
                : AchievementCatalog.Compact(def.Goals[Mathf.Max(1, tier) - 1]));
            _detailName.text = def.Name;
            _detailTier.text = earned ? $"Tier {tier} of {AchievementCatalog.Tiers}" : "No badge yet";
            _detailTier.color = earned ? UiSkin.Accent : UiSkin.TextMuted;

            string date = def.IsRecord && value > 0 ? FormatDate(def.SetAtTicks(p)) : null;
            _detailDate.gameObject.SetActive(date != null);
            _detailDate.text = date != null ? "Record set on " + date : "";

            _detailNext.text = allDone ? "Every tier earned"
                : (earned ? "Next tier: " : "First badge: ") + def.GoalText(tier + 1);
            _detailBar.SetActive(!allDone);
            if (!allDone)
            {
                int goal = def.Goals[tier];
                float fill = Mathf.Clamp01(value / (float)goal);
                _detailBarFill.anchorMax = new Vector2(fill, 1f);
                _detailBarFill.gameObject.SetActive(fill > 0f);
                _detailProgress.text = $"{N(value)} / {N(goal)}";
            }
            else
            {
                _detailProgress.text = def.IsRecord ? $"Record: {N(value)}" : $"{N(value)} so far";
            }

            for (int t = 0; t < AchievementCatalog.Tiers; t++)
            {
                bool has = t < tier;
                _stripEmblems[t].sprite = BadgeArt.Emblem(def.Key, t + 1);
                _stripEmblems[t].color = has ? Color.white : LockedTierTint;
                _stripGoals[t].text = AchievementCatalog.Compact(def.Goals[t]);
                _stripGoals[t].color = has ? Color.white : UiSkin.TextMuted;
            }

            bool shown = earned && p.ShownBadge == def.Key;
            _profileButton.gameObject.SetActive(earned);
            _profileButtonLabel.text = shown ? "Remove from profile" : "Show on profile";
            _profileButtonBg.color = shown ? UiSkin.SurfaceRaised : UiSkin.Primary;
        }

        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"Nov 21, 2026" in the player's time zone, or null when the date isn't known.</summary>
        static string FormatDate(long utcTicks)
        {
            if (utcTicks <= 0 || utcTicks > DateTime.MaxValue.Ticks) return null;
            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        }

        void OnTileTapped(Tile tile)
        {
            tile.Badge.Rect.DOKill(true);
            tile.Badge.Rect.DOPunchScale(Vector3.one * 0.08f, 0.25f, 8, 0.6f).SetUpdate(true);
            SfxPlayer.Play(Sfx.UiTap);
            Haptics.Light();
            OpenDetail(tile.Def);
        }

        void OnProfileButton()
        {
            if (_service == null || _detailDef == null) return;
            bool shown = _service.Progress.ShownBadge == _detailDef.Key;
            _service.SetShownBadge(shown ? null : _detailDef.Key); // raises Changed, which redraws both views
            SfxPlayer.Play(Sfx.UiTap);
            Haptics.Light();
        }

        void OpenDetail(AchievementDef def)
        {
            if (def == null || _detailObj == null) return;
            _detailDef = def;
            _detailPanel.sizeDelta = new Vector2(Mathf.Min(DetailWidth, _overlayRect.rect.width - 2 * ScreenMargin), _detailPanel.sizeDelta.y);
            RefreshDetail();
            _detailObj.SetActive(true);

            _detailGroup.DOKill();
            _detailPanel.DOKill();
            _detailGroup.alpha = 0f;
            _detailGroup.blocksRaycasts = true;
            _detailPanel.localScale = Vector3.one * 0.9f;
            _detailGroup.DOFade(1f, 0.2f).SetUpdate(true);
            _detailPanel.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        void CloseDetail()
        {
            if (_detailDef == null) return;
            _detailDef = null;
            _detailGroup.DOKill();
            _detailPanel.DOKill();
            _detailGroup.blocksRaycasts = false;
            _detailGroup.DOFade(0f, 0.15f).SetUpdate(true).OnComplete(() => _detailObj.SetActive(false));
        }

        // ── Show / Hide ─────────────────────────────────────────────────
        public void Show()
        {
            if (_overlayObj == null || IsOpen) return;
            IsOpen = true;
            _detailDef = null;
            _detailObj.SetActive(false);
            _overlayObj.SetActive(true);
            ResizeDialog();
            Refresh();

            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;

            _canvasGroup.DOKill();
            _dialogPanel.DOKill();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _dialogPanel.localScale = Vector3.one * 0.9f;
            _canvasGroup.DOFade(1f, 0.25f).SetUpdate(true);
            _dialogPanel.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        /// <summary>The back button: closes the details first, then the screen.</summary>
        public void Back()
        {
            if (_detailDef != null) CloseDetail();
            else Hide();
        }

        public void Hide()
        {
            if (!IsOpen || _overlayObj == null) return;
            IsOpen = false;
            CloseDetail();
            _canvasGroup.DOKill();
            _dialogPanel.DOKill();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            _dialogPanel.DOScale(0.92f, 0.2f).SetUpdate(true);
            _canvasGroup.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() => _overlayObj.SetActive(false));
            _service?.FlushBadgeChoice();
            Closed?.Invoke();
        }

        /// <summary>Fits the visible (safe-area) height and width; three badges to a row, the list scrolls inside.</summary>
        void ResizeDialog()
        {
            Rect area = _overlayRect.rect;
            float maxWidth = area.width > 0f ? area.width - 2 * ScreenMargin : DialogWidth;
            float width = Mathf.Min(DialogWidth, maxWidth);
            float cellWidth = Mathf.Floor((width - 2 * DialogPadding - (Columns - 1) * ColumnGap) / Columns);
            _recordsGrid.cellSize = new Vector2(cellWidth, RecordCardHeight);
            _awardsGrid.cellSize = new Vector2(cellWidth, AwardCellHeight);

            int records = _recordsGrid.transform.childCount, awards = _awardsGrid.transform.childCount;
            float GridHeight(int count, float cell)
            {
                int rows = (count + Columns - 1) / Columns;
                return rows * cell + Mathf.Max(0, rows - 1) * AwardRowGap;
            }
            float list = 2 * SectionTitleHeight + GridHeight(records, RecordCardHeight) + GridHeight(awards, AwardCellHeight) + 3 * ListSpacing;

            float safeFraction = Screen.height > 0 ? Mathf.Clamp01(Screen.safeArea.height / Screen.height) : 1f;
            float desired = 2 * DialogPadding + HeaderHeight + HintHeight + list + CloseButtonHeight + 3 * SectionSpacing;
            float maxHeight = area.height > 0f ? area.height * safeFraction - 2 * ScreenMargin : desired;
            _dialogPanel.sizeDelta = new Vector2(width, Mathf.Min(desired, maxHeight));
        }

        // ── Helpers ─────────────────────────────────────────────────────
        TextMeshProUGUI AddText(Transform parent, string name, string text, float size, FontStyles style, Color color, TMP_FontAsset font = null)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var t = obj.AddComponent<TextMeshProUGUI>();
            if (font == null) font = _font;
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        TextMeshProUGUI AddCentered(Transform parent, string name, float size, FontStyles style, Color color)
        {
            var t = AddText(parent, name, "", size, style, color);
            t.alignment = TextAlignmentOptions.Center;
            return t;
        }

        static void AddSpacer(Transform parent, float height)
        {
            var obj = new GameObject("Space", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            SetFixedHeight(obj, height);
        }

        Button AddButton(Transform parent, string name, string label, Color color, out Image background, out TextMeshProUGUI text)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            background = obj.AddComponent<Image>();
            background.color = color;
            UiSprites.ApplyRounded(background, 20f);
            var button = obj.AddComponent<Button>();
            button.targetGraphic = background;
            text = AddText(obj.transform, "Text", label, 34, FontStyles.Bold, Color.white);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(18f, 0f); // keeps "Remove from profile" off the button's edges
            text.rectTransform.offsetMax = new Vector2(-18f, 0f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMax = 34;
            text.fontSizeMin = 22;
            return button;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void SetFixedHeight(GameObject obj, float height)
        {
            if (!obj.TryGetComponent(out LayoutElement le)) le = obj.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0;
        }
    }
}

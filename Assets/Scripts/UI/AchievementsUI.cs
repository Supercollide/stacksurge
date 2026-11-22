using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// The Achievements screen, in two wells. Personal records: best score on a wide tile (the number the leaderboard
    /// ranks by), longest chain and longest streak on half tiles, each with the record, the day it was set and its tier
    /// meter. Awards for all-time totals: a grid of tiles, three to a row, each with its badge, a meter of six blocks for
    /// its tiers and the progress to the next one. Tiles are raised like the board's blocks; the badge on the profile
    /// has a gold edge and an "ON PROFILE" tag. Tapping a tile opens the details: every tier, progress to the next one,
    /// and the button that shows the badge on the profile.
    /// Built in code, on its own overlay canvas above the main UI (below Settings and toasts).
    /// </summary>
    public class AchievementsUI : MonoBehaviour
    {
        AchievementService _service;
        TMP_FontAsset _font;

        GameObject _overlayObj;
        RectTransform _overlayRect;
        RectTransform _dialogPanel;
        CanvasGroup _canvasGroup;
        TextMeshProUGUI _countText;
        TextMeshProUGUI _recordsCount;
        TextMeshProUGUI _awardsCount;
        ScrollRect _scroll;
        GridLayoutGroup _awardsGrid;
        readonly List<Tile> _tiles = new List<Tile>();

        // Details
        GameObject _detailObj;
        CanvasGroup _detailGroup;
        RectTransform _detailPanel;
        AchievementDef _detailDef;
        Image _detailBadge;
        TextMeshProUGUI _detailName;
        TextMeshProUGUI _detailTier;
        TextMeshProUGUI _detailDate;
        TextMeshProUGUI _detailNext;
        GameObject _detailBar;
        RectTransform _detailBarFill;
        Image _detailBarFillImage;
        TextMeshProUGUI _detailProgress;
        Image[] _stripEmblems;
        TextMeshProUGUI[] _stripGoals;
        Button _profileButton;
        Image _profileButtonBg;
        TextMeshProUGUI _profileButtonLabel;

        enum Kind { Hero, Half, Award }

        class Tile
        {
            public AchievementDef Def;
            public Kind Kind;
            public Image Badge;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI Date;
            public TextMeshProUGUI Count;
            public Image[] Meter;
            public GameObject Outline;
            public GameObject Tag;
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
        const float ListSpacing = 16f;
        const int WellPadSide = 16, WellPadTop = 12, WellPadBottom = 18;
        const float WellSpacing = 12f;
        const float WellHeaderHeight = 30f;
        const float WellRadius = 22f;
        const float TileGap = 14f;
        const float TileRadius = 18f;
        const float TileLip = 6f;      // the darker edge under each tile, like the board's blocks
        const float HeroHeight = 178f;
        const float HalfHeight = 180f;
        const float AwardHeight = 276f;
        const float HeroBadgeSize = 140f;
        const float HalfBadgeSize = 104f;
        const float AwardBadgeSize = 132f;
        const float DetailWidth = 860f;
        const float DetailEmblemSize = 230f;
        const float StripEmblemSize = 96f;
        const float BarHeight = 16f;

        static readonly Color WellColor = new Color32(0x11, 0x18, 0x27, 0xFF);
        static readonly Color TileFace = new Color32(0x1E, 0x28, 0x40, 0xFF);
        static readonly Color TileLipColor = new Color32(0x13, 0x1A, 0x2B, 0xFF);
        static readonly Color EmptyBlock = new Color32(0x2A, 0x33, 0x49, 0xFF);
        static readonly Color LabelColor = new Color32(0xC9, 0xD1, 0xE0, 0xFF);
        static readonly Color SectionColor = new Color32(0xAE, 0xB8, 0xCE, 0xFF);
        static readonly Color TagTextColor = new Color32(0x2A, 0x22, 0x10, 0xFF);
        static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        static readonly Color LockedTint = new Color32(0x3A, 0x44, 0x5E, 0xFF);   // a badge not earned yet: a dark shape
        static readonly Color LockedTierTint = new Color32(0x3A, 0x44, 0x5E, 0x80); // half see-through, so the strip's bright tiers go dark too

        public bool IsOpen { get; private set; }
        public event Action Closed;

        public void Initialize(AchievementService service, TMP_FontAsset font = null)
        {
            _service = service;
            _font = font != null ? font : TMP_Settings.defaultFontAsset;
            Build();
            if (_service != null) _service.Changed += OnChanged;
        }

        void OnDestroy()
        {
            if (_service != null) _service.Changed -= OnChanged;
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
            Transform records = AddWell(list, "Records", "PERSONAL RECORDS", out _recordsCount);
            Transform awards = AddWell(list, "Awards", "AWARDS", out _awardsCount);
            _awardsGrid = AddGrid(awards);

            Transform halves = null;
            bool heroDone = false;
            foreach (var def in AchievementCatalog.All)
            {
                if (!def.IsRecord)
                {
                    _tiles.Add(BuildTile(_awardsGrid.transform, def, Kind.Award));
                    continue;
                }
                if (!heroDone)
                {
                    heroDone = true; // the first record, best score, leads
                    _tiles.Add(BuildTile(records, def, Kind.Hero));
                    continue;
                }
                if (halves == null) halves = AddTileRow(records);
                _tiles.Add(BuildTile(halves, def, Kind.Half));
            }

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
            scrollLayout.minHeight = HeroHeight;
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

        /// <summary>A darker inset that groups a section: its title and count on top, its tiles below.</summary>
        Transform AddWell(Transform parent, string name, string title, out TextMeshProUGUI count)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var bg = obj.AddComponent<Image>();
            bg.color = WellColor;
            bg.raycastTarget = false;
            UiSprites.ApplyRounded(bg, WellRadius);
            var v = obj.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(WellPadSide, WellPadSide, WellPadTop, WellPadBottom);
            v.spacing = WellSpacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(obj.transform, false);
            SetFixedHeight(header, WellHeaderHeight);
            var label = AddText(header.transform, "Title", title, 20, FontStyles.Bold, SectionColor);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.characterSpacing = 6f; // matches the profile card's labels (ALL-TIME HIGH, STREAK)
            count = AddText(header.transform, "Count", "", 20, FontStyles.Bold, UiSkin.TextMuted);
            Stretch(count.rectTransform);
            count.alignment = TextAlignmentOptions.MidlineRight;
            count.textWrappingMode = TextWrappingModes.NoWrap;
            return obj.transform;
        }

        static Transform AddTileRow(Transform parent)
        {
            var obj = new GameObject("Row", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            SetFixedHeight(obj, HalfHeight);
            var h = obj.AddComponent<HorizontalLayoutGroup>();
            h.spacing = TileGap;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            return obj.transform;
        }

        static GridLayoutGroup AddGrid(Transform parent)
        {
            var obj = new GameObject("Grid", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var grid = obj.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.spacing = new Vector2(TileGap, TileGap);
            grid.cellSize = new Vector2(260f, AwardHeight); // width set to fit in ResizeDialog
            return grid;
        }

        Tile BuildTile(Transform parent, AchievementDef def, Kind kind)
        {
            var tile = new Tile { Def = def, Kind = kind };
            var obj = new GameObject(kind + "_" + def.Key, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            if (kind == Kind.Hero) SetFixedHeight(obj, HeroHeight);

            // A raised tile: the face sits on a darker lip, like the board's blocks.
            var lip = obj.AddComponent<Image>();
            lip.color = TileLipColor;
            UiSprites.ApplyRounded(lip, TileRadius);
            var faceRect = AddStretched(obj.transform, "Face");
            faceRect.offsetMin = new Vector2(0f, TileLip);
            var face = faceRect.gameObject.AddComponent<Image>();
            face.color = TileFace;
            UiSprites.ApplyRounded(face, TileRadius);
            var button = obj.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnTileTapped(tile));
            Transform f = faceRect;

            switch (kind)
            {
                case Kind.Hero:
                    tile.Badge = AddBadge(f, new Vector2(0f, 0.5f), new Vector2(20f, 0f), HeroBadgeSize);
                    tile.Name = AddLine(f, 180f, 20f, 18f, 28f, 22, LabelColor, TextAlignmentOptions.MidlineLeft);
                    tile.Name.text = def.Name;
                    tile.Date = AddLine(f, 180f, 20f, 18f, 28f, 18, UiSkin.TextMuted, TextAlignmentOptions.MidlineRight);
                    tile.Date.fontStyle = FontStyles.Normal;
                    tile.Value = AddLine(f, 180f, 20f, 44f, 64f, 56, Color.white, TextAlignmentOptions.MidlineLeft, 32);
                    tile.Meter = AddMeter(f, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, -120f), 22f, 5f);
                    float meterWidth = MeterWidth(22f, 5f);
                    tile.Count = AddLine(f, 180f + meterWidth + 14f, 20f, 120f, 22f, 18, UiSkin.TextMuted, TextAlignmentOptions.MidlineLeft);
                    tile.Count.fontStyle = FontStyles.Normal;
                    break;
                case Kind.Half:
                    tile.Badge = AddBadge(f, new Vector2(0f, 0.5f), new Vector2(14f, 0f), HalfBadgeSize);
                    tile.Name = AddLine(f, 132f, 14f, 20f, 26f, 20, LabelColor, TextAlignmentOptions.MidlineLeft, 16);
                    tile.Name.text = def.Name;
                    tile.Value = AddLine(f, 132f, 14f, 46f, 48f, 40, Color.white, TextAlignmentOptions.MidlineLeft, 26);
                    tile.Date = AddLine(f, 132f, 14f, 96f, 24f, 18, UiSkin.TextMuted, TextAlignmentOptions.MidlineLeft, 14);
                    tile.Date.fontStyle = FontStyles.Normal;
                    tile.Meter = AddMeter(f, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(132f, -128f), 18f, 4f);
                    break;
                default:
                    tile.Badge = AddBadge(f, new Vector2(0.5f, 1f), new Vector2(0f, -18f), AwardBadgeSize);
                    tile.Name = AddLine(f, 8f, 8f, 160f, 28f, 21, Color.white, TextAlignmentOptions.Center, 16);
                    tile.Name.text = def.Name;
                    tile.Meter = AddMeter(f, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -198f), 18f, 4f);
                    tile.Count = AddLine(f, 8f, 8f, 226f, 26f, 18, UiSkin.TextMuted, TextAlignmentOptions.Center, 14);
                    tile.Count.fontStyle = FontStyles.Normal;
                    break;
            }

            // The badge on the profile: a gold edge, and a tag sitting on the tile's top edge.
            var outline = AddStretched(obj.transform, "OnProfile").gameObject.AddComponent<Image>();
            UiSprites.ApplyRoundedOutline(outline, TileRadius);
            outline.color = UiSkin.Accent;
            outline.raycastTarget = false;
            tile.Outline = outline.gameObject;

            var tagObj = new GameObject("Tag", typeof(RectTransform));
            tagObj.transform.SetParent(obj.transform, false);
            var tag = (RectTransform)tagObj.transform;
            tag.anchorMin = tag.anchorMax = new Vector2(1f, 1f);
            tag.pivot = new Vector2(1f, 0.5f);
            tag.anchoredPosition = new Vector2(-16f, 0f);
            tag.sizeDelta = new Vector2(128f, 30f);
            var tagBg = tagObj.AddComponent<Image>();
            tagBg.color = UiSkin.Accent;
            tagBg.raycastTarget = false;
            UiSprites.ApplyRounded(tagBg, 8f);
            var tagText = AddText(tagObj.transform, "Text", "ON PROFILE", 16, FontStyles.Bold, TagTextColor);
            Stretch(tagText.rectTransform);
            tagText.alignment = TextAlignmentOptions.Center;
            tagText.textWrappingMode = TextWrappingModes.NoWrap;
            tagText.characterSpacing = 4f;
            tile.Tag = tagObj;
            return tile;
        }

        static Image AddBadge(Transform parent, Vector2 anchor, Vector2 position, float size)
        {
            var obj = new GameObject("Badge", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rt = (RectTransform)obj.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(size, size);
            var image = obj.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>One line of text across the face, inset left and right, top units down from its top edge.</summary>
        TextMeshProUGUI AddLine(Transform parent, float left, float right, float top, float height, float size, Color color,
            TextAlignmentOptions alignment, float minSize = 0f)
        {
            var t = AddText(parent, "Text", "", size, FontStyles.Bold, color);
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
            t.alignment = alignment;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            if (minSize > 0f)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = size;
                t.fontSizeMin = minSize;
            }
            return t;
        }

        /// <summary>Six small blocks, one per tier, filled in the badge's colour as tiers are earned.</summary>
        static Image[] AddMeter(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, float block, float gap)
        {
            var obj = new GameObject("Tiers", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rt = (RectTransform)obj.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(MeterWidth(block, gap), block);
            var blocks = new Image[AchievementCatalog.Tiers];
            for (int i = 0; i < blocks.Length; i++)
            {
                var b = new GameObject("Tier" + (i + 1), typeof(RectTransform));
                b.transform.SetParent(obj.transform, false);
                var brt = (RectTransform)b.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = new Vector2(i * (block + gap), 0f);
                brt.sizeDelta = new Vector2(block, block);
                blocks[i] = b.AddComponent<Image>();
                blocks[i].sprite = UiSprites.Tile;
                blocks[i].raycastTarget = false;
            }
            return blocks;
        }

        static float MeterWidth(float block, float gap) => AchievementCatalog.Tiers * block + (AchievementCatalog.Tiers - 1) * gap;

        static void SetMeter(Image[] blocks, int tier, Color color)
        {
            for (int i = 0; i < blocks.Length; i++) blocks[i].color = i < tier ? color : EmptyBlock;
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
            _detailBadge = AddBadge(holder.transform, new Vector2(0.5f, 0.5f), Vector2.zero, DetailEmblemSize);

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
            _detailBarFillImage = fillObj.AddComponent<Image>();
            _detailBarFillImage.color = UiSkin.Teal;
            _detailBarFillImage.raycastTarget = false;
            UiSprites.ApplyRounded(_detailBarFillImage, BarHeight * 0.5f);

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

        /// <summary>All six badges of the achievement in a row, each with the goal that earns it underneath. These keep
        /// the art's own size, so higher tiers stand bigger.</summary>
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
            int records = 0, recordTiers = 0, awards = 0, awardTiers = 0;
            foreach (var tile in _tiles)
            {
                var def = tile.Def;
                int tier = AchievementCatalog.TierOf(p, def);
                int value = def.Value(p);
                bool earned = tier > 0;
                bool onProfile = earned && p.ShownBadge == def.Key;
                if (def.IsRecord) { records += tier; recordTiers += AchievementCatalog.Tiers; }
                else { awards += tier; awardTiers += AchievementCatalog.Tiers; }

                tile.Badge.sprite = BadgeArt.Trimmed(def.Key, Mathf.Max(1, tier));
                tile.Badge.color = earned ? Color.white : LockedTint;
                SetMeter(tile.Meter, tier, BadgeArt.ColorOf(def.Key));
                tile.Outline.SetActive(onProfile);
                tile.Tag.SetActive(onProfile);

                if (def.IsRecord)
                {
                    tile.Value.text = RecordText(def, value);
                    tile.Date.text = value <= 0 ? "No record yet" : FormatDate(def.SetAtTicks(p)) ?? "All-time best";
                    if (tile.Count != null) tile.Count.text = earned ? $"Tier {tier} of {AchievementCatalog.Tiers}" : "No badge yet";
                }
                else
                {
                    tile.Name.color = earned ? Color.white : UiSkin.TextMuted;
                    tile.Count.text = tier >= AchievementCatalog.Tiers ? "Every tier earned" : $"{N(value)} / {N(def.Goals[tier])}";
                }
            }
            _recordsCount.text = $"{records} / {recordTiers}";
            _awardsCount.text = $"{awards} / {awardTiers}";
        }

        static string RecordText(AchievementDef def, int value)
        {
            if (def.Key == "streak") return value == 1 ? "1 day" : $"{N(Mathf.Max(0, value))} days";
            return N(Mathf.Max(0, value));
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

            _detailBadge.sprite = BadgeArt.Trimmed(def.Key, Mathf.Max(1, tier));
            _detailBadge.color = earned ? Color.white : LockedTint;
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
                _detailBarFillImage.color = BadgeArt.ColorOf(def.Key);
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
            var badge = tile.Badge.rectTransform;
            badge.DOKill(true);
            badge.DOPunchScale(Vector3.one * 0.08f, 0.25f, 8, 0.6f).SetUpdate(true);
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

        /// <summary>Fits the visible (safe-area) height and width; three awards to a row, the list scrolls inside.</summary>
        void ResizeDialog()
        {
            Rect area = _overlayRect.rect;
            float maxWidth = area.width > 0f ? area.width - 2 * ScreenMargin : DialogWidth;
            float width = Mathf.Min(DialogWidth, maxWidth);
            float inner = width - 2 * DialogPadding - 2 * WellPadSide;
            float cellWidth = Mathf.Floor((inner - (Columns - 1) * TileGap) / Columns);
            _awardsGrid.cellSize = new Vector2(cellWidth, AwardHeight);

            int awards = _awardsGrid.transform.childCount;
            int rows = (awards + Columns - 1) / Columns;
            float well = WellPadTop + WellHeaderHeight + WellSpacing + WellPadBottom;
            float recordsWell = well + HeroHeight + WellSpacing + HalfHeight;
            float awardsWell = well + rows * AwardHeight + Mathf.Max(0, rows - 1) * TileGap;
            float list = recordsWell + ListSpacing + awardsWell;

            float safeFraction = Screen.height > 0 ? Mathf.Clamp01(Screen.safeArea.height / Screen.height) : 1f;
            float desired = 2 * DialogPadding + HeaderHeight + HintHeight + list + CloseButtonHeight + 3 * SectionSpacing;
            float maxHeight = area.height > 0f ? area.height * safeFraction - 2 * ScreenMargin : desired;
            _dialogPanel.sizeDelta = new Vector2(width, Mathf.Min(desired, maxHeight));
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

        static RectTransform AddStretched(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rt = (RectTransform)obj.transform;
            Stretch(rt);
            return rt;
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

using System;
using DG.Tweening;
using StackSurge.Core;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// The profile card on the main menu. Its top band takes the colour of the badge on show (or of the player's block
    /// when there's none), and the badge stands on a raised tile over the band's edge with the name beside it; the best
    /// scores, the streak and the nickname field follow. It moves at three moments: when the menu opens (the colour
    /// fills in and the badge lands), when another badge goes on show (the card repaints), and when the badge on show
    /// goes up a tier (the logo's bolt crosses the band and the badge hops up a tier).
    /// The scores, streak, name and nickname field are the menu prefab's own components, moved into this layout.
    /// </summary>
    public class ProfileCardView : MonoBehaviour
    {
        // Layout in the card's units, from its top-left corner (the prefab card is 750 x 420).
        const float CardHeight = 420f;
        const float CardRadius = 32f;
        const float Pad = 22f;
        const float BandHeight = 160f;
        const float LipHeight = 6f;
        const float PlateTop = 32f;
        const float PlateSize = 194f;
        const float ArtBox = 255f;      // the badge's longer side: bigger than its tile, so it stands out of it
        const float ArtBase = 25f;      // the badge's bottom edge, above the tile's
        const float BlockSize = 116f;
        const float TextLeft = 236f;   // the stat rows
        const float NameLeft = 256f;   // the name and caption, clear of a wide badge's wings
        const float NameTop = 26f;
        const float NameHeight = 62f;
        const float CaptionTop = 92f;
        const float CaptionHeight = 32f;
        const float RowTop = 178f;
        const float RowHeight = 62f;
        const float RowStep = 72f;
        const float StreakTop = 240f;
        const float StreakHeight = 72f;
        const float InputTop = 334f;
        const float InputHeight = 60f;
        const float SaveWidth = 118f;
        const float BoltWidth = 118f, BoltHeight = 181f;

        // Motion
        const float DropHeight = 300f;
        const float DropTime = 0.6f;
        const float FirstTouch = DropTime / 2.75f; // Ease.OutBounce first reaches the bottom here
        const float HopHeight = 63f;

        static readonly Color PlateFace = new Color32(0x26, 0x30, 0x49, 0xFF);
        static readonly Color LabelColor = new Color32(0xC9, 0xD1, 0xE0, 0xFF);
        static readonly Color RowStroke = new Color32(0x3A, 0x42, 0x58, 0xFF);
        static readonly Color StreakFill = new Color32(0x43, 0x3E, 0x2F, 0xFF);
        static readonly Color StreakStroke = new Color32(0x6E, 0x5B, 0x2E, 0xFF);

        /// <summary>The badge or block on the tile. There are two, so the old one can sink while the new one drops.</summary>
        class Look
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Art;
            public Image Block;
            public Image Glyph;
        }

        RectTransform _card;
        Color _cardColor;
        TMP_FontAsset _font;
        float _width;

        RectTransform _wipe;
        Image _bandFrom, _lipFrom, _bandTo, _lipTo;
        RectTransform _bolt;
        Image _boltImage;
        RectTransform _plate;
        Image _ring;
        RectTransform _sink;
        Look _current, _spare;
        RectTransform _identity;
        CanvasGroup _identityGroup;
        TextMeshProUGUI _caption;

        string _shownBadge, _shownId;   // what the card shows now
        string _badge = "", _playerId = ""; // what it should show
        bool _drawn;
        Sequence _motion;
        string _motionBadge, _motionId; // the look the running motion ends on

        /// <summary>Builds the card's layout over the prefab card and moves the prefab's own fields into it.</summary>
        public static ProfileCardView Build(RectTransform card, TMP_FontAsset font, TMP_Text name, TMP_Text allTime,
            TMP_Text dailyBest, TMP_Text streak, TMP_InputField nameInput, Button save, Action onEmblemTapped)
        {
            var view = card.gameObject.AddComponent<ProfileCardView>();
            view.Create(font, name, allTime, dailyBest, streak, nameInput, save, onEmblemTapped);
            return view;
        }

        void Create(TMP_FontAsset font, TMP_Text name, TMP_Text allTime, TMP_Text dailyBest, TMP_Text streak,
            TMP_InputField nameInput, Button save, Action onEmblemTapped)
        {
            _card = (RectTransform)transform;
            _font = font;
            _card.sizeDelta = new Vector2(_card.sizeDelta.x, CardHeight);
            _width = _card.sizeDelta.x;
            var cardImage = GetComponent<Image>();
            _cardColor = cardImage != null ? cardImage.color : UiSkin.Surface;
            if (cardImage != null) UiSprites.ApplyRounded(cardImage, CardRadius);

            BuildBand();
            BuildPlate(onEmblemTapped);
            BuildIdentity(name);
            StatRow("AllTimeRow", "ALL-TIME HIGH", allTime, RowTop);
            StatRow("DailyBestRow", "DAILY BEST", dailyBest, RowTop + RowStep);
            StreakTile(streak);
            if (nameInput != null) Place((RectTransform)nameInput.transform, Pad, InputTop, _width - 2 * Pad - SaveWidth - 12f, InputHeight, transform);
            if (save != null) Place((RectTransform)save.transform, _width - Pad - SaveWidth, InputTop, SaveWidth, InputHeight, transform);

            // The prefab's own header, stat boxes, streak row and name label give way to the layout above.
            foreach (string old in new[] { "CardHeader", "StatsText", "HighScores", "Streak", "Divider", "NameLabelTitle" })
                if (transform.Find(old) is Transform t) t.gameObject.SetActive(false);
        }

        // ── Build ───────────────────────────────────────────────────────
        void BuildBand()
        {
            var band = Box("Band", transform, 0f, 0f, _width, BandHeight + LipHeight);
            band.gameObject.AddComponent<RectMask2D>(); // square bottom; the card's rounded top shows through the shape below
            band.SetAsFirstSibling();
            (_bandFrom, _lipFrom) = BandLayer(band);
            _wipe = Box("Wipe", band, 0f, 0f, _width, BandHeight + LipHeight);
            _wipe.gameObject.AddComponent<RectMask2D>();
            (_bandTo, _lipTo) = BandLayer(_wipe);

            _bolt = Box("Bolt", band, 0f, -13f, BoltWidth, BoltHeight);
            _boltImage = _bolt.gameObject.AddComponent<Image>();
            _boltImage.sprite = UiSprites.Bolt;
            _boltImage.raycastTarget = false;
            _bolt.gameObject.SetActive(false);
        }

        (Image band, Image lip) BandLayer(Transform parent)
        {
            // Taller than the band, so only its top corners are rounded once the mask cuts it.
            var bandImage = Box("Colour", parent, 0f, 0f, _width, BandHeight + LipHeight + 2 * CardRadius).gameObject.AddComponent<Image>();
            UiSprites.ApplyRounded(bandImage, CardRadius);
            bandImage.raycastTarget = false;
            var lip = Box("Lip", parent, 0f, BandHeight, _width, LipHeight).gameObject.AddComponent<Image>();
            lip.raycastTarget = false;
            return (bandImage, lip);
        }

        void BuildPlate(Action onTapped)
        {
            _plate = Box("Emblem", transform, Pad, PlateTop, PlateSize, PlateSize);
            _plate.pivot = new Vector2(0.5f, 0f); // squashes onto its bottom edge when the badge lands
            _plate.anchoredPosition = new Vector2(Pad + PlateSize * 0.5f, -(PlateTop + PlateSize));
            var tile = _plate.gameObject.AddComponent<Image>();
            tile.sprite = UiSprites.Tile;
            tile.color = PlateFace;
            var button = _plate.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onTapped?.Invoke());

            _ring = Stretch("Ring", _plate).gameObject.AddComponent<Image>();
            UiSprites.ApplyRoundedOutline(_ring, PlateSize * 22f / 128f); // the tile sprite's corner radius at this size
            _ring.raycastTarget = false;

            // The old badge sinks into the tile: cut off at the face's bottom edge only, as it stands out above and to the sides.
            _sink = Stretch("Sink", _plate);
            _sink.offsetMin = new Vector2(-PlateSize, PlateSize * 7f / 128f);
            _sink.offsetMax = new Vector2(PlateSize, PlateSize * 2f);
            _sink.gameObject.AddComponent<RectMask2D>();

            _current = BuildLook(_plate);
            _spare = BuildLook(_plate);
            _spare.Group.alpha = 0f;
        }

        Look BuildLook(Transform parent)
        {
            var root = Stretch("Look", parent);
            var look = new Look { Root = root, Group = root.gameObject.AddComponent<CanvasGroup>() };
            look.Group.blocksRaycasts = false;
            float lift = PlateSize * PlayerEmblem.FaceLift;
            look.Art = Centered("Badge", root, ArtBox, lift);
            look.Art.sprite = UiSprites.Circle; // replaced in Fill; a sprite-less image would get rounded by UiSkin
            var art = look.Art.rectTransform; // stands on a line near the tile's bottom; sized to the art in Fill
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0f);
            art.anchoredPosition = new Vector2(0f, ArtBase);
            look.Block = Centered("Block", root, BlockSize, lift);
            look.Block.sprite = UiSprites.Tile;
            look.Glyph = Centered("Glyph", look.Block.transform, BlockSize * PlayerEmblem.GlyphFill, BlockSize * PlayerEmblem.FaceLift);
            look.Glyph.sprite = UiSprites.Circle;
            look.Glyph.preserveAspect = true;
            return look;
        }

        void BuildIdentity(TMP_Text name)
        {
            float width = _width - NameLeft - Pad;
            _identity = Box("Identity", transform, NameLeft, NameTop, width, CaptionTop + CaptionHeight - NameTop);
            _identityGroup = _identity.gameObject.AddComponent<CanvasGroup>();
            _identityGroup.blocksRaycasts = false;
            if (name != null)
            {
                Place(name.rectTransform, 0f, 0f, width, NameHeight, _identity);
                name.font = _font;
                name.fontStyle = FontStyles.Bold;
                name.color = Color.white;
                name.alignment = TextAlignmentOptions.MidlineLeft;
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.enableAutoSizing = true;
                name.fontSizeMax = 46f;
                name.fontSizeMin = 28f;
            }
            _caption = AddText(_identity, "Caption", 22f, new Color(1f, 1f, 1f, 0.82f));
            Place(_caption.rectTransform, 0f, CaptionTop - NameTop, width, CaptionHeight, _identity);
            _caption.rectTransform.pivot = new Vector2(0f, 0.5f); // grows from its left edge on a tier up
            _caption.rectTransform.anchoredPosition = new Vector2(0f, -(CaptionTop - NameTop) - CaptionHeight * 0.5f);
            _caption.alignment = TextAlignmentOptions.MidlineLeft;
        }

        void StatRow(string name, string label, TMP_Text score, float top)
        {
            float width = _width - TextLeft - Pad;
            var row = Box(name, transform, TextLeft, top, width, RowHeight);
            var frame = row.gameObject.AddComponent<Image>();
            UiSprites.ApplyRoundedOutline(frame, 14f);
            frame.color = RowStroke;
            frame.raycastTarget = false;

            var caption = AddText(row, "Label", 19f, LabelColor);
            Place(caption.rectTransform, 20f, 0f, width * 0.55f, RowHeight, row);
            caption.text = label;
            caption.characterSpacing = 4f;
            caption.alignment = TextAlignmentOptions.MidlineLeft;

            if (score == null) return;
            Place(score.rectTransform, width * 0.4f, 0f, width * 0.6f - 20f, RowHeight, row);
            score.alignment = TextAlignmentOptions.MidlineRight;
            score.textWrappingMode = TextWrappingModes.NoWrap;
            score.enableAutoSizing = true;
            score.fontSizeMax = 30f;
            score.fontSizeMin = 20f;
            if (score.TryGetComponent(out LayoutElement element)) element.ignoreLayout = true;
        }

        void StreakTile(TMP_Text streak)
        {
            var tile = Box("StreakTile", transform, Pad, StreakTop, PlateSize, StreakHeight);
            var fill = tile.gameObject.AddComponent<Image>();
            UiSprites.ApplyRounded(fill, 14f);
            fill.color = StreakFill;
            fill.raycastTarget = false;
            var frame = Stretch("Frame", tile).gameObject.AddComponent<Image>();
            UiSprites.ApplyRoundedOutline(frame, 14f);
            frame.color = StreakStroke;
            frame.raycastTarget = false;

            var caption = AddText(tile, "Label", 16f, LabelColor);
            Place(caption.rectTransform, 0f, 8f, PlateSize, 22f, tile);
            caption.text = "STREAK";
            caption.characterSpacing = 4f;
            caption.alignment = TextAlignmentOptions.Center;

            if (streak == null) return;
            Place(streak.rectTransform, 8f, 30f, PlateSize - 16f, 36f, tile);
            streak.alignment = TextAlignmentOptions.Center;
            streak.textWrappingMode = TextWrappingModes.NoWrap;
            streak.enableAutoSizing = true;
            streak.fontSizeMax = 28f;
            streak.fontSizeMin = 18f;
            streak.color = UiSkin.Accent;
            if (streak.TryGetComponent(out LayoutElement element)) element.ignoreLayout = true;
        }

        // ── State ───────────────────────────────────────────────────────
        /// <summary>
        /// Sets what the card shows: a badge ("chain:3", or "" for none) and the player ID that picks the block.
        /// The first call draws it at once. After that a change waits for <see cref="PlayChanges"/> or
        /// <see cref="PlayEntrance"/>, unless animateNow is true (the menu is in view).
        /// </summary>
        public void SetLook(string badge, string playerId, bool animateNow)
        {
            badge ??= "";
            playerId ??= "";
            if (_drawn && badge == _badge && playerId == _playerId) return; // the menu refreshes often; don't cut a motion short
            StopMotion(); // a motion still running ends on the look it was heading for
            _badge = badge;
            _playerId = playerId;
            if (!_drawn)
            {
                _drawn = true;
                Finish();
                return;
            }
            if (animateNow) PlayChanges();
        }

        bool Pending => _badge != _shownBadge || (!HasBadge(_badge) && _playerId != _shownId);

        /// <summary>A motion is already on its way to the look the card should show.</summary>
        bool Heading => _motion != null && _motion.IsActive() && _motionBadge == _badge && _motionId == _playerId;

        /// <summary>The menu just opened: the colour fills in and the badge lands (or a change waiting since the last run plays).</summary>
        public void PlayEntrance()
        {
            if (!_drawn || Heading) return;
            StopMotion();
            if (Pending)
            {
                PlayChanges();
                return;
            }
            Color colour = PlayerEmblem.LookColor(_badge, _playerId);
            SetBand(_bandFrom, _lipFrom, _cardColor, _cardColor);
            SetBand(_bandTo, _lipTo, Band(colour), Lip(colour));
            _wipe.sizeDelta = new Vector2(0f, _wipe.sizeDelta.y);
            _identityGroup.alpha = 0f;
            _identity.anchoredPosition = new Vector2(NameLeft + 36f, _identity.anchoredPosition.y);
            _current.Group.alpha = 0f;
            _current.Root.anchoredPosition = new Vector2(0f, DropHeight);

            StartMotion();
            _motion.Insert(0.05f, Wipe());
            _motion.Insert(0.2f, _identityGroup.DOFade(1f, 0.3f));
            _motion.Insert(0.2f, _identity.DOAnchorPosX(NameLeft, 0.35f).SetEase(Ease.OutCubic));
            Land(_current, 0.3f);
            _motion.OnComplete(Finish);
        }

        /// <summary>Plays a change that waited (a badge picked in Achievements, a tier earned during a run).</summary>
        public void PlayChanges()
        {
            if (!_drawn || Heading) return;
            StopMotion();
            if (!Pending) return;
            bool tierUp = AchievementCatalog.TryParseBadge(_shownBadge, out var was, out int wasTier)
                && AchievementCatalog.TryParseBadge(_badge, out var now, out int nowTier)
                && was.Key == now.Key && nowTier > wasTier;
            if (tierUp) PlayTierUp();
            else PlayRepaint();
        }

        /// <summary>The old badge sinks into its tile, the new colour paints across, and the new badge drops in.</summary>
        void PlayRepaint()
        {
            Color from = PlayerEmblem.LookColor(_shownBadge, _shownId), to = PlayerEmblem.LookColor(_badge, _playerId);
            SetBand(_bandFrom, _lipFrom, Band(from), Lip(from));
            SetBand(_bandTo, _lipTo, Band(to), Lip(to));
            _wipe.sizeDelta = new Vector2(0f, _wipe.sizeDelta.y);

            Look old = _current, next = _spare;
            _current = next;
            _spare = old;
            old.Root.SetParent(_sink, true);
            Rest(next, 0f);
            Fill(next);
            next.Root.SetAsLastSibling();
            next.Group.alpha = 0f;
            next.Root.anchoredPosition = new Vector2(0f, DropHeight);

            StartMotion();
            // Far enough that even a tall badge, standing above its tile, is gone below the face's bottom edge.
            _motion.Insert(0f, old.Root.DOAnchorPosY(old.Root.anchoredPosition.y - (ArtBase + ArtBox) * 1.05f, 0.3f).SetEase(Ease.InCubic));
            _motion.InsertCallback(0.3f, () => old.Group.alpha = 0f);
            _motion.Insert(0.25f, Wipe());
            _motion.InsertCallback(0.3f, () =>
            {
                _caption.text = Caption(_badge);
                _ring.color = Lip(to);
            });
            Land(next, 0.35f);
            _motion.OnComplete(Finish);
        }

        /// <summary>The logo's bolt crosses the band, and the badge hops and comes down as the new tier.</summary>
        void PlayTierUp()
        {
            Color colour = PlayerEmblem.LookColor(_badge, _playerId);
            _bolt.gameObject.SetActive(true);
            Color bolt = Color.Lerp(Band(colour), Color.white, 0.35f);
            bolt.a = 0.85f;
            _boltImage.color = bolt;
            _bolt.anchoredPosition = new Vector2(-BoltWidth - 20f, _bolt.anchoredPosition.y);

            StartMotion();
            _motion.Insert(0.1f, _bolt.DOAnchorPosX(_width + 20f, 0.5f).SetEase(Ease.Linear));
            _motion.Insert(0.3f, _current.Root.DOAnchorPosY(HopHeight, 0.21f).SetEase(Ease.OutQuad));
            _motion.InsertCallback(0.51f, () =>
            {
                Fill(_current);
                _caption.text = Caption(_badge);
                _caption.rectTransform.localScale = Vector3.one * 1.18f;
            });
            _motion.Insert(0.51f, _current.Root.DOAnchorPosY(0f, 0.21f).SetEase(Ease.InQuad));
            _motion.Insert(0.51f, _caption.rectTransform.DOScale(1f, 0.3f).SetEase(Ease.OutCubic));
            _motion.InsertCallback(0.72f, Haptics.Light);
            Squash(0.72f);
            _motion.OnComplete(Finish);
        }

        Tween Wipe() => _wipe.DOSizeDelta(new Vector2(_width, _wipe.sizeDelta.y), 0.4f).SetEase(Ease.OutCubic);

        /// <summary>Drops a look onto the tile from above, with the bounce the board's blocks land with.</summary>
        void Land(Look look, float at)
        {
            _motion.InsertCallback(at, () => look.Group.alpha = 1f);
            _motion.Insert(at, look.Root.DOAnchorPosY(0f, DropTime).SetEase(Ease.OutBounce));
            Squash(at + FirstTouch);
        }

        void Squash(float at)
        {
            _motion.Insert(at, _plate.DOScaleY(0.93f, 0.09f).SetEase(Ease.OutQuad));
            _motion.Insert(at + 0.09f, _plate.DOScaleY(1f, 0.09f).SetEase(Ease.InQuad));
        }

        void StartMotion()
        {
            _motion = DOTween.Sequence().SetUpdate(true);
            _motionBadge = _badge;
            _motionId = _playerId;
        }

        void StopMotion()
        {
            if (_motion != null && _motion.IsActive()) _motion.Kill(true);
            _motion = null;
        }

        /// <summary>Draws the card at rest for the look it should show.</summary>
        void Finish()
        {
            _shownBadge = _badge;
            _shownId = _playerId;
            Color colour = PlayerEmblem.LookColor(_badge, _playerId);
            SetBand(_bandFrom, _lipFrom, Band(colour), Lip(colour));
            SetBand(_bandTo, _lipTo, Band(colour), Lip(colour));
            _wipe.sizeDelta = new Vector2(_width, _wipe.sizeDelta.y);
            _ring.color = Lip(colour);
            _bolt.gameObject.SetActive(false);
            _plate.localScale = Vector3.one;
            _identityGroup.alpha = 1f;
            _identity.anchoredPosition = new Vector2(NameLeft, _identity.anchoredPosition.y);
            _caption.text = Caption(_badge);
            _caption.rectTransform.localScale = Vector3.one;

            Rest(_spare, 0f);
            Fill(_current);
            Rest(_current, 1f);
        }

        void Rest(Look look, float alpha)
        {
            if (look.Root.parent != _plate) look.Root.SetParent(_plate, false);
            look.Root.anchorMin = Vector2.zero;
            look.Root.anchorMax = Vector2.one;
            look.Root.offsetMin = look.Root.offsetMax = Vector2.zero;
            look.Group.alpha = alpha;
        }

        void Fill(Look look)
        {
            Sprite badge = BadgeArt.TrimmedForBadge(_badge);
            look.Art.enabled = badge != null;
            look.Art.sprite = badge;
            if (badge != null)
            {
                // The longer side gets the full box, so wide and tall badges come out the same size.
                Vector2 art = badge.rect.size;
                float scale = ArtBox / Mathf.Max(art.x, art.y);
                look.Art.rectTransform.sizeDelta = art * scale;
            }
            look.Block.enabled = badge == null;
            look.Glyph.enabled = badge == null;
            if (badge != null) return;
            TileKind kind = PlayerEmblem.BlockFor(_playerId);
            look.Block.color = PlayerEmblem.BlockColor(kind);
            look.Glyph.sprite = UiSprites.GlyphFor(kind);
            look.Glyph.color = PlayerEmblem.GlyphColor(kind);
        }

        static bool HasBadge(string badge) => AchievementCatalog.TryParseBadge(badge, out _, out _);

        static string Caption(string badge) =>
            AchievementCatalog.TryParseBadge(badge, out var def, out int tier) ? $"{def.Name}, tier {tier}" : "No badge yet";

        static Color Band(Color c) => Shade(c, 0.6f);
        static Color Lip(Color c) => Shade(c, 0.42f);
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        static void SetBand(Image band, Image lip, Color bandColor, Color lipColor)
        {
            band.color = bandColor;
            lip.color = lipColor;
        }

        void OnDestroy() => StopMotion();

        // ── Helpers ─────────────────────────────────────────────────────
        /// <summary>A child rect at (x, y) from the parent's top-left corner.</summary>
        static RectTransform Box(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Size(rt, x, y, w, h);
            return rt;
        }

        /// <summary>Moves an existing rect (one of the prefab's) under parent, at (x, y) from its top-left corner.</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h, Transform parent)
        {
            if (rt.parent != parent) rt.SetParent(parent, false);
            Size(rt, x, y, w, h);
        }

        static void Size(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image Centered(string name, Transform parent, float size, float lift)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(0f, lift);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI AddText(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }
    }
}

using StackSurge.Core;
using StackSurge.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// What stands beside a player's name: the badge they show, on a raised tile, or, with no badge on show, their own
    /// block from the board. The block is picked from the player ID, so every phone draws the same one for a player,
    /// including players on a version without badges. The wild block isn't one of them.
    /// </summary>
    public class PlayerEmblem : MonoBehaviour
    {
        static readonly TileKind[] Blocks = { TileKind.Red, TileKind.Blue, TileKind.Green, TileKind.Yellow, TileKind.Purple };

        /// <summary>The tile a badge stands on, lighter than the rows and cards so dark badges stand out.</summary>
        public static readonly Color PlateColor = new Color32(0x2B, 0x36, 0x53, 0xFF);
        /// <summary>The share of the tile the badge art fills.</summary>
        public const float ArtFill = 0.84f;
        /// <summary>The glyph on a block, as on the board.</summary>
        public const float GlyphFill = 0.5f;
        /// <summary>How far the tile's face sits above its centre, as a share of its height (the tile sprite has a lip).</summary>
        public const float FaceLift = 0.027f;

        Image _tile;
        Image _art;
        Image _glyph;

        public bool HasBadge { get; private set; }

        /// <summary>The player's block: one of the five coloured blocks, the same for a player ID everywhere.</summary>
        public static TileKind BlockFor(string playerId)
        {
            uint hash = 2166136261; // FNV-1a: string.GetHashCode isn't the same on every runtime
            if (!string.IsNullOrEmpty(playerId))
                foreach (char c in playerId)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
            return Blocks[hash % (uint)Blocks.Length];
        }

        public static Color BlockColor(TileKind kind) => GameView.TileColor(kind);
        public static Color GlyphColor(TileKind kind) => GameView.TileGlyphColor(kind);

        /// <summary>The colour of a player's look: their badge's, or their block's when they show none.</summary>
        public static Color LookColor(string badge, string playerId) =>
            AchievementCatalog.TryParseBadge(badge, out var def, out _) ? BadgeArt.ColorOf(def.Key) : BlockColor(BlockFor(playerId));

        /// <summary>A square emblem of the given size, anchored at the parent's left middle. Nothing shows until <see cref="Set"/>.</summary>
        public static PlayerEmblem Create(Transform parent, float size)
        {
            var go = new GameObject("Emblem", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            var emblem = go.AddComponent<PlayerEmblem>();

            emblem._tile = AddImage(go.transform, "Tile", size);
            emblem._tile.sprite = UiSprites.Tile;
            emblem._art = AddImage(go.transform, "Badge", size * ArtFill);
            emblem._art.preserveAspect = true;
            emblem._glyph = AddImage(go.transform, "Glyph", size * GlyphFill);
            emblem._glyph.preserveAspect = true;
            float lift = size * FaceLift;
            emblem._art.rectTransform.anchoredPosition = new Vector2(0f, lift);
            emblem._glyph.rectTransform.anchoredPosition = new Vector2(0f, lift);
            emblem.Clear();
            return emblem;
        }

        /// <summary>Shows the badge ("chain:3"), or the player's block when the badge is "" or unknown.</summary>
        public void Set(string badge, string playerId)
        {
            Sprite sprite = BadgeArt.TrimmedForBadge(badge);
            HasBadge = sprite != null;
            _tile.enabled = true;
            if (HasBadge)
            {
                _tile.color = PlateColor;
                _art.sprite = sprite;
                _art.enabled = true;
                _glyph.enabled = false;
                return;
            }
            TileKind kind = BlockFor(playerId);
            _tile.color = BlockColor(kind);
            _art.enabled = false;
            _glyph.sprite = UiSprites.GlyphFor(kind);
            _glyph.color = GlyphColor(kind);
            _glyph.enabled = _glyph.sprite != null;
        }

        /// <summary>Keeps the space but shows nothing (while a player's badge is still loading).</summary>
        public void Clear()
        {
            HasBadge = false;
            _tile.enabled = _art.enabled = _glyph.enabled = false;
        }

        static Image AddImage(Transform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            var image = go.AddComponent<Image>();
            image.sprite = UiSprites.Circle; // replaced in Set; a sprite-less image would get rounded by UiSkin
            image.raycastTarget = false;
            return image;
        }
    }
}

using System.Collections.Generic;
using StackSurge.Meta;
using UnityEngine;

namespace StackSurge.UI
{
    /// <summary>
    /// Badge and achievement icon sprites, loaded from Resources/Badges on first use.
    /// Badges: RhosGFX "Vector Ranks" (CC0), steps 1, 3, 5, 7, 9 and 12 of one shape and colour per achievement.
    /// Icons: gvesster "Free Icon Pack" (used unedited, as its licence requires). Licences: Assets/Art/Badges.
    /// </summary>
    public static class BadgeArt
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static readonly Dictionary<Sprite, Sprite> TrimmedCache = new Dictionary<Sprite, Sprite>();

        /// <summary>The badge for an achievement's tier (1-6), or null. The art grows with the tier inside a fixed
        /// square (tier 1 fills about 40% of it), which the tier strip uses to show rank by size.</summary>
        public static Sprite Emblem(string key, int tier) =>
            tier < 1 || tier > AchievementCatalog.Tiers ? null : Load($"Badges/{key}_{tier}");

        /// <summary>The same badge cut to the art's own bounds, so it fills its box whatever the tier.</summary>
        public static Sprite Trimmed(string key, int tier) => Trim(Emblem(key, tier));

        /// <summary>The trimmed badge for a published value like "chain:3", or null for none.</summary>
        public static Sprite TrimmedForBadge(string badge) =>
            AchievementCatalog.TryParseBadge(badge, out var def, out int tier) ? Trimmed(def.Key, tier) : null;

        public static Sprite Icon(string key) => Load($"Badges/Icons/{key}");

        /// <summary>The main colour of an achievement's badge art (tier meters, the profile card's band).</summary>
        public static Color ColorOf(string key) => key switch
        {
            "score" => new Color32(0xF2, 0xB5, 0x3A, 0xFF),
            "chain" => new Color32(0x4F, 0xD6, 0xC8, 0xFF),
            "perfect" => new Color32(0x3D, 0x7B, 0xF0, 0xFF),
            "rows" => new Color32(0xA3, 0xB1, 0xC2, 0xFF),
            "bombs" => new Color32(0xE0, 0x39, 0x4E, 0xFF),
            "runs" => new Color32(0xE0, 0x70, 0x48, 0xFF),
            "streak" => new Color32(0xDD, 0x55, 0x40, 0xFF),
            "daily" => new Color32(0x8A, 0x5C, 0xE6, 0xFF),
            _ => UiSkin.Primary
        };

        static Sprite Load(string path)
        {
            if (Cache.TryGetValue(path, out var sprite)) return sprite;
            sprite = Resources.Load<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[BadgeArt] Missing sprite: Resources/" + path);
            Cache[path] = sprite;
            return sprite;
        }

        /// <summary>
        /// A sprite over the same texture, cut to the bounds of the sprite's tight mesh (the importer's outline of the
        /// art). The texture needn't be readable. Returns the sprite itself when it has no mesh or is packed in an atlas.
        /// </summary>
        static Sprite Trim(Sprite sprite)
        {
            if (sprite == null) return null;
            if (TrimmedCache.TryGetValue(sprite, out var trimmed)) return trimmed;
            trimmed = sprite;
            var vertices = sprite.vertices;
            if (!sprite.packed && vertices != null && vertices.Length > 0)
            {
                Vector2 min = vertices[0], max = vertices[0];
                foreach (var v in vertices)
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
                float ppu = sprite.pixelsPerUnit;
                Rect full = sprite.rect;
                Vector2 origin = full.position + sprite.pivot; // the pivot in texture pixels
                float x0 = Mathf.Max(full.xMin, Mathf.Floor(origin.x + min.x * ppu));
                float y0 = Mathf.Max(full.yMin, Mathf.Floor(origin.y + min.y * ppu));
                float x1 = Mathf.Min(full.xMax, Mathf.Ceil(origin.x + max.x * ppu));
                float y1 = Mathf.Min(full.yMax, Mathf.Ceil(origin.y + max.y * ppu));
                if (x1 - x0 >= 4f && y1 - y0 >= 4f)
                {
                    trimmed = Sprite.Create(sprite.texture, Rect.MinMaxRect(x0, y0, x1, y1), new Vector2(0.5f, 0.5f), ppu, 0,
                        SpriteMeshType.FullRect);
                    trimmed.name = sprite.name + " (trimmed)";
                }
            }
            TrimmedCache[sprite] = trimmed;
            return trimmed;
        }
    }
}

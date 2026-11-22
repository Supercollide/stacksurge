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

        /// <summary>The badge for an achievement's tier (1-6), or null.</summary>
        public static Sprite Emblem(string key, int tier) =>
            tier < 1 || tier > AchievementCatalog.Tiers ? null : Load($"Badges/{key}_{tier}");

        /// <summary>The badge for a published value like "chain:3", or null for none.</summary>
        public static Sprite ForBadge(string badge) =>
            AchievementCatalog.TryParseBadge(badge, out var def, out int tier) ? Emblem(def.Key, tier) : null;

        public static Sprite Icon(string key) => Load($"Badges/Icons/{key}");

        static Sprite Load(string path)
        {
            if (Cache.TryGetValue(path, out var sprite)) return sprite;
            sprite = Resources.Load<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[BadgeArt] Missing sprite: Resources/" + path);
            Cache[path] = sprite;
            return sprite;
        }
    }
}

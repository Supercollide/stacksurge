using System;
using UnityEngine;

namespace StackSurge.UI
{
    /// <summary>
    /// Device-local game feel preferences (Settings dialog and the main-menu vibration toggle), stored in
    /// PlayerPrefs. <see cref="Changed"/> fires after any of them changes so open screens can refresh.
    /// </summary>
    public static class GamePrefs
    {
        const string SoundKey = "SoundEnabled";      // old on/off switch; only read to seed the volume
        const string SfxVolumeKey = "SfxVolume";
        const string VibrationKey = "VibrationEnabled"; // existing key used by the main-menu toggle
        const string TileShapesKey = "TileShapesEnabled";

        public static event Action Changed;

        // Read on every sound and haptic, so cached after the first PlayerPrefs read.
        static bool? _vibration, _tileShapes;
        static float? _sfxVolume;

        /// <summary>
        /// Sound effects volume, 0–1 (0 = off). Players who had the old Sound switch off start at 0, everyone else at 1.
        /// </summary>
        public static float SfxVolume
        {
            get
            {
                if (_sfxVolume == null)
                {
                    _sfxVolume = PlayerPrefs.HasKey(SfxVolumeKey)
                        ? Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey))
                        : (PlayerPrefs.GetInt(SoundKey, 1) == 1 ? 1f : 0f);
                }
                return _sfxVolume.Value;
            }
        }

        /// <param name="save">False while a slider is being dragged; pass true on release to write it to disk once.</param>
        public static void SetSfxVolume(float value, bool save)
        {
            value = Mathf.Clamp01(value);
            bool changed = !Mathf.Approximately(SfxVolume, value);
            _sfxVolume = value;
            if (save)
            {
                PlayerPrefs.SetFloat(SfxVolumeKey, value);
                PlayerPrefs.Save();
            }
            if (changed) Changed?.Invoke();
        }

        public static bool Sound => SfxVolume > 0.001f;

        public static bool Vibration
        {
            get => Get(ref _vibration, VibrationKey);
            set => Set(ref _vibration, VibrationKey, value);
        }

        /// <summary>Colour-blind support: draw a distinct shape on each tile colour. On by default.</summary>
        public static bool TileShapes
        {
            get => Get(ref _tileShapes, TileShapesKey);
            set => Set(ref _tileShapes, TileShapesKey, value);
        }

        static bool Get(ref bool? cache, string key)
        {
            if (cache == null) cache = PlayerPrefs.GetInt(key, 1) == 1;
            return cache.Value;
        }

        static void Set(ref bool? cache, string key, bool value)
        {
            if (Get(ref cache, key) == value) return;
            cache = value;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}

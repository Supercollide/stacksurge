using UnityEngine;

namespace StackSurge.UI
{
    public enum Sfx
    {
        Drop,
        Clear,
        ClearBig,
        Bomb,
        Reject,
        Rise,
        GameOver,
        NewBest,
        UiTap
    }

    /// <summary>
    /// Plays the game's short sound effects from Resources/Audio through a small pool of 2D AudioSources, so
    /// overlapping sounds (cascade pops, a drop during a clear) don't cut each other off. The clips are
    /// replaceable placeholders: drop a new WAV with the same name into Assets/Resources/Audio.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        const int VoiceCount = 6;
        // The same sound fired again within this window (e.g. two cascade waves in one frame) is skipped.
        const float RepeatGuardSeconds = 0.03f;

        static SfxPlayer _instance;

        static readonly string[] ClipNames =
        {
            "sfx_drop", "sfx_clear", "sfx_clear_big", "sfx_bomb", "sfx_reject",
            "sfx_rise", "sfx_gameover", "sfx_newbest", "sfx_ui_tap"
        };

        // Relative mix: the drop and UI tick happen constantly, so they sit under the clears.
        static readonly float[] Volumes = { 0.55f, 0.8f, 0.85f, 0.9f, 0.6f, 0.35f, 0.8f, 0.85f, 0.45f };

        AudioClip[] _clips;
        AudioSource[] _voices;
        float[] _lastPlayed;
        int _nextVoice;

        /// <summary>Player's Sound toggle (Settings).</summary>
        public static bool Enabled => GamePrefs.Sound;

        /// <summary>Creates the player and loads the clips. Call during loading so the first sound doesn't hitch.</summary>
        public static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("[SfxPlayer]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SfxPlayer>();
        }

        /// <param name="pitch">1 = as recorded; used to step clears up with the combo.</param>
        public static void Play(Sfx sfx, float pitch = 1f)
        {
            if (!Enabled) return;
            EnsureInstance();
            _instance.PlayInternal(sfx, pitch);
        }

        /// <summary>Clear-sound pitch for a combo multiplier: climbs a major pentatonic-ish ladder, x1..x5.</summary>
        public static float ComboPitch(int comboMultiplier)
        {
            switch (Mathf.Clamp(comboMultiplier, 1, 5))
            {
                case 1: return 1f;
                case 2: return 1.1225f;  // +2 semitones
                case 3: return 1.2599f;  // +4
                case 4: return 1.4983f;  // +7
                default: return 1.6818f; // +9
            }
        }

        void Awake()
        {
            _clips = new AudioClip[ClipNames.Length];
            for (int i = 0; i < ClipNames.Length; i++)
            {
                _clips[i] = Resources.Load<AudioClip>("Audio/" + ClipNames[i]);
                if (_clips[i] == null) Debug.LogWarning("[SfxPlayer] Missing clip Resources/Audio/" + ClipNames[i]);
                else _clips[i].LoadAudioData(); // the clips don't preload; load now, not on the first Play
            }

            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true; // UI sounds still play while the game is paused
                _voices[i] = source;
            }

            _lastPlayed = new float[ClipNames.Length];
            for (int i = 0; i < _lastPlayed.Length; i++) _lastPlayed[i] = -1f;
        }

        void PlayInternal(Sfx sfx, float pitch)
        {
            int index = (int)sfx;
            var clip = _clips[index];
            if (clip == null) return;

            float now = Time.unscaledTime;
            if (now - _lastPlayed[index] < RepeatGuardSeconds) return;
            _lastPlayed[index] = now;

            // Prefer a silent voice; only cut off a playing one (oldest first) when all six are busy.
            AudioSource voice = null;
            for (int i = 0; i < _voices.Length && voice == null; i++)
            {
                var candidate = _voices[(_nextVoice + i) % _voices.Length];
                if (!candidate.isPlaying) voice = candidate;
            }
            if (voice == null) voice = _voices[_nextVoice];
            _nextVoice = (System.Array.IndexOf(_voices, voice) + 1) % _voices.Length;
            voice.Stop();
            voice.clip = clip;
            voice.pitch = pitch;
            voice.volume = Volumes[index] * GamePrefs.SfxVolume;
            voice.Play();
        }
    }
}

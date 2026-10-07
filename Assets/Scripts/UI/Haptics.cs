using System;
using UnityEngine;

namespace StackSurge.UI
{
    /// <summary>
    /// Short haptic pulses. Handheld.Vibrate is a long (~0.4 s) buzz, so it is only used as a fallback for strong
    /// pulses (never for light ticks such as a rejected tap). On Android this uses the Vibrator service
    /// (VibrationEffect.createOneShot on API 26+). The Handheld.Vibrate reference also keeps Unity adding the
    /// VIBRATE permission to the manifest.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Mirrors the player's vibration toggle.</summary>
        public static bool Enabled = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static int _sdkInt;
        static bool _resolved;
#endif

        public static void Light() => Pulse(12, 90);
        public static void Medium() => Pulse(22, 160);
        public static void Heavy() => Pulse(35, 255);

        /// <param name="milliseconds">Pulse length.</param>
        /// <param name="amplitude">1–255 (ignored below API 26).</param>
        public static void Pulse(long milliseconds, int amplitude)
        {
            if (!Enabled) return;
            bool strong = amplitude >= 128;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Resolve();
                if (_vibrator == null)
                {
                    if (strong) Handheld.Vibrate();
                    return;
                }

                if (_sdkInt >= 26)
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, Mathf.Clamp(amplitude, 1, 255)))
                    {
                        _vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    _vibrator.Call("vibrate", milliseconds);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Haptics] Pulse failed, using fallback for strong pulses only: " + ex.Message);
                _vibrator = null;
                if (strong) Handheld.Vibrate();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            // iOS has no short-pulse API without a native plugin; skip light ticks rather than buzz for 0.4 s.
            if (strong) Handheld.Vibrate();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                _sdkInt = version.GetStatic<int>("SDK_INT");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator != null && vibrator.Call<bool>("hasVibrator")) _vibrator = vibrator;
                else vibrator?.Dispose();
            }
        }
#endif
    }
}

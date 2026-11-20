using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace StackSurge.Meta
{
    /// <summary>
    /// Detects Google's test phones. The Play Console pre-launch report installs every build uploaded to a testing
    /// track on Firebase Test Lab devices, where a robot taps through the game. Those devices set the system setting
    /// "firebase.test.lab" to "true". The game reads it to keep robot runs off the leaderboard and to stop robots
    /// sending friend requests to real players.
    /// </summary>
    public static class PlayTestDevice
    {
        static bool? _isTestLab;

        /// <summary>True on a Google test device. Read it on the main thread (it calls into Android the first time).</summary>
        public static bool IsTestLab => _isTestLab ??= Detect();

        static bool Detect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var settings = new AndroidJavaClass("android.provider.Settings$System");
                using var resolver = AndroidApplication.currentContext.Call<AndroidJavaObject>("getContentResolver");
                bool isTestLab = settings.CallStatic<string>("getString", resolver, "firebase.test.lab") == "true";
                if (isTestLab) Debug.Log("[PlayTestDevice] Google test device: scores and friend requests are not sent.");
                return isTestLab;
            }
            catch (Exception e)
            {
                // Treat it as a normal phone; a real player must never lose their scores to a failed check.
                Debug.LogWarning("[PlayTestDevice] Couldn't read firebase.test.lab: " + e.Message);
            }
#endif
            return false;
        }
    }
}

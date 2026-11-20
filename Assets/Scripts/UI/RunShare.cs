using System;
using UnityEngine;

namespace StackSurge.UI
{
    /// <summary>
    /// Shares a finished run as text. On Android this opens the system share sheet (any app that takes text);
    /// elsewhere (the Editor, desktop, iOS without a share plugin) the text is copied to the clipboard instead.
    /// </summary>
    public static class RunShare
    {
        /// <summary>The Play Store page for this package. It only resolves once the app is published there.</summary>
        public const string StoreLink = "https://play.google.com/store/apps/details?id=com.supercollide.stacksurge";

        public static string TextFor(int score, float secondsSurvived, bool isNewBest)
        {
            string points = score.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            string time = GameOverPanel.FormatTime(secondsSurvived);
            return isNewBest
                ? $"New best in Stack Surge: {points} points in {time}. Think you can beat it? {StoreLink}"
                : $"I scored {points} in Stack Surge and lasted {time}. Think you can beat it? {StoreLink}";
        }

        /// <summary>Returns true when the share sheet opened, false when the text was copied instead.</summary>
        public static bool Share(string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share your run");
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("startActivity", chooser);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RunShare] Share sheet failed, copying instead: " + e.Message);
            }
#endif
            GUIUtility.systemCopyBuffer = text;
            return false;
        }
    }
}

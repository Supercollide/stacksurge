using System;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>
    /// Unity Analytics only runs after the player agrees. The choice is stored in SaveData.AnalyticsConsent
    /// (0 = not asked yet, 1 = allowed, -1 = declined) and can be changed in Settings.
    /// </summary>
    public static class AnalyticsConsent
    {
        public const int NotAsked = 0;
        public const int Allowed = 1;
        public const int Declined = -1;

        public const string PrivacyPolicyUrl = "https://supercollidegames.com/privacy";

        private static bool _collecting;

        public static bool NeedsPrompt(SaveData save) => save != null && save.AnalyticsConsent == NotAsked;

        /// <summary>Records the player's choice and starts or stops collection to match.</summary>
        public static void SetConsent(SaveData save, bool allowed)
        {
            if (save == null) return;
            bool wasAllowed = save.AnalyticsConsent == Allowed;
            save.AnalyticsConsent = allowed ? Allowed : Declined;
            LocalProgress.Save(save);

            if (allowed)
            {
                Apply(save);
            }
            else if (wasAllowed)
            {
                // Withdrawing consent: stop collecting and ask Unity to delete what was already sent.
                RequestDeletion();
            }
        }

        /// <summary>
        /// Starts collection if the player has allowed it. Safe to call repeatedly; does nothing until
        /// Unity Services has finished initializing (call again once it has).
        /// </summary>
        public static void Apply(SaveData save)
        {
#if !UNITY_EDITOR
            if (save == null || save.AnalyticsConsent != Allowed || _collecting) return;
            if (UnityServices.State != ServicesInitializationState.Initialized) return;
            try
            {
                AnalyticsService.Instance.StartDataCollection();
                _collecting = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnalyticsConsent] StartDataCollection failed: " + ex.Message);
            }
#endif
        }

        private static void RequestDeletion()
        {
#if !UNITY_EDITOR
            if (UnityServices.State != ServicesInitializationState.Initialized) { _collecting = false; return; }
            try
            {
                // Also stops collection for this session.
                AnalyticsService.Instance.RequestDataDeletion();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnalyticsConsent] RequestDataDeletion failed: " + ex.Message);
            }
#endif
            _collecting = false;
        }
    }
}

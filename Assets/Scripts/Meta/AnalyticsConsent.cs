using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>
    /// Unity Analytics consent. In countries that require opt-in (see <see cref="ConsentCountries"/>) analytics only
    /// runs after the player agrees to the prompt; elsewhere it runs by default and the player can opt out in Settings.
    /// The choice is stored in SaveData.AnalyticsConsent (0 = not asked yet, 1 = allowed, -1 = declined).
    /// </summary>
    public static class AnalyticsConsent
    {
        public const int NotAsked = 0;
        public const int Allowed = 1;
        public const int Declined = -1;

        public const string PrivacyPolicyUrl = "https://supercollidegames.com/privacy";

        /// <summary>
        /// ISO 3166-1 country codes where the consent prompt is shown: the EU (incl. its outermost regions),
        /// the rest of the EEA, the UK and Switzerland. Edit this list to change where the prompt appears.
        /// Other regimes you may want to add: BR (LGPD), CN (PIPL), KR (PIPA), and Quebec (Canada-wide "CA").
        /// </summary>
        static readonly HashSet<string> ConsentCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // EU
            "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV",
            "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
            // EU outermost regions / territories with their own codes
            "GF", "GP", "MQ", "RE", "YT", "MF", "AX",
            // EEA, UK, Switzerland
            "IS", "LI", "NO", "GB", "CH",
        };

        /// <summary>Time zones outside "Europe/*" that belong to a consent country.</summary>
        static readonly HashSet<string> ConsentTimeZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Atlantic/Canary", "Atlantic/Madeira", "Atlantic/Azores", "Atlantic/Faroe", "Atlantic/Reykjavik",
            "Asia/Nicosia", "Asia/Famagusta", "Indian/Reunion", "Indian/Mayotte", "America/Guadeloupe",
            "America/Martinique", "America/Cayenne", "America/Marigot", "America/St_Barthelemy", "Arctic/Longyearbyen",
        };

        private static bool _collecting;
        private static bool? _consentRequiredHere;

        /// <summary>
        /// True if the player appears to be in a country that requires consent. The device can't know its location
        /// without asking for permission, so this checks every signal it has (mobile network country, SIM country,
        /// device region, time zone) and says yes if any of them points to a consent country: asking where it isn't
        /// required is harmless, skipping it where it is required is not. Evaluated once per launch.
        /// </summary>
        public static bool ConsentRequiredHere
        {
            get
            {
                if (_consentRequiredHere == null) _consentRequiredHere = DetectConsentRequired();
                return _consentRequiredHere.Value;
            }
        }

        public static bool NeedsPrompt(SaveData save) =>
            save != null && save.AnalyticsConsent == NotAsked && ConsentRequiredHere;

        /// <summary>Allowed explicitly, or never asked in a country where analytics doesn't need opt-in.</summary>
        public static bool IsCollectionAllowed(SaveData save) =>
            save != null && (save.AnalyticsConsent == Allowed || (save.AnalyticsConsent == NotAsked && !ConsentRequiredHere));

        /// <summary>Records the player's choice and starts or stops collection to match.</summary>
        public static void SetConsent(SaveData save, bool allowed)
        {
            if (save == null) return;
            bool wasAllowed = IsCollectionAllowed(save);
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
        /// Starts collection if it is allowed (see <see cref="IsCollectionAllowed"/>). Safe to call repeatedly; does
        /// nothing until Unity Services has finished initializing (call again once it has).
        /// </summary>
        public static void Apply(SaveData save)
        {
#if !UNITY_EDITOR
            if (!IsCollectionAllowed(save) || _collecting) return;
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

        // ── Country detection ───────────────────────────────────────────────
        static bool DetectConsentRequired()
        {
            foreach (string country in DeviceCountryCodes())
            {
                if (ConsentCountries.Contains(country))
                {
                    Debug.Log($"[AnalyticsConsent] Consent required (country signal: {country}).");
                    return true;
                }
            }

            string timeZone = LocalTimeZoneId();
            if (timeZone.StartsWith("Europe/", StringComparison.OrdinalIgnoreCase) || ConsentTimeZones.Contains(timeZone))
            {
                Debug.Log($"[AnalyticsConsent] Consent required (time zone: {timeZone}).");
                return true;
            }
            return false;
        }

        /// <summary>Every country code the device reports (may be empty, e.g. a Wi-Fi-only tablet).</summary>
        static IEnumerable<string> DeviceCountryCodes()
        {
            var codes = new List<string>(3);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var telephony = activity.Call<AndroidJavaObject>("getSystemService", "phone"))
                {
                    if (telephony != null)
                    {
                        // Neither call needs a permission. The network country follows the player when roaming.
                        codes.Add(telephony.Call<string>("getNetworkCountryIso"));
                        codes.Add(telephony.Call<string>("getSimCountryIso"));
                    }
                }
                using (var localeClass = new AndroidJavaClass("java.util.Locale"))
                using (var locale = localeClass.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    codes.Add(locale.Call<string>("getCountry"));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnalyticsConsent] Country lookup failed: " + ex.Message);
            }
#else
            try { codes.Add(RegionInfo.CurrentRegion.TwoLetterISORegionName); }
            catch (Exception) { /* invariant culture on some platforms */ }
#endif
            codes.RemoveAll(string.IsNullOrWhiteSpace);
            return codes;
        }

        static string LocalTimeZoneId()
        {
            try { return TimeZoneInfo.Local.Id ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }
    }
}

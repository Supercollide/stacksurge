using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace StackSurge.Meta
{
    [Serializable]
    public class SaveData
    {
        public int AllTimeHigh;
        public string DailyDate = "";
        public int DailyBest;
        public string LastPlayDate = "";
        public int Streak;
        public int[] ChallengeBits = Array.Empty<int>();

        /// <summary>Granular progress values parallel to ChallengeBits (e.g. current score towards target).</summary>
        public int[] ChallengeProgress = Array.Empty<int>();

        /// <summary>ISO date of when the current challenge set was fetched; used to detect daily rotation.</summary>
        public string ChallengeSetDate = "";

        /// <summary>Ids of the challenge set the progress above belongs to (a different set starts fresh).</summary>
        public string ChallengeSetIds = "";

        /// <summary>Claimed flags parallel to ChallengeBits, kept locally so an offline claim can't be repeated.</summary>
        public bool[] ChallengeClaimed = Array.Empty<bool>();

        /// <summary>The ChallengeSetDate whose finish-all-three bonus was already added.</summary>
        public string ChallengeBonusDate = "";

        /// <summary>Local day (yyyy-MM-dd) the last 7-day streak reward was added, so it's given once per milestone.</summary>
        public string StreakRewardDate = "";

        /// <summary>Unclaimed reward points to apply at the start of the next run.</summary>
        public int PendingRewardPoints;

        /// <summary>Display name chosen by the player, synced to UGS Authentication.</summary>
        public string PlayerDisplayName = "";

        /// <summary>True if the user has completed or skipped the interactive tutorial.</summary>
        public bool TutorialCompleted;

        /// <summary>
        /// Score queued while offline, waiting to be submitted to the leaderboard on next connection.
        /// -1 means no pending score.
        /// </summary>
        public int PendingLeaderboardScore = -1;
        public string PendingPlayerName = "";      // a name changed while offline, sent on the next sign-in

        // ── Notification Settings & State ──────────────────────────────
        public bool NotifyLeaderboardDrops = true;
        public bool NotifyFriendActivity = true;
        public bool NotifyReminders = true;
        // Per-scope last known ranks (-1 = never seen)
        public int LastKnownRankDaily = -1;
        public int LastKnownRankWeekly = -1;
        public int LastKnownRankAllTime = -1;
        // Period the daily/weekly rank was recorded in, so a board reset is not mistaken for a drop.
        public string LastKnownRankDailyPeriod = "";
        public string LastKnownRankWeeklyPeriod = "";

        // ── Streak & Reminder State ────────────────────────────────────
        /// <summary>
        /// How LastPlayDate is interpreted: 0 = legacy UTC date (old saves), 1 = the player's local date.
        /// Converted to 1 on the first streak normalize/update (see LocalProgress.MigrateLegacyStreakDay).
        /// </summary>
        public int StreakDayMode;
        /// <summary>Completed runs per local hour of day (24 buckets). The busiest hour times the reminders.</summary>
        public int[] PlayHourHistogram = new int[24];

        // ── Push Permission Soft Prompt ────────────────────────────────
        /// <summary>How many times the in-game "turn on notifications" prompt was shown (max 2).</summary>
        public int PushAskCount;
        /// <summary>ISO-8601 UTC time the prompt was last shown ("" = never).</summary>
        public string LastPushAskUtc = "";
        /// <summary>Local day (yyyy-MM-dd) the one late "streak on the line" nudge was armed for, so leaving the app
        /// repeatedly that evening doesn't re-arm it every time.</summary>
        public string LastLateStreakNudgeDay = "";

        // ── Social Settings ────────────────────────────────────────────
        /// <summary>When false, other players cannot send this player friend requests (published to Cloud Save public data).</summary>
        public bool AllowFriendRequests = true;

        // ── Privacy ────────────────────────────────────────────────────
        /// <summary>Unity Analytics consent: 0 = not asked yet, 1 = allowed, -1 = declined (see AnalyticsConsent).</summary>
        public int AnalyticsConsent = 0;

        // ── Achievements ───────────────────────────────────────────────
        /// <summary>Lifetime totals, earned tiers and the badge on show (see AchievementService).</summary>
        public AchievementProgress Achievements = new AchievementProgress();
    }

    public static class LocalProgress
    {
        const string FileName = "stack_surge_save.json";

        static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var json = File.ReadAllText(Path);
                    var d = JsonUtility.FromJson<SaveData>(json);
                    if (d != null)
                    {
                        // Persist a one-time streak migration right away so a crash cannot replay it differently.
                        if (NormalizeDailyState(d)) Save(d);
                        return d;
                    }

                    return new SaveData();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Load failed: " + e.Message);
            }

            return new SaveData();
        }

        /// <summary>
        /// Brings a loaded (or long-lived) save up to date with the clock: clears a DailyBest from a past UTC day
        /// and zeroes a streak that has already lapsed, so the menu and the reminder scheduler never show a dead streak.
        /// Returns true if the streak state changed and should be saved.
        /// </summary>
        public static bool NormalizeDailyState(SaveData data)
        {
            if (data == null) return false;

            var today = DateTime.UtcNow.Date;
            string todayStr = today.ToString("yyyy-MM-dd");

            if (string.IsNullOrWhiteSpace(data.DailyDate) || data.DailyDate != todayStr)
                data.DailyBest = 0;

            EnsurePlayHourHistogram(data);
            return NormalizeStreak(data, DateTime.Now.Date);
        }

        public static void Save(SaveData data)
        {
            try
            {
                File.WriteAllText(Path, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Save failed: " + e.Message);
            }
        }

        public static void RegisterRunEnd(int score, SaveData data)
        {
            // The daily leaderboard best stays on UTC days (matches the server board); the streak uses local days.
            var today = DateTime.UtcNow.Date;
            string todayStr = today.ToString("yyyy-MM-dd");

            if (score > data.AllTimeHigh) data.AllTimeHigh = score;

            if (data.DailyDate != todayStr)
            {
                data.DailyDate = todayStr;
                data.DailyBest = score;
            }
            else if (score > data.DailyBest)
            {
                data.DailyBest = score;
            }

            UpdateStreak(data, DateTime.Now.Date);
            Save(data);
        }

        // ── Streak (local days) ─────────────────────────────────────────
        static void UpdateStreak(SaveData data, DateTime todayLocal)
        {
            MigrateLegacyStreakDay(data, todayLocal);
            string todayStr = FormatDay(todayLocal);

            if (!TryParseDay(data.LastPlayDate, out var lastDay))
            {
                // First run ever (or an unreadable date): start counting, keeping any streak we can't disprove.
                if (data.Streak <= 0) data.Streak = 1;
                data.LastPlayDate = todayStr;
                return;
            }

            if (lastDay == todayLocal)
            {
                if (data.Streak <= 0) data.Streak = 1;
                return;
            }

            if (lastDay == todayLocal.AddDays(-1) && data.Streak > 0)
                data.Streak++;
            else if (lastDay < todayLocal)
                data.Streak = 1;
            else if (data.Streak <= 0)
                data.Streak = 1; // last play is "in the future" (clock or time zone moved back): keep the streak

            data.LastPlayDate = todayStr;
        }

        /// <summary>Zeroes a streak whose last local play day is before yesterday. Returns true if anything changed.</summary>
        static bool NormalizeStreak(SaveData data, DateTime todayLocal)
        {
            bool changed = MigrateLegacyStreakDay(data, todayLocal);

            if (data.Streak > 0 && TryParseDay(data.LastPlayDate, out var lastDay) && lastDay < todayLocal.AddDays(-1))
            {
                data.Streak = 0; // LastPlayDate is kept; the next run restarts the streak at 1
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Old saves stored LastPlayDate as a UTC date. That can be a day off from the player's local date in
        /// either direction, so the first time we see a legacy save we are lenient exactly once: a last play up to
        /// 2 local days ago still counts as alive (re-anchored to yesterday), and a date ahead of today is treated
        /// as today. From then on LastPlayDate is a local date (StreakDayMode = 1).
        /// </summary>
        static bool MigrateLegacyStreakDay(SaveData data, DateTime todayLocal)
        {
            if (data.StreakDayMode == StreakDayModeLocal) return false;
            data.StreakDayMode = StreakDayModeLocal;

            if (data.Streak > 0 && TryParseDay(data.LastPlayDate, out var lastDay))
            {
                int gapDays = (int)(todayLocal - lastDay).TotalDays;
                if (gapDays < 0)
                    data.LastPlayDate = FormatDay(todayLocal);
                else if (gapDays == 2)
                    data.LastPlayDate = FormatDay(todayLocal.AddDays(-1));
                else if (gapDays == 0 && TimeZoneInfo.Local.GetUtcOffset(DateTime.Now) < TimeSpan.Zero)
                    // West of UTC an evening run is stored under the next UTC day, so "today" may really be
                    // yesterday's local play. Re-anchor to yesterday so today's run still advances the streak
                    // (lenient: a morning player may gain one day once, nobody loses one).
                    data.LastPlayDate = FormatDay(todayLocal.AddDays(-1));
            }
            return true;
        }

        public const int StreakDayModeLegacyUtc = 0;
        public const int StreakDayModeLocal = 1;

        /// <summary>Local calendar day of the last completed run, if known.</summary>
        public static bool TryGetLastPlayDay(SaveData data, out DateTime lastPlayLocal)
        {
            lastPlayLocal = default;
            return data != null && TryParseDay(data.LastPlayDate, out lastPlayLocal);
        }

        static string FormatDay(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        static bool TryParseDay(string value, out DateTime day)
        {
            day = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                || DateTime.TryParse(value, out day))
            {
                day = day.Date;
                return true;
            }
            return false;
        }

        // ── Play-hour histogram ─────────────────────────────────────────
        const int HistogramBuckets = 24;
        const int HistogramDecayAt = 500; // halve all buckets past this so recent habits win over old ones

        /// <summary>Old saves have no histogram (null or empty after JsonUtility); give them a zeroed one.</summary>
        public static void EnsurePlayHourHistogram(SaveData data)
        {
            if (data == null) return;
            if (data.PlayHourHistogram != null && data.PlayHourHistogram.Length == HistogramBuckets) return;

            var fixedUp = new int[HistogramBuckets];
            if (data.PlayHourHistogram != null)
                Array.Copy(data.PlayHourHistogram, fixedUp, Math.Min(data.PlayHourHistogram.Length, HistogramBuckets));
            data.PlayHourHistogram = fixedUp;
        }

        /// <summary>Counts one completed run at the given local hour (0-23). Does not save.</summary>
        public static void RecordPlayHour(SaveData data, int localHour)
        {
            if (data == null || localHour < 0 || localHour >= HistogramBuckets) return;
            EnsurePlayHourHistogram(data);

            int[] h = data.PlayHourHistogram;
            if (++h[localHour] >= HistogramDecayAt)
            {
                for (int i = 0; i < h.Length; i++) h[i] /= 2;
            }
        }

        /// <summary>Total completed runs recorded in the histogram (runs before this feature shipped are not counted).</summary>
        public static int RecordedRunCount(SaveData data)
        {
            if (data?.PlayHourHistogram == null) return 0;
            int sum = 0;
            foreach (int c in data.PlayHourHistogram) sum += Math.Max(0, c);
            return sum;
        }
    }

    /// <summary>
    /// Every 7th day in a row (day 7, 14, 21...) the run that ends that day adds bonus points to the next run, once.
    /// The bonus grows each week of the streak: 3,000 on day 7, 5,000 on day 14, 7,000 on day 21... up to 10,000.
    /// </summary>
    public static class StreakReward
    {
        public const int Days = 7;
        public const int FirstWeekPoints = 3000;
        public const int ExtraPerWeek = 2000;
        public const int MaxPoints = 10000;

        /// <summary>Bonus for reaching the given week of a streak (1 = day 7, 2 = day 14...).</summary>
        public static int PointsForWeek(int week) =>
            week <= 0 ? 0 : Mathf.Min(FirstWeekPoints + ExtraPerWeek * (week - 1), MaxPoints);

        /// <summary>The bonus of the next milestone this streak reaches (today's, on a 7th day).</summary>
        public static int NextPoints(int streak) => PointsForWeek((Mathf.Max(1, streak) + Days - 1) / Days);

        /// <summary>Call after <see cref="LocalProgress.RegisterRunEnd"/>. Returns true when this run earned it.</summary>
        public static bool TryGrant(SaveData data, ChallengeProvider provider)
        {
            if (data == null || provider == null || data.Streak <= 0 || data.Streak % Days != 0) return false;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (data.StreakRewardDate == today) return false;
            data.StreakRewardDate = today;
            provider.AddBonusPoints(PointsForWeek(data.Streak / Days)); // also saves
            return true;
        }

        /// <summary>Where today sits in the current run of 7: 1-7 (7 = reward day), or 0 with no streak.</summary>
        public static int DayInCycle(int streak) => streak <= 0 ? 0 : (streak - 1) % Days + 1;
    }
}

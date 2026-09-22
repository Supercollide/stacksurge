using System;
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
        public string DevicePushToken = "";

        // ── Social Settings ────────────────────────────────────────────
        /// <summary>When false, other players cannot send this player friend requests (published to Cloud Save public data).</summary>
        public bool AllowFriendRequests = true;
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
                        NormalizeDailyState(d);
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

        static void NormalizeDailyState(SaveData data)
        {
            var today = DateTime.UtcNow.Date;
            string todayStr = today.ToString("yyyy-MM-dd");

            if (string.IsNullOrWhiteSpace(data.DailyDate) || data.DailyDate != todayStr)
                data.DailyBest = 0;
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

            UpdateStreak(data, today);
            Save(data);
        }

        static void UpdateStreak(SaveData data, DateTime today)
        {
            if (string.IsNullOrEmpty(data.LastPlayDate))
            {
                data.Streak = 1;
                data.LastPlayDate = today.ToString("yyyy-MM-dd");
                return;
            }

            if (!DateTime.TryParse(data.LastPlayDate, out var last)) last = today;
            var lastDay = last.Date;
            if (lastDay == today)
            {
                return;
            }

            if (today == lastDay.AddDays(1))
            {
                data.Streak++;
            }
            else if (today > lastDay.AddDays(1))
            {
                data.Streak = 1;
            }

            data.LastPlayDate = today.ToString("yyyy-MM-dd");
        }
    }
}

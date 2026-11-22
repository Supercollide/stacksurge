using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>Highest tier earned for one achievement.</summary>
    [Serializable]
    public struct EarnedTier
    {
        public string Key;
        public int Tier;
    }

    /// <summary>
    /// Lifetime totals and records the achievements count, the tiers earned and the badge on show.
    /// Kept in the local save and in Cloud Save, so a reinstall (or a linked account on a new phone) gets it back.
    /// </summary>
    [Serializable]
    public class AchievementProgress
    {
        public int Runs;
        public int RowClears;
        public int PerfectClears;
        public int Bombs;
        public int DailyAllDoneDays;  // days on which all of that day's challenges were completed
        public string LastAllDoneDay = ""; // the latest of those days (UTC, yyyy-MM-dd), so a day counts once
        public int BestChain;
        public int BestStreak;
        public int BestScore;

        /// <summary>When each record was set (UTC ticks). 0 when unknown: a record carried over from before achievements.</summary>
        public long BestChainTicks;
        public long BestStreakTicks;
        public long BestScoreTicks;

        /// <summary>Highest tier earned per achievement. Kept so that raising a goal later never takes a badge away.</summary>
        public List<EarnedTier> Earned = new List<EarnedTier>();

        /// <summary>Key of the achievement whose badge is on the profile. "" before any badge, <see cref="AchievementCatalog.HiddenBadge"/> when the player hid it.</summary>
        public string ShownBadge = "";
        /// <summary>When ShownBadge last changed (UTC ticks), so the newer choice wins when two devices are merged.</summary>
        public long ShownBadgeTicks;

        /// <summary>1 once the records the game kept before achievements existed (best score, streak, runs) were counted.</summary>
        public int Seeded;
    }

    /// <summary>One achievement: what it counts and the six goals that earn its badge tiers.</summary>
    public sealed class AchievementDef
    {
        public readonly string Key;
        public readonly string Name;
        /// <summary>True for a best-ever record (score, chain, streak); false for an all-time total.</summary>
        public readonly bool IsRecord;
        public readonly int[] Goals;
        readonly Func<AchievementProgress, int> _value;
        readonly Func<int, string> _goalText;
        readonly Func<AchievementProgress, long> _setAt;

        public AchievementDef(string key, string name, bool isRecord, int[] goals,
            Func<AchievementProgress, int> value, Func<int, string> goalText, Func<AchievementProgress, long> setAt = null)
        {
            Key = key;
            Name = name;
            IsRecord = isRecord;
            Goals = goals;
            _value = value;
            _goalText = goalText;
            _setAt = setAt;
        }

        public int Value(AchievementProgress p) => p == null ? 0 : _value(p);

        /// <summary>For a record: when it was set (UTC ticks), or 0 when unknown.</summary>
        public long SetAtTicks(AchievementProgress p) => p == null || _setAt == null ? 0 : _setAt(p);

        /// <summary>The goal for a tier (1-6) in words, e.g. "a chain of 28" or "600 bombs".</summary>
        public string GoalText(int tier) => _goalText(Goals[Mathf.Clamp(tier, 1, Goals.Length) - 1]);

        /// <summary>How many goals this value reaches (0-6).</summary>
        public int TierForValue(int value)
        {
            int tier = 0;
            while (tier < Goals.Length && value >= Goals[tier]) tier++;
            return tier;
        }
    }

    public static class AchievementCatalog
    {
        public const int Tiers = 6;
        public const string HiddenBadge = "-";

        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>A number short enough to sit on a badge: "800", "2,500", "15K", "750K", "1.2M".</summary>
        public static string Compact(int n)
        {
            if (n >= 1_000_000) return (n / 1_000_000f).ToString(n % 1_000_000 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 10_000) return (n / 1_000).ToString(CultureInfo.InvariantCulture) + "K";
            return N(n);
        }

        // Placeholder goals, to be retuned with the owner once everything is in. Tier 1 comes in the first days;
        // tier 6 of the totals takes thousands of runs. Records use the simulator's runs (median "you" run: 137k,
        // chain 17; top player: 242k, chain 24, 15% of runs reach chain 41).
        public static readonly AchievementDef[] All =
        {
            new AchievementDef("score", "Best score", true,
                new[] { 25_000, 60_000, 125_000, 250_000, 450_000, 750_000 },
                p => p.BestScore, g => $"{N(g)} in one run", p => p.BestScoreTicks),
            new AchievementDef("chain", "Longest chain", true,
                new[] { 8, 14, 20, 28, 38, 50 },
                p => p.BestChain, g => $"a chain of {g}", p => p.BestChainTicks),
            new AchievementDef("perfect", "Perfect clears", false,
                new[] { 1, 10, 40, 120, 300, 800 },
                p => p.PerfectClears, g => g == 1 ? "1 perfect clear" : $"{N(g)} perfect clears"),
            new AchievementDef("rows", "Row clears", false,
                new[] { 1, 10, 40, 120, 350, 1_000 },
                p => p.RowClears, g => g == 1 ? "1 row clear" : $"{N(g)} row clears"),
            new AchievementDef("bombs", "Bombs", false,
                new[] { 25, 150, 600, 2_000, 6_000, 15_000 },
                p => p.Bombs, g => $"{N(g)} bombs"),
            new AchievementDef("runs", "Runs played", false,
                new[] { 10, 50, 150, 400, 1_000, 2_500 },
                p => p.Runs, g => $"{N(g)} runs"),
            new AchievementDef("streak", "Longest streak", true,
                new[] { 3, 7, 14, 30, 100, 365 },
                p => p.BestStreak, g => $"a {g}-day streak", p => p.BestStreakTicks),
            new AchievementDef("daily", "Daily challenges", false,
                new[] { 1, 7, 30, 90, 180, 365 },
                p => p.DailyAllDoneDays, g => g == 1 ? "1 day with all 3 challenges done" : $"{N(g)} days with all 3 challenges done"),
        };

        public static int TotalBadges => All.Length * Tiers;

        public static AchievementDef Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var def in All)
                if (def.Key == key) return def;
            return null;
        }

        /// <summary>Tier earned for an achievement: the stored tier, or what the current numbers reach if higher.</summary>
        public static int TierOf(AchievementProgress p, AchievementDef def)
        {
            if (p == null || def == null) return 0;
            return Mathf.Max(StoredTier(p, def.Key), def.TierForValue(def.Value(p)));
        }

        public static int StoredTier(AchievementProgress p, string key)
        {
            if (p?.Earned == null) return 0;
            foreach (var e in p.Earned)
                if (e.Key == key) return e.Tier;
            return 0;
        }

        public static int EarnedBadges(AchievementProgress p)
        {
            int sum = 0;
            foreach (var def in All) sum += TierOf(p, def);
            return sum;
        }

        /// <summary>The badge other players see ("chain:3"), or "" when none is chosen or it isn't earned.</summary>
        public static string PublicBadge(AchievementProgress p)
        {
            var def = Find(p?.ShownBadge);
            int tier = TierOf(p, def);
            return tier > 0 ? $"{def.Key}:{tier}" : "";
        }

        /// <summary>Reads a published badge. False for "", an unknown achievement or a tier out of range.</summary>
        public static bool TryParseBadge(string badge, out AchievementDef def, out int tier)
        {
            def = null;
            tier = 0;
            if (string.IsNullOrEmpty(badge)) return false;
            int colon = badge.IndexOf(':');
            if (colon <= 0) return false;
            def = Find(badge.Substring(0, colon));
            if (def == null || !int.TryParse(badge.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out tier)) return false;
            return tier >= 1 && tier <= Tiers;
        }
    }

    /// <summary>
    /// Counts what the achievements need, records new tiers, and keeps the progress in Cloud Save. The badge on show is
    /// published with the public social preferences (see <see cref="FriendsService.PublishSocialPrefsAsync"/>).
    /// </summary>
    public class AchievementService
    {
        public const string CloudKey = "achievements";

        readonly SaveData _save;
        readonly IOnlineConnection _connection;
        Task _syncTask;
        string _announcedBadge;    // the public badge as of the last PublicBadgeChanged (or launch)
        bool _choiceUnsaved;       // the player picked a badge; saved and published once the screen closes

        /// <summary>Raised when the badge other players see changes (a new choice, or the shown one went up a tier).</summary>
        public event Action PublicBadgeChanged;
        /// <summary>Raised after progress or the chosen badge changed, so open screens can redraw.</summary>
        public event Action Changed;

        public AchievementProgress Progress => _save.Achievements;

        public AchievementService(SaveData save, IOnlineConnection connection)
        {
            _save = save;
            _connection = connection;
            Ensure(_save);
            SeedFromOldSave();
            _announcedBadge = AchievementCatalog.PublicBadge(Progress);
            if (_connection != null) _connection.WentOnline += () => _ = SyncAsync();
        }

        /// <summary>Old saves have no achievements object (JsonUtility leaves it null); give them an empty one.</summary>
        public static void Ensure(SaveData save)
        {
            if (save == null) return;
            if (save.Achievements == null) save.Achievements = new AchievementProgress();
            if (save.Achievements.Earned == null) save.Achievements.Earned = new List<EarnedTier>();
            if (save.Achievements.ShownBadge == null) save.Achievements.ShownBadge = "";
            if (save.Achievements.LastAllDoneDay == null) save.Achievements.LastAllDoneDay = "";
        }

        /// <summary>
        /// Players who played before achievements existed keep their best score and streak, and the runs the
        /// play-hour histogram counted (an undercount: it halves itself past 500). The other totals start at zero.
        /// </summary>
        void SeedFromOldSave()
        {
            var p = Progress;
            // The save from before has the last day all three challenges were done: the day of the all-done bonus.
            // Outside the Seeded check, because test builds counted single challenges here before.
            if (p.DailyAllDoneDays == 0 && !string.IsNullOrEmpty(_save.ChallengeBonusDate))
            {
                p.DailyAllDoneDays = 1;
                p.LastAllDoneDay = _save.ChallengeBonusDate;
                LocalProgress.Save(_save);
            }
            if (p.Seeded != 0) return;
            p.Seeded = 1;
            p.BestScore = Mathf.Max(p.BestScore, _save.AllTimeHigh);
            if (_save.Streak > p.BestStreak)
            {
                // The streak last went up on the last day played, which the save does keep.
                p.BestStreak = _save.Streak;
                if (DateTime.TryParseExact(_save.LastPlayDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    p.BestStreakTicks = DateTime.SpecifyKind(day.AddHours(12), DateTimeKind.Local).ToUniversalTime().Ticks;
            }
            p.Runs = Mathf.Max(p.Runs, LocalProgress.RecordedRunCount(_save));
            LocalProgress.Save(_save);
        }

        public int TierOf(AchievementDef def) => AchievementCatalog.TierOf(Progress, def);

        // ── Counting ────────────────────────────────────────────────────
        /// <summary>
        /// Adds a finished run (call after <see cref="LocalProgress.RegisterRunEnd"/>, so the streak is today's).
        /// Returns the tiers this earned, best first.
        /// </summary>
        public List<(AchievementDef def, int tier)> RecordRun(RunStats run)
        {
            var p = Progress;
            p.Runs++;
            p.RowClears += Mathf.Max(0, run.RowsCleared);
            p.PerfectClears += Mathf.Max(0, run.PerfectClears);
            p.Bombs += Mathf.Max(0, run.Bombs);
            long now = DateTime.UtcNow.Ticks;
            if (run.LongestChain > p.BestChain) { p.BestChain = run.LongestChain; p.BestChainTicks = now; }
            int score = Mathf.Max(run.Score, _save.AllTimeHigh);
            if (score > p.BestScore) { p.BestScore = score; p.BestScoreTicks = now; }
            if (_save.Streak > p.BestStreak) { p.BestStreak = _save.Streak; p.BestStreakTicks = now; }
            return Commit();
        }

        /// <summary>
        /// All of a day's challenges are done (<paramref name="day"/>: the UTC day of the set). A day counts once, even if
        /// Remote Config swaps in a new set that day and the player finishes that one too.
        /// </summary>
        public List<(AchievementDef def, int tier)> RecordAllChallengesDone(string day)
        {
            var p = Progress;
            if (string.IsNullOrEmpty(day) || string.CompareOrdinal(day, p.LastAllDoneDay ?? "") <= 0)
                return new List<(AchievementDef def, int tier)>();
            p.DailyAllDoneDays++;
            p.LastAllDoneDay = day;
            return Commit();
        }

        /// <summary>Stores tiers the numbers reach but the save doesn't have yet (first launch, merged cloud data).</summary>
        public List<(AchievementDef def, int tier)> CheckNewTiers() => Commit();

        List<(AchievementDef def, int tier)> Commit()
        {
            var earned = new List<(AchievementDef, int)>();
            var p = Progress;
            foreach (var def in AchievementCatalog.All)
            {
                int stored = AchievementCatalog.StoredTier(p, def.Key);
                int reached = def.TierForValue(def.Value(p));
                if (reached <= stored) continue;
                SetStoredTier(p, def.Key, reached);
                earned.Add((def, reached));
            }

            // The first badge goes on the profile by itself; after that the player chooses.
            if (earned.Count > 0 && string.IsNullOrEmpty(p.ShownBadge))
            {
                var best = earned[0];
                foreach (var e in earned) if (e.Item2 > best.Item2) best = e;
                p.ShownBadge = best.Item1.Key;
                p.ShownBadgeTicks = DateTime.UtcNow.Ticks;
            }

            earned.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            LocalProgress.Save(_save);
            RaiseChanged();
            AnnounceBadgeIfChanged();
            return earned;
        }

        static void SetStoredTier(AchievementProgress p, string key, int tier)
        {
            for (int i = 0; i < p.Earned.Count; i++)
            {
                if (p.Earned[i].Key != key) continue;
                p.Earned[i] = new EarnedTier { Key = key, Tier = tier };
                return;
            }
            p.Earned.Add(new EarnedTier { Key = key, Tier = tier });
        }

        // ── The badge on show ───────────────────────────────────────────
        /// <summary>
        /// Shows this achievement's badge on the profile, or hides the badge when key is null. Saved on the device at
        /// once; the cloud copy and what other players see update in <see cref="FlushBadgeChoice"/>, so trying a few
        /// badges in a row costs one upload.
        /// </summary>
        public void SetShownBadge(string key)
        {
            var def = AchievementCatalog.Find(key);
            if (def != null && TierOf(def) == 0) return; // not earned yet
            string value = def != null ? def.Key : AchievementCatalog.HiddenBadge;
            if (Progress.ShownBadge == value) return;

            Progress.ShownBadge = value;
            Progress.ShownBadgeTicks = DateTime.UtcNow.Ticks;
            LocalProgress.Save(_save);
            _choiceUnsaved = true;
            RaiseChanged();
        }

        /// <summary>Call when the Achievements screen closes: uploads and publishes a badge choice made there.</summary>
        public void FlushBadgeChoice()
        {
            if (!_choiceUnsaved) return;
            _choiceUnsaved = false;
            AnnounceBadgeIfChanged();
            _ = SaveAsync();
        }

        void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[AchievementService] Changed handler failed: " + e.Message); }
        }

        void AnnounceBadgeIfChanged()
        {
            string badge = AchievementCatalog.PublicBadge(Progress);
            if (badge == _announcedBadge) return;
            _announcedBadge = badge;
            try { PublicBadgeChanged?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[AchievementService] PublicBadgeChanged handler failed: " + e.Message); }
        }

        // ── Cloud Save ──────────────────────────────────────────────────
        static bool SignedIn => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

        /// <summary>
        /// Loads the cloud copy, keeps the higher of each number (so neither a reinstall nor offline play loses
        /// anything), and uploads the result when this device was ahead. Tiers the merge added are in
        /// <see cref="CloudMergeEarned"/> afterwards.
        /// </summary>
        /// <summary>
        /// The badge on the profile of whoever is signed in, from their Cloud Save copy ("chain:3", or "" for none or
        /// on failure). For the switch dialog's look at the other player; this phone's own progress isn't touched.
        /// </summary>
        public static async Task<string> LoadSignedInPublicBadgeAsync()
        {
            try
            {
                var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CloudKey });
                if (!data.TryGetValue(CloudKey, out var item)) return "";
                var progress = JsonUtility.FromJson<AchievementProgress>(item.Value.GetAs<string>());
                if (progress == null) return "";
                if (progress.Earned == null) progress.Earned = new List<EarnedTier>();
                return AchievementCatalog.PublicBadge(progress) ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AchievementService] Couldn't load the signed-in player's badge: " + e.Message);
                return "";
            }
        }

        public Task SyncAsync()
        {
            if (_syncTask != null && !_syncTask.IsCompleted) return _syncTask;
            _syncTask = SyncInternalAsync();
            return _syncTask;
        }

        async Task SyncInternalAsync()
        {
            if (_connection == null || !_connection.IsOnline || !SignedIn) return;

            AchievementProgress cloud = null;
            try
            {
                var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CloudKey });
                if (data.TryGetValue(CloudKey, out var item))
                    cloud = JsonUtility.FromJson<AchievementProgress>(item.Value.GetAs<string>());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AchievementService] Cloud Save load failed: " + e.Message);
                return;
            }

            bool localAhead = cloud == null || Merge(Progress, cloud);
            LocalProgress.Save(_save);
            CloudMergeEarned = Commit();
            if (localAhead) await SaveAsync();
        }

        /// <summary>Tiers the last cloud merge added (e.g. restored on a new phone). Read once by the game for its toast.</summary>
        public List<(AchievementDef def, int tier)> CloudMergeEarned { get; private set; }

        /// <summary>Folds the cloud copy into the local one. Returns true when the local copy had anything newer.</summary>
        static bool Merge(AchievementProgress local, AchievementProgress cloud)
        {
            bool localAhead = false;
            int Max(int mine, int theirs)
            {
                if (mine > theirs) localAhead = true;
                return Math.Max(mine, theirs);
            }

            local.Runs = Max(local.Runs, cloud.Runs);
            local.RowClears = Max(local.RowClears, cloud.RowClears);
            local.PerfectClears = Max(local.PerfectClears, cloud.PerfectClears);
            local.Bombs = Max(local.Bombs, cloud.Bombs);
            local.DailyAllDoneDays = Max(local.DailyAllDoneDays, cloud.DailyAllDoneDays);
            int dayOrder = string.CompareOrdinal(local.LastAllDoneDay ?? "", cloud.LastAllDoneDay ?? "");
            if (dayOrder < 0) local.LastAllDoneDay = cloud.LastAllDoneDay;
            else if (dayOrder > 0) localAhead = true;
            // A record keeps the date it was set on whichever device set it; for a tie, the earlier date.
            void Record(ref int mine, ref long mineTicks, int theirs, long theirTicks)
            {
                if (theirs > mine) { mine = theirs; mineTicks = theirTicks; return; }
                if (mine > theirs) { localAhead = true; return; }
                if (theirTicks != 0 && (mineTicks == 0 || theirTicks < mineTicks)) mineTicks = theirTicks;
                else if (mineTicks != theirTicks) localAhead = true;
            }

            Record(ref local.BestChain, ref local.BestChainTicks, cloud.BestChain, cloud.BestChainTicks);
            Record(ref local.BestStreak, ref local.BestStreakTicks, cloud.BestStreak, cloud.BestStreakTicks);
            Record(ref local.BestScore, ref local.BestScoreTicks, cloud.BestScore, cloud.BestScoreTicks);
            local.Seeded = Math.Max(local.Seeded, cloud.Seeded);

            if (cloud.Earned != null)
            {
                foreach (var e in cloud.Earned)
                {
                    int mine = AchievementCatalog.StoredTier(local, e.Key);
                    if (e.Tier > mine) SetStoredTier(local, e.Key, e.Tier);
                }
            }
            foreach (var e in local.Earned)
                if (e.Tier > AchievementCatalog.StoredTier(cloud, e.Key)) localAhead = true;

            if (cloud.ShownBadgeTicks > local.ShownBadgeTicks)
            {
                local.ShownBadge = cloud.ShownBadge ?? "";
                local.ShownBadgeTicks = cloud.ShownBadgeTicks;
            }
            else if (local.ShownBadgeTicks > cloud.ShownBadgeTicks) localAhead = true;

            return localAhead;
        }

        public async Task SaveAsync()
        {
            if (_connection == null || !_connection.IsOnline || !SignedIn) return;
            try
            {
                var data = new Dictionary<string, object> { { CloudKey, JsonUtility.ToJson(Progress) } };
                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AchievementService] Cloud Save write failed: " + e.Message);
            }
        }
    }
}

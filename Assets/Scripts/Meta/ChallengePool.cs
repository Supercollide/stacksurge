using System;
using System.Collections.Generic;

namespace StackSurge.Meta
{
    /// <summary>
    /// The pool daily challenges are picked from, and the pick itself: one easy, one medium and one hard each UTC day,
    /// never two from the same family (no "score" goal twice). The pick is seeded by the date, so every player gets the
    /// same three on the same day. Remote Config can replace the pool ("challenge_pool") or fix the day's set outright
    /// ("daily_challenges"); see <see cref="ChallengeProvider"/>.
    ///
    /// Starting targets (tune in Remote Config): watching without playing reaches about 4,000 points; on the live
    /// All-time board (27 players, Nov 2026) the median best was 13,650 and the top quarter was above 83,500.
    /// The survival times are a first guess.
    /// </summary>
    public static class ChallengePool
    {
        public static ChallengeData[] BuiltIn() => new[]
        {
            // Easy: just above what an idle board does on its own, 1,000 points.
            C("e_score", "Score 6,000", "Reach 6,000 points in one run.", ChallengeType.ScoreInRun, 6000, ChallengeDifficulty.Easy),
            C("e_rows", "Clear 3 rows", "Clear 3 full rows in one run.", ChallengeType.RowsClearedInRun, 3, ChallengeDifficulty.Easy),
            C("e_combo", "Combo x3", "Reach a x3 combo in one run.", ChallengeType.MaxComboMultiplier, 3, ChallengeDifficulty.Easy),
            C("e_runs", "Play 3 runs", "Finish 3 runs today.", ChallengeType.RunsToday, 3, ChallengeDifficulty.Easy),
            C("e_bombs", "Set off 2 bombs", "Set off 2 bombs in one run.", ChallengeType.BombsInRun, 2, ChallengeDifficulty.Easy),
            C("e_perfect", "Perfect clear", "Empty the whole board once.", ChallengeType.PerfectClearsInRun, 1, ChallengeDifficulty.Easy),
            C("e_survive", "Last 2:00", "Stay alive for 2 minutes.", ChallengeType.SurviveSeconds, 120, ChallengeDifficulty.Easy),

            // Medium: around a typical best run, 2,000 points.
            C("m_score", "Score 15,000", "Reach 15,000 points in one run.", ChallengeType.ScoreInRun, 15000, ChallengeDifficulty.Medium),
            C("m_rows", "Clear 8 rows", "Clear 8 full rows in one run.", ChallengeType.RowsClearedInRun, 8, ChallengeDifficulty.Medium),
            C("m_combo", "Combo x4", "Reach a x4 combo in one run.", ChallengeType.MaxComboMultiplier, 4, ChallengeDifficulty.Medium),
            C("m_perfect", "2 perfect clears", "Empty the board twice in one run.", ChallengeType.PerfectClearsInRun, 2, ChallengeDifficulty.Medium),
            C("m_match5", "Match 5", "Match 5 tiles in a line.", ChallengeType.Match5InRun, 1, ChallengeDifficulty.Medium),
            C("m_survive", "Last 4:00", "Stay alive for 4 minutes.", ChallengeType.SurviveSeconds, 240, ChallengeDifficulty.Medium),
            C("m_rows_today", "20 rows today", "Clear 20 rows across today's runs.", ChallengeType.RowsClearedToday, 20, ChallengeDifficulty.Medium),

            // Hard: a strong run, 3,000 points.
            C("h_score", "Score 40,000", "Reach 40,000 points in one run.", ChallengeType.ScoreInRun, 40000, ChallengeDifficulty.Hard),
            C("h_combo", "Combo x5", "Reach a x5 combo in one run.", ChallengeType.MaxComboMultiplier, 5, ChallengeDifficulty.Hard),
            C("h_perfect", "3 perfect clears", "Empty the board 3 times in one run.", ChallengeType.PerfectClearsInRun, 3, ChallengeDifficulty.Hard),
            C("h_rows", "Clear 15 rows", "Clear 15 full rows in one run.", ChallengeType.RowsClearedInRun, 15, ChallengeDifficulty.Hard),
            C("h_match5", "Match 5, three times", "Match 5 tiles in a line, 3 times.", ChallengeType.Match5InRun, 3, ChallengeDifficulty.Hard),
            C("h_survive", "Last 7:00", "Stay alive for 7 minutes.", ChallengeType.SurviveSeconds, 420, ChallengeDifficulty.Hard),
            C("h_score_today", "60,000 today", "Score 60,000 across today's runs.", ChallengeType.ScoreToday, 60000, ChallengeDifficulty.Hard),
        };

        /// <summary>Points for a challenge of each difficulty when the pool entry doesn't set its own.</summary>
        public static int DefaultReward(string difficulty) => ChallengeDifficulty.Rank(difficulty) switch { 0 => 1000, 2 => 3000, _ => 2000 };

        /// <summary>One challenge per difficulty for the given UTC day (yyyy-MM-dd), easy first.</summary>
        public static ChallengeDefinition[] PickForDay(IReadOnlyList<ChallengeDefinition> pool, string day)
        {
            var random = new System.Random(StableSeed(day));
            var picked = new List<ChallengeDefinition>(3);
            var usedFamilies = new HashSet<int>();

            for (int rank = 0; rank <= 2; rank++)
            {
                var options = new List<ChallengeDefinition>();
                foreach (var c in pool)
                    if (c != null && ChallengeDifficulty.Rank(c.Difficulty) == rank && !usedFamilies.Contains(c.Type.Family()))
                        options.Add(c);
                if (options.Count == 0) continue; // a pool without this difficulty just has fewer challenges that day

                var choice = options[random.Next(options.Count)];
                picked.Add(choice);
                usedFamilies.Add(choice.Type.Family());
            }
            return picked.ToArray();
        }

        /// <summary>FNV-1a: the same number for the same day on every device (string.GetHashCode isn't guaranteed to be).</summary>
        static int StableSeed(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in text) { hash ^= ch; hash *= 16777619; }
                return (int)hash;
            }
        }

        static ChallengeData C(string id, string title, string description, ChallengeType type, int target, string difficulty) =>
            new ChallengeData
            {
                Id = id,
                Title = title,
                Description = description,
                Type = type.ToString(),
                TargetValue = target,
                RewardPoints = DefaultReward(difficulty),
                Category = "daily",
                Difficulty = difficulty,
            };

        public static ChallengeDefinition[] ToDefinitions(ChallengeData[] data)
        {
            var defs = new ChallengeDefinition[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                defs[i] = ChallengeDefinition.FromData(data[i]);
                if (data[i].RewardPoints <= 0) defs[i].RewardPoints = DefaultReward(defs[i].Difficulty);
            }
            return defs;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>
    /// The pool daily challenges are picked from, and the pick itself: one easy, one medium and one hard each UTC day,
    /// never two from the same family (no "score" goal twice). The pick is seeded by the date, so every player gets the
    /// same three on the same day. Remote Config can replace the pool ("challenge_pool") or fix the day's set outright
    /// ("daily_challenges"); see <see cref="ChallengeProvider"/>.
    ///
    /// Targets come from the developer's play and a simulation of the game's own rules (Nov 2026); tune them in
    /// Remote Config. No points goal is below 10,000. A ROW CLEAR (a whole row popping at once) is the rarest thing in
    /// the game, so row clears count across the day. The combo multiplier stops at x5 and nearly every run reaches it,
    /// so combo goals are chains instead: clears in a row, each within 3 s of the last, with no cap. A player scoring
    /// about 130,000 a run gets a chain of 15 in two runs out of three. Google's test phones fill the live leaderboard
    /// with low scores, so its median is no guide.
    /// </summary>
    public static class ChallengePool
    {
        public static ChallengeData[] BuiltIn() => new[]
        {
            // Easy, 1,000 points.
            C("e_score", "Score 15,000", "Reach 15,000 points in one run.", ChallengeType.ScoreInRun, 15000, ChallengeDifficulty.Easy),
            C("e_chain8", "Chain 8", "Keep a combo going for 8 clears.", ChallengeType.ChainInRun, 8, ChallengeDifficulty.Easy),
            C("e_runs", "Play 3 runs", "Finish 3 runs today.", ChallengeType.RunsToday, 3, ChallengeDifficulty.Easy),
            C("e_bombs", "Set off 5 bombs", "Set off 5 bombs in one run.", ChallengeType.BombsInRun, 5, ChallengeDifficulty.Easy),
            C("e_match5", "Match 5", "Match 5 tiles in a line.", ChallengeType.Match5InRun, 1, ChallengeDifficulty.Easy),
            C("e_survive3", "Last 3:00", "Stay alive for 3 minutes.", ChallengeType.SurviveSeconds, 180, ChallengeDifficulty.Easy),
            C("e_score_today", "50,000 today", "Score 50,000 across today's runs.", ChallengeType.ScoreToday, 50000, ChallengeDifficulty.Easy),

            // Medium, 2,000 points.
            C("m_score", "Score 40,000", "Reach 40,000 points in one run.", ChallengeType.ScoreInRun, 40000, ChallengeDifficulty.Medium),
            C("m_rowclear", "Row clear today", "Get a ROW CLEAR today.", ChallengeType.RowsClearedToday, 1, ChallengeDifficulty.Medium),
            C("m_chain15", "Chain 15", "Keep a combo going for 15 clears.", ChallengeType.ChainInRun, 15, ChallengeDifficulty.Medium),
            C("m_perfect1", "Perfect clear", "Empty the whole board once.", ChallengeType.PerfectClearsInRun, 1, ChallengeDifficulty.Medium),
            C("m_match5x3", "Match 5, three times", "Match 5 tiles in a line, 3 times.", ChallengeType.Match5InRun, 3, ChallengeDifficulty.Medium),
            C("m_survive", "Last 4:00", "Stay alive for 4 minutes.", ChallengeType.SurviveSeconds, 240, ChallengeDifficulty.Medium),
            C("m_score_today", "120,000 today", "Score 120,000 across today's runs.", ChallengeType.ScoreToday, 120000, ChallengeDifficulty.Medium),

            // Hard, 3,000 points.
            C("h_score150", "Score 150,000", "Reach 150,000 points in one run.", ChallengeType.ScoreInRun, 150000, ChallengeDifficulty.Hard),
            C("h_perfect2", "2 perfect clears", "Empty the board twice in one run.", ChallengeType.PerfectClearsInRun, 2, ChallengeDifficulty.Hard),
            C("h_perfect", "3 perfect clears", "Empty the board 3 times in one run.", ChallengeType.PerfectClearsInRun, 3, ChallengeDifficulty.Hard),
            C("h_rowclears", "3 row clears today", "Get ROW CLEAR 3 times today.", ChallengeType.RowsClearedToday, 3, ChallengeDifficulty.Hard),
            C("h_match5x6", "Match 5, six times", "Match 5 tiles in a line, 6 times.", ChallengeType.Match5InRun, 6, ChallengeDifficulty.Hard),
            C("h_survive", "Last 7:00", "Stay alive for 7 minutes.", ChallengeType.SurviveSeconds, 420, ChallengeDifficulty.Hard),
            C("h_score_today400", "400,000 today", "Score 400,000 across today's runs.", ChallengeType.ScoreToday, 400000, ChallengeDifficulty.Hard),
            C("h_chain25", "Chain 25", "Keep a combo going for 25 clears.", ChallengeType.ChainInRun, 25, ChallengeDifficulty.Hard),
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
            var defs = new List<ChallengeDefinition>(data.Length);
            foreach (var d in data)
            {
                // A misspelt Type in Remote Config would otherwise become a score goal; leave that entry out instead.
                if (!Enum.TryParse<ChallengeType>(d.Type, true, out _))
                {
                    Debug.LogWarning($"[ChallengePool] Skipping '{d.Id}': unknown Type '{d.Type}'.");
                    continue;
                }
                var def = ChallengeDefinition.FromData(d);
                if (d.RewardPoints <= 0) def.RewardPoints = DefaultReward(def.Difficulty);
                defs.Add(def);
            }
            return defs.ToArray();
        }
    }
}

using System;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>
    /// What a challenge counts. The first three are the original ones (keep their order: assets and Remote Config
    /// store the name, saves don't). "InRun" goals must be reached within one run; "Today" goals add up across all of
    /// the day's runs.
    /// </summary>
    public enum ChallengeType
    {
        ScoreInRun,
        SurviveSeconds,
        MaxComboMultiplier,
        RowsClearedInRun,
        PerfectClearsInRun,
        BombsInRun,
        Match5InRun,
        RowsClearedToday,
        ScoreToday,
        RunsToday,
        ChainInRun      // longest run of clears, each within the combo window of the last (no x5 cap)
    }

    public static class ChallengeTypeExtensions
    {
        /// <summary>True for goals that add up across all of the day's runs.</summary>
        public static bool IsDayTotal(this ChallengeType type) =>
            type == ChallengeType.RowsClearedToday || type == ChallengeType.ScoreToday || type == ChallengeType.RunsToday;

        /// <summary>Goals in the same family aren't picked together on one day (no "score" twice).</summary>
        public static int Family(this ChallengeType type) => type switch
        {
            ChallengeType.ScoreInRun or ChallengeType.ScoreToday => 0,
            ChallengeType.RowsClearedInRun or ChallengeType.RowsClearedToday => 1,
            _ => 2 + (int)type,
        };
    }

    public static class ChallengeDifficulty
    {
        public const string Easy = "easy";
        public const string Medium = "medium";
        public const string Hard = "hard";

        public static int Rank(string difficulty) => difficulty switch { Easy => 0, Hard => 2, _ => 1 };
        public static string Label(string difficulty) => difficulty switch { Easy => "EASY", Hard => "HARD", _ => "MEDIUM" };
    }

    [CreateAssetMenu(fileName = "Challenge", menuName = "Stack Surge/Challenge")]
    public class ChallengeDefinition : ScriptableObject
    {
        public string Id;
        public string Title;
        [TextArea] public string Description;
        public ChallengeType Type;
        public int TargetValue;
        public int RewardPoints = 500;
        public string Category = "daily";
        public string Difficulty = ChallengeDifficulty.Medium;

        /// <summary>
        /// Creates a runtime ChallengeDefinition from a serialisable data object
        /// (used when hydrating from Remote Config JSON).
        /// </summary>
        public static ChallengeDefinition FromData(ChallengeData data)
        {
            var inst = CreateInstance<ChallengeDefinition>();
            inst.Id = data.Id;
            inst.Title = data.Title;
            inst.Description = data.Description;
            inst.Type = Enum.TryParse<ChallengeType>(data.Type, true, out var t) ? t : ChallengeType.ScoreInRun;
            inst.TargetValue = data.TargetValue;
            inst.RewardPoints = data.RewardPoints > 0 ? data.RewardPoints : 500;
            inst.Category = string.IsNullOrEmpty(data.Category) ? "daily" : data.Category;
            inst.Difficulty = string.IsNullOrEmpty(data.Difficulty) ? ChallengeDifficulty.Medium : data.Difficulty.ToLowerInvariant();
            return inst;
        }
    }

    /// <summary>
    /// Plain serialisable struct matching the Remote Config JSON shape.
    /// </summary>
    [Serializable]
    public struct ChallengeData
    {
        public string Id;
        public string Title;
        public string Description;
        public string Type;
        public int TargetValue;
        public int RewardPoints;
        public string Category;
        public string Difficulty; // "easy", "medium" or "hard"
    }

    /// <summary>
    /// Wrapper for JSON array deserialisation via JsonUtility.
    /// </summary>
    [Serializable]
    public class ChallengeDataList
    {
        public ChallengeData[] Items;
    }

    /// <summary>
    /// Structured data passed from game logic to the UI for rendering challenge rows.
    /// </summary>
    public struct ChallengeDisplayData
    {
        public string Title;
        public string Description;
        public ChallengeType Type; // how Progress reads: points, seconds or a combo multiplier
        public string Difficulty;
        public int Progress;
        public int TargetValue;
        public bool Completed;
        public bool Claimed;
        public int RewardPoints;
    }
}

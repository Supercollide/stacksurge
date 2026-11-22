using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>What happened in the current run so far, as the challenges count it.</summary>
    public struct RunStats
    {
        public float SecondsAlive;
        public int Score;
        public int BestComboMultiplier;
        public int RowsCleared;    // full rows cleared
        public int PerfectClears;  // times the whole board was emptied
        public int Bombs;          // bombs set off
        public int Match5s;        // matches of 5 or more tiles in one line
        public int LongestChain;   // most clears in a row, each within the combo window of the last
    }

    /// <summary>
    /// Evaluates per-run metrics against active challenge definitions
    /// and records granular progress through the ChallengeProvider.
    /// </summary>
    public class ChallengeTracker
    {
        readonly ChallengeProvider _provider;
        int[] _dayTotalsAtRunStart = new int[0];

        public ChallengeTracker(ChallengeProvider provider) => _provider = provider;

        /// <summary>
        /// Call as a run starts: moves to today's challenges if the day changed, and remembers how far the "today"
        /// goals were, so this run's amounts are added on top.
        /// </summary>
        public void BeginRun()
        {
            if (_provider == null) return;
            _provider.EnsureToday();
            _dayTotalsAtRunStart = (int[])_provider.Progress.Clone();
        }

        /// <summary>
        /// Called each match-resolve wave and at run end to update challenge progress.
        /// </summary>
        /// <param name="runEnded">True only for the final call, which is when "play N runs" counts the run.</param>
        /// <summary>What this run alone counts toward a challenge of the given type (no earlier runs added).</summary>
        public static int RunValue(ChallengeType type, RunStats run, bool runEnded) => type switch
        {
            ChallengeType.ScoreInRun or ChallengeType.ScoreToday => run.Score,
            ChallengeType.SurviveSeconds => (int)run.SecondsAlive,
            ChallengeType.MaxComboMultiplier => run.BestComboMultiplier,
            ChallengeType.RowsClearedInRun or ChallengeType.RowsClearedToday => run.RowsCleared,
            ChallengeType.PerfectClearsInRun => run.PerfectClears,
            ChallengeType.BombsInRun => run.Bombs,
            ChallengeType.Match5InRun => run.Match5s,
            ChallengeType.ChainInRun => run.LongestChain,
            ChallengeType.RunsToday => runEnded ? 1 : 0,
            _ => 0
        };

        public void TickRun(RunStats run, bool runEnded)
        {
            if (_provider == null) return;
            var defs = _provider.ActiveChallenges;
            if (defs == null || defs.Length == 0) return;

            for (int i = 0; i < defs.Length; i++)
            {
                if (_provider.CompletionBits[i] != 0) continue;
                var d = defs[i];
                if (d == null) continue;

                int value = RunValue(d.Type, run, runEnded);

                if (d.Type.IsDayTotal())
                    value += i < _dayTotalsAtRunStart.Length ? _dayTotalsAtRunStart[i] : 0;

                _provider.RecordProgress(i, value);
            }
        }
    }
}

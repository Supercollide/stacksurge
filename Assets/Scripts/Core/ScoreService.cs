using UnityEngine;

namespace StackSurge.Core
{
    /// <summary>
    /// GDD scoring: base points by largest match size, carry multiplier from previous 4/5 clears,
    /// combo window 3s (+100 per link, combo mult up to x5), survival bonus, full row, perfect clear.
    /// </summary>
    public class ScoreService
    {
        public int TotalScore { get; private set; }
        public int BestComboMultiplier { get; private set; }
        public int CurrentComboMultiplier => Mathf.RoundToInt(_comboMultiplier);
        public float CurrentCarryMultiplier => _carryMultiplier;

        float _carryMultiplier = 1f;
        float _comboMultiplier = 1f;
        int _comboLinks;
        float _lastClearTime = -999f;
        float _comboWindow = 3f;
        float _survivalSeconds;

        public void Reset(float comboWindowSeconds)
        {
            TotalScore = 0;
            BestComboMultiplier = 1;
            _carryMultiplier = 1f;
            _comboMultiplier = 1f;
            _comboLinks = 0;
            _lastClearTime = -999f;
            _comboWindow = comboWindowSeconds;
            _survivalSeconds = 0f;
        }

        public void AddDirectPoints(int points)
        {
            TotalScore += points;
        }

        /// <summary>
        /// 10 points per whole second spent with the board over 60% full. Time is accumulated so the payout
        /// doesn't depend on frame rate (rounding 10 * deltaTime every frame paid 0 at 20+ fps).
        /// </summary>
        public void TickSurvivalBonus(float deltaTime, float occupancy01)
        {
            if (occupancy01 <= 0.6f) return;

            _survivalSeconds += deltaTime;
            if (_survivalSeconds < 1f) return;

            int wholeSeconds = (int)_survivalSeconds;
            _survivalSeconds -= wholeSeconds;
            TotalScore += wholeSeconds * 10;
        }

        public void RegisterClearWave(int largestMatchSize, int tilesCleared, bool fullRowClear, bool perfectClear,
            float now, out int pointsAdded, out int comboMult, out float carryMult)
        {
            pointsAdded = 0;

            if (perfectClear)
            {
                const int perfect = 2000;
                TotalScore += perfect;
                pointsAdded += perfect;
            }

            if (tilesCleared > 0)
            {
                if (now - _lastClearTime <= _comboWindow)
                {
                    _comboLinks++;
                    _comboMultiplier = Mathf.Min(_comboMultiplier + 1f, 5f);
                }
                else
                {
                    _comboLinks = 1;
                    _comboMultiplier = 1f;
                }

                BestComboMultiplier = Mathf.Max(BestComboMultiplier, Mathf.RoundToInt(_comboMultiplier));

                int basePts = largestMatchSize <= 3 ? 100 : largestMatchSize == 4 ? 250 : 500;
                int comboBonus = Mathf.Max(0, _comboLinks - 1) * 100;
                int wave = Mathf.RoundToInt(basePts * _carryMultiplier * _comboMultiplier) + comboBonus;
                TotalScore += wave;
                pointsAdded += wave;

                _carryMultiplier = largestMatchSize == 4 ? 1.5f : largestMatchSize >= 5 ? 2f : 1f;
                _lastClearTime = now;
            }

            if (fullRowClear)
            {
                const int rowBonus = 200;
                TotalScore += rowBonus;
                pointsAdded += rowBonus;
            }

            comboMult = Mathf.RoundToInt(_comboMultiplier);
            carryMult = _carryMultiplier;
        }
    }
}

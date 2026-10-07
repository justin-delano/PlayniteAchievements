using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Models.Achievements.Scoring
{
    /// <summary>
    /// An explicit level curve: one start score per rank plus the score that completes the last
    /// rank. Levels inside a rank are spaced linearly between that rank's start and the next one's.
    /// Immutable; the per-level start scores are computed once in the constructor.
    /// </summary>
    public sealed class AchievementMilestoneLadder
    {
        private readonly int[] _levelStarts;

        public AchievementMilestoneLadder(
            IReadOnlyList<int> rankStartScores,
            int cycleEndScore,
            int levelsPerRank = 10)
        {
            if (rankStartScores == null || rankStartScores.Count == 0)
            {
                throw new ArgumentException("At least one rank start score is required.", nameof(rankStartScores));
            }

            if (levelsPerRank <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelsPerRank));
            }

            var starts = rankStartScores.ToList();
            for (var i = 0; i < starts.Count; i++)
            {
                var next = i + 1 < starts.Count ? starts[i + 1] : cycleEndScore;
                if (starts[i] < 0 || next - starts[i] < levelsPerRank)
                {
                    throw new ArgumentException(
                        "Rank start scores must be non-negative, increasing, and leave at least one point per level.",
                        nameof(rankStartScores));
                }
            }

            RankStartScores = starts.AsReadOnly();
            CycleEndScore = cycleEndScore;
            LevelsPerRank = levelsPerRank;

            _levelStarts = new int[(starts.Count * levelsPerRank) + 1];
            for (var rank = 0; rank < starts.Count; rank++)
            {
                long rankStart = starts[rank];
                long rankEnd = rank + 1 < starts.Count ? starts[rank + 1] : cycleEndScore;
                for (var step = 0; step < levelsPerRank; step++)
                {
                    var start = rankStart + (long)Math.Round(
                        (rankEnd - rankStart) * step / (double)levelsPerRank,
                        MidpointRounding.AwayFromZero);
                    _levelStarts[(rank * levelsPerRank) + step] = (int)start;
                }
            }

            _levelStarts[_levelStarts.Length - 1] = cycleEndScore;

            // Level 0 starts at 1, as on the formula curves; a score of 0 still reads as level 0.
            _levelStarts[0] = Math.Max(1, _levelStarts[0]);
        }

        public IReadOnlyList<int> RankStartScores { get; }

        public int CycleEndScore { get; }

        public int LevelsPerRank { get; }

        /// <summary>The level whose start equals <see cref="CycleEndScore"/>.</summary>
        public int TopLevel => _levelStarts.Length - 1;

        /// <summary>
        /// Start score of <paramref name="level"/>. Levels past <see cref="TopLevel"/> continue at
        /// the size of the last level.
        /// </summary>
        public int GetLevelStart(int level)
        {
            if (level <= 0)
            {
                return _levelStarts[0];
            }

            if (level <= TopLevel)
            {
                return _levelStarts[level];
            }

            long lastSize = Math.Max(1, _levelStarts[TopLevel] - _levelStarts[TopLevel - 1]);
            var start = _levelStarts[TopLevel] + ((level - TopLevel) * lastSize);
            return start > int.MaxValue ? int.MaxValue : (int)start;
        }

        /// <summary>Highest level whose start is at or below <paramref name="score"/>, capped at <paramref name="maxLevel"/>.</summary>
        public int FindLevel(int score, int maxLevel)
        {
            if (score <= _levelStarts[0] || maxLevel <= 0)
            {
                return 0;
            }

            if (score >= _levelStarts[TopLevel])
            {
                long lastSize = Math.Max(1, _levelStarts[TopLevel] - _levelStarts[TopLevel - 1]);
                var beyond = TopLevel + ((score - (long)_levelStarts[TopLevel]) / lastSize);
                return (int)Math.Min(maxLevel, beyond);
            }

            var index = Array.BinarySearch(_levelStarts, score);
            var level = index >= 0 ? index : (~index) - 1;
            return Math.Min(maxLevel, level);
        }
    }
}

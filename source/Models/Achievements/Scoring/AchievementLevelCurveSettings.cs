using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models.Achievements.Scoring
{
    public sealed class AchievementLevelCurveSettings
    {
        public int InitialLevelSize { get; set; }

        public int BaseLevelGrowth { get; set; }

        public int TopEndEaseStartLevel { get; set; }

        public double TopEndGrowthMultiplier { get; set; }

        public int MaxDisplayLevel { get; set; }

        /// <summary>
        /// Mastery: reaching MaxDisplayLevel starts the same ladder again from the first rank
        /// instead of capping. Each pass costs the same points; the level number keeps counting.
        /// Only meaningful with a finite MaxDisplayLevel.
        /// </summary>
        public bool RepeatsAfterMax { get; set; }

        public IReadOnlyList<AchievementRankThreshold> RankThresholds { get; set; }

        /// <summary>
        /// Optional explicit curve. When set, level start scores come from the ladder and the
        /// growth fields are ignored; one mastery cycle ends at <see cref="AchievementMilestoneLadder.CycleEndScore"/>.
        /// </summary>
        public AchievementMilestoneLadder Ladder { get; set; }

        /// <summary>
        /// Lifetime Gamerscore rank starts, one per rank from Bronze 5 to Master 1.
        /// 1k, 3k, 5k, 10k, 20k, 35k, 50k, 75k, 100k, 200k, 500k, 1M, 3M, 5M and the 10M cycle end
        /// are Xbox's official lifetime Gamerscore badge tiers; the other rank starts are interpolated.
        /// </summary>
        private static readonly int[] XboxGamerscoreRankStarts =
        {
            0, 1000, 2000, 3000, 5000,
            7500, 10000, 15000, 20000, 35000,
            50000, 75000, 100000, 150000, 200000,
            300000, 500000, 750000, 1000000, 1500000,
            2000000, 3000000, 4000000, 5000000, 7500000
        };

        private const int XboxGamerscoreCycleEnd = 10000000;

        private static readonly AchievementMilestoneLadder GamerscoreLadder = CreateMilestoneLadder(1d);

        // Scale 1.25: a full Epic game pays a 1,000 XP base pool plus the 250 XP Platinum, against ~1,000 Gamerscore.
        private static readonly AchievementMilestoneLadder EpicXpLadder = CreateMilestoneLadder(1.25d);

        // Scale 0.4: RetroAchievements' former 400-point per-set cap, against ~1,000 Gamerscore.
        private static readonly AchievementMilestoneLadder RetroAchievementsPointsLadder = CreateMilestoneLadder(0.4d);

        public static AchievementLevelCurveSettings Gamerscore => MilestoneLadder(GamerscoreLadder);

        public static AchievementLevelCurveSettings EpicXp => MilestoneLadder(EpicXpLadder);

        public static AchievementLevelCurveSettings RetroAchievementsPoints => MilestoneLadder(RetroAchievementsPointsLadder);

        /// <summary>
        /// The Gamerscore ladder with every rank start and the cycle end multiplied by <paramref name="scale"/>,
        /// on the Modern rank table: 10 levels per rank, 250 levels per pass, repeating as mastery.
        /// </summary>
        public static AchievementLevelCurveSettings MilestoneLadder(double scale)
        {
            return MilestoneLadder(CreateMilestoneLadder(scale));
        }

        private static AchievementLevelCurveSettings MilestoneLadder(AchievementMilestoneLadder ladder)
        {
            var settings = ModernDefault;
            settings.Ladder = ladder;
            return settings;
        }

        private static AchievementMilestoneLadder CreateMilestoneLadder(double scale)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }

            var starts = new int[XboxGamerscoreRankStarts.Length];
            for (var i = 0; i < starts.Length; i++)
            {
                starts[i] = ScaleScore(XboxGamerscoreRankStarts[i], scale);
            }

            return new AchievementMilestoneLadder(starts, ScaleScore(XboxGamerscoreCycleEnd, scale));
        }

        private static int ScaleScore(int score, double scale)
        {
            return (int)Math.Min(int.MaxValue, Math.Round(score * scale, MidpointRounding.AwayFromZero));
        }

        public static AchievementLevelCurveSettings LegacyCompatible => new AchievementLevelCurveSettings
        {
            InitialLevelSize = 100,
            BaseLevelGrowth = 100,
            TopEndEaseStartLevel = int.MaxValue,
            TopEndGrowthMultiplier = 1d,
            MaxDisplayLevel = int.MaxValue,
            RankThresholds = CreateDefaultRankThresholds()
        };

        public static AchievementLevelCurveSettings ModernDefault => new AchievementLevelCurveSettings
        {
            InitialLevelSize = 100,
            BaseLevelGrowth = 40,
            TopEndEaseStartLevel = 98,
            TopEndGrowthMultiplier = 0.5d,
            MaxDisplayLevel = 250,
            RepeatsAfterMax = true,
            RankThresholds = CreateDefaultRankThresholds()
        };

        public static IReadOnlyList<AchievementRankThreshold> CreateDefaultRankThresholds()
        {
            return new List<AchievementRankThreshold>
            {
                new AchievementRankThreshold { MaxLevel = 9, Rank = AchievementRank.Bronze5 },
                new AchievementRankThreshold { MaxLevel = 19, Rank = AchievementRank.Bronze4 },
                new AchievementRankThreshold { MaxLevel = 29, Rank = AchievementRank.Bronze3 },
                new AchievementRankThreshold { MaxLevel = 39, Rank = AchievementRank.Bronze2 },
                new AchievementRankThreshold { MaxLevel = 49, Rank = AchievementRank.Bronze1 },
                new AchievementRankThreshold { MaxLevel = 59, Rank = AchievementRank.Silver5 },
                new AchievementRankThreshold { MaxLevel = 69, Rank = AchievementRank.Silver4 },
                new AchievementRankThreshold { MaxLevel = 79, Rank = AchievementRank.Silver3 },
                new AchievementRankThreshold { MaxLevel = 89, Rank = AchievementRank.Silver2 },
                new AchievementRankThreshold { MaxLevel = 99, Rank = AchievementRank.Silver1 },
                new AchievementRankThreshold { MaxLevel = 109, Rank = AchievementRank.Gold5 },
                new AchievementRankThreshold { MaxLevel = 119, Rank = AchievementRank.Gold4 },
                new AchievementRankThreshold { MaxLevel = 129, Rank = AchievementRank.Gold3 },
                new AchievementRankThreshold { MaxLevel = 139, Rank = AchievementRank.Gold2 },
                new AchievementRankThreshold { MaxLevel = 149, Rank = AchievementRank.Gold1 },
                new AchievementRankThreshold { MaxLevel = 159, Rank = AchievementRank.Plat5 },
                new AchievementRankThreshold { MaxLevel = 169, Rank = AchievementRank.Plat4 },
                new AchievementRankThreshold { MaxLevel = 179, Rank = AchievementRank.Plat3 },
                new AchievementRankThreshold { MaxLevel = 189, Rank = AchievementRank.Plat2 },
                new AchievementRankThreshold { MaxLevel = 199, Rank = AchievementRank.Plat1 },
                new AchievementRankThreshold { MaxLevel = 209, Rank = AchievementRank.Master5 },
                new AchievementRankThreshold { MaxLevel = 219, Rank = AchievementRank.Master4 },
                new AchievementRankThreshold { MaxLevel = 229, Rank = AchievementRank.Master3 },
                new AchievementRankThreshold { MaxLevel = 239, Rank = AchievementRank.Master2 },
                new AchievementRankThreshold { MaxLevel = 249, Rank = AchievementRank.Master1 }
            }.AsReadOnly();
        }

        internal static AchievementLevelCurveSettings Normalize(AchievementLevelCurveSettings settings)
        {
            settings ??= ModernDefault;

            var multiplier = settings.TopEndGrowthMultiplier;
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0)
            {
                multiplier = 1d;
            }

            return new AchievementLevelCurveSettings
            {
                InitialLevelSize = Math.Max(1, settings.InitialLevelSize),
                BaseLevelGrowth = Math.Max(1, settings.BaseLevelGrowth),
                TopEndEaseStartLevel = Math.Max(0, settings.TopEndEaseStartLevel),
                TopEndGrowthMultiplier = multiplier,
                MaxDisplayLevel = settings.MaxDisplayLevel <= 0
                    ? int.MaxValue
                    : Math.Max(1, settings.MaxDisplayLevel),
                RepeatsAfterMax = settings.RepeatsAfterMax && settings.MaxDisplayLevel > 0,
                RankThresholds = settings.RankThresholds ?? CreateDefaultRankThresholds(),
                Ladder = settings.Ladder
            };
        }
    }
}

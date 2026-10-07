using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements.Scoring;

namespace PlayniteAchievements.Tests.Models
{
    [TestClass]
    public class AchievementMilestoneLadderTests
    {
        private static readonly int[] GamerscoreRankStarts =
        {
            0, 1000, 2000, 3000, 5000,
            7500, 10000, 15000, 20000, 35000,
            50000, 75000, 100000, 150000, 200000,
            300000, 500000, 750000, 1000000, 1500000,
            2000000, 3000000, 4000000, 5000000, 7500000
        };

        private static readonly AchievementRank[] Ranks =
        {
            AchievementRank.Bronze5, AchievementRank.Bronze4, AchievementRank.Bronze3, AchievementRank.Bronze2, AchievementRank.Bronze1,
            AchievementRank.Silver5, AchievementRank.Silver4, AchievementRank.Silver3, AchievementRank.Silver2, AchievementRank.Silver1,
            AchievementRank.Gold5, AchievementRank.Gold4, AchievementRank.Gold3, AchievementRank.Gold2, AchievementRank.Gold1,
            AchievementRank.Plat5, AchievementRank.Plat4, AchievementRank.Plat3, AchievementRank.Plat2, AchievementRank.Plat1,
            AchievementRank.Master5, AchievementRank.Master4, AchievementRank.Master3, AchievementRank.Master2, AchievementRank.Master1
        };

        [TestMethod]
        public void Gamerscore_EachMilestoneLandsOnItsRankStart()
        {
            var settings = AchievementLevelCurveSettings.Gamerscore;
            for (var i = 1; i < GamerscoreRankStarts.Length; i++)
            {
                var atStart = AchievementLevelCalculator.Calculate(GamerscoreRankStarts[i], settings);
                var before = AchievementLevelCalculator.Calculate(GamerscoreRankStarts[i] - 1, settings);

                Assert.AreEqual(Ranks[i], atStart.RankValue, "rank at " + GamerscoreRankStarts[i]);
                Assert.AreEqual(i * 10, atStart.Level, "level at " + GamerscoreRankStarts[i]);
                Assert.AreEqual(GamerscoreRankStarts[i], atStart.CurrentLevelStartScore);
                Assert.AreEqual(Ranks[i - 1], before.RankValue, "rank before " + GamerscoreRankStarts[i]);
                Assert.AreEqual(GamerscoreRankStarts[i], before.NextRankScoreThreshold);
            }
        }

        [TestMethod]
        public void Gamerscore_ZeroIsLevelZeroBronzeFive()
        {
            var zero = AchievementLevelCalculator.Calculate(0, AchievementLevelCurveSettings.Gamerscore);
            var one = AchievementLevelCalculator.Calculate(1, AchievementLevelCurveSettings.Gamerscore);

            Assert.AreEqual(0, zero.Level);
            Assert.AreEqual(0, zero.Mastery);
            Assert.AreEqual(AchievementRank.Bronze5, zero.RankValue);
            Assert.AreEqual(0, one.Level);
            Assert.AreEqual(1, one.CurrentLevelStartScore);
            Assert.AreEqual(99, one.CurrentLevelEndScore);
            Assert.AreEqual(100, AchievementLevelCalculator.Calculate(100, AchievementLevelCurveSettings.Gamerscore).CurrentLevelStartScore);
        }

        [TestMethod]
        public void Gamerscore_InterpolatesLevelsInsideARank()
        {
            var settings = AchievementLevelCurveSettings.Gamerscore;

            // Silver 2 spans 20,000 to 35,000 in ten levels of 1,500.
            var midSilverTwo = AchievementLevelCalculator.Calculate(27500, settings);
            Assert.AreEqual(AchievementRank.Silver2, midSilverTwo.RankValue);
            Assert.AreEqual(85, midSilverTwo.Level);
            Assert.AreEqual(27500, midSilverTwo.CurrentLevelStartScore);
            Assert.AreEqual(28999, midSilverTwo.CurrentLevelEndScore);
            Assert.AreEqual(5, midSilverTwo.LevelsCompletedInRank);

            var partway = AchievementLevelCalculator.Calculate(28250, settings);
            Assert.AreEqual(85, partway.Level);
            Assert.AreEqual(50, partway.LevelProgress);

            // Master 1 spans 7.5M to the 10M cycle end.
            Assert.AreEqual(7750000, AchievementLevelCalculator.GetScoreForLevel(241, settings));
        }

        [TestMethod]
        public void Gamerscore_CycleEndEarnsMasteryOne()
        {
            var settings = AchievementLevelCurveSettings.Gamerscore;
            var passEnd = AchievementLevelCalculator.Calculate(9999999, settings);
            var masteryOne = AchievementLevelCalculator.Calculate(10000000, settings);
            var masteryOneBronzeFour = AchievementLevelCalculator.Calculate(9999999 + 1000, settings);

            Assert.AreEqual(0, passEnd.Mastery);
            Assert.AreEqual(249, passEnd.Level);
            Assert.AreEqual(AchievementRank.Master1, passEnd.RankValue);
            Assert.AreEqual(10000000, passEnd.NextRankScoreThreshold);

            Assert.AreEqual(1, masteryOne.Mastery);
            Assert.AreEqual(250, masteryOne.Level);
            Assert.AreEqual(AchievementRank.Bronze5, masteryOne.RankValue);

            Assert.AreEqual(1, masteryOneBronzeFour.Mastery);
            Assert.AreEqual(260, masteryOneBronzeFour.Level);
            Assert.AreEqual(AchievementRank.Bronze4, masteryOneBronzeFour.RankValue);
        }

        [TestMethod]
        public void ScaledPresets_MultiplyEveryRankStart()
        {
            var epic = AchievementLevelCurveSettings.EpicXp;
            var retro = AchievementLevelCurveSettings.RetroAchievementsPoints;

            Assert.AreEqual(AchievementRank.Silver4, AchievementLevelCalculator.Calculate(12500, epic).RankValue);
            Assert.AreEqual(AchievementRank.Silver5, AchievementLevelCalculator.Calculate(12499, epic).RankValue);
            Assert.AreEqual(1, AchievementLevelCalculator.Calculate(12500000, epic).Mastery);
            Assert.AreEqual(0, AchievementLevelCalculator.Calculate(12499999, epic).Mastery);

            Assert.AreEqual(AchievementRank.Silver4, AchievementLevelCalculator.Calculate(4000, retro).RankValue);
            Assert.AreEqual(AchievementRank.Silver5, AchievementLevelCalculator.Calculate(3999, retro).RankValue);
            Assert.AreEqual(1, AchievementLevelCalculator.Calculate(4000000, retro).Mastery);
            Assert.AreEqual(0, AchievementLevelCalculator.Calculate(3999999, retro).Mastery);

            var custom = AchievementLevelCurveSettings.MilestoneLadder(2d);
            Assert.AreEqual(AchievementRank.Bronze4, AchievementLevelCalculator.Calculate(2000, custom).RankValue);
        }

        [TestMethod]
        public void GetScoreForLevel_RoundTripsOnTheLadderAcrossMasteries()
        {
            var settings = AchievementLevelCurveSettings.Gamerscore;
            foreach (var level in new[] { 1, 9, 10, 85, 249, 250, 251, 499, 500, 1234 })
            {
                var score = AchievementLevelCalculator.GetScoreForLevel(level, settings);
                Assert.AreEqual(level, AchievementLevelCalculator.Calculate(score, settings).Level, "start of " + level);
                Assert.AreEqual(level - 1, AchievementLevelCalculator.Calculate(score - 1, settings).Level, "before " + level);
            }
        }

        [TestMethod]
        public void LadderAndModernCurves_DoNotShareTheCycleCache()
        {
            // Alternating curves must not reuse each other's cycle length.
            var ladderMastery = AchievementLevelCalculator.Calculate(10000000, AchievementLevelCurveSettings.Gamerscore).Mastery;
            var modernMastery = AchievementLevelCalculator.Calculate(10000000).Mastery;
            var ladderAgain = AchievementLevelCalculator.Calculate(10000000, AchievementLevelCurveSettings.Gamerscore).Mastery;

            Assert.AreEqual(1, ladderMastery);
            Assert.AreEqual((10000000 - 1) / 1040480, modernMastery);
            Assert.AreEqual(1, ladderAgain);
        }
    }
}

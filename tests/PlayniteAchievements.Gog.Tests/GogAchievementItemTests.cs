using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.GOG.Models;
using System;

namespace PlayniteAchievements.Gog.Tests
{
    [TestClass]
    public class GogAchievementItemTests
    {
        [TestMethod]
        public void ResolvedTitle_UsesAchievementKeyWhenNameLooksNumeric()
        {
            var item = new GogAchievementItem
            {
                Name = "12345",
                AchievementKey = "master_collector",
                AchievementId = "achievement-12345"
            };

            Assert.AreEqual("master_collector", item.ResolvedTitle);
            Assert.AreEqual("master_collector", item.ResolvedAchievementId);
        }

        [TestMethod]
        public void ResolvedTitle_FallsBackToResolvedIdWhenTitleMissing()
        {
            var item = new GogAchievementItem
            {
                Name = null,
                AchievementKey = null,
                AchievementId = "achievement-id"
            };

            Assert.AreEqual("achievement-id", item.ResolvedTitle);
        }

        [TestMethod]
        public void ResolvedRarityPercent_ClampsAndRejectsInvalidValues()
        {
            var low = new GogAchievementItem { Rarity = -2 };
            var high = new GogAchievementItem { Rarity = 150 };
            var invalid = new GogAchievementItem { Rarity = double.NaN };

            Assert.AreEqual(0d, low.ResolvedRarityPercent);
            Assert.AreEqual(100d, high.ResolvedRarityPercent);
            Assert.IsNull(invalid.ResolvedRarityPercent);
        }
    }
}

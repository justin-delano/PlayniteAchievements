using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class WorkshopGameDataPresenceTests
    {
        private static GameCustomDataFile Baseline()
        {
            return new GameCustomDataFile
            {
                AchievementUnlockedIconOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ACH_1"] = "a.png" },
                AchievementCategoryOrder = new List<string> { "DLC" }
            };
        }

        [TestMethod]
        public void ResetGame_WithOnlyCategoryLeftovers_IsNotPresent()
        {
            var current = new GameCustomDataFile { AchievementCategoryOrder = new List<string> { "DLC" } };
            Assert.IsFalse(WorkshopGameDataPresence.IsPresent(Baseline(), current, currentHasAnyData: true));
        }

        [TestMethod]
        public void PackageIconStillApplied_IsPresent()
        {
            var current = new GameCustomDataFile
            {
                AchievementUnlockedIconOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ach_1"] = "b.png" }
            };
            Assert.IsTrue(WorkshopGameDataPresence.IsPresent(Baseline(), current, currentHasAnyData: true));
        }

        [TestMethod]
        public void CustomAchievementStillThere_IsPresent()
        {
            var baseline = new GameCustomDataFile { CustomAchievements = new List<CustomAchievementDefinition> { new CustomAchievementDefinition { Id = "c1" } } };
            var current = new GameCustomDataFile { CustomAchievements = new List<CustomAchievementDefinition> { new CustomAchievementDefinition { Id = "C1" } } };
            Assert.IsTrue(WorkshopGameDataPresence.IsPresent(baseline, current, currentHasAnyData: true));
        }

        [TestMethod]
        public void NoBaseline_FallsBackToAnyData()
        {
            var current = new GameCustomDataFile();
            Assert.IsTrue(WorkshopGameDataPresence.IsPresent(null, current, currentHasAnyData: true));
            Assert.IsFalse(WorkshopGameDataPresence.IsPresent(null, current, currentHasAnyData: false));
        }

        [TestMethod]
        public void BaselineWithOnlyGameLevelData_FallsBackToAnyData()
        {
            var baseline = new GameCustomDataFile { AchievementCategoryOrder = new List<string> { "DLC" } };
            Assert.IsTrue(WorkshopGameDataPresence.IsPresent(baseline, new GameCustomDataFile(), currentHasAnyData: true));
        }
    }
}

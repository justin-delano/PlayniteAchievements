using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Steam;
using System.Collections.Generic;

namespace PlayniteAchievements.Steam.Tests
{
    [TestClass]
    public class SteamHuntersCategoryEnricherTests
    {
        [TestMethod]
        public void ApplyGroups_DlcAndUpdate_MarksFirstGroupBase_AndRemainingGroupsDlc()
        {
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "base_ach" },
                new AchievementDetail { ApiName = "dlc_ach" },
                new AchievementDetail { ApiName = "ungrouped_ach" }
            };
            var groups = new List<SteamHuntersAchievementGroup>
            {
                new SteamHuntersAchievementGroup
                {
                    Name = "Main Game",
                    AchievementApiNames = new List<string> { "base_ach" }
                },
                new SteamHuntersAchievementGroup
                {
                    Name = "Expansion",
                    AchievementApiNames = new List<string> { "dlc_ach" }
                }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(achievements, groups, "dlcandupdate", "My Game");

            Assert.AreEqual("Base", achievements[0].CategoryType);
            Assert.AreEqual("Main Game", achievements[0].Category);
            Assert.AreEqual("DLC", achievements[1].CategoryType);
            Assert.AreEqual("Expansion", achievements[1].Category);
            Assert.AreEqual("Base", achievements[2].CategoryType);
            Assert.AreEqual("My Game", achievements[2].Category);
        }

        [TestMethod]
        public void ApplyGroups_GameGroupBy_MarksAllGroupsBase()
        {
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "halo_ce" },
                new AchievementDetail { ApiName = "halo_2" }
            };
            var groups = new List<SteamHuntersAchievementGroup>
            {
                new SteamHuntersAchievementGroup
                {
                    Name = "Halo: Combat Evolved",
                    AchievementApiNames = new List<string> { "halo_ce" }
                },
                new SteamHuntersAchievementGroup
                {
                    Name = "Halo 2: Anniversary",
                    AchievementApiNames = new List<string> { "halo_2" }
                }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(achievements, groups, "game");

            Assert.AreEqual("Base", achievements[0].CategoryType);
            Assert.AreEqual("Halo: Combat Evolved", achievements[0].Category);
            Assert.AreEqual("Base", achievements[1].CategoryType);
            Assert.AreEqual("Halo 2: Anniversary", achievements[1].Category);
        }

        [TestMethod]
        public void ApplyGroups_EmptyGroups_MarksAllAchievementsBase()
        {
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "one" },
                new AchievementDetail { ApiName = "two" }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(
                achievements,
                new List<SteamHuntersAchievementGroup>(),
                groupBy: null,
                gameName: "Fallback Game");

            Assert.AreEqual("Base", achievements[0].CategoryType);
            Assert.AreEqual("Fallback Game", achievements[0].Category);
            Assert.AreEqual("Base", achievements[1].CategoryType);
            Assert.AreEqual("Fallback Game", achievements[1].Category);
        }

        [TestMethod]
        public void ApplyGroups_DuplicateApiName_KeepsFirstGroupAssignment()
        {
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "shared" }
            };
            var groups = new List<SteamHuntersAchievementGroup>
            {
                new SteamHuntersAchievementGroup
                {
                    Name = "Base Label",
                    AchievementApiNames = new List<string> { "shared" }
                },
                new SteamHuntersAchievementGroup
                {
                    Name = "DLC Label",
                    AchievementApiNames = new List<string> { "shared" }
                }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(achievements, groups);

            Assert.AreEqual("Base", achievements[0].CategoryType);
            Assert.AreEqual("Base Label", achievements[0].Category);
        }
    }
}

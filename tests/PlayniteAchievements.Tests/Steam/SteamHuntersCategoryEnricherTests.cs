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
        public void ApplyGroups_DlcAndUpdate_TypesByDlcAppIdPresence()
        {
            // Mirrors Starfield/Atomic Heart ordering: the DLC group (has DlcAppId) comes
            // first in the array, and the update group (no DlcAppId) comes after. Position
            // must not matter -- only DlcAppId presence should decide Base vs DLC.
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "dlc_ach" },
                new AchievementDetail { ApiName = "update_ach" },
                new AchievementDetail { ApiName = "ungrouped_ach" }
            };
            var groups = new List<SteamHuntersAchievementGroup>
            {
                new SteamHuntersAchievementGroup
                {
                    DlcAppId = 2721670,
                    DlcAppName = "Expansion",
                    AchievementApiNames = new List<string> { "dlc_ach" }
                },
                new SteamHuntersAchievementGroup
                {
                    Name = "Update #1",
                    AchievementApiNames = new List<string> { "update_ach" }
                }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(achievements, groups, "dlcandupdate", "My Game");

            Assert.AreEqual("DLC", achievements[0].CategoryType);
            Assert.AreEqual("Expansion", achievements[0].Category);
            Assert.AreEqual("Base", achievements[1].CategoryType);
            Assert.AreEqual("Update #1", achievements[1].Category);
            Assert.AreEqual("Base", achievements[2].CategoryType);
            Assert.AreEqual("My Game", achievements[2].Category);
        }

        [TestMethod]
        public void ApplyGroups_SingleDlcGroup_TypesAsDlc()
        {
            // Mirrors Cyberpunk 2077: a single DLC group at index 0. Previously this was
            // always forced to Base, so nothing was ever typed DLC.
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "dlc_ach" },
                new AchievementDetail { ApiName = "ungrouped_ach" }
            };
            var groups = new List<SteamHuntersAchievementGroup>
            {
                new SteamHuntersAchievementGroup
                {
                    DlcAppId = 2138330,
                    DlcAppName = "Phantom Liberty",
                    AchievementApiNames = new List<string> { "dlc_ach" }
                }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(achievements, groups, "dlcandupdate", "Cyberpunk 2077");

            Assert.AreEqual("DLC", achievements[0].CategoryType);
            Assert.AreEqual("Phantom Liberty", achievements[0].Category);
            Assert.AreEqual("Base", achievements[1].CategoryType);
        }

        [TestMethod]
        public void ApplyGroups_MultipleDlcGroups_AllTypeAsDlc()
        {
            // Mirrors Atomic Heart: four DLC groups in sequence, all carrying DlcAppId.
            var achievements = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "annihilation_ach" },
                new AchievementDetail { ApiName = "limbo_ach" },
                new AchievementDetail { ApiName = "sea_ach" },
                new AchievementDetail { ApiName = "crystal_ach" }
            };
            var groups = new List<SteamHuntersAchievementGroup>
            {
                new SteamHuntersAchievementGroup
                {
                    DlcAppId = 2214821,
                    DlcAppName = "Annihilation Instinct",
                    AchievementApiNames = new List<string> { "annihilation_ach" }
                },
                new SteamHuntersAchievementGroup
                {
                    DlcAppId = 2214822,
                    DlcAppName = "Trapped in Limbo",
                    AchievementApiNames = new List<string> { "limbo_ach" }
                },
                new SteamHuntersAchievementGroup
                {
                    DlcAppId = 2214823,
                    DlcAppName = "Enchantment Under the Sea",
                    AchievementApiNames = new List<string> { "sea_ach" }
                },
                new SteamHuntersAchievementGroup
                {
                    DlcAppId = 2214824,
                    DlcAppName = "Blood On Crystal",
                    AchievementApiNames = new List<string> { "crystal_ach" }
                }
            };

            SteamHuntersCategoryEnricher.ApplyGroups(achievements, groups, "dlcandupdate", "Atomic Heart");

            Assert.AreEqual("DLC", achievements[0].CategoryType);
            Assert.AreEqual("DLC", achievements[1].CategoryType);
            Assert.AreEqual("DLC", achievements[2].CategoryType);
            Assert.AreEqual("DLC", achievements[3].CategoryType);
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
                    // DlcAppId set to prove the "game" groupBy override forces Base even
                    // when a group happens to carry a DlcAppId.
                    DlcAppId = 12345,
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
                    DlcAppId = 999,
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

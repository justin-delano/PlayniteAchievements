using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Models.Tests
{
    [TestClass]
    public class GridOptionsSettingsMigrationTests
    {
        [TestMethod]
        public void MigrateFromJson_SeedsSingleGameAppearanceFromOverviewSelectedGame()
        {
            // The View Achievements window used to follow the overview selected-game options, so a
            // config written before the switch only carries the values on that entry.
            const string json = @"{
                ""Persisted"": {
                    ""GridOptions"": {
                        ""Achievement"": {
                            ""OverviewSelectedGame"": {
                                ""ShowRarityGlow"": false,
                                ""ColorNamesByRarity"": true
                            }
                        }
                    }
                }
            }";

            var singleGame = MigrateSingleGame(json);

            Assert.IsFalse(singleGame["ShowRarityGlow"].Value<bool>());
            Assert.IsTrue(singleGame["ColorNamesByRarity"].Value<bool>());
        }

        [TestMethod]
        public void MigrateFromJson_KeepsExistingSingleGameAppearance()
        {
            const string json = @"{
                ""Persisted"": {
                    ""GridOptions"": {
                        ""Achievement"": {
                            ""OverviewSelectedGame"": { ""ShowRarityGlow"": false },
                            ""SingleGame"": { ""ShowRarityGlow"": true }
                        }
                    }
                }
            }";

            var singleGame = MigrateSingleGame(json);

            Assert.IsTrue(singleGame["ShowRarityGlow"].Value<bool>());
        }

        [TestMethod]
        public void MigrateFromJson_LeavesSingleGameUnseeded_WhenOverviewSelectedGameHasNoAppearance()
        {
            const string json = @"{ ""Persisted"": { ""GlobalLanguage"": ""english"" } }";

            var achievement = (JObject)JObject.Parse(GridOptionsSettingsMigration.MigrateFromJson(json))
                ["Persisted"]?["GridOptions"]?["Achievement"];

            Assert.IsNull(achievement?["SingleGame"]?["ShowRarityGlow"]);
        }

        private static JObject MigrateSingleGame(string json)
        {
            var migrated = JObject.Parse(GridOptionsSettingsMigration.MigrateFromJson(json));
            return (JObject)migrated["Persisted"]["GridOptions"]["Achievement"]["SingleGame"];
        }

        [TestMethod]
        public void MigrateFromJson_SeedsShowcaseSortDefaultsOnce()
        {
            // A file written before showcase surfaces consumed sort carries the never-applied
            // class defaults; the one-shot seed normalizes them to the order-preserving modes.
            const string json = @"{
                ""Persisted"": {
                    ""GridOptions"": {
                        ""Achievement"": {
                            ""ShowcasePinnedAchievements:abc"": { ""SortMode"": ""UnlockTime"" },
                            ""ShowcaseRecentAchievements"": { ""SortMode"": ""UnlockTime"" },
                            ""OverviewRecent"": { ""SortMode"": ""UnlockTime"" }
                        },
                        ""GameSummaries"": {
                            ""ShowcasePinnedGames:abc"": { ""SortMode"": ""RecentUnlock"" },
                            ""ShowcaseGameSummaries:abc"": { ""SortMode"": ""Alphabetical"" }
                        }
                    }
                }
            }";

            var migrated = JObject.Parse(GridOptionsSettingsMigration.MigrateFromJson(json));
            var gridOptions = (JObject)migrated["Persisted"]["GridOptions"];

            Assert.AreEqual(
                "None",
                gridOptions["Achievement"]["ShowcasePinnedAchievements:abc"]["SortMode"].Value<string>());
            Assert.AreEqual(
                "None",
                gridOptions["Achievement"]["ShowcaseRecentAchievements"]["SortMode"].Value<string>());
            Assert.AreEqual(
                "PinOrder",
                gridOptions["GameSummaries"]["ShowcasePinnedGames:abc"]["SortMode"].Value<string>());
            // Non-showcase surfaces and showcase game-summaries (whose sort was already
            // consumed) are left alone.
            Assert.AreEqual(
                "UnlockTime",
                gridOptions["Achievement"]["OverviewRecent"]["SortMode"].Value<string>());
            Assert.AreEqual(
                "Alphabetical",
                gridOptions["GameSummaries"]["ShowcaseGameSummaries:abc"]["SortMode"].Value<string>());
            Assert.IsTrue(gridOptions["ShowcaseSortSeeded"].Value<bool>());
        }

        [TestMethod]
        public void MigrateFromJson_SkipsShowcaseSortSeed_WhenMarkerPresent()
        {
            const string json = @"{
                ""Persisted"": {
                    ""GridOptions"": {
                        ""ShowcaseSortSeeded"": true,
                        ""Achievement"": {
                            ""ShowcasePinnedAchievements:abc"": { ""SortMode"": ""UnlockTime"" }
                        }
                    }
                }
            }";

            var migrated = JObject.Parse(GridOptionsSettingsMigration.MigrateFromJson(json));

            Assert.AreEqual(
                "UnlockTime",
                migrated["Persisted"]["GridOptions"]["Achievement"]["ShowcasePinnedAchievements:abc"]
                    ["SortMode"].Value<string>());
        }

        [TestMethod]
        public void ShowcaseSurfaceDefaults_PreserveSourceOrder()
        {
            var catalog = new GridOptionsCatalog();

            Assert.AreEqual(
                CompactListSortMode.None,
                catalog.GetAchievement("ShowcasePinnedAchievements:abc").SortMode);
            Assert.AreEqual(
                CompactListSortMode.None,
                catalog.GetAchievement("ShowcaseRecentAchievements:abc").SortMode);
            Assert.AreEqual(
                GameSummariesSortMode.PinOrder,
                catalog.GetGameSummaries("ShowcasePinnedGames:abc").SortMode);
            Assert.AreEqual(
                GameSummariesSortMode.RecentUnlock,
                catalog.GetGameSummaries("ShowcaseGameSummaries:abc").SortMode);
            Assert.IsTrue(catalog.ShowcaseSortSeeded);
        }
    }
}

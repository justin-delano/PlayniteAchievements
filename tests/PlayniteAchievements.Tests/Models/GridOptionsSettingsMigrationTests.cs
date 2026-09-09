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
        public void ShowcaseSurfaceDefaults_PreserveSourceOrder()
        {
            var catalog = new GridOptionsCatalog();

            // None keeps the projection's source order (recency, or pin order for the pinned
            // source); game grids default to the recent-unlock sort with PinOrder available.
            Assert.AreEqual(
                CompactListSortMode.None,
                catalog.GetAchievement("ShowcaseRecentAchievements:abc").SortMode);
            Assert.AreEqual(
                GameSummariesSortMode.RecentUnlock,
                catalog.GetGameSummaries("ShowcaseGameSummaries:abc").SortMode);
        }
    }
}

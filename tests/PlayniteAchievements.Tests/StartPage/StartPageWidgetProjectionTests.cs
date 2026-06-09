using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.StartPage;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Tests.StartPage
{
    [TestClass]
    public class StartPageWidgetProjectionTests
    {
        [TestMethod]
        public void ProjectGamesOverview_UsesMainSettingsSortAndDefaultLimit()
        {
            var items = Enumerable.Range(0, 30)
                .Select(index => new GameOverviewItem
                {
                    GameName = $"Game {index:D2}",
                    SortingName = $"Game {index:D2}",
                    LastUnlockUtc = new DateTime(2026, 1, 1).AddDays(index),
                    TotalAchievements = index
                })
                .ToList();
            var settings = new PersistedSettings
            {
                GamesOverviewGridSortMode = GamesOverviewSortMode.RecentUnlock,
                GamesOverviewGridSortDescending = true
            };

            var result = StartPageWidgetProjection.ProjectGamesOverview(items, settings);

            Assert.AreEqual(StartPageWidgetProjection.DefaultGridRowLimit, result.Count);
            Assert.AreEqual("Game 29", result[0].GameName);
            Assert.AreEqual("Game 05", result[result.Count - 1].GameName);
        }

        [TestMethod]
        public void ProjectGamesOverview_UsesAlphabeticalSortFromMainSettings()
        {
            var items = new[]
            {
                new GameOverviewItem { GameName = "Zed", SortingName = "Zed" },
                new GameOverviewItem { GameName = "Alpha", SortingName = "Alpha" }
            };
            var settings = new PersistedSettings
            {
                GamesOverviewGridSortMode = GamesOverviewSortMode.Alphabetical,
                GamesOverviewGridSortDescending = false
            };

            var result = StartPageWidgetProjection.ProjectGamesOverview(items, settings, rowLimit: 10);

            Assert.AreEqual("Alpha", result[0].GameName);
            Assert.AreEqual("Zed", result[1].GameName);
        }

        [TestMethod]
        public void ProjectRecentUnlocks_UsesMainRecentSortAndDefaultLimit()
        {
            var items = Enumerable.Range(0, 30)
                .Select(index => new AchievementDisplayItem
                {
                    DisplayName = $"Achievement {index:D2}",
                    GameName = "Game",
                    UnlockTimeUtc = new DateTime(2026, 1, 1).AddMinutes(index),
                    RaritySortValue = index
                })
                .ToList();

            var result = StartPageWidgetProjection.ProjectRecentUnlocks(items, new PersistedSettings());

            Assert.AreEqual(StartPageWidgetProjection.DefaultGridRowLimit, result.Count);
            Assert.AreEqual("Achievement 29", result[0].DisplayName);
            Assert.AreEqual("Achievement 05", result[result.Count - 1].DisplayName);
            Assert.AreNotSame(items[0], result.Last());
        }
    }
}

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.StartPage;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.StartPage
{
    [TestClass]
    public class StartPageWidgetProjectionTests
    {
        [TestMethod]
        public void GameSummaryControlBarAdapter_AppliesProviderProgressAndActivityFilters()
        {
            var items = new[]
            {
                new GameSummaryItem
                {
                    GameName = "Steam Complete",
                    ProviderKey = "Steam",
                    Platforms = new[] { "PC" },
                    IsCompleted = true,
                    UnlockedAchievements = 10,
                    TotalAchievements = 10,
                    LastPlayed = new DateTime(2026, 1, 1)
                },
                new GameSummaryItem
                {
                    GameName = "Steam No Progress",
                    ProviderKey = "Steam",
                    Platforms = new[] { "Steam Deck" },
                    UnlockedAchievements = 0,
                    TotalAchievements = 10
                },
                new GameSummaryItem
                {
                    GameName = "Xbox Complete",
                    ProviderKey = "Xbox",
                    Platforms = new[] { "Xbox" },
                    IsCompleted = true,
                    UnlockedAchievements = 10,
                    TotalAchievements = 10,
                    LastPlayed = new DateTime(2026, 1, 2)
                }
            };
            var toolbar = new GameSummaryGridControlBarAdapter();
            toolbar.UpdateOptions(items);

            toolbar.ProviderFilterGroups.Single(group => group.ProviderKey == "Steam").SetAll(true);
            toolbar.SetProgressFilterSelected(toolbar.ProgressFilterOptions[0], true);
            toolbar.SetActivityFilterSelected(toolbar.ActivityFilterOptions[0], true);

            var result = toolbar.Apply(items)
                .Select(item => item.GameName)
                .ToList();

            CollectionAssert.AreEqual(new[] { "Steam Complete" }, result);
        }

        [TestMethod]
        public void FilterGameSummariesForStartPage_DefaultScopeUsesPlayedCompletedAndInProgress()
        {
            var result = StartPageWidgetProjection.FilterGameSummariesForStartPage(
                    CreateScopeTestGames(),
                    new PersistedSettings(),
                    includeProgressScope: true)
                .Select(game => game.GameName)
                .ToList();

            CollectionAssert.AreEqual(new[] { "Complete", "In Progress" }, result);
        }

        [TestMethod]
        public void FilterGameSummariesForStartPage_NoneAndAllScopesDoNotFilter()
        {
            var settings = new PersistedSettings
            {
                StartPageActivityScope = GameActivityScope.None,
                StartPageProgressScope = GameProgressScope.None
            };

            var noneResult = StartPageWidgetProjection.FilterGameSummariesForStartPage(
                    CreateScopeTestGames(),
                    settings,
                    includeProgressScope: true)
                .Select(game => game.GameName)
                .ToList();

            settings.StartPageActivityScope = GameActivityScope.All;
            settings.StartPageProgressScope = GameProgressScope.All;
            var allResult = StartPageWidgetProjection.FilterGameSummariesForStartPage(
                    CreateScopeTestGames(),
                    settings,
                    includeProgressScope: true)
                .Select(game => game.GameName)
                .ToList();

            var expected = new[] { "Complete", "In Progress", "No Progress Played", "No Progress Unplayed" };
            CollectionAssert.AreEqual(expected, noneResult);
            CollectionAssert.AreEqual(expected, allResult);
        }

        [TestMethod]
        public void FilterGameSummariesForStartPage_ProgressOnlyScopeUsesStrictOr()
        {
            var settings = new PersistedSettings
            {
                StartPageActivityScope = GameActivityScope.None,
                StartPageProgressScope = GameProgressScope.InProgress | GameProgressScope.NoProgress
            };

            var result = StartPageWidgetProjection.FilterGameSummariesForStartPage(
                    CreateScopeTestGames(),
                    settings,
                    includeProgressScope: true)
                .Select(game => game.GameName)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { "In Progress", "No Progress Played", "No Progress Unplayed" },
                result);
        }

        [TestMethod]
        public void FilterGameSummariesForStartPage_CanIgnoreProgressForCompletedGamesPie()
        {
            var settings = new PersistedSettings
            {
                StartPageActivityScope = GameActivityScope.Played,
                StartPageProgressScope = GameProgressScope.Completed
            };

            var result = StartPageWidgetProjection.FilterGameSummariesForStartPage(
                    CreateScopeTestGames(),
                    settings,
                    includeProgressScope: false)
                .Select(game => game.GameName)
                .ToList();

            CollectionAssert.AreEqual(new[] { "Complete", "In Progress", "No Progress Played" }, result);
        }

        private static GameSummaryItem[] CreateScopeTestGames()
        {
            return new[]
            {
                new GameSummaryItem
                {
                    GameName = "Complete",
                    SortingName = "Complete",
                    IsCompleted = true,
                    UnlockedAchievements = 10,
                    TotalAchievements = 10,
                    LastPlayed = new DateTime(2026, 1, 1)
                },
                new GameSummaryItem
                {
                    GameName = "In Progress",
                    SortingName = "In Progress",
                    UnlockedAchievements = 3,
                    TotalAchievements = 10
                },
                new GameSummaryItem
                {
                    GameName = "No Progress Played",
                    SortingName = "No Progress Played",
                    UnlockedAchievements = 0,
                    TotalAchievements = 10,
                    LastPlayed = new DateTime(2026, 1, 2)
                },
                new GameSummaryItem
                {
                    GameName = "No Progress Unplayed",
                    SortingName = "No Progress Unplayed",
                    UnlockedAchievements = 0,
                    TotalAchievements = 10
                }
            };
        }
    }
}

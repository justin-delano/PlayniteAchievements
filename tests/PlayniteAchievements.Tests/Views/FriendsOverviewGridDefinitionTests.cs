using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class FriendsOverviewGridDefinitionTests
    {
        [TestMethod]
        public void FriendSummariesGrid_DefinesExpectedColumnsAndPersistenceKeys()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "Controls", "FriendSummariesGridControl.xaml"));
            var code = File.ReadAllText(FindRepoFile("source", "Views", "Controls", "FriendSummariesGridControl.xaml.cs"));

            AssertContainsAll(
                xaml,
                "ColumnKey=\"Avatar\"",
                "FriendSummaryFriend",
                "FriendSummaryProvider",
                "FriendSummarySharedGames",
                "FriendSummaryGamesWithUnlocks",
                "FriendSummaryUnlocks",
                "FriendSummaryPrestigeScore",
                "FriendSummaryCollectionScore",
                "FriendSummaryLastUnlock",
                "GameSummaryProviderIconStyle");
            AssertContainsNone(
                xaml,
                "FriendSummaryRecentUnlocks",
                "FriendSummaryLastRefreshed",
                "FriendSummaryTotalPlaytime");
            AssertContainsAll(
                code,
                "FriendsOverviewFriendSummariesColumnVisibility",
                "FriendsOverviewFriendSummariesColumnWidths",
                "FriendsOverviewFriendSummariesColumnOrder",
                "FriendsOverviewFriendSummariesColumnAlignments",
                "FriendsOverviewFriendSummariesColumnVerticalAlignments",
                "FriendsOverviewFriendSummariesColumnHeaderAlignments");
        }

        [TestMethod]
        public void FriendGameSummariesGrid_DefinesFriendColumnsAndDedicatedSurface()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "Controls", "GameSummariesGridControl.xaml"));
            var code = File.ReadAllText(FindRepoFile("source", "Views", "Controls", "GameSummariesGridControl.xaml.cs"));

            AssertContainsAll(
                xaml,
                "ColumnKey=\"GameSummaryLastUnlock\"",
                "ColumnKey=\"FriendGameFriendsWithUnlocks\"",
                "ColumnKey=\"FriendGameLastUnlock\"");
            AssertContainsNone(
                xaml,
                "ColumnKey=\"FriendGamePlaytime\"",
                "ColumnKey=\"FriendGameFriends\"",
                "ColumnKey=\"FriendGameUnlocks\"",
                "ColumnKey=\"FriendGameUniqueUnlocks\"",
                "ColumnKey=\"FriendGameCompletion\"",
                "ColumnKey=\"FriendGameTotalPlaytime\"",
                "ColumnKey=\"FriendGameAveragePlaytime\"",
                "ColumnKey=\"FriendGameLastPlayed\"",
                "ColumnKey=\"FriendGameScrapeStatus\"");
            AssertContainsAll(
                code,
                "FriendsOverviewGameSummaries",
                "FriendsOverviewSelectedFriendGameSummaries",
                "FriendsOverviewGameSummariesColumnVisibility",
                "FriendsOverviewGameSummariesColumnWidths",
                "FriendsOverviewGameSummariesColumnOrder",
                "FriendsOverviewGameSummariesColumnAlignments",
                "FriendsOverviewGameSummariesColumnVerticalAlignments",
                "FriendsOverviewGameSummariesColumnHeaderAlignments",
                "FriendsOverviewSelectedFriendGameSummariesColumnVisibility",
                "FriendsOverviewSelectedFriendGameSummariesColumnWidths",
                "FriendsOverviewSelectedFriendGameSummariesColumnOrder",
                "FriendsOverviewSelectedFriendGameSummariesColumnAlignments",
                "FriendsOverviewSelectedFriendGameSummariesColumnVerticalAlignments",
                "FriendsOverviewSelectedFriendGameSummariesColumnHeaderAlignments");
        }

        [TestMethod]
        public void FriendsOverview_WiresSeparateAggregateAndSelectedFriendGameSummaryGrids()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml"));
            var code = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml.cs"));

            AssertContainsAll(
                xaml,
                "x:Name=\"FriendGameSummariesGridControl\"",
                "x:Name=\"SelectedFriendGameSummariesGridControl\"",
                "ColumnSettingsKey=\"FriendsOverviewGameSummaries\"",
                "ColumnSettingsKey=\"FriendsOverviewSelectedFriendGameSummaries\"",
                "Visibility=\"{Binding HasFriendSelection, Converter={StaticResource InverseBoolToVis}}\"",
                "Visibility=\"{Binding HasFriendSelection, Converter={StaticResource BoolToVis}}\"");
            AssertContainsAll(
                code,
                "SelectedFriendGameSummariesGridControl?.Dispose()",
                "ClearGridSelection(SelectedFriendGameSummariesGridControl?.InternalDataGrid)");
        }

        [TestMethod]
        public void FriendAchievementsGrid_DefinesFriendColumnAndDedicatedSurface()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "Controls", "AchievementDataGridControl.xaml"));
            var code = File.ReadAllText(FindRepoFile("source", "Views", "Controls", "AchievementDataGridControl.xaml.cs"));

            AssertContainsAll(
                xaml,
                "x:Name=\"FriendAvatarColumn\"",
                "ColumnKey=\"Avatar\"",
                "ColumnKey=\"Friend\"",
                "FriendAvatarPath");
            AssertContainsNone(
                xaml,
                "ColumnKey=\"FriendUnlockDate\"",
                "ColumnKey=\"MyUnlockDate\"",
                "ColumnKey=\"UnlockDelta\"",
                "ColumnKey=\"UnlockRelation\"",
                "ColumnKey=\"FriendProvider\"");
            AssertContainsAll(
                code,
                "FriendsOverviewRecentAchievements",
                "RowPreviewMouseLeftButtonDownEvent",
                "friendAvatar: true",
                "FriendsOverviewAchievementColumnVisibility",
                "FriendsOverviewAchievementColumnWidths",
                "FriendsOverviewAchievementColumnOrder",
                "FriendsOverviewAchievementColumnAlignments",
                "unlockDate: true");
        }

        [TestMethod]
        public void FriendsOverview_WiresAchievementRowToggleAndHeaderClearButton()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml"));
            var code = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml.cs"));

            AssertContainsAll(
                xaml,
                "RowPreviewMouseLeftButtonDown=\"AchievementRow_PreviewMouseLeftButtonDown\"",
                "Click=\"ClearSelection_Click\"",
                "HasAnySelection");
            AssertContainsAll(
                code,
                "AchievementRow_PreviewMouseLeftButtonDown",
                "TryResolveSelectedRow",
                "ResolveDataGridRow");
        }

        [TestMethod]
        public void ThemeControlRegistry_RegistersFriendThemeControls()
        {
            var registry = File.ReadAllText(FindRepoFile("source", "Services", "ThemeIntegration", "ThemeControlRegistry.cs"));
            var summariesXaml = File.ReadAllText(FindRepoFile("source", "Views", "ThemeIntegration", "Modern", "AchievementFriendSummariesGridControl.xaml"));
            var gamesXaml = File.ReadAllText(FindRepoFile("source", "Views", "ThemeIntegration", "Modern", "AchievementFriendGameSummariesGridControl.xaml"));
            var gamesCode = File.ReadAllText(FindRepoFile("source", "Views", "ThemeIntegration", "Modern", "AchievementFriendGameSummariesGridControl.xaml.cs"));
            var achievementsXaml = File.ReadAllText(FindRepoFile("source", "Views", "ThemeIntegration", "Modern", "AchievementFriendAchievementsGridControl.xaml"));

            AssertContainsAll(
                registry,
                "AchievementFriendSummariesGrid",
                "AchievementFriendGameSummariesGrid",
                "AchievementFriendAchievementsGrid",
                "AchievementFriendSummariesGridControl",
                "AchievementFriendGameSummariesGridControl",
                "AchievementFriendAchievementsGridControl");
            AssertContainsAll(
                summariesXaml,
                "FriendSummariesGridControl",
                "ItemsSource=\"{Binding DisplayItems");
            AssertContainsAll(
                gamesXaml,
                "GameSummariesGridControl",
                "ItemsSource=\"{Binding DisplayItems");
            AssertContainsAll(
                gamesCode,
                "FriendsOverviewGameSummaries",
                "FriendsOverviewSelectedFriendGameSummaries",
                "DynamicFriendScopeUserKey");
            AssertContainsAll(
                achievementsXaml,
                "AchievementDataGridControl",
                "ColumnSettingsKey=\"FriendsOverviewRecentAchievements\"",
                "ShowFriendColumn=\"True\"");
        }

        private static void AssertContainsAll(string content, params string[] expected)
        {
            var missing = expected
                .Where(value => !content.Contains(value))
                .ToList();

            CollectionAssert.AreEqual(new List<string>(), missing);
        }

        private static void AssertContainsNone(string content, params string[] unexpected)
        {
            var present = unexpected
                .Where(value => content.Contains(value))
                .ToList();

            CollectionAssert.AreEqual(new List<string>(), present);
        }

        private static string FindRepoFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not find " + Path.Combine(parts));
            return null;
        }
    }
}

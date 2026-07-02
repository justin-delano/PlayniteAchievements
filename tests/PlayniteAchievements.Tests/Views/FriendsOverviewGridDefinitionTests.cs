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
        public void FriendsOverview_FriendContextMenuIncludesRefreshFriend()
        {
            var code = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml.cs"));
            var localization = File.ReadAllText(FindRepoFile("source", "Localization", "en_US.xaml"));

            AssertContainsAll(
                code,
                "LOCPlayAch_Menu_RefreshFriend",
                "RefreshFriendSelectedGameCommand",
                "GameRowContextMenuBuilder.ExecuteCommand(refreshCommand, friend)");
            AssertContainsAll(
                localization,
                "LOCPlayAch_Menu_RefreshFriend");
        }

        [TestMethod]
        public void FriendsOverview_ExophaseFriendGameContextMenuCanEditMappings()
        {
            var code = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml.cs"));

            AssertContainsAll(
                code,
                "IsMappableExophaseFriendGame",
                "PlayniteGamePickerDialog.Pick",
                "settings.FriendGameMappings = mappings",
                "ExophaseFriendPlatformMatcher.IsSameProviderPlatform",
                "RefreshExophaseProviderGame(game)");
        }

        [TestMethod]
        public void MergedOverview_HeaderWiresSubviewSwitcherBeforeRefreshControls()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "OverviewControl.xaml"));

            AssertContainsAll(
                xaml,
                "x:Name=\"OverviewSubViewButton\"",
                "x:Name=\"FriendsSubViewButton\"",
                "LOCPlayAch_Overview_SubView_Overview",
                "LOCPlayAch_Overview_SubView_Friends",
                "ActiveRefreshHeader.RefreshModeSelectionText",
                "ActiveRefreshHeader.RefreshOrCancelCommand",
                "x:Name=\"FriendsOverviewContentHost\"");

            Assert.IsTrue(
                xaml.IndexOf("x:Name=\"FriendsSubViewButton\"", StringComparison.Ordinal) <
                xaml.IndexOf("x:Name=\"RefreshModeSelectionButton\"", StringComparison.Ordinal),
                "Subview switcher should appear before refresh controls.");
        }

        [TestMethod]
        public void MergedOverview_OnlyRegistersOneSidebarEntry()
        {
            var plugin = File.ReadAllText(FindRepoFile("source", "PlayniteAchievementsPlugin.cs"));

            Assert.AreEqual(1, CountOccurrences(plugin, "yield return new SidebarItem"));
            AssertContainsNone(
                plugin,
                "LOCPlayAch_Menu_OpenFriendsOverview",
                "return new FriendsOverviewControl");
        }

        [TestMethod]
        public void MergedOverview_RemovesStandaloneFriendsOverviewWindowPath()
        {
            var windowService = File.ReadAllText(FindRepoFile("source", "Services", "UI", "PluginWindowService.cs"));
            var windows = File.ReadAllText(FindRepoFile("source", "PlayniteAchievementsPlugin.Windows.cs"));
            var menus = File.ReadAllText(FindRepoFile("source", "PlayniteAchievementsPlugin.Menus.cs"));

            AssertContainsNone(
                windowService,
                "OpenFriendsOverviewWindow",
                "FriendsOverviewWindowPlacementKey",
                "_friendsOverviewWindow",
                "ContainsFriendsOverviewControl");
            AssertContainsNone(windows, "OpenFriendsOverviewWindow");
            AssertContainsNone(menus, "LOCPlayAch_Menu_OpenFriendsOverview");
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
                "AchievementChart",
                "AchievementBarChart",
                "AchievementPieChart",
                "AchievementFriendSummariesGrid",
                "AchievementFriendGameSummariesGrid",
                "AchievementFriendAchievementsGrid",
                "AchievementBarChartControl",
                "AchievementPieChartControl",
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

        private static int CountOccurrences(string content, string value)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(value))
            {
                return 0;
            }

            var count = 0;
            var index = 0;
            while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
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

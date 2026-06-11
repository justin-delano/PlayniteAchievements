using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Tests.StartPage
{
    [TestClass]
    public class StartPageSettingsTests
    {
        [TestMethod]
        public void CloneAndCopyFrom_PreserveStartPageColumnSettings()
        {
            var source = new PersistedSettings
            {
                StartPageAchievementColumnVisibility = new Dictionary<string, bool>
                {
                    ["Achievement"] = false
                },
                StartPageAchievementColumnWidths = new Dictionary<string, double>
                {
                    ["Achievement"] = 320
                },
                StartPageAchievementColumnOrder = new Dictionary<string, int>
                {
                    ["Achievement"] = 2
                },
                StartPageAchievementColumnAlignments = new Dictionary<string, GridAlignment>
                {
                    ["Achievement"] = GridAlignment.Center
                },
                StartPageGamesOverviewColumnVisibility = new Dictionary<string, bool>
                {
                    ["OverviewProvider"] = false
                },
                StartPageGamesOverviewColumnWidths = new Dictionary<string, double>
                {
                    ["OverviewProvider"] = 140
                },
                StartPageGamesOverviewColumnOrder = new Dictionary<string, int>
                {
                    ["OverviewProvider"] = 3
                },
                StartPageGamesOverviewColumnAlignments = new Dictionary<string, GridAlignment>
                {
                    ["OverviewProvider"] = GridAlignment.Right
                }
            };

            var clone = source.Clone();
            var copy = new PersistedSettings();
            copy.CopyFrom(source);

            Assert.IsFalse(clone.StartPageAchievementColumnVisibility["Achievement"]);
            Assert.AreEqual(320, clone.StartPageAchievementColumnWidths["Achievement"]);
            Assert.AreEqual(2, clone.StartPageAchievementColumnOrder["Achievement"]);
            Assert.AreEqual(GridAlignment.Center, clone.StartPageAchievementColumnAlignments["Achievement"]);
            Assert.IsFalse(clone.StartPageGamesOverviewColumnVisibility["OverviewProvider"]);
            Assert.AreEqual(140, clone.StartPageGamesOverviewColumnWidths["OverviewProvider"]);
            Assert.AreEqual(3, clone.StartPageGamesOverviewColumnOrder["OverviewProvider"]);
            Assert.AreEqual(GridAlignment.Right, clone.StartPageGamesOverviewColumnAlignments["OverviewProvider"]);

            Assert.IsFalse(copy.StartPageAchievementColumnVisibility["Achievement"]);
            Assert.AreEqual(320, copy.StartPageAchievementColumnWidths["Achievement"]);
            Assert.AreEqual(2, copy.StartPageAchievementColumnOrder["Achievement"]);
            Assert.AreEqual(GridAlignment.Center, copy.StartPageAchievementColumnAlignments["Achievement"]);
            Assert.IsFalse(copy.StartPageGamesOverviewColumnVisibility["OverviewProvider"]);
            Assert.AreEqual(140, copy.StartPageGamesOverviewColumnWidths["OverviewProvider"]);
            Assert.AreEqual(3, copy.StartPageGamesOverviewColumnOrder["OverviewProvider"]);
            Assert.AreEqual(GridAlignment.Right, copy.StartPageGamesOverviewColumnAlignments["OverviewProvider"]);

            Assert.AreNotSame(source.StartPageAchievementColumnVisibility, clone.StartPageAchievementColumnVisibility);
            Assert.AreNotSame(source.StartPageAchievementColumnWidths, clone.StartPageAchievementColumnWidths);
            Assert.AreNotSame(source.StartPageAchievementColumnOrder, clone.StartPageAchievementColumnOrder);
            Assert.AreNotSame(source.StartPageAchievementColumnAlignments, clone.StartPageAchievementColumnAlignments);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnVisibility, copy.StartPageGamesOverviewColumnVisibility);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnWidths, copy.StartPageGamesOverviewColumnWidths);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnOrder, copy.StartPageGamesOverviewColumnOrder);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnAlignments, copy.StartPageGamesOverviewColumnAlignments);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveStartPageWidgetSettings()
        {
            var source = new PersistedSettings();
            source.StartPageGamesOverviewGrid.UseCoverImages = false;
            source.StartPageGamesOverviewGrid.ShowGameMetadata = false;
            source.StartPageGamesOverviewGrid.ShowCompletionBorder = false;
            source.StartPageGamesOverviewGrid.ShowColumnHeaders = false;
            source.StartPageGamesOverviewGrid.RowHeight = 72d;
            source.StartPageGamesOverviewGrid.MaxRows = 11;
            source.StartPageGamesOverviewGrid.SortMode = GamesOverviewSortMode.Alphabetical;
            source.StartPageGamesOverviewGrid.SortDescending = false;

            source.StartPageRecentUnlocksGrid.UseCoverImages = false;
            source.StartPageRecentUnlocksGrid.ShowColumnHeaders = false;
            source.StartPageRecentUnlocksGrid.RowHeight = 84d;
            source.StartPageRecentUnlocksGrid.MaxRows = 12;
            source.StartPageRecentUnlocksGrid.SortMode = CompactListSortMode.Rarity;
            source.StartPageRecentUnlocksGrid.SortDescending = false;

            source.StartPagePieCharts.ShowCenterPercentage = false;
            source.StartPagePieCharts.SmallSliceMode = SidebarPieSmallSliceMode.Hide;

            var clone = source.Clone();
            var copy = new PersistedSettings();
            copy.CopyFrom(source);

            Assert.IsFalse(clone.StartPageGamesOverviewGrid.UseCoverImages);
            Assert.IsFalse(clone.StartPageGamesOverviewGrid.ShowGameMetadata);
            Assert.IsFalse(clone.StartPageGamesOverviewGrid.ShowCompletionBorder);
            Assert.IsFalse(clone.StartPageGamesOverviewGrid.ShowColumnHeaders);
            Assert.AreEqual(72d, clone.StartPageGamesOverviewGrid.RowHeight);
            Assert.AreEqual(11, clone.StartPageGamesOverviewGrid.MaxRows);
            Assert.AreEqual(GamesOverviewSortMode.Alphabetical, clone.StartPageGamesOverviewGrid.SortMode);
            Assert.IsFalse(clone.StartPageGamesOverviewGrid.SortDescending);

            Assert.IsFalse(copy.StartPageRecentUnlocksGrid.UseCoverImages);
            Assert.IsFalse(copy.StartPageRecentUnlocksGrid.ShowColumnHeaders);
            Assert.AreEqual(84d, copy.StartPageRecentUnlocksGrid.RowHeight);
            Assert.AreEqual(12, copy.StartPageRecentUnlocksGrid.MaxRows);
            Assert.AreEqual(CompactListSortMode.Rarity, copy.StartPageRecentUnlocksGrid.SortMode);
            Assert.IsFalse(copy.StartPageRecentUnlocksGrid.SortDescending);

            Assert.IsFalse(clone.StartPagePieCharts.ShowCenterPercentage);
            Assert.AreEqual(SidebarPieSmallSliceMode.Hide, clone.StartPagePieCharts.SmallSliceMode);
            Assert.IsFalse(copy.StartPagePieCharts.ShowCenterPercentage);
            Assert.AreEqual(SidebarPieSmallSliceMode.Hide, copy.StartPagePieCharts.SmallSliceMode);

            Assert.AreNotSame(source.StartPageGamesOverviewGrid, clone.StartPageGamesOverviewGrid);
            Assert.AreNotSame(source.StartPageRecentUnlocksGrid, copy.StartPageRecentUnlocksGrid);
            Assert.AreNotSame(source.StartPagePieCharts, clone.StartPagePieCharts);
            Assert.AreNotSame(source.StartPagePieCharts, copy.StartPagePieCharts);
        }
    }
}

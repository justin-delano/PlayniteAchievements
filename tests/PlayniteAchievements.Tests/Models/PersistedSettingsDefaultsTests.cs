using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Models.Tests
{
    [TestClass]
    public class PersistedSettingsDefaultsTests
    {
        [TestMethod]
        public void Constructor_DefaultsAchievementDataGridMaxHeight()
        {
            var settings = new PersistedSettings();

            Assert.AreEqual(
                PersistedSettings.DefaultAchievementDataGridMaxHeight,
                settings.AchievementDataGridMaxHeight);
        }

        [TestMethod]
        public void Constructor_DefaultsOverviewColumnRatio()
        {
            var settings = new PersistedSettings();

            Assert.AreEqual(
                PersistedSettings.DefaultOverviewLeftColumnRatio,
                settings.OverviewLeftColumnRatio);
        }

        [TestMethod]
        public void Constructor_DefaultsOverviewScoreCardsVisible()
        {
            var settings = new PersistedSettings();

            Assert.IsTrue(settings.ShowOverviewCollectionScoreCard);
            Assert.IsTrue(settings.ShowOverviewPrestigeScoreCard);
        }

        [TestMethod]
        public void Constructor_DefaultsColumnHeadersVisible()
        {
            var settings = new PersistedSettings();

            Assert.IsTrue(settings.ShowGameSummariesGridColumnHeaders);
            Assert.IsTrue(settings.ShowAchievementGridColumnHeaders);
            Assert.IsTrue(settings.ShowDesktopThemeAchievementGridColumnHeaders);
        }

        [TestMethod]
        public void Constructor_DefaultsGridAlignmentSettings()
        {
            var settings = new PersistedSettings();

            Assert.AreEqual(GridAlignment.Center, settings.GridColumnHeaderAlignment);
            Assert.AreEqual(GridAlignment.Left, settings.GridCellAlignment);
            Assert.AreEqual(GridVerticalAlignment.Center, settings.GridCellVerticalAlignment);
        }

        [TestMethod]
        public void Constructor_DefaultsGridLayoutSettings()
        {
            var settings = new PersistedSettings();

            Assert.IsNull(settings.SingleGameGridRowHeight);
            Assert.IsNull(settings.OverviewGameSummariesGridRowHeight);
            Assert.IsNull(settings.OverviewRecentAchievementsGridRowHeight);
            Assert.IsNull(settings.OverviewSelectedGameGridRowHeight);
            Assert.IsNull(settings.StartPageGameSummariesGridRowHeight);
            Assert.IsNull(settings.StartPageRecentAchievementsGridRowHeight);
            Assert.IsNull(settings.DesktopThemeAchievementGridRowHeight);

            Assert.IsNull(settings.SingleGameGridMaxRows);
            Assert.IsNull(settings.OverviewGameSummariesGridMaxRows);
            Assert.IsNull(settings.OverviewRecentAchievementsGridMaxRows);
            Assert.IsNull(settings.OverviewSelectedGameGridMaxRows);
            Assert.AreEqual(PersistedSettings.DefaultStartPageGridMaxRows, settings.StartPageGameSummariesGridMaxRows);
            Assert.AreEqual(PersistedSettings.DefaultStartPageGridMaxRows, settings.StartPageRecentAchievementsGridMaxRows);
            Assert.IsNull(settings.DesktopThemeAchievementGridMaxRows);
        }

        [TestMethod]
        public void GridLayoutSettings_NormalizeInvalidValues()
        {
            var settings = new PersistedSettings();

            settings.SingleGameGridRowHeight = 12d;
            Assert.AreEqual(PersistedSettings.MinimumGridRowHeight, settings.SingleGameGridRowHeight);

            settings.SingleGameGridRowHeight = 64d;
            Assert.AreEqual(64d, settings.SingleGameGridRowHeight);

            settings.SingleGameGridRowHeight = 0d;
            Assert.IsNull(settings.SingleGameGridRowHeight);

            settings.SingleGameGridRowHeight = double.NaN;
            Assert.IsNull(settings.SingleGameGridRowHeight);

            settings.SingleGameGridRowHeight = double.PositiveInfinity;
            Assert.IsNull(settings.SingleGameGridRowHeight);

            settings.SingleGameGridMaxRows = 0;
            Assert.IsNull(settings.SingleGameGridMaxRows);

            settings.SingleGameGridMaxRows = -4;
            Assert.IsNull(settings.SingleGameGridMaxRows);

            settings.SingleGameGridMaxRows = 1;
            Assert.AreEqual(1, settings.SingleGameGridMaxRows);
        }

        [TestMethod]
        public void OverviewColumnRatio_ClampsInvalidValues()
        {
            var settings = new PersistedSettings();

            settings.OverviewLeftColumnRatio = -1d;
            Assert.AreEqual(
                PersistedSettings.MinOverviewLeftColumnRatio,
                settings.OverviewLeftColumnRatio);

            settings.OverviewLeftColumnRatio = 2d;
            Assert.AreEqual(
                PersistedSettings.MaxOverviewLeftColumnRatio,
                settings.OverviewLeftColumnRatio);

            settings.OverviewLeftColumnRatio = double.NaN;
            Assert.AreEqual(
                PersistedSettings.DefaultOverviewLeftColumnRatio,
                settings.OverviewLeftColumnRatio);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveOverviewColumnRatio()
        {
            var source = new PersistedSettings
            {
                OverviewLeftColumnRatio = 0.64d
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.AreEqual(0.64d, clone.OverviewLeftColumnRatio);
            Assert.AreEqual(0.64d, target.OverviewLeftColumnRatio);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveOverviewScoreCardVisibility()
        {
            var source = new PersistedSettings
            {
                ShowOverviewCollectionScoreCard = false,
                ShowOverviewPrestigeScoreCard = false
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.IsFalse(clone.ShowOverviewCollectionScoreCard);
            Assert.IsFalse(clone.ShowOverviewPrestigeScoreCard);
            Assert.IsFalse(target.ShowOverviewCollectionScoreCard);
            Assert.IsFalse(target.ShowOverviewPrestigeScoreCard);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveColumnHeaderVisibilityAndColumnOrder()
        {
            var source = new PersistedSettings
            {
                ShowGameSummariesGridColumnHeaders = false,
                ShowAchievementGridColumnHeaders = false,
                ShowDesktopThemeAchievementGridColumnHeaders = false,
                GridColumnHeaderAlignment = GridAlignment.Right,
                GridCellAlignment = GridAlignment.Center,
                GridCellVerticalAlignment = GridVerticalAlignment.Bottom,
                OverviewRecentAchievementColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Title"] = 2
                },
                OverviewRecentAchievementColumnAlignments = new System.Collections.Generic.Dictionary<string, GridAlignment>
                {
                    ["Title"] = GridAlignment.Center
                },
                OverviewSelectedGameAchievementColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Rarity"] = 3
                },
                OverviewSelectedGameAchievementColumnAlignments = new System.Collections.Generic.Dictionary<string, GridAlignment>
                {
                    ["Rarity"] = GridAlignment.Right
                },
                SingleGameColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Achievement"] = 1
                },
                SingleGameColumnAlignments = new System.Collections.Generic.Dictionary<string, GridAlignment>
                {
                    ["Achievement"] = GridAlignment.Left
                },
                DesktopThemeColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Points"] = 4
                },
                DesktopThemeColumnAlignments = new System.Collections.Generic.Dictionary<string, GridAlignment>
                {
                    ["Points"] = GridAlignment.Right
                },
                GameSummariesColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["GameSummaryName"] = 1
                },
                GameSummariesColumnAlignments = new System.Collections.Generic.Dictionary<string, GridAlignment>
                {
                    ["GameSummaryName"] = GridAlignment.Center
                },
                DataGridColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Legacy"] = 5
                }
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.IsFalse(clone.ShowGameSummariesGridColumnHeaders);
            Assert.IsFalse(clone.ShowAchievementGridColumnHeaders);
            Assert.IsFalse(clone.ShowDesktopThemeAchievementGridColumnHeaders);
            Assert.IsFalse(target.ShowGameSummariesGridColumnHeaders);
            Assert.IsFalse(target.ShowAchievementGridColumnHeaders);
            Assert.IsFalse(target.ShowDesktopThemeAchievementGridColumnHeaders);
            Assert.AreEqual(GridAlignment.Right, clone.GridColumnHeaderAlignment);
            Assert.AreEqual(GridAlignment.Center, clone.GridCellAlignment);
            Assert.AreEqual(GridVerticalAlignment.Bottom, clone.GridCellVerticalAlignment);
            Assert.AreEqual(GridAlignment.Right, target.GridColumnHeaderAlignment);
            Assert.AreEqual(GridAlignment.Center, target.GridCellAlignment);
            Assert.AreEqual(GridVerticalAlignment.Bottom, target.GridCellVerticalAlignment);

            Assert.AreEqual(2, clone.OverviewRecentAchievementColumnOrder["Title"]);
            Assert.AreEqual(3, clone.OverviewSelectedGameAchievementColumnOrder["Rarity"]);
            Assert.AreEqual(1, clone.SingleGameColumnOrder["Achievement"]);
            Assert.AreEqual(4, clone.DesktopThemeColumnOrder["Points"]);
            Assert.AreEqual(1, clone.GameSummariesColumnOrder["GameSummaryName"]);
            Assert.AreEqual(5, clone.DataGridColumnOrder["Legacy"]);
            Assert.AreEqual(GridAlignment.Center, clone.OverviewRecentAchievementColumnAlignments["Title"]);
            Assert.AreEqual(GridAlignment.Right, clone.OverviewSelectedGameAchievementColumnAlignments["Rarity"]);
            Assert.AreEqual(GridAlignment.Left, clone.SingleGameColumnAlignments["Achievement"]);
            Assert.AreEqual(GridAlignment.Right, clone.DesktopThemeColumnAlignments["Points"]);
            Assert.AreEqual(GridAlignment.Center, clone.GameSummariesColumnAlignments["GameSummaryName"]);

            Assert.AreEqual(2, target.OverviewRecentAchievementColumnOrder["Title"]);
            Assert.AreEqual(3, target.OverviewSelectedGameAchievementColumnOrder["Rarity"]);
            Assert.AreEqual(1, target.SingleGameColumnOrder["Achievement"]);
            Assert.AreEqual(4, target.DesktopThemeColumnOrder["Points"]);
            Assert.AreEqual(1, target.GameSummariesColumnOrder["GameSummaryName"]);
            Assert.AreEqual(5, target.DataGridColumnOrder["Legacy"]);
            Assert.AreEqual(GridAlignment.Center, target.OverviewRecentAchievementColumnAlignments["Title"]);
            Assert.AreEqual(GridAlignment.Right, target.OverviewSelectedGameAchievementColumnAlignments["Rarity"]);
            Assert.AreEqual(GridAlignment.Left, target.SingleGameColumnAlignments["Achievement"]);
            Assert.AreEqual(GridAlignment.Right, target.DesktopThemeColumnAlignments["Points"]);
            Assert.AreEqual(GridAlignment.Center, target.GameSummariesColumnAlignments["GameSummaryName"]);

            Assert.AreNotSame(source.OverviewRecentAchievementColumnOrder, clone.OverviewRecentAchievementColumnOrder);
            Assert.AreNotSame(source.OverviewSelectedGameAchievementColumnOrder, target.OverviewSelectedGameAchievementColumnOrder);
            Assert.AreNotSame(source.DesktopThemeColumnOrder, clone.DesktopThemeColumnOrder);
            Assert.AreNotSame(source.GameSummariesColumnOrder, target.GameSummariesColumnOrder);
            Assert.AreNotSame(source.OverviewRecentAchievementColumnAlignments, clone.OverviewRecentAchievementColumnAlignments);
            Assert.AreNotSame(source.OverviewSelectedGameAchievementColumnAlignments, target.OverviewSelectedGameAchievementColumnAlignments);
            Assert.AreNotSame(source.SingleGameColumnAlignments, clone.SingleGameColumnAlignments);
            Assert.AreNotSame(source.DesktopThemeColumnAlignments, clone.DesktopThemeColumnAlignments);
            Assert.AreNotSame(source.GameSummariesColumnAlignments, target.GameSummariesColumnAlignments);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveGridLayoutSettings()
        {
            var source = new PersistedSettings
            {
                SingleGameGridRowHeight = 72d,
                OverviewGameSummariesGridRowHeight = 84d,
                OverviewRecentAchievementsGridRowHeight = 96d,
                OverviewSelectedGameGridRowHeight = 108d,
                StartPageGameSummariesGridRowHeight = 120d,
                StartPageRecentAchievementsGridRowHeight = 132d,
                DesktopThemeAchievementGridRowHeight = 144d,
                SingleGameGridMaxRows = 2,
                OverviewGameSummariesGridMaxRows = 3,
                OverviewRecentAchievementsGridMaxRows = 4,
                OverviewSelectedGameGridMaxRows = 5,
                StartPageGameSummariesGridMaxRows = 6,
                StartPageRecentAchievementsGridMaxRows = 7,
                DesktopThemeAchievementGridMaxRows = 8
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.AreEqual(72d, clone.SingleGameGridRowHeight);
            Assert.AreEqual(84d, clone.OverviewGameSummariesGridRowHeight);
            Assert.AreEqual(96d, clone.OverviewRecentAchievementsGridRowHeight);
            Assert.AreEqual(108d, clone.OverviewSelectedGameGridRowHeight);
            Assert.AreEqual(120d, clone.StartPageGameSummariesGridRowHeight);
            Assert.AreEqual(132d, clone.StartPageRecentAchievementsGridRowHeight);
            Assert.AreEqual(144d, clone.DesktopThemeAchievementGridRowHeight);
            Assert.AreEqual(2, clone.SingleGameGridMaxRows);
            Assert.AreEqual(3, clone.OverviewGameSummariesGridMaxRows);
            Assert.AreEqual(4, clone.OverviewRecentAchievementsGridMaxRows);
            Assert.AreEqual(5, clone.OverviewSelectedGameGridMaxRows);
            Assert.AreEqual(6, clone.StartPageGameSummariesGridMaxRows);
            Assert.AreEqual(7, clone.StartPageRecentAchievementsGridMaxRows);
            Assert.AreEqual(8, clone.DesktopThemeAchievementGridMaxRows);

            Assert.AreEqual(clone.SingleGameGridRowHeight, target.SingleGameGridRowHeight);
            Assert.AreEqual(clone.OverviewGameSummariesGridRowHeight, target.OverviewGameSummariesGridRowHeight);
            Assert.AreEqual(clone.OverviewRecentAchievementsGridRowHeight, target.OverviewRecentAchievementsGridRowHeight);
            Assert.AreEqual(clone.OverviewSelectedGameGridRowHeight, target.OverviewSelectedGameGridRowHeight);
            Assert.AreEqual(clone.StartPageGameSummariesGridRowHeight, target.StartPageGameSummariesGridRowHeight);
            Assert.AreEqual(clone.StartPageRecentAchievementsGridRowHeight, target.StartPageRecentAchievementsGridRowHeight);
            Assert.AreEqual(clone.DesktopThemeAchievementGridRowHeight, target.DesktopThemeAchievementGridRowHeight);
            Assert.AreEqual(clone.SingleGameGridMaxRows, target.SingleGameGridMaxRows);
            Assert.AreEqual(clone.OverviewGameSummariesGridMaxRows, target.OverviewGameSummariesGridMaxRows);
            Assert.AreEqual(clone.OverviewRecentAchievementsGridMaxRows, target.OverviewRecentAchievementsGridMaxRows);
            Assert.AreEqual(clone.OverviewSelectedGameGridMaxRows, target.OverviewSelectedGameGridMaxRows);
            Assert.AreEqual(clone.StartPageGameSummariesGridMaxRows, target.StartPageGameSummariesGridMaxRows);
            Assert.AreEqual(clone.StartPageRecentAchievementsGridMaxRows, target.StartPageRecentAchievementsGridMaxRows);
            Assert.AreEqual(clone.DesktopThemeAchievementGridMaxRows, target.DesktopThemeAchievementGridMaxRows);
        }
    }
}

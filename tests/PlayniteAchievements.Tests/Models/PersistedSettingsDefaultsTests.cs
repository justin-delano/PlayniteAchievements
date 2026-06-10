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
        public void Constructor_DefaultsSidebarOverviewColumnRatio()
        {
            var settings = new PersistedSettings();

            Assert.AreEqual(
                PersistedSettings.DefaultSidebarOverviewLeftColumnRatio,
                settings.SidebarOverviewLeftColumnRatio);
        }

        [TestMethod]
        public void Constructor_DefaultsSidebarScoreCardsVisible()
        {
            var settings = new PersistedSettings();

            Assert.IsTrue(settings.ShowSidebarCollectionScoreCard);
            Assert.IsTrue(settings.ShowSidebarPrestigeScoreCard);
        }

        [TestMethod]
        public void Constructor_DefaultsColumnHeadersVisible()
        {
            var settings = new PersistedSettings();

            Assert.IsTrue(settings.ShowOverviewGridColumnHeaders);
            Assert.IsTrue(settings.ShowAchievementGridColumnHeaders);
            Assert.IsTrue(settings.ShowDesktopThemeAchievementGridColumnHeaders);
        }

        [TestMethod]
        public void Constructor_DefaultsGridLayoutSettings()
        {
            var settings = new PersistedSettings();

            Assert.IsNull(settings.SingleGameGridRowHeight);
            Assert.IsNull(settings.SidebarOverviewGridRowHeight);
            Assert.IsNull(settings.SidebarRecentAchievementsGridRowHeight);
            Assert.IsNull(settings.SidebarSelectedGameGridRowHeight);
            Assert.IsNull(settings.StartPageGamesOverviewGridRowHeight);
            Assert.IsNull(settings.StartPageRecentAchievementsGridRowHeight);
            Assert.IsNull(settings.DesktopThemeAchievementGridRowHeight);

            Assert.IsNull(settings.SingleGameGridMaxRows);
            Assert.IsNull(settings.SidebarOverviewGridMaxRows);
            Assert.IsNull(settings.SidebarRecentAchievementsGridMaxRows);
            Assert.IsNull(settings.SidebarSelectedGameGridMaxRows);
            Assert.AreEqual(PersistedSettings.DefaultStartPageGridMaxRows, settings.StartPageGamesOverviewGridMaxRows);
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
        public void SidebarOverviewColumnRatio_ClampsInvalidValues()
        {
            var settings = new PersistedSettings();

            settings.SidebarOverviewLeftColumnRatio = -1d;
            Assert.AreEqual(
                PersistedSettings.MinSidebarOverviewLeftColumnRatio,
                settings.SidebarOverviewLeftColumnRatio);

            settings.SidebarOverviewLeftColumnRatio = 2d;
            Assert.AreEqual(
                PersistedSettings.MaxSidebarOverviewLeftColumnRatio,
                settings.SidebarOverviewLeftColumnRatio);

            settings.SidebarOverviewLeftColumnRatio = double.NaN;
            Assert.AreEqual(
                PersistedSettings.DefaultSidebarOverviewLeftColumnRatio,
                settings.SidebarOverviewLeftColumnRatio);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveSidebarOverviewColumnRatio()
        {
            var source = new PersistedSettings
            {
                SidebarOverviewLeftColumnRatio = 0.64d
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.AreEqual(0.64d, clone.SidebarOverviewLeftColumnRatio);
            Assert.AreEqual(0.64d, target.SidebarOverviewLeftColumnRatio);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveSidebarScoreCardVisibility()
        {
            var source = new PersistedSettings
            {
                ShowSidebarCollectionScoreCard = false,
                ShowSidebarPrestigeScoreCard = false
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.IsFalse(clone.ShowSidebarCollectionScoreCard);
            Assert.IsFalse(clone.ShowSidebarPrestigeScoreCard);
            Assert.IsFalse(target.ShowSidebarCollectionScoreCard);
            Assert.IsFalse(target.ShowSidebarPrestigeScoreCard);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveColumnHeaderVisibilityAndColumnOrder()
        {
            var source = new PersistedSettings
            {
                ShowOverviewGridColumnHeaders = false,
                ShowAchievementGridColumnHeaders = false,
                ShowDesktopThemeAchievementGridColumnHeaders = false,
                SidebarAchievementColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Title"] = 2
                },
                SidebarGameColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Rarity"] = 3
                },
                SingleGameColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Achievement"] = 1
                },
                DesktopThemeColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Points"] = 4
                },
                GamesOverviewColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["OverviewGameName"] = 1
                },
                DataGridColumnOrder = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["Legacy"] = 5
                }
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.IsFalse(clone.ShowOverviewGridColumnHeaders);
            Assert.IsFalse(clone.ShowAchievementGridColumnHeaders);
            Assert.IsFalse(clone.ShowDesktopThemeAchievementGridColumnHeaders);
            Assert.IsFalse(target.ShowOverviewGridColumnHeaders);
            Assert.IsFalse(target.ShowAchievementGridColumnHeaders);
            Assert.IsFalse(target.ShowDesktopThemeAchievementGridColumnHeaders);

            Assert.AreEqual(2, clone.SidebarAchievementColumnOrder["Title"]);
            Assert.AreEqual(3, clone.SidebarGameColumnOrder["Rarity"]);
            Assert.AreEqual(1, clone.SingleGameColumnOrder["Achievement"]);
            Assert.AreEqual(4, clone.DesktopThemeColumnOrder["Points"]);
            Assert.AreEqual(1, clone.GamesOverviewColumnOrder["OverviewGameName"]);
            Assert.AreEqual(5, clone.DataGridColumnOrder["Legacy"]);

            Assert.AreEqual(2, target.SidebarAchievementColumnOrder["Title"]);
            Assert.AreEqual(3, target.SidebarGameColumnOrder["Rarity"]);
            Assert.AreEqual(1, target.SingleGameColumnOrder["Achievement"]);
            Assert.AreEqual(4, target.DesktopThemeColumnOrder["Points"]);
            Assert.AreEqual(1, target.GamesOverviewColumnOrder["OverviewGameName"]);
            Assert.AreEqual(5, target.DataGridColumnOrder["Legacy"]);

            Assert.AreNotSame(source.SidebarAchievementColumnOrder, clone.SidebarAchievementColumnOrder);
            Assert.AreNotSame(source.SidebarGameColumnOrder, target.SidebarGameColumnOrder);
            Assert.AreNotSame(source.DesktopThemeColumnOrder, clone.DesktopThemeColumnOrder);
            Assert.AreNotSame(source.GamesOverviewColumnOrder, target.GamesOverviewColumnOrder);
        }

        [TestMethod]
        public void CloneAndCopyFrom_PreserveGridLayoutSettings()
        {
            var source = new PersistedSettings
            {
                SingleGameGridRowHeight = 72d,
                SidebarOverviewGridRowHeight = 84d,
                SidebarRecentAchievementsGridRowHeight = 96d,
                SidebarSelectedGameGridRowHeight = 108d,
                StartPageGamesOverviewGridRowHeight = 120d,
                StartPageRecentAchievementsGridRowHeight = 132d,
                DesktopThemeAchievementGridRowHeight = 144d,
                SingleGameGridMaxRows = 2,
                SidebarOverviewGridMaxRows = 3,
                SidebarRecentAchievementsGridMaxRows = 4,
                SidebarSelectedGameGridMaxRows = 5,
                StartPageGamesOverviewGridMaxRows = 6,
                StartPageRecentAchievementsGridMaxRows = 7,
                DesktopThemeAchievementGridMaxRows = 8
            };

            var clone = source.Clone();
            var target = new PersistedSettings();
            target.CopyFrom(source);

            Assert.AreEqual(72d, clone.SingleGameGridRowHeight);
            Assert.AreEqual(84d, clone.SidebarOverviewGridRowHeight);
            Assert.AreEqual(96d, clone.SidebarRecentAchievementsGridRowHeight);
            Assert.AreEqual(108d, clone.SidebarSelectedGameGridRowHeight);
            Assert.AreEqual(120d, clone.StartPageGamesOverviewGridRowHeight);
            Assert.AreEqual(132d, clone.StartPageRecentAchievementsGridRowHeight);
            Assert.AreEqual(144d, clone.DesktopThemeAchievementGridRowHeight);
            Assert.AreEqual(2, clone.SingleGameGridMaxRows);
            Assert.AreEqual(3, clone.SidebarOverviewGridMaxRows);
            Assert.AreEqual(4, clone.SidebarRecentAchievementsGridMaxRows);
            Assert.AreEqual(5, clone.SidebarSelectedGameGridMaxRows);
            Assert.AreEqual(6, clone.StartPageGamesOverviewGridMaxRows);
            Assert.AreEqual(7, clone.StartPageRecentAchievementsGridMaxRows);
            Assert.AreEqual(8, clone.DesktopThemeAchievementGridMaxRows);

            Assert.AreEqual(clone.SingleGameGridRowHeight, target.SingleGameGridRowHeight);
            Assert.AreEqual(clone.SidebarOverviewGridRowHeight, target.SidebarOverviewGridRowHeight);
            Assert.AreEqual(clone.SidebarRecentAchievementsGridRowHeight, target.SidebarRecentAchievementsGridRowHeight);
            Assert.AreEqual(clone.SidebarSelectedGameGridRowHeight, target.SidebarSelectedGameGridRowHeight);
            Assert.AreEqual(clone.StartPageGamesOverviewGridRowHeight, target.StartPageGamesOverviewGridRowHeight);
            Assert.AreEqual(clone.StartPageRecentAchievementsGridRowHeight, target.StartPageRecentAchievementsGridRowHeight);
            Assert.AreEqual(clone.DesktopThemeAchievementGridRowHeight, target.DesktopThemeAchievementGridRowHeight);
            Assert.AreEqual(clone.SingleGameGridMaxRows, target.SingleGameGridMaxRows);
            Assert.AreEqual(clone.SidebarOverviewGridMaxRows, target.SidebarOverviewGridMaxRows);
            Assert.AreEqual(clone.SidebarRecentAchievementsGridMaxRows, target.SidebarRecentAchievementsGridMaxRows);
            Assert.AreEqual(clone.SidebarSelectedGameGridMaxRows, target.SidebarSelectedGameGridMaxRows);
            Assert.AreEqual(clone.StartPageGamesOverviewGridMaxRows, target.StartPageGamesOverviewGridMaxRows);
            Assert.AreEqual(clone.StartPageRecentAchievementsGridMaxRows, target.StartPageRecentAchievementsGridMaxRows);
            Assert.AreEqual(clone.DesktopThemeAchievementGridMaxRows, target.DesktopThemeAchievementGridMaxRows);
        }
    }
}

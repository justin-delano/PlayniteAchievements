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
    }
}

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
                }
            };

            var clone = source.Clone();
            var copy = new PersistedSettings();
            copy.CopyFrom(source);

            Assert.IsFalse(clone.StartPageAchievementColumnVisibility["Achievement"]);
            Assert.AreEqual(320, clone.StartPageAchievementColumnWidths["Achievement"]);
            Assert.AreEqual(2, clone.StartPageAchievementColumnOrder["Achievement"]);
            Assert.IsFalse(clone.StartPageGamesOverviewColumnVisibility["OverviewProvider"]);
            Assert.AreEqual(140, clone.StartPageGamesOverviewColumnWidths["OverviewProvider"]);
            Assert.AreEqual(3, clone.StartPageGamesOverviewColumnOrder["OverviewProvider"]);

            Assert.IsFalse(copy.StartPageAchievementColumnVisibility["Achievement"]);
            Assert.AreEqual(320, copy.StartPageAchievementColumnWidths["Achievement"]);
            Assert.AreEqual(2, copy.StartPageAchievementColumnOrder["Achievement"]);
            Assert.IsFalse(copy.StartPageGamesOverviewColumnVisibility["OverviewProvider"]);
            Assert.AreEqual(140, copy.StartPageGamesOverviewColumnWidths["OverviewProvider"]);
            Assert.AreEqual(3, copy.StartPageGamesOverviewColumnOrder["OverviewProvider"]);

            Assert.AreNotSame(source.StartPageAchievementColumnVisibility, clone.StartPageAchievementColumnVisibility);
            Assert.AreNotSame(source.StartPageAchievementColumnWidths, clone.StartPageAchievementColumnWidths);
            Assert.AreNotSame(source.StartPageAchievementColumnOrder, clone.StartPageAchievementColumnOrder);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnVisibility, copy.StartPageGamesOverviewColumnVisibility);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnWidths, copy.StartPageGamesOverviewColumnWidths);
            Assert.AreNotSame(source.StartPageGamesOverviewColumnOrder, copy.StartPageGamesOverviewColumnOrder);
        }
    }
}

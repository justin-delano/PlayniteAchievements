using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.Models
{
    [TestClass]
    public class OverviewDataSnapshotTotalsTests
    {
        [TestMethod]
        public void FromGameSummaries_TotalsOnlyTheGivenGames()
        {
            var games = new List<GameSummaryItem>
            {
                new GameSummaryItem { ProviderKey = "Steam", TotalAchievements = 10, UnlockedAchievements = 8 },
                new GameSummaryItem { ProviderKey = "steam", TotalAchievements = 5, UnlockedAchievements = 1 },
                new GameSummaryItem { ProviderKey = "Epic", TotalAchievements = 20, UnlockedAchievements = 13 }
            };

            var snapshot = OverviewDataSnapshot.FromGameSummaries(games);

            Assert.AreEqual(3, snapshot.TotalGames);
            Assert.AreEqual(35, snapshot.TotalAchievements);
            Assert.AreEqual(22, snapshot.TotalUnlocked);
            Assert.AreEqual(13, snapshot.TotalLocked);
            Assert.AreEqual(9, snapshot.UnlockedByProvider["STEAM"]);
            Assert.AreEqual(15, snapshot.TotalByProvider["Steam"]);
            Assert.AreEqual(13, snapshot.UnlockedByProvider["Epic"]);
            Assert.AreEqual(3, snapshot.GameSummaries.Count);
        }

        [TestMethod]
        public void FromGameSummaries_EmptyOrNullListHasZeroTotals()
        {
            foreach (var games in new[] { new List<GameSummaryItem>(), null })
            {
                var snapshot = OverviewDataSnapshot.FromGameSummaries(games);

                Assert.AreEqual(0, snapshot.TotalGames);
                Assert.AreEqual(0, snapshot.TotalAchievements);
                Assert.AreEqual(0, snapshot.TotalLocked);
                Assert.AreEqual(0, snapshot.TotalByProvider.Count);
                Assert.AreEqual(0, snapshot.GameSummaries.Count);
            }
        }
    }
}

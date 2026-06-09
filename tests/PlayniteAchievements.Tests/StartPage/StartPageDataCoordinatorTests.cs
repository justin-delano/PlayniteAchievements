using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Sidebar;
using PlayniteAchievements.Services.StartPage;

namespace PlayniteAchievements.Tests.StartPage
{
    [TestClass]
    public class StartPageDataCoordinatorTests
    {
        [TestMethod]
        public async Task GetSnapshotAsync_ReusesSnapshotUntilInvalidated()
        {
            var buildCount = 0;
            var coordinator = new StartPageDataCoordinator(() =>
            {
                buildCount++;
                return new SidebarDataSnapshot { TotalGames = buildCount };
            });

            var first = await coordinator.GetSnapshotAsync(default);
            var second = await coordinator.GetSnapshotAsync(default);
            coordinator.Invalidate();
            var third = await coordinator.GetSnapshotAsync(default);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, second.TotalGames);
            Assert.AreEqual(2, third.TotalGames);
            Assert.AreEqual(2, buildCount);
        }

        [TestMethod]
        public void Invalidate_RaisesSnapshotInvalidated()
        {
            var coordinator = new StartPageDataCoordinator(() => new SidebarDataSnapshot());
            var raised = false;
            coordinator.SnapshotInvalidated += (_, __) => raised = true;

            coordinator.Invalidate();

            Assert.IsTrue(raised);
        }
    }
}

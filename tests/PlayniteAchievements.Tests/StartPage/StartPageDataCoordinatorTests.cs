using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Overview;
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
                return new OverviewDataSnapshot { TotalGames = buildCount };
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
            var coordinator = new StartPageDataCoordinator(() => new OverviewDataSnapshot());
            var raised = false;
            coordinator.SnapshotInvalidated += (_, __) => raised = true;

            coordinator.Invalidate();

            Assert.IsTrue(raised);
        }

        [TestMethod]
        public async Task ConcurrentForcedRefreshes_ReuseSingleInFlightBuild()
        {
            var buildCount = 0;
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var coordinator = new StartPageDataCoordinator(() =>
                {
                    Interlocked.Increment(ref buildCount);
                    started.Set();
                    release.Wait();
                    return new OverviewDataSnapshot();
                });

                var first = coordinator.GetSnapshotAsync(true, default);
                Assert.IsTrue(started.Wait(2000));
                var second = coordinator.GetSnapshotAsync(true, default);
                release.Set();
                await Task.WhenAll(first, second);

                Assert.AreEqual(1, buildCount);
                Assert.AreSame(first.Result, second.Result);
            }
        }

        [TestMethod]
        public async Task InvalidationDuringBuild_RebuildsBeforePublishingSnapshot()
        {
            var buildCount = 0;
            using (var firstStarted = new ManualResetEventSlim())
            using (var releaseFirst = new ManualResetEventSlim())
            {
                var coordinator = new StartPageDataCoordinator(() =>
                {
                    var build = Interlocked.Increment(ref buildCount);
                    if (build == 1)
                    {
                        firstStarted.Set();
                        releaseFirst.Wait();
                    }

                    return new OverviewDataSnapshot { TotalGames = build };
                });

                var request = coordinator.GetSnapshotAsync(default);
                Assert.IsTrue(firstStarted.Wait(2000));
                coordinator.Invalidate();
                releaseFirst.Set();

                var snapshot = await request;

                Assert.AreEqual(2, buildCount);
                Assert.AreEqual(2, snapshot.TotalGames);
            }
        }
    }
}

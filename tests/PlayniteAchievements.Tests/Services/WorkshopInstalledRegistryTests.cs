using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Workshop;
using System;
using System.IO;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class WorkshopInstalledRegistryTests
    {
        [TestMethod]
        public void Record_Find_Forget_RoundTripThroughDisk()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                var item = new WorkshopItem { Id = "themes/neon", Kind = WorkshopItemKind.Theme, Name = "Neon", Version = "1.0.0" };
                registry.Record(item);

                var reloaded = new WorkshopInstalledRegistry(dir);
                var found = reloaded.Find("themes/neon");
                Assert.IsNotNull(found);
                Assert.AreEqual("1.0.0", found.Version);
                Assert.AreEqual(WorkshopItemKind.Theme, found.Kind);

                item.Version = "1.1.0";
                reloaded.Record(item);
                Assert.AreEqual(1, reloaded.Items.Count, "re-recording replaces the earlier record");
                Assert.AreEqual("1.1.0", reloaded.Find("themes/neon").Version);

                reloaded.Forget("themes/neon");
                Assert.IsNull(new WorkshopInstalledRegistry(dir).Find("themes/neon"));
            });
        }

        [TestMethod]
        public void GameData_IsRecordedPerGame()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                var item = new WorkshopItem { Id = "game-data/steam-440/icons", Kind = WorkshopItemKind.GameCustomData, Name = "Icons", Version = "1.0.0" };
                var a = Guid.NewGuid();
                var b = Guid.NewGuid();
                registry.Record(item, a);
                registry.Record(item, b);

                Assert.AreEqual(2, registry.Items.Count);
                Assert.IsNotNull(registry.Find(item.Id, a));
                Assert.IsNotNull(registry.Find(item.Id, b));
                Assert.IsNotNull(registry.Find(item.Id), "a game-agnostic lookup still finds the item");
            });
        }

        [TestMethod]
        public void IsNewer_ComparesNumerically()
        {
            Assert.IsTrue(WorkshopInstalledRegistry.IsNewer("1.10.0", "1.9.3"));
            Assert.IsFalse(WorkshopInstalledRegistry.IsNewer("1.9.3", "1.10.0"));
            Assert.IsFalse(WorkshopInstalledRegistry.IsNewer("1.0.0", "1.0.0"));
            Assert.IsFalse(WorkshopInstalledRegistry.IsNewer("garbage", "1.0.0"));
            Assert.IsTrue(WorkshopInstalledRegistry.IsNewer("4.1.0", "4.0.1"));
        }

        [TestMethod]
        public void SubmitterKey_IsStableAndHashed_AndSubmissionsPersist()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                var key = registry.GetOrCreateSubmitterKey();
                var hash = registry.GetSubmitterHash();
                Assert.AreEqual(64, key.Length);
                Assert.AreEqual(64, hash.Length);
                Assert.AreNotEqual(key, hash);

                registry.DisplayName = "Someone";
                registry.RecordSubmission(new WorkshopSubmissionRecord { IssueNumber = 7, Name = "Neon", Kind = WorkshopItemKind.Theme, SubmittedUtc = DateTime.UtcNow });
                registry.UpdateSubmissionState(7, "in-review", "themes/neon");

                var reloaded = new WorkshopInstalledRegistry(dir);
                Assert.AreEqual(key, reloaded.GetOrCreateSubmitterKey(), "the key survives a restart");
                Assert.AreEqual("Someone", reloaded.DisplayName);
                Assert.AreEqual(1, reloaded.Submissions.Count);
                Assert.AreEqual("in-review", reloaded.Submissions[0].LastState);
                Assert.AreEqual("themes/neon", reloaded.Submissions[0].ItemId);
            });
        }

        private static void WithTemp(Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                body(dir);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

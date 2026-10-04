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
                var item = new WorkshopItem { Id = "bundles/neon", Kind = WorkshopItemKind.Bundle, Name = "Neon", Version = "1.0.0" };
                registry.Record(item);

                var reloaded = new WorkshopInstalledRegistry(dir);
                var found = reloaded.Find("bundles/neon");
                Assert.IsNotNull(found);
                Assert.AreEqual("1.0.0", found.Version);
                Assert.AreEqual(WorkshopItemKind.Bundle, found.Kind);

                item.Version = "1.1.0";
                reloaded.Record(item);
                Assert.AreEqual(1, reloaded.Items.Count, "re-recording replaces the earlier record");
                Assert.AreEqual("1.1.0", reloaded.Find("bundles/neon").Version);

                reloaded.Forget("bundles/neon");
                Assert.IsNull(new WorkshopInstalledRegistry(dir).Find("bundles/neon"));
            });
        }

        [TestMethod]
        public void Find_ReturnsContentHashAndBaselineFile()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                var item = new WorkshopItem { Id = "game-data/steam-1/pack", Kind = WorkshopItemKind.GameCustomData, Name = "Pack", Version = "1.0.0" };
                var game = Guid.NewGuid();
                registry.Record(item, game, "icons=abc;data=def", Path.Combine(dir, "baseline.json"));

                var found = registry.Find(item.Id, game);
                Assert.AreEqual("icons=abc;data=def", found.ContentHash);
                Assert.AreEqual(Path.Combine(dir, "baseline.json"), found.BaselineFile);

                var reloaded = new WorkshopInstalledRegistry(dir).Find(item.Id);
                Assert.AreEqual("icons=abc;data=def", reloaded.ContentHash, "the hash survives a reload");
                Assert.AreEqual(Path.Combine(dir, "baseline.json"), reloaded.BaselineFile, "the baseline path survives a reload");
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
                registry.RecordSubmission(new WorkshopSubmissionRecord { IssueNumber = 7, Name = "Neon", Kind = WorkshopItemKind.Bundle, SubmittedUtc = DateTime.UtcNow });
                registry.UpdateSubmissionState(7, "in-review", "bundles/neon");

                var reloaded = new WorkshopInstalledRegistry(dir);
                Assert.AreEqual(key, reloaded.GetOrCreateSubmitterKey(), "the key survives a restart");
                Assert.AreEqual("Someone", reloaded.DisplayName);
                Assert.AreEqual(1, reloaded.Submissions.Count);
                Assert.AreEqual("in-review", reloaded.Submissions[0].LastState);
                Assert.AreEqual("bundles/neon", reloaded.Submissions[0].ItemId);
            });
        }

        [TestMethod]
        public void TrySetSubmitterKey_AcceptsAKeyFromAnotherInstall_AndRejectsAnythingElse()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                var original = registry.GetOrCreateSubmitterKey();
                var other = new string('a', 32) + new string('B', 32);

                Assert.IsFalse(registry.TrySetSubmitterKey(null));
                Assert.IsFalse(registry.TrySetSubmitterKey("not a key"));
                Assert.IsFalse(registry.TrySetSubmitterKey(original.Substring(1)));
                Assert.IsFalse(registry.TrySetSubmitterKey(new string('g', 64)));
                Assert.AreEqual(original, registry.GetOrCreateSubmitterKey(), "rejected input leaves the key alone");

                Assert.IsTrue(registry.IsValidSubmitterKey("  " + other + "  "));
                Assert.IsTrue(registry.TrySetSubmitterKey("  " + other + "  "));
                Assert.AreEqual(other.ToLowerInvariant(), registry.GetOrCreateSubmitterKey(), "stored lower-case and trimmed");
                Assert.AreEqual(other.ToLowerInvariant(), new WorkshopInstalledRegistry(dir).GetOrCreateSubmitterKey(), "persisted");
            });
        }

        [TestMethod]
        public void LinkSubmissions_GivesUnlinkedRecordsTheirPublishedId()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                registry.RecordSubmission(new WorkshopSubmissionRecord { IssueNumber = 1, Name = "Neon", Kind = WorkshopItemKind.Colors, SubmittedUtc = DateTime.UtcNow });
                registry.RecordSubmission(new WorkshopSubmissionRecord { IssueNumber = 2, Name = "Neon", Kind = WorkshopItemKind.UnlockSounds, SubmittedUtc = DateTime.UtcNow });
                registry.RecordSubmission(new WorkshopSubmissionRecord { IssueNumber = 3, Name = "Other", Kind = WorkshopItemKind.Colors, ItemId = "colors/kept", SubmittedUtc = DateTime.UtcNow });

                registry.LinkSubmissions(new[]
                {
                    new WorkshopItem { Id = "colors/neon", Kind = WorkshopItemKind.Colors, Name = "neon" },
                    new WorkshopItem { Id = "colors/other", Kind = WorkshopItemKind.Colors, Name = "Other" }
                });

                var reloaded = new WorkshopInstalledRegistry(dir);
                Assert.AreEqual("colors/neon", Find(reloaded, 1).ItemId, "same kind and name, case-insensitive");
                Assert.IsNull(Find(reloaded, 2).ItemId, "a different kind is not linked");
                Assert.AreEqual("colors/kept", Find(reloaded, 3).ItemId, "an existing id is left alone");
            });
        }

        private static WorkshopSubmissionRecord Find(WorkshopInstalledRegistry registry, int issueNumber)
        {
            foreach (var record in registry.Submissions)
            {
                if (record.IssueNumber == issueNumber)
                {
                    return record;
                }
            }

            return null;
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

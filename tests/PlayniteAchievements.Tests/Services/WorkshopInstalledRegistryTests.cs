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
        public void ReadLegacyInstalls_ReadsInstalledJson_PerGameRecordsIncluded()
        {
            WithTemp(dir =>
            {
                var game = Guid.NewGuid();
                WriteInstalled(dir, "[" +
                    "{\"Id\":\"bundles/neon\",\"Kind\":\"Bundle\",\"Name\":\"Neon\",\"Version\":\"1.0.0\",\"ContentHash\":\"colors=abc\"}," +
                    "{\"Id\":\"game-data/steam-1/pack\",\"Kind\":\"GameCustomData\",\"Name\":\"Pack\",\"Version\":\"1.0.0\",\"PlayniteGameId\":\"" + game + "\",\"BaselineFile\":\"baseline.json\"}," +
                    "{\"Name\":\"no id\"}" +
                    "]");

                var installs = new WorkshopInstalledRegistry(dir).ReadLegacyInstalls();

                Assert.AreEqual(2, installs.Count, "a record without an id is skipped");
                Assert.AreEqual("colors=abc", installs[0].ContentHash);
                Assert.AreEqual(WorkshopItemKind.Bundle, installs[0].Kind);
                Assert.AreEqual(game, installs[1].PlayniteGameId);
                Assert.AreEqual("baseline.json", installs[1].BaselineFile);
            });
        }

        [TestMethod]
        public void ReadLegacyInstalls_IsEmptyWithoutAFileOrForAnUnreadableOne()
        {
            WithTemp(dir =>
            {
                Assert.AreEqual(0, new WorkshopInstalledRegistry(dir).ReadLegacyInstalls().Count);

                WriteInstalled(dir, "not json");
                Assert.AreEqual(0, new WorkshopInstalledRegistry(dir).ReadLegacyInstalls().Count);
            });
        }

        [TestMethod]
        public void RetireLegacyInstalls_KeepsTheFileAsABackup_AndItIsReadNoMore()
        {
            WithTemp(dir =>
            {
                WriteInstalled(dir, "[{\"Id\":\"colors/neon\",\"Kind\":\"Colors\",\"Version\":\"1.0.0\"}]");
                var registry = new WorkshopInstalledRegistry(dir);

                Assert.IsTrue(registry.RetireLegacyInstalls());

                Assert.AreEqual(0, registry.ReadLegacyInstalls().Count);
                Assert.IsTrue(File.Exists(Path.Combine(dir, WorkshopInstalledRegistry.DirectoryName, "installed.migrated.json")));
                Assert.IsFalse(registry.RetireLegacyInstalls(), "nothing is left to retire");
            });
        }

        [TestMethod]
        public void Changed_IsRaisedForASubmission()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                var raised = 0;
                registry.Changed += (_, __) => raised++;

                registry.RecordSubmission(new WorkshopSubmissionRecord { IssueNumber = 7, Name = "Neon", Kind = WorkshopItemKind.Colors });

                Assert.AreEqual(1, raised, "a submission is announced");
            });
        }

        [TestMethod]
        public void TryGetSubmitterHash_IsNullUntilAKeyExists_AndCreatesNone()
        {
            WithTemp(dir =>
            {
                var registry = new WorkshopInstalledRegistry(dir);
                Assert.IsNull(registry.TryGetSubmitterHash());
                Assert.IsNull(new WorkshopInstalledRegistry(dir).TryGetSubmitterHash(), "the lookup saved no key");

                var hash = registry.GetSubmitterHash();
                Assert.AreEqual(hash, registry.TryGetSubmitterHash());
                Assert.AreEqual(hash, new WorkshopInstalledRegistry(dir).TryGetSubmitterHash());
            });
        }

        private static void WriteInstalled(string dir, string json)
        {
            var directory = Path.Combine(dir, WorkshopInstalledRegistry.DirectoryName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "installed.json"), json);
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

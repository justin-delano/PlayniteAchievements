using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class LibraryMigrationTests
    {
        private static readonly DateTime InstalledAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchLibraryMigration_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void UnchangedColorPreset_BecomesAWorkshopItem()
        {
            var path = WritePreset(LibraryItemKind.Colors, "Neon", "neon colors");
            WriteRegistry(Record("neon", WorkshopItemKind.Colors, "Neon", "1.2.0", "colors=" + LibraryStore.HashFile(path)));

            var plan = Run();

            var item = Store().Items.Single();
            Assert.AreEqual("ws:neon", item.Id);
            Assert.AreEqual(LibraryItemOrigin.Workshop, item.Origin);
            Assert.AreEqual("neon", item.WorkshopItemId);
            Assert.AreEqual("colors", item.Part);
            Assert.AreEqual("1.2.0", item.Version);
            Assert.AreEqual(Path.Combine("color_presets", "Neon.pacolors"), item.RelativePath);
            Assert.AreEqual(InstalledAt, item.AddedUtc);
            Assert.IsTrue(plan.CreatedIndex);
            Assert.AreEqual(1, plan.RenamedIds.Count());
        }

        [TestMethod]
        public void EditedPreset_StaysLocal()
        {
            var path = WritePreset(LibraryItemKind.Sounds, "Chimes", "original sounds");
            var recorded = LibraryStore.HashFile(path);
            File.WriteAllText(path, "sounds the user changed");
            WriteRegistry(Record("chimes", WorkshopItemKind.UnlockSounds, "Chimes", "1.0.0", "sounds=" + recorded));

            var plan = Run();

            var item = Store().Items.Single();
            Assert.AreEqual(LibraryItemOrigin.Local, item.Origin);
            Assert.AreEqual(0, plan.Steps.Count);
        }

        [TestMethod]
        public void Bundle_BecomesOneItemPerUnchangedPart()
        {
            var colors = WritePreset(LibraryItemKind.Colors, "Retro", "bundle colors");
            var toast = WritePreset(LibraryItemKind.Toast, "Retro", "bundle toast");
            var sounds = WritePreset(LibraryItemKind.Sounds, "Retro", "bundle sounds");
            var soundsHash = LibraryStore.HashFile(sounds);
            File.WriteAllText(sounds, "sounds edited after the install");
            WriteRegistry(Record("retro", WorkshopItemKind.Bundle, "Retro", "2.0.0",
                $"colors={LibraryStore.HashFile(colors)};toast={LibraryStore.HashFile(toast)};sounds={soundsHash}"));

            Run();

            var items = Store().Items;
            Assert.AreEqual(LibraryItemKind.Colors, items.Single(item => item.Id == "ws:retro#colors").Kind);
            Assert.AreEqual(LibraryItemKind.Toast, items.Single(item => item.Id == "ws:retro#toast").Kind);
            Assert.AreEqual(LibraryItemOrigin.Local, items.Single(item => item.Kind == LibraryItemKind.Sounds).Origin);
        }

        [TestMethod]
        public void NotificationStyleWithFrame_KeepsThePlainIdForTheToast()
        {
            var toast = WritePreset(LibraryItemKind.Toast, "Glass", "glass toast");
            var frame = WritePreset(LibraryItemKind.Frame, "Glass", "glass frame");
            WriteRegistry(Record("glass", WorkshopItemKind.NotificationStyle, "Glass", "1.0.0",
                $"toast={LibraryStore.HashFile(toast)};frame={LibraryStore.HashFile(frame)}"));

            Run();

            var items = Store().Items;
            Assert.AreEqual(LibraryItemKind.Toast, items.Single(item => item.Id == "ws:glass").Kind);
            Assert.AreEqual(LibraryItemKind.Frame, items.Single(item => item.Id == "ws:glass#frame").Kind);
        }

        [TestMethod]
        public void ShowcasePage_BecomesAWorkshopItemWithoutPackage()
        {
            WriteRegistry(Record("stats", WorkshopItemKind.ShowcasePage, "Stats page", "1.1.0", null));

            Run();

            var item = Store().Find("ws:stats");
            Assert.AreEqual(LibraryItemKind.ShowcasePage, item.Kind);
            Assert.AreEqual(LibraryItemOrigin.Workshop, item.Origin);
            Assert.IsNull(item.RelativePath);
            Assert.AreEqual("1.1.0", item.Version);
        }

        [TestMethod]
        public void GameData_IsNoLibraryItemAndReportsEveryGameWithItsVersionAndBaseline()
        {
            var gameA = Guid.NewGuid();
            var gameB = Guid.NewGuid();
            var older = Record("notes", WorkshopItemKind.GameCustomData, "Notes", "1.0.0", null);
            older.PlayniteGameId = gameA;
            older.BaselineFile = @"C:\baselines\notes-a.json";
            var newer = Record("notes", WorkshopItemKind.GameCustomData, "Notes", "1.1.0", null);
            newer.PlayniteGameId = gameB;
            newer.BaselineFile = @"C:\baselines\notes-b.json";
            newer.InstalledUtc = InstalledAt.AddDays(1);
            WriteRegistry(older, newer);

            var plan = Run();

            Assert.IsNull(Store().Find("ws:notes"));
            Assert.AreEqual(0, plan.Steps.Count);
            Assert.AreEqual(2, plan.GameDataInstalls.Count);
            var a = plan.GameDataInstalls.Single(install => install.PlayniteGameId == gameA);
            Assert.AreEqual("ws:notes", a.LibraryItemId);
            Assert.AreEqual("Notes", a.Name);
            Assert.AreEqual("1.0.0", a.Version);
            Assert.AreEqual(@"C:\baselines\notes-a.json", a.BaselineFile);
        }

        [TestMethod]
        public void MoveGameDataItemsToLinks_CopiesTheNameAndKeepsVersionAndBaseline()
        {
            var linked = Guid.NewGuid();
            var named = Guid.NewGuid();
            var index = Path.Combine(_root, LibraryStore.DirectoryName, LibraryStore.IndexFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(index));
            File.WriteAllText(index, JsonConvert.SerializeObject(new
            {
                SchemaVersion = 1,
                Items = new object[]
                {
                    new { Id = "ws:notes", Kind = "GameData", Name = "Notes", Origin = "Workshop", WorkshopItemId = "notes", Version = "2.0.0" }
                }
            }));
            var store = Store();
            var links = new GameLinkStore(store.LibraryDirectory);
            links.Set(LibraryTargetKeys.GameData(linked), new LibraryLink { LibraryItemId = "ws:notes", AppliedVersion = "1.0.0", BaselineFile = "a.json" });
            links.Set(LibraryTargetKeys.GameData(named), new LibraryLink { LibraryItemId = "ws:notes", Name = "Kept", AppliedVersion = "1.5.0" });

            Assert.AreEqual(0, store.Items.Count);
            var moved = LibraryMigration.MoveGameDataItemsToLinks(store.TakeLegacyGameDataItems(), links);

            Assert.AreEqual(1, moved);
            var link = links.Get(LibraryTargetKeys.GameData(linked));
            Assert.AreEqual("Notes", link.Name);
            Assert.AreEqual("1.0.0", link.AppliedVersion);
            Assert.AreEqual("a.json", link.BaselineFile);
            Assert.AreEqual("Kept", links.Get(LibraryTargetKeys.GameData(named)).Name);
            Assert.AreEqual(0, store.TakeLegacyGameDataItems().Count);
            StringAssert.DoesNotMatch(File.ReadAllText(index), new System.Text.RegularExpressions.Regex("GameData"));
        }

        [TestMethod]
        public void SecondRun_ChangesNothing()
        {
            var path = WritePreset(LibraryItemKind.Colors, "Neon", "neon colors");
            WritePreset(LibraryItemKind.Colors, "Mine", "my colors");
            WriteRegistry(
                Record("neon", WorkshopItemKind.Colors, "Neon", "1.2.0", "colors=" + LibraryStore.HashFile(path)),
                Record("stats", WorkshopItemKind.ShowcasePage, "Stats page", "1.1.0", null));
            Run();
            var first = Store().Items.OrderBy(item => item.Id).Select(item => item.Id + "|" + item.Version).ToList();

            var plan = Run();

            Assert.AreEqual(0, plan.Steps.Count);
            Assert.IsFalse(plan.CreatedIndex);
            CollectionAssert.AreEqual(first, Store().Items.OrderBy(item => item.Id).Select(item => item.Id + "|" + item.Version).ToList());
        }

        [TestMethod]
        public void LaterRun_PicksUpInstallsMadeSince()
        {
            WritePreset(LibraryItemKind.Colors, "Mine", "my colors");
            Run();

            var path = WritePreset(LibraryItemKind.Colors, "Neon", "neon colors");
            WriteRegistry(Record("neon", WorkshopItemKind.Colors, "Neon", "1.0.0", "colors=" + LibraryStore.HashFile(path)));
            var plan = Run();

            Assert.AreEqual(1, plan.Steps.Count);
            Assert.AreEqual(LibraryItemOrigin.Workshop, Store().Find("ws:neon").Origin);
        }

        [TestMethod]
        public void UpdateOverAnUnchangedPreset_RaisesTheVersion()
        {
            var path = WritePreset(LibraryItemKind.Colors, "Neon", "neon colors 1");
            WriteRegistry(Record("neon", WorkshopItemKind.Colors, "Neon", "1.0.0", "colors=" + LibraryStore.HashFile(path)));
            Run();

            File.WriteAllText(path, "neon colors 2, longer");
            WriteRegistry(Record("neon", WorkshopItemKind.Colors, "Neon", "1.1.0", "colors=" + LibraryStore.HashFile(path)));
            Run();

            var item = Store().Find("ws:neon");
            Assert.AreEqual("1.1.0", item.Version);
            Assert.AreEqual(LibraryStore.HashFile(path), item.ContentHash);
        }

        [TestMethod]
        public void UpdateBesideAnEditedPreset_MovesTheItemAndKeepsTheEditLocal()
        {
            var original = WritePreset(LibraryItemKind.Colors, "Neon", "neon colors 1");
            WriteRegistry(Record("neon", WorkshopItemKind.Colors, "Neon", "1.0.0", "colors=" + LibraryStore.HashFile(original)));
            Run();

            // The user edits the preset; the next update lands beside it under a fresh name.
            File.WriteAllText(original, "neon colors edited by the user");
            var updated = WritePreset(LibraryItemKind.Colors, "Neon (2)", "neon colors 2");
            WriteRegistry(Record("neon", WorkshopItemKind.Colors, "Neon", "1.1.0", "colors=" + LibraryStore.HashFile(updated)));
            Run();

            var items = Store().Items;
            Assert.AreEqual(2, items.Count);
            var workshop = items.Single(item => item.Id == "ws:neon");
            Assert.AreEqual(Path.Combine("color_presets", "Neon (2).pacolors"), workshop.RelativePath);
            Assert.AreEqual("1.1.0", workshop.Version);
            var local = items.Single(item => item.Id != "ws:neon");
            Assert.AreEqual(LibraryItemOrigin.Local, local.Origin);
            Assert.AreEqual(Path.Combine("color_presets", "Neon.pacolors"), local.RelativePath);
        }

        [TestMethod]
        public void IdenticalCopies_PreferTheOneWithTheItemName()
        {
            var plan = LibraryMigration.Plan(
                new[] { Record("neon", WorkshopItemKind.Colors, "Neon", "1.0.0", "colors=abc") },
                new[]
                {
                    LocalItem("first", LibraryItemKind.Colors, "Copy of neon", "abc", InstalledAt.AddDays(-2)),
                    LocalItem("second", LibraryItemKind.Colors, "Neon", "abc", InstalledAt.AddDays(-1))
                });

            Assert.AreEqual("second", plan.Steps.Single().ReplacesId);
        }

        [TestMethod]
        public void LegacyRecordWithoutHashes_ChangesNothing()
        {
            var plan = LibraryMigration.Plan(
                new[] { Record("neon", WorkshopItemKind.Colors, "Neon", "1.0.0", null) },
                new[] { LocalItem("first", LibraryItemKind.Colors, "Neon", "abc", InstalledAt) });

            Assert.AreEqual(0, plan.Steps.Count);
        }

        [TestMethod]
        public void LinkGameDataInstalls_LinksEachGameWithItsBaseline_AndKeepsExistingLinks()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var onFirst = Record("icons", WorkshopItemKind.GameCustomData, "Icons", "1.0.0", null);
            onFirst.PlayniteGameId = first;
            onFirst.BaselineFile = @"C:\baselines\first.json";
            var onSecond = Record("icons", WorkshopItemKind.GameCustomData, "Icons", "1.1.0", null);
            onSecond.PlayniteGameId = second;
            WriteRegistry(onFirst, onSecond);
            var store = Store();
            var links = new GameLinkStore(store.LibraryDirectory);
            links.Set(LibraryTargetKeys.GameData(second), new LibraryLink { LibraryItemId = "other" });

            var plan = LibraryMigration.Run(store, new WorkshopIdentityStore(_root));
            var added = LibraryMigration.LinkGameDataInstalls(plan, links);

            Assert.AreEqual(1, added);
            var link = links.Get(LibraryTargetKeys.GameData(first));
            Assert.AreEqual("ws:icons", link.LibraryItemId);
            Assert.AreEqual("Icons", link.Name);
            Assert.IsNull(link.PackageFile);
            Assert.AreEqual("1.0.0", link.AppliedVersion);
            Assert.AreEqual(@"C:\baselines\first.json", link.BaselineFile);
            Assert.AreEqual("other", links.Get(LibraryTargetKeys.GameData(second)).LibraryItemId);
        }

        [TestMethod]
        public void ParsePartHashes_ReadsTheInstallerFormat()
        {
            var parts = LibraryMigration.ParsePartHashes("colors=AA;Toast=bb;broken;=cc");

            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual("colors", parts[0].Key);
            Assert.AreEqual("AA", parts[0].Value);
            Assert.AreEqual("toast", parts[1].Key);
        }

        private LibraryMigrationPlan Run()
        {
            return LibraryMigration.Run(Store(), new WorkshopIdentityStore(_root));
        }

        private LibraryStore Store() => new LibraryStore(_root);

        private void WriteRegistry(params WorkshopInstalledItem[] records)
        {
            var directory = Path.Combine(_root, WorkshopIdentityStore.DirectoryName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "installed.json"), JsonConvert.SerializeObject(records.ToList()));
        }

        private static WorkshopInstalledItem Record(string id, WorkshopItemKind kind, string name, string version, string contentHash)
        {
            return new WorkshopInstalledItem
            {
                Id = id,
                Kind = kind,
                Name = name,
                Version = version,
                ContentHash = contentHash,
                InstalledUtc = InstalledAt
            };
        }

        private static LibraryItem LocalItem(string id, LibraryItemKind kind, string name, string hash, DateTime added)
        {
            return new LibraryItem
            {
                Id = id,
                Kind = kind,
                Name = name,
                RelativePath = Path.Combine(LibraryStore.FolderOf(kind), name + LibraryStore.ExtensionOf(kind)),
                Origin = LibraryItemOrigin.Local,
                ContentHash = hash,
                Version = hash,
                AddedUtc = added
            };
        }

        private string WritePreset(LibraryItemKind kind, string name, string content)
        {
            var directory = Path.Combine(_root, LibraryStore.FolderOf(kind));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + LibraryStore.ExtensionOf(kind));
            File.WriteAllText(path, content);
            return path;
        }
    }
}

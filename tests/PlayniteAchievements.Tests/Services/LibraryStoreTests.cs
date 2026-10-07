using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Library;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class LibraryStoreTests
    {
        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchLibraryStore_" + Guid.NewGuid().ToString("N"));
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
        public void FirstLoad_IndexesEveryPresetFolderAsLocalItems()
        {
            var colors = WritePreset(LibraryItemKind.Colors, "Sunset", "colors-1");
            WritePreset(LibraryItemKind.Sounds, "Chimes", "sounds-1");
            WritePreset(LibraryItemKind.Toast, "Glass", "toast-1");
            WritePreset(LibraryItemKind.Frame, "Gold", "frame-1");
            WritePreset(LibraryItemKind.ShowcasePage, "Stats", "page-1");
            File.WriteAllText(Path.Combine(_root, LibraryStore.FolderOf(LibraryItemKind.Colors), "readme.txt"), "not a preset");

            // Game data is recorded on the games: a file in the old game data folder is no item.
            Directory.CreateDirectory(Path.Combine(_root, "gamedata_presets"));
            File.WriteAllText(Path.Combine(_root, "gamedata_presets", "Notes.pa"), "data-1");

            var store = new LibraryStore(_root);
            Assert.IsFalse(store.IndexExists);

            var items = store.Items;

            Assert.AreEqual(5, items.Count);
            Assert.IsTrue(store.IndexExists);
            Assert.IsTrue(items.All(item => item.Origin == LibraryItemOrigin.Local));
            var sunset = items.Single(item => item.Kind == LibraryItemKind.Colors);
            Assert.AreEqual("Sunset", sunset.Name);
            Assert.AreEqual(LibraryStore.HashFile(colors), sunset.ContentHash);
            Assert.AreEqual(sunset.ContentHash, sunset.Version, "a local item's version is its hash");
            Assert.AreEqual(Path.Combine("library", "colors", "Sunset.pacolors"), sunset.RelativePath);
            Assert.IsTrue(Guid.TryParseExact(sunset.Id, "N", out _));
        }

        [TestMethod]
        public void Reload_KeepsIds()
        {
            WritePreset(LibraryItemKind.Colors, "Sunset", "colors-1");
            var first = new LibraryStore(_root).Items.Single();

            var second = new LibraryStore(_root).Items.Single();

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(first.AddedUtc, second.AddedUtc);
        }

        [TestMethod]
        public void MissingFile_DropsTheEntryAndReportsIt()
        {
            var path = WritePreset(LibraryItemKind.Sounds, "Chimes", "sounds-1");
            var store = new LibraryStore(_root);
            var id = store.Items.Single().Id;
            Assert.AreEqual(0, store.TakeDroppedIds().Count);

            File.Delete(path);
            var result = store.Reconcile();

            CollectionAssert.AreEqual(new[] { id }, result.DroppedIds);
            Assert.AreEqual(0, store.Items.Count);
            CollectionAssert.AreEqual(new[] { id }, store.TakeDroppedIds().ToList());
            Assert.AreEqual(0, store.TakeDroppedIds().Count, "taking empties the queue");
        }

        [TestMethod]
        public void MissingFile_AtLoad_IsQueuedForUnlinking()
        {
            var path = WritePreset(LibraryItemKind.Sounds, "Chimes", "sounds-1");
            var id = new LibraryStore(_root).Items.Single().Id;
            File.Delete(path);

            var store = new LibraryStore(_root);

            CollectionAssert.AreEqual(new[] { id }, store.TakeDroppedIds().ToList());
        }

        [TestMethod]
        public void ResavedLocalPreset_IsANewVersion()
        {
            var path = WritePreset(LibraryItemKind.Colors, "Sunset", "colors-1");
            var store = new LibraryStore(_root);
            var before = store.Items.Single();

            File.WriteAllText(path, "colors-2 with more content");
            var result = store.Reconcile();

            var after = store.Items.Single();
            CollectionAssert.AreEqual(new[] { before.Id }, result.NewVersionIds);
            Assert.AreEqual(before.Id, after.Id);
            Assert.AreNotEqual(before.Version, after.Version);
            Assert.AreEqual(LibraryStore.HashFile(path), after.Version);
        }

        [TestMethod]
        public void RenamedFile_KeepsItsId()
        {
            var path = WritePreset(LibraryItemKind.Toast, "Glass", "toast-1");
            var store = new LibraryStore(_root);
            var id = store.Items.Single().Id;

            File.Move(path, Path.Combine(Path.GetDirectoryName(path), "Frosted" + LibraryStore.ExtensionOf(LibraryItemKind.Toast)));
            var result = store.Reconcile();

            var item = store.Items.Single();
            Assert.AreEqual(id, item.Id);
            Assert.AreEqual("Frosted", item.Name);
            CollectionAssert.AreEqual(new[] { id }, result.MovedIds);
            Assert.AreEqual(0, result.DroppedIds.Count);
        }

        [TestMethod]
        public void WorkshopItemWithoutPackage_SurvivesReconcile()
        {
            var store = new LibraryStore(_root);
            store.Upsert(new LibraryItem
            {
                Id = LibraryItem.WorkshopId("page-1"),
                Kind = LibraryItemKind.ShowcasePage,
                Name = "Stats page",
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = "page-1",
                Version = "1.0.0"
            });

            var result = new LibraryStore(_root).Reconcile();

            Assert.AreEqual(0, result.DroppedIds.Count);
            var item = new LibraryStore(_root).Find("ws:page-1");
            Assert.IsNotNull(item);
            Assert.IsNull(item.RelativePath);
            Assert.AreEqual("1.0.0", item.Version);
        }

        [TestMethod]
        public void EditedWorkshopPreset_IsReportedAndKeepsItsVersion()
        {
            var path = WritePreset(LibraryItemKind.Colors, "Neon", "colors-1");
            var store = new LibraryStore(_root);
            var local = store.Items.Single();
            store.Replace(local.Id, new LibraryItem
            {
                Id = LibraryItem.WorkshopId("neon"),
                Kind = LibraryItemKind.Colors,
                Name = "Neon",
                RelativePath = local.RelativePath,
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = "neon",
                Version = "1.2.0"
            });

            File.WriteAllText(path, "colors-1 edited by hand");
            var result = store.Reconcile();

            var item = store.Items.Single();
            CollectionAssert.AreEqual(new[] { "ws:neon" }, result.ModifiedWorkshopIds);
            Assert.AreEqual("1.2.0", item.Version);
            Assert.AreEqual(LibraryStore.HashFile(path), item.ContentHash);
        }

        [TestMethod]
        public void Replace_SwapsTheIdAndKeepsTheAddedTime()
        {
            WritePreset(LibraryItemKind.Colors, "Neon", "colors-1");
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var store = new LibraryStore(_root, utcNow: () => now);
            var local = store.Items.Single();

            now = now.AddDays(3);
            store.Replace(local.Id, new LibraryItem
            {
                Id = LibraryItem.WorkshopId("neon"),
                Kind = LibraryItemKind.Colors,
                Name = "Neon",
                RelativePath = local.RelativePath,
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = "neon",
                Version = "1.0.0"
            });

            var item = store.Items.Single();
            Assert.AreEqual("ws:neon", item.Id);
            Assert.AreEqual(local.AddedUtc, item.AddedUtc);
            Assert.AreEqual(now, item.UpdatedUtc);
            Assert.AreEqual(local.ContentHash, item.ContentHash);
            Assert.IsNull(store.Find(local.Id));
        }

        [TestMethod]
        public void Upsert_OnTheSamePath_ReplacesTheEntry()
        {
            var path = WritePreset(LibraryItemKind.Sounds, "Chimes", "sounds-1");
            var store = new LibraryStore(_root);
            var before = store.Items.Single();

            store.Upsert(new LibraryItem
            {
                Id = LibraryItem.NewLocalId(),
                Kind = LibraryItemKind.Sounds,
                Name = "Chimes",
                RelativePath = path,
                Origin = LibraryItemOrigin.Local
            });

            var after = store.Items.Single();
            Assert.AreNotEqual(before.Id, after.Id);
            Assert.AreEqual(before.ContentHash, after.Version);
            Assert.AreEqual(before.RelativePath, after.RelativePath, "an absolute path is stored relative to the user data folder");
        }

        [TestMethod]
        public void UnreadableIndex_IsRebuiltAndKeptAside()
        {
            WritePreset(LibraryItemKind.Colors, "Sunset", "colors-1");
            Directory.CreateDirectory(Path.Combine(_root, LibraryStore.DirectoryName));
            File.WriteAllText(Path.Combine(_root, LibraryStore.DirectoryName, LibraryStore.IndexFileName), "{ not json");

            var store = new LibraryStore(_root);

            Assert.AreEqual(1, store.Items.Count);
            Assert.IsTrue(File.Exists(store.IndexPath + ".unreadable"));
        }

        [TestMethod]
        public void ConcurrentUpserts_AllLand()
        {
            var store = new LibraryStore(_root);

            Parallel.For(0, 40, i => store.Upsert(new LibraryItem
            {
                Id = LibraryItem.WorkshopId("item-" + i),
                Kind = LibraryItemKind.ShowcasePage,
                Name = "Item " + i,
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = "item-" + i,
                Version = "1.0.0"
            }));

            Assert.AreEqual(40, store.Items.Count);
            Assert.AreEqual(40, new LibraryStore(_root).Items.Count);
        }

        [TestMethod]
        public void WorkshopId_FormatsPartsInLowerCase()
        {
            Assert.AreEqual("ws:abc", LibraryItem.WorkshopId("abc"));
            Assert.AreEqual("ws:abc#toast", LibraryItem.WorkshopId("abc", "Toast"));
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

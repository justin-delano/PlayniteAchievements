using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class GameLinkStoreTests
    {
        private string _directory;

        [TestInitialize]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "PlayAchGameLinks_" + Guid.NewGuid().ToString("N"), LibraryStore.DirectoryName);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(_directory), recursive: true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void TargetKeys_HaveThePlannedShapes()
        {
            var game = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

            Assert.AreEqual("colors", LibraryTargetKeys.Colors);
            Assert.AreEqual("sounds", LibraryTargetKeys.Sounds);
            Assert.AreEqual("toast:global", LibraryTargetKeys.ToastGlobal);
            Assert.AreEqual("frame:global", LibraryTargetKeys.FrameGlobal);
            Assert.AreEqual("toast:provider:Steam", LibraryTargetKeys.ToastProvider("Steam"));
            Assert.AreEqual("frame:provider:Steam", LibraryTargetKeys.FrameProvider("Steam"));
            Assert.AreEqual("showcase:page-1", LibraryTargetKeys.Showcase("page-1"));
            Assert.AreEqual("toast:game:0f8fad5b-d9cb-469f-a165-70867728950e", LibraryTargetKeys.ToastGame(game));
            Assert.AreEqual("frame:game:0f8fad5b-d9cb-469f-a165-70867728950e", LibraryTargetKeys.FrameGame(game));
            Assert.AreEqual("gamedata:0f8fad5b-d9cb-469f-a165-70867728950e", LibraryTargetKeys.GameData(game));
        }

        [TestMethod]
        public void TargetKeys_TellPerGameFromSettingsBacked()
        {
            var game = Guid.NewGuid();

            Assert.IsTrue(LibraryTargetKeys.IsPerGame(LibraryTargetKeys.ToastGame(game)));
            Assert.IsTrue(LibraryTargetKeys.IsPerGame(LibraryTargetKeys.FrameGame(game)));
            Assert.IsTrue(LibraryTargetKeys.TryGetGameId(LibraryTargetKeys.GameData(game).ToUpperInvariant(), out var parsed));
            Assert.AreEqual(game, parsed);
            Assert.IsFalse(LibraryTargetKeys.IsPerGame(LibraryTargetKeys.ToastGlobal));
            Assert.IsFalse(LibraryTargetKeys.IsPerGame(LibraryTargetKeys.FrameProvider("Steam")));
            Assert.IsFalse(LibraryTargetKeys.IsPerGame(LibraryTargetKeys.Showcase("page-1")));
            Assert.IsFalse(LibraryTargetKeys.IsPerGame("gamedata:not-a-guid"));
            Assert.ThrowsException<ArgumentException>(() => LibraryTargetKeys.GameData(Guid.Empty));
        }

        [TestMethod]
        public void SetAndReload_KeepsLinks()
        {
            var key = LibraryTargetKeys.GameData(Guid.NewGuid());
            new GameLinkStore(_directory).Set(key, Link("ws:notes"));

            var loaded = new GameLinkStore(_directory).Get(key.ToUpperInvariant());

            Assert.AreEqual("ws:notes", loaded.LibraryItemId);
            Assert.AreEqual("b.json", loaded.BaselineFile);
            Assert.IsTrue(File.Exists(Path.Combine(_directory, GameLinkStore.FileName)));
        }

        [TestMethod]
        public void Set_RejectsSettingsBackedTargets()
        {
            var store = new GameLinkStore(_directory);

            Assert.ThrowsException<ArgumentException>(() => store.Set(LibraryTargetKeys.Colors, Link("a")));
        }

        [TestMethod]
        public void Get_ReturnsCopies()
        {
            var key = LibraryTargetKeys.ToastGame(Guid.NewGuid());
            var store = new GameLinkStore(_directory);
            store.Set(key, Link("a"));

            store.Get(key).LibraryItemId = "changed";

            Assert.AreEqual("a", store.Get(key).LibraryItemId);
        }

        [TestMethod]
        public void UnlinkItems_RemovesTheLinksOfDroppedItems()
        {
            var store = new GameLinkStore(_directory);
            var first = LibraryTargetKeys.FrameGame(Guid.NewGuid());
            var second = LibraryTargetKeys.FrameGame(Guid.NewGuid());
            var other = LibraryTargetKeys.GameData(Guid.NewGuid());
            store.Set(first, Link("gone"));
            store.Set(second, Link("GONE"));
            store.Set(other, Link("kept"));

            var removed = store.UnlinkItems(new[] { "gone" });

            CollectionAssert.AreEquivalent(new[] { first, second }, removed.ToList());
            Assert.AreEqual(1, new GameLinkStore(_directory).All.Count);
            CollectionAssert.AreEqual(new[] { other }, store.TargetsOf("kept").ToList());
        }

        [TestMethod]
        public void RenameItem_RepointsLinks()
        {
            var store = new GameLinkStore(_directory);
            var key = LibraryTargetKeys.ToastGame(Guid.NewGuid());
            store.Set(key, Link("3f2a"));

            Assert.AreEqual(1, store.RenameItem("3f2a", "ws:glass"));

            Assert.AreEqual("ws:glass", new GameLinkStore(_directory).Get(key).LibraryItemId);
        }

        [TestMethod]
        public void UnreadableFile_StartsEmptyAndIsKeptAside()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, GameLinkStore.FileName), "{ broken");
            var store = new GameLinkStore(_directory);

            Assert.AreEqual(0, store.All.Count);
            Assert.IsTrue(File.Exists(Path.Combine(_directory, GameLinkStore.FileName + ".unreadable")));
        }

        [TestMethod]
        public void ConcurrentSets_AllLand()
        {
            var store = new GameLinkStore(_directory);

            Parallel.For(0, 30, i => store.Set(LibraryTargetKeys.GameData(Guid.NewGuid()), Link("item-" + i)));

            Assert.AreEqual(30, new GameLinkStore(_directory).All.Count);
        }

        private static LibraryLink Link(string itemId)
        {
            return new LibraryLink
            {
                LibraryItemId = itemId,
                AppliedVersion = "1.0.0",
                BaselineFile = "b.json",
                BaselineHash = "h",
                AppliedUtc = DateTime.UtcNow
            };
        }
    }
}

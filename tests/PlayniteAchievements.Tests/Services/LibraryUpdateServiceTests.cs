using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    [DoNotParallelize]
    public class LibraryUpdateServiceTests
    {
        private string _root;
        private LibraryStore _library;
        private LibraryApplyService _apply;
        private GameLinkStore _gameLinks;
        private ColorsLibraryAdapter _adapter;
        private PersistedSettings _live;
        private PersistedSettings _snapshot;
        private LibraryUpdateService _service;
        private DirectoryPackageFolder _folder;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchLibraryUpdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _library = new LibraryStore(_root);
            _apply = new LibraryApplyService(_library, new LibraryBaselineStore(_library.LibraryDirectory));
            _gameLinks = new GameLinkStore(_library.LibraryDirectory);
            _adapter = new ColorsLibraryAdapter();
            _live = new PersistedSettings();
            _snapshot = new PersistedSettings();
            _service = new LibraryUpdateService(
                _apply,
                _gameLinks,
                new ISettingsLibraryAdapter[] { _adapter },
                () => _live,
                update =>
                {
                    update(_live);
                    update(_snapshot);
                },
                update => update(_live));
            _folder = new DirectoryPackageFolder(
                Path.Combine(_root, LibraryStore.FolderOf(LibraryItemKind.Colors)),
                ColorPackPortableStore.PackageFileExtension);
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
        public void WriteWorkshopPart_FirstInstall_LeavesAPresetOfTheSameNameAlone()
        {
            var own = _folder.Save("Neon", Package("own", "#010101"));

            var write = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder);

            Assert.AreEqual("Neon (2)", write.WrittenName);
            Assert.IsNull(write.KeptLocalCopyName);
            Assert.IsTrue(File.Exists(own));
            Assert.AreEqual("#010101", new ColorPackPortableStore().Read(own).RarityColors.Common);
            Assert.AreEqual(LibraryItemOrigin.Workshop, _library.Find("ws:neon").Origin);
        }

        [TestMethod]
        public void WriteWorkshopPart_AnUntouchedFile_IsReplacedInPlace()
        {
            var first = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder);

            var second = _service.WriteWorkshopPart(Workshop("1.1.0"), Package("v2", "#222222"), _folder);

            Assert.IsNull(second.KeptLocalCopyName);
            Assert.AreEqual(first.Item.RelativePath, second.Item.RelativePath);
            Assert.AreEqual("1.1.0", second.Item.Version);
            Assert.AreEqual(second.Item.ContentHash, second.Item.PublishedHash);
            Assert.AreEqual(1, _library.Items.Count);
        }

        [TestMethod]
        public void WriteWorkshopPart_AFileEditedInPlace_StaysAsALocalItem()
        {
            var first = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder);
            var path = _library.FullPath(first.Item);
            new ColorPackPortableStore().Export(Colors("#ABCDEF"), path);

            var second = _service.WriteWorkshopPart(Workshop("1.1.0"), Package("v2", "#222222"), _folder);

            Assert.AreEqual("Neon", second.KeptLocalCopyName);
            var kept = _library.FindByPath(path);
            Assert.IsNotNull(kept);
            Assert.AreEqual(LibraryItemOrigin.Local, kept.Origin);
            Assert.AreEqual("#ABCDEF", new ColorPackPortableStore().Read(path).RarityColors.Common);
            var workshop = _library.Find("ws:neon");
            Assert.AreNotEqual(path, _library.FullPath(workshop));
            Assert.AreEqual("#222222", new ColorPackPortableStore().Read(_library.FullPath(workshop)).RarityColors.Common);
        }

        [TestMethod]
        public void MergeIntoTargets_UpdatesTheLiveSettingsAndTheSnapshot_KeepingEdits()
        {
            var item = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder).Item;
            _apply.ApplyToSettings(_adapter, item, _live);
            _apply.ApplyToSettings(_adapter, item, _snapshot);
            var edited = _live.RarityColors.Clone();
            edited.Common = "#ABCDEF";
            _live.RarityColors = edited;

            _service.WriteWorkshopPart(Workshop("1.1.0"), Package("v2", "#222222"), _folder);
            var report = _service.MergeIntoTargets(item.Id, LibraryApplyMode.Merge);

            CollectionAssert.AreEqual(new[] { LibraryTargetKeys.Colors }, report.UpdatedTargets);
            Assert.AreEqual(1, report.KeptEdits);
            Assert.AreEqual("#ABCDEF", _live.RarityColors.Common, "the live edit stays");
            Assert.AreEqual("#222222", _snapshot.RarityColors.Common, "the snapshot had no edit, so it follows the package");
            Assert.AreEqual("1.1.0", _live.GetLibraryLink(LibraryTargetKeys.Colors).AppliedVersion);
            Assert.AreEqual("1.1.0", _snapshot.GetLibraryLink(LibraryTargetKeys.Colors).AppliedVersion);
        }

        [TestMethod]
        public void MergeIntoTargets_Replace_DropsTheEdits()
        {
            var item = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder).Item;
            _apply.ApplyToSettings(_adapter, item, _live);
            var edited = _live.RarityColors.Clone();
            edited.Common = "#ABCDEF";
            _live.RarityColors = edited;

            _service.MergeIntoTargets(item.Id, LibraryApplyMode.Replace);

            Assert.AreEqual("#111111", _live.RarityColors.Common);
        }

        [TestMethod]
        public void MergeIntoTargets_AKindWithoutAnAdapter_ReportsItsFollowersAsPending()
        {
            _library.Upsert(new LibraryItem { Id = "ws:glass", Kind = LibraryItemKind.Toast, Name = "Glass", Origin = LibraryItemOrigin.Workshop, WorkshopItemId = "glass", Version = "2.0.0" });
            _live.SetLibraryLink(LibraryTargetKeys.ToastGlobal, new LibraryLink { LibraryItemId = "ws:glass", AppliedVersion = "1.0.0" });
            var game = Guid.NewGuid();
            _gameLinks.Set(LibraryTargetKeys.ToastGame(game), new LibraryLink { LibraryItemId = "ws:glass", AppliedVersion = "1.0.0" });

            var report = _service.MergeIntoTargets("ws:glass", LibraryApplyMode.Merge);

            Assert.AreEqual(0, report.UpdatedTargets.Count);
            CollectionAssert.AreEquivalent(
                new[] { LibraryTargetKeys.ToastGlobal, LibraryTargetKeys.ToastGame(game) },
                report.PendingTargets.ToList());
            var uses = _service.UsesOf(_library.Find("ws:glass"));
            Assert.AreEqual(2, uses.Count);
            Assert.IsTrue(uses.All(use => use.IsUpdateAvailable && !use.CanReset));
        }

        [TestMethod]
        public void UnlinkDropped_EndsTheLinksToItemsWhoseFilesAreGone()
        {
            var item = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder).Item;
            _apply.ApplyToSettings(_adapter, item, _live);
            _apply.ApplyToSettings(_adapter, item, _snapshot);
            var game = Guid.NewGuid();
            _gameLinks.Set(LibraryTargetKeys.FrameGame(game), new LibraryLink { LibraryItemId = item.Id });

            // A game's Workshop data record names a Workshop item, not a library item: it stays.
            _gameLinks.Set(LibraryTargetKeys.GameData(game), new LibraryLink { LibraryItemId = item.Id });

            File.Delete(_library.FullPath(item));
            _library.Reconcile();

            Assert.AreEqual(1, _service.UnlinkDropped());
            Assert.IsNull(_live.GetLibraryLink(LibraryTargetKeys.Colors));
            Assert.IsNull(_snapshot.GetLibraryLink(LibraryTargetKeys.Colors));
            Assert.IsNull(_gameLinks.Get(LibraryTargetKeys.FrameGame(game)));
            Assert.IsNotNull(_gameLinks.Get(LibraryTargetKeys.GameData(game)));
            Assert.AreEqual(0, _service.UnlinkDropped(), "dropped ids are taken once");
        }

        [TestMethod]
        public void UsesOf_LeavesGameDataRecordsOut()
        {
            var item = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder).Item;
            _gameLinks.Set(LibraryTargetKeys.GameData(Guid.NewGuid()), new LibraryLink { LibraryItemId = item.Id, AppliedVersion = "0.9.0" });

            Assert.AreEqual(0, _service.UsesOf(item).Count);
            CollectionAssert.AreEqual(new string[0], _service.TargetsOf(item.Id).ToList());
        }

        [TestMethod]
        public void StopFollowing_AndReset_ActOnTheLiveTargetOnly()
        {
            var item = _service.WriteWorkshopPart(Workshop("1.0.0"), Package("v1", "#111111"), _folder).Item;
            _apply.ApplyToSettings(_adapter, item, _live);
            _apply.ApplyToSettings(_adapter, item, _snapshot);
            var edited = _live.RarityColors.Clone();
            edited.Common = "#ABCDEF";
            _live.RarityColors = edited;

            Assert.IsTrue(_service.UsesOf(item).Single().IsEdited);
            Assert.IsTrue(_service.Reset(LibraryTargetKeys.Colors));
            Assert.AreEqual("#111111", _live.RarityColors.Common);

            _service.StopFollowing(LibraryTargetKeys.Colors);
            Assert.IsNull(_live.GetLibraryLink(LibraryTargetKeys.Colors));
            Assert.IsNotNull(_snapshot.GetLibraryLink(LibraryTargetKeys.Colors), "a Cancel brings the link back");
        }

        private static LibraryItem Workshop(string version)
        {
            return new LibraryItem
            {
                Id = "ws:neon",
                Kind = LibraryItemKind.Colors,
                Name = "Neon",
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = "neon",
                Part = "colors",
                Version = version
            };
        }

        private string Package(string name, string common)
        {
            var path = Path.Combine(_root, "downloads", name + ColorPackPortableStore.PackageFileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            new ColorPackPortableStore().Export(Colors(common), path);
            return path;
        }

        private static PersistedSettings Colors(string common)
        {
            var settings = new PersistedSettings();
            var rarity = RarityColorSettings.CreateDefault();
            rarity.Common = common;
            settings.RarityColors = rarity;
            settings.ProviderColorOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            settings.ResourceOverrides = PersistedSettings.CreateDefaultResourceOverrides();
            return settings;
        }
    }
}

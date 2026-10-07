using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class SoundsLibraryAdapterTests
    {
        private string _root;
        private LibraryStore _library;
        private LibraryApplyService _service;
        private UnlockSoundPortableStore _store;
        private SoundsLibraryAdapter _adapter;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchSoundsLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _library = new LibraryStore(_root);
            _service = new LibraryApplyService(_library, new LibraryBaselineStore(_library.LibraryDirectory));
            _store = new UnlockSoundPortableStore(_root);
            _adapter = new SoundsLibraryAdapter(_store);
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
        public void Apply_SetsTheCarriedTiers_AndOwnsOnlyThem()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1), (UnlockSoundTier.Rare, 2));
            var mine = WriteWav("mine.wav", 9);
            var settings = new PersistedSettings();
            settings.UnlockSounds.Uncommon = mine;

            _service.ApplyToSettings(_adapter, item, settings);

            Assert.IsTrue(_store.IsManagedPath(settings.UnlockSounds.Common));
            Assert.IsTrue(_store.IsManagedPath(settings.UnlockSounds.Rare));
            Assert.AreEqual(mine, settings.UnlockSounds.Uncommon, "a tier the pack does not carry keeps its sound");

            var baseline = _service.Baselines.Read(settings.GetLibraryLink(LibraryTargetKeys.Sounds).BaselineFile);
            CollectionAssert.AreEquivalent(
                new[] { "Common", "Rare" },
                baseline.Children<Newtonsoft.Json.Linq.JProperty>().Select(property => property.Name).ToList());

            var state = _service.GetSettingsState(_adapter, settings);
            Assert.IsTrue(state.IsFollowing);
            Assert.IsFalse(state.IsEdited);

            settings.UnlockSounds.Uncommon = WriteWav("other.wav", 8);
            Assert.IsFalse(_service.GetSettingsState(_adapter, settings).IsEdited, "a tier the item does not own is not an edit");
        }

        [TestMethod]
        public void ChangingACarriedTier_MarksTheTargetEdited()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1), (UnlockSoundTier.Rare, 2));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);

            settings.UnlockSounds.Rare = WriteWav("mine.wav", 7);

            Assert.IsTrue(_service.GetSettingsState(_adapter, settings).IsEdited);
        }

        [TestMethod]
        public void SameFileAtANewPath_IsNotAnEdit()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);

            var copy = Path.Combine(_root, "copy.wav");
            File.Copy(settings.UnlockSounds.Common, copy);
            settings.UnlockSounds.Common = copy;

            Assert.IsFalse(_service.GetSettingsState(_adapter, settings).IsEdited);
        }

        [TestMethod]
        public void Update_KeepsAnEditedTier_TakesTheOthers_AndPrunesOldFiles()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1), (UnlockSoundTier.Rare, 2));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            var firstFolder = Path.GetDirectoryName(settings.UnlockSounds.Common);
            var mine = WriteWav("mine.wav", 7);
            settings.UnlockSounds.Rare = mine;

            var updated = SavePack("Chimes", (UnlockSoundTier.Common, 3), (UnlockSoundTier.Rare, 4), (UnlockSoundTier.Capstone, 5));
            Assert.AreNotEqual(item.Version, updated.Version);
            Assert.IsTrue(_service.GetSettingsState(_adapter, settings).IsUpdateAvailable);

            Assert.IsTrue(_service.UpdateSettings(_adapter, settings, out var kept));

            Assert.AreEqual(1, kept);
            Assert.AreEqual(mine, settings.UnlockSounds.Rare, "the edited tier stays");
            CollectionAssert.AreEqual(Wav(3), File.ReadAllBytes(settings.UnlockSounds.Common), "an untouched tier follows the new version");
            CollectionAssert.AreEqual(Wav(5), File.ReadAllBytes(settings.UnlockSounds.Capstone), "a tier the new version adds is set");
            Assert.IsFalse(Directory.Exists(firstFolder), "the replaced pack folder is removed");

            var state = _service.GetSettingsState(_adapter, settings);
            Assert.IsFalse(state.IsUpdateAvailable);
            Assert.IsFalse(state.IsEdited);
        }

        [TestMethod]
        public void Update_DropsATierTheNewVersionNoLongerCarries()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1), (UnlockSoundTier.Rare, 2));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);

            SavePack("Chimes", (UnlockSoundTier.Common, 3));
            Assert.IsTrue(_service.UpdateSettings(_adapter, settings, out var kept));

            Assert.AreEqual(0, kept);
            Assert.IsNull(settings.UnlockSounds.Rare);
            CollectionAssert.AreEqual(Wav(3), File.ReadAllBytes(settings.UnlockSounds.Common));
        }

        [TestMethod]
        public void Reset_PutsThePackSoundsBack()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1), (UnlockSoundTier.Rare, 2));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            settings.UnlockSounds.Rare = WriteWav("mine.wav", 7);

            Assert.IsTrue(_service.ResetSettings(_adapter, settings));

            CollectionAssert.AreEqual(Wav(2), File.ReadAllBytes(settings.UnlockSounds.Rare));
            Assert.IsFalse(_service.GetSettingsState(_adapter, settings).IsEdited);
        }

        [TestMethod]
        public void StopFollowing_KeepsTheSounds()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            var common = settings.UnlockSounds.Common;

            LibraryApplyService.StopFollowing(_adapter, settings);

            Assert.IsNull(settings.GetLibraryLink(LibraryTargetKeys.Sounds));
            Assert.AreEqual(common, settings.UnlockSounds.Common);
        }

        [TestMethod]
        public void Prune_KeepsFilesAnotherSettingsCopyReferences()
        {
            var snapshot = new UnlockSoundSettings();
            var adapter = new SoundsLibraryAdapter(_store, () => new[] { snapshot });
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(adapter, item, settings);
            snapshot.Common = settings.UnlockSounds.Common;
            var snapshotFolder = Path.GetDirectoryName(snapshot.Common);

            _service.ResetSettings(adapter, settings);

            Assert.AreNotEqual(snapshotFolder, Path.GetDirectoryName(settings.UnlockSounds.Common));
            Assert.IsTrue(Directory.Exists(snapshotFolder), "the edit snapshot still points into it");
        }

        [TestMethod]
        public void PlatformTarget_GetsItsOwnPack_AndLeavesTheGlobalPackAlone()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Common, 1));
            var settings = new PersistedSettings();
            var mine = WriteWav("mine.wav", 9);
            settings.UnlockSounds.Uncommon = mine;
            var targets = new SoundsLibraryTargets(_adapter, () => null, () => settings);
            var platform = targets.SettingsAdapter(LibraryTargetKeys.SoundsProvider("Steam"));

            _service.ApplyToSettings(platform, item, settings);

            var own = settings.GetProviderUnlockSounds("Steam");
            Assert.IsNotNull(own, "applying a pack gives the platform its own copy");
            Assert.IsTrue(_store.IsManagedPath(own.Common));
            Assert.AreEqual(mine, own.Uncommon, "the copy starts from the pack the platform inherited");
            Assert.IsNull(settings.UnlockSounds.Common, "the global pack is untouched");
            Assert.IsNotNull(settings.GetLibraryLink(LibraryTargetKeys.SoundsProvider("Steam")));
            Assert.IsNull(settings.GetLibraryLink(LibraryTargetKeys.Sounds));
            Assert.IsFalse(_service.GetSettingsState(platform, settings).IsEdited);
        }

        [TestMethod]
        public void GameTarget_GetsItsOwnPack()
        {
            var item = SavePack("Chimes", (UnlockSoundTier.Rare, 3));
            var settings = new PersistedSettings();
            var customData = new GameCustomDataStore(Path.Combine(_root, "custom-data"));
            var gameId = Guid.NewGuid();
            var targets = new SoundsLibraryTargets(_adapter, () => customData, () => settings);

            var target = targets.GameTarget(LibraryTargetKeys.SoundsGame(gameId));
            target.Apply(_service, item);

            Assert.IsTrue(customData.TryLoad(gameId, out var data));
            Assert.IsTrue(_store.IsManagedPath(data.UnlockSounds.Rare));
            Assert.IsNull(settings.UnlockSounds.Rare);
        }

        [TestMethod]
        public void Prune_KeepsTheFilesOfEveryOtherPack()
        {
            var settings = new PersistedSettings();
            var adapter = new SoundsLibraryAdapter(_store, () => new[] { settings.UnlockSounds }.Concat(settings.ProviderUnlockSounds.Values));
            var targets = new SoundsLibraryTargets(adapter, () => null, () => settings);
            var platform = targets.SettingsAdapter(LibraryTargetKeys.SoundsProvider("Steam"));
            _service.ApplyToSettings(platform, SavePack("Steam pack", (UnlockSoundTier.Common, 1)), settings);
            var platformFolder = Path.GetDirectoryName(settings.GetProviderUnlockSounds("Steam").Common);

            _service.ApplyToSettings(adapter, SavePack("Global pack", (UnlockSoundTier.Common, 2)), settings);

            Assert.IsTrue(Directory.Exists(platformFolder), "the platform pack still points into it");
        }

        [TestMethod]
        public void PruneUnreferenced_RemovesAReplacedPick_AndKeepsTheWrittenPack()
        {
            var settings = new PersistedSettings();
            var adapter = new SoundsLibraryAdapter(_store, () => new[] { settings.UnlockSounds });
            var replaced = _store.ImportFile(WriteWav("old.wav", 4));
            settings.UnlockSounds.Common = replaced;

            // The pack written now is not the one the referenced packs see, as when a window
            // edits a settings copy the live settings replaced.
            var written = new UnlockSoundSettings { Common = _store.ImportFile(WriteWav("new.wav", 5)) };
            settings.UnlockSounds.Common = null;
            adapter.PruneUnreferenced(written);

            Assert.IsFalse(File.Exists(replaced), "nothing points at the replaced copy");
            Assert.IsTrue(File.Exists(written.Common), "the pack just written keeps its copy");
        }

        [TestMethod]
        public void PruneUnreferenced_WithoutReferencedPacks_RemovesNothing()
        {
            var kept = _store.ImportFile(WriteWav("kept.wav", 6));

            _adapter.PruneUnreferenced(new UnlockSoundSettings());

            Assert.IsTrue(File.Exists(kept));
        }

        private LibraryItem SavePack(string name, params (UnlockSoundTier Tier, byte Seed)[] tiers)
        {
            var path = Path.Combine(_root, LibraryStore.FolderOf(LibraryItemKind.Sounds), name + UnlockSoundPortableStore.PackageFileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var resolved = tiers
                .Select(pair => new ResolvedUnlockSound(pair.Tier, UnlockSoundSource.Custom, WriteWav("src_" + pair.Tier + "_" + pair.Seed + ".wav", pair.Seed)))
                .ToList();
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _store.Export(resolved, path);
            var item = _library.FindByPath(path) ?? new LibraryItem
            {
                Id = LibraryItem.NewLocalId(),
                Kind = LibraryItemKind.Sounds,
                Origin = LibraryItemOrigin.Local
            };
            item.Name = name;
            item.RelativePath = path;
            item.ContentHash = null;
            return _library.Upsert(item);
        }

        private string WriteWav(string name, byte seed)
        {
            var directory = Path.Combine(_root, "source-sounds");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, Wav(seed));
            return path;
        }

        private static byte[] Wav(byte seed)
        {
            var bytes = new byte[64];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
            for (var i = 16; i < bytes.Length; i++)
            {
                bytes[i] = seed;
            }

            return bytes;
        }
    }
}

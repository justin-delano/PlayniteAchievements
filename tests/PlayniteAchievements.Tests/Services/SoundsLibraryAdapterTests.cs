using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
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

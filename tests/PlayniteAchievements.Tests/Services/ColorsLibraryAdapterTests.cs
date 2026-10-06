using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class ColorsLibraryAdapterTests
    {
        private string _root;
        private LibraryStore _library;
        private LibraryApplyService _service;
        private ColorsLibraryAdapter _adapter;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchColorsLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _library = new LibraryStore(_root);
            _service = new LibraryApplyService(_library, new LibraryBaselineStore(_library.LibraryDirectory));
            _adapter = new ColorsLibraryAdapter();
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
        public void Project_IsStableAndCoversTheThreeColorGroups()
        {
            var settings = Colors("#111111", "#222222", "#333333");

            var first = _adapter.Project(settings);
            var second = _adapter.Project(settings);

            Assert.AreEqual(LibraryBaselineStore.HashProjection(first), LibraryBaselineStore.HashProjection(second));
            Assert.AreEqual("#111111", (string)first["RarityColors"]["Common"]);
            Assert.AreEqual("#333333", (string)first["ProviderColorOverrides"]["Steam"]);
            Assert.AreEqual("Transparent", (string)first["ResourceOverrides"]["PlayAch.Brush.GridSurface"]["Mode"]);
        }

        [TestMethod]
        public void Apply_WritesThePackAndLinksTheTargetUntouched()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();

            _service.ApplyToSettings(_adapter, item, settings);

            Assert.AreEqual("#111111", settings.RarityColors.Common);
            Assert.AreEqual("#333333", settings.ProviderColorOverrides["Steam"]);
            var link = settings.GetLibraryLink(LibraryTargetKeys.Colors);
            Assert.IsNotNull(link);
            Assert.AreEqual(item.Id, link.LibraryItemId);
            Assert.AreEqual(item.Version, link.AppliedVersion);
            Assert.IsTrue(File.Exists(_service.Baselines.Resolve(link.BaselineFile)));

            var state = _service.GetSettingsState(_adapter, settings);
            Assert.IsTrue(state.IsFollowing);
            Assert.IsFalse(state.IsEdited);
            Assert.IsFalse(state.IsUpdateAvailable);
        }

        [TestMethod]
        public void EditingAColor_MarksTheTargetEdited()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);

            settings.RarityColors = WithCommon(settings.RarityColors, "#ABCDEF");

            Assert.IsTrue(_service.GetSettingsState(_adapter, settings).IsEdited);
        }

        [TestMethod]
        public void ResavingALocalPreset_IsANewVersion_AndUpdateKeepsTheEditedColor()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            settings.RarityColors = WithCommon(settings.RarityColors, "#ABCDEF");

            var updated = SavePreset("Mine", Colors("#555555", "#666666", "#777777"));
            Assert.AreEqual(item.Id, updated.Id);
            Assert.AreNotEqual(item.Version, updated.Version);
            Assert.IsTrue(_service.GetSettingsState(_adapter, settings).IsUpdateAvailable);

            Assert.IsTrue(_service.UpdateSettings(_adapter, settings, out var kept));

            Assert.AreEqual(1, kept);
            Assert.AreEqual("#ABCDEF", settings.RarityColors.Common, "the edited color stays");
            Assert.AreEqual("#666666", settings.RarityColors.Rare, "an untouched color follows the new version");
            Assert.AreEqual("#777777", settings.ProviderColorOverrides["Steam"]);
            var state = _service.GetSettingsState(_adapter, settings);
            Assert.IsFalse(state.IsUpdateAvailable);
            Assert.IsFalse(state.IsEdited, "the baseline is the projection read back after the update");
            Assert.AreEqual(updated.Version, settings.GetLibraryLink(LibraryTargetKeys.Colors).AppliedVersion);
        }

        [TestMethod]
        public void Reset_AppliesThePresetAgain()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            settings.RarityColors = WithCommon(settings.RarityColors, "#ABCDEF");

            Assert.IsTrue(_service.ResetSettings(_adapter, settings));

            Assert.AreEqual("#111111", settings.RarityColors.Common);
            Assert.IsFalse(_service.GetSettingsState(_adapter, settings).IsEdited);
        }

        [TestMethod]
        public void StopFollowing_KeepsTheColorsAndRemovesTheLink()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            settings.RarityColors = WithCommon(settings.RarityColors, "#ABCDEF");

            LibraryApplyService.StopFollowing(_adapter, settings);

            Assert.IsNull(settings.GetLibraryLink(LibraryTargetKeys.Colors));
            Assert.AreEqual("#ABCDEF", settings.RarityColors.Common);
            Assert.IsFalse(_service.GetSettingsState(_adapter, settings).IsFollowing);
        }

        [TestMethod]
        public void Link_FollowsAPresetSavedFromTheTargetWithoutChangingIt()
        {
            var settings = Colors("#111111", "#222222", "#333333");
            var item = SavePreset("Mine", settings);

            _service.LinkSettings(_adapter, item, settings);

            var state = _service.GetSettingsState(_adapter, settings);
            Assert.IsTrue(state.IsFollowing);
            Assert.IsFalse(state.IsEdited);
            Assert.IsFalse(state.IsUpdateAvailable);
        }

        [TestMethod]
        public void UnlinkItem_RemovesEveryLinkToTheItem()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            settings.SetLibraryLink(LibraryTargetKeys.Sounds, new LibraryLink { LibraryItemId = "other" });

            Assert.AreEqual(1, LibraryApplyService.UnlinkItem(settings, item.Id));

            Assert.IsNull(settings.GetLibraryLink(LibraryTargetKeys.Colors));
            Assert.IsNotNull(settings.GetLibraryLink(LibraryTargetKeys.Sounds));
        }

        [TestMethod]
        public void ResetDisplaySettingsToDefaults_RemovesTheColorsLink()
        {
            var item = SavePreset("Mine", Colors("#111111", "#222222", "#333333"));
            var settings = new PersistedSettings();
            _service.ApplyToSettings(_adapter, item, settings);
            settings.SetLibraryLink(LibraryTargetKeys.Sounds, new LibraryLink { LibraryItemId = "sounds" });

            settings.ResetDisplaySettingsToDefaults();

            Assert.IsNull(settings.GetLibraryLink(LibraryTargetKeys.Colors));
            Assert.IsNotNull(settings.GetLibraryLink(LibraryTargetKeys.Sounds), "the sounds are not display settings");
            Assert.AreEqual(RarityColorSettings.DefaultCommon, settings.RarityColors.Common);
        }

        private LibraryItem SavePreset(string name, PersistedSettings colors)
        {
            var path = Path.Combine(_root, LibraryStore.FolderOf(LibraryItemKind.Colors), name + ColorPackPortableStore.PackageFileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            new ColorPackPortableStore().Export(colors, path);
            var item = _library.FindByPath(path) ?? new LibraryItem
            {
                Id = LibraryItem.NewLocalId(),
                Kind = LibraryItemKind.Colors,
                Origin = LibraryItemOrigin.Local
            };
            item.Name = name;
            item.RelativePath = path;
            item.ContentHash = null;
            return _library.Upsert(item);
        }

        private static PersistedSettings Colors(string common, string rare, string steam)
        {
            var settings = new PersistedSettings();
            var rarity = RarityColorSettings.CreateDefault();
            rarity.Common = common;
            rarity.Rare = rare;
            settings.RarityColors = rarity;
            settings.ProviderColorOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Steam"] = steam };
            settings.ResourceOverrides = PersistedSettings.CreateDefaultResourceOverrides();
            return settings;
        }

        private static RarityColorSettings WithCommon(RarityColorSettings colors, string common)
        {
            var copy = colors.Clone();
            copy.Common = common;
            return copy;
        }
    }
}

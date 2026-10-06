using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Notifications;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class NotificationStyleLibraryAdapterTests
    {
        private const string TemplateV1 = "<DataTemplate>one</DataTemplate>";
        private const string TemplateV2 = "<DataTemplate>two</DataTemplate>";

        private string _root;
        private NotificationImageStore _images;
        private NotificationStylePortableStore _portable;
        private MemoryTemplates _templates;
        private NotificationStyleLibraryAdapter _toast;
        private NotificationStyleLibraryAdapter _frame;
        private LibraryStore _library;
        private LibraryApplyService _apply;
        private GameLinkStore _gameLinks;
        private PersistedSettings _live;
        private PersistedSettings _snapshot;
        private GameCustomDataStore _customData;
        private NotificationLibraryTargets _targets;
        private LibraryUpdateService _updates;
        private DirectoryPackageFolder _toastFolder;

        private sealed class MemoryTemplates : INotificationTemplateFiles
        {
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Read(bool isFrame, string providerKey, Guid gameId)
            {
                return Files.TryGetValue(Key(isFrame, providerKey, gameId), out var xaml) ? xaml : null;
            }

            public void Write(bool isFrame, string providerKey, Guid gameId, string xamlOrNull)
            {
                if (string.IsNullOrWhiteSpace(xamlOrNull))
                {
                    Files.Remove(Key(isFrame, providerKey, gameId));
                }
                else
                {
                    Files[Key(isFrame, providerKey, gameId)] = xamlOrNull;
                }
            }

            public static string Key(bool isFrame, string providerKey, Guid gameId) =>
                (isFrame ? "frame|" : "toast|") + (providerKey ?? string.Empty) + "|" + gameId.ToString("N");
        }

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchStyleLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _images = new NotificationImageStore(new DiskImageService(logger: null, cacheRoot: Path.Combine(_root, "icon_cache")), logger: null);
            _portable = new NotificationStylePortableStore(_images, logger: null);
            _templates = new MemoryTemplates();
            _live = new PersistedSettings();
            _snapshot = new PersistedSettings();
            _customData = new GameCustomDataStore(Path.Combine(_root, "data"));
            Action prune = () => _images.PruneOrphans(new[] { _live, _snapshot }, _customData.LoadAll());
            _toast = new NotificationStyleLibraryAdapter(false, _portable, _images, _templates, prune);
            _frame = new NotificationStyleLibraryAdapter(true, _portable, _images, _templates, prune);
            _library = new LibraryStore(_root);
            _apply = new LibraryApplyService(_library, new LibraryBaselineStore(_library.LibraryDirectory));
            _gameLinks = new GameLinkStore(_library.LibraryDirectory);
            _targets = new NotificationLibraryTargets(_toast, _frame, () => _customData, () => _live);
            _updates = new LibraryUpdateService(
                _apply,
                _gameLinks,
                Enumerable.Empty<ISettingsLibraryAdapter>(),
                () => _live,
                update =>
                {
                    update(_live);
                    update(_snapshot);
                },
                update => update(_live),
                new ILibraryTargetResolver[] { _targets });
            _toastFolder = new DirectoryPackageFolder(
                Path.Combine(_root, LibraryStore.FolderOf(LibraryItemKind.Toast)),
                NotificationStylePortableStore.ToastPackageFileExtension);
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
        public void Apply_CopiesTheImagesIntoTheScope_AndAnUntouchedScopeIsNotEdited()
        {
            var item = WriteItem("1.0.0", Style(showHeader: false, background: Image("bg", 1)), TemplateV1);
            var adapter = Global(false);

            _apply.ApplyToSettings(adapter, item, _live);

            Assert.IsFalse(_live.NotificationStyle.Toast.ShowHeader);
            StringAssert.Contains(_live.NotificationStyle.ToastBackgroundImagePath, Path.Combine("notification_images", "global"));
            Assert.IsTrue(File.Exists(_live.NotificationStyle.ToastBackgroundImagePath));
            Assert.AreEqual(TemplateV1, _templates.Read(false, null, Guid.Empty));
            var state = _apply.GetSettingsState(adapter, _live);
            Assert.IsTrue(state.IsFollowing);
            Assert.IsFalse(state.IsEdited);

            _live.NotificationStyle.Toast.ShowGameName = false;
            Assert.IsTrue(_apply.GetSettingsState(adapter, _live).IsEdited);
        }

        [TestMethod]
        public void Apply_ToAPlatformWithoutItsOwnStyle_GivesItOne()
        {
            _live.NotificationStyle.Frame.ShowIcon = false;
            var item = WriteItem("1.0.0", Style(showHeader: false, background: Image("bg", 1)), null);

            _apply.ApplyToSettings(Provider("Steam", false), item, _live);

            var own = _live.GetProviderNotificationStyle("Steam");
            Assert.IsNotNull(own);
            Assert.IsFalse(own.Toast.ShowHeader);
            Assert.IsFalse(own.Frame.ShowIcon, "the surface the item does not carry comes from the inherited style");
            StringAssert.Contains(own.ToastBackgroundImagePath, Path.Combine("providers", "steam"));
            Assert.IsTrue(_live.NotificationStyle.Toast.ShowHeader, "the global style is left alone");
            Assert.IsNotNull(_live.GetLibraryLink(LibraryTargetKeys.ToastProvider("Steam")));
        }

        [TestMethod]
        public void Update_KeepsTheEdits_AndTakesTheRest()
        {
            var v1 = Style(showHeader: false, background: Image("bg", 1));
            var item = WriteItem("1.0.0", v1, TemplateV1);
            var adapter = Global(false);
            _apply.ApplyToSettings(adapter, item, _live);
            _live.NotificationStyle.Toast.ShowGameName = false;

            var v2 = Style(showHeader: false, background: Image("bg", 2));
            v2.Toast.ShowGameName = true;
            v2.Toast.ShowIcon = false;
            item = WriteItem("1.1.0", v2, TemplateV2);
            Assert.IsTrue(_apply.UpdateSettings(adapter, _live, out var kept));

            Assert.AreEqual(1, kept);
            Assert.IsFalse(_live.NotificationStyle.Toast.ShowGameName, "the user's edit stays");
            Assert.IsFalse(_live.NotificationStyle.Toast.ShowIcon, "an untouched field follows the new version");
            Assert.AreEqual(Hash(Image("bg", 2)), Hash(_live.NotificationStyle.ToastBackgroundImagePath), "an untouched image follows the new version");
            Assert.AreEqual(TemplateV2, _templates.Read(false, null, Guid.Empty));
            Assert.AreEqual("1.1.0", _live.GetLibraryLink(LibraryTargetKeys.ToastGlobal).AppliedVersion);
            Assert.IsFalse(_apply.GetSettingsState(adapter, _live).IsEdited, "the merged look is the new baseline");
        }

        [TestMethod]
        public void Update_KeepsAnImageAndATemplateTheUserReplaced()
        {
            var item = WriteItem("1.0.0", Style(showHeader: false, background: Image("bg", 1)), TemplateV1);
            var adapter = Global(false);
            _apply.ApplyToSettings(adapter, item, _live);
            var own = Image("mine", 9);
            File.Copy(own, _live.NotificationStyle.ToastBackgroundImagePath, overwrite: true);
            File.SetLastWriteTimeUtc(_live.NotificationStyle.ToastBackgroundImagePath, DateTime.UtcNow.AddMinutes(1));
            _templates.Write(false, null, Guid.Empty, "<DataTemplate>mine</DataTemplate>");

            WriteItem("1.1.0", Style(showHeader: false, background: Image("bg", 2)), TemplateV2);
            _apply.UpdateSettings(adapter, _live, out var kept);

            Assert.AreEqual(2, kept);
            Assert.AreEqual(Hash(own), Hash(_live.NotificationStyle.ToastBackgroundImagePath));
            Assert.AreEqual("<DataTemplate>mine</DataTemplate>", _templates.Read(false, null, Guid.Empty));
        }

        [TestMethod]
        public void Replace_RemovesTheTemplate_WhenTheItemHasNone()
        {
            _templates.Write(false, null, Guid.Empty, TemplateV1);
            var item = WriteItem("1.0.0", Style(showHeader: false, background: null), null);

            _apply.ApplyToSettings(Global(false), item, _live);

            Assert.IsNull(_templates.Read(false, null, Guid.Empty));
        }

        [TestMethod]
        public void Kinds_TheItemCarriesAreApplied_AndAKindTheUserStoppedStylingStaysStopped()
        {
            var v1 = Style(showHeader: false, background: null);
            v1.EnableKindStyle(NotificationKind.Capstone).Toast.ShowIcon = false;
            var item = WriteItem("1.0.0", v1, null);
            var adapter = Global(false);
            _apply.ApplyToSettings(adapter, item, _live);
            Assert.IsTrue(_live.NotificationStyle.HasKindStyle(NotificationKind.Capstone));
            Assert.IsFalse(_live.NotificationStyle.ResolveKind(NotificationKind.Capstone).Toast.ShowIcon);

            _live.NotificationStyle.ClearKindStyle(NotificationKind.Capstone);
            Assert.IsTrue(_apply.GetSettingsState(adapter, _live).IsEdited);

            var v2 = Style(showHeader: true, background: null);
            v2.EnableKindStyle(NotificationKind.Capstone).Toast.ShowIcon = true;
            WriteItem("1.1.0", v2, null);
            _apply.UpdateSettings(adapter, _live, out _);

            Assert.IsFalse(_live.NotificationStyle.HasKindStyle(NotificationKind.Capstone), "the user's choice stays");
            Assert.IsTrue(_live.NotificationStyle.Toast.ShowHeader);
        }

        [TestMethod]
        public void ApplyToKind_ChangesOnlyThatKind_AndLeavesTheTemplateAndLink()
        {
            _templates.Write(false, null, Guid.Empty, TemplateV1);
            var item = WriteItem("1.0.0", Style(showHeader: false, background: Image("bg", 1)), TemplateV2);
            var path = _library.FullPath(item);
            var globalBackground = Image("global", 5);
            _live.NotificationStyle.ToastBackgroundImagePath = globalBackground;

            _toast.ApplyToKind(path, NotificationStyleScope.ForSettings(_live, null), NotificationKind.Completion);

            Assert.IsTrue(_live.NotificationStyle.Toast.ShowHeader);
            Assert.AreEqual(globalBackground, _live.NotificationStyle.ToastBackgroundImagePath);
            var kind = _live.NotificationStyle.ResolveKind(NotificationKind.Completion);
            Assert.IsFalse(kind.Toast.ShowHeader);
            StringAssert.Contains(kind.ToastBackgroundImagePath, Path.Combine("kinds", "completion"));
            Assert.AreEqual(TemplateV1, _templates.Read(false, null, Guid.Empty));
            Assert.IsNull(_live.GetLibraryLink(LibraryTargetKeys.ToastGlobal));
        }

        [TestMethod]
        public void UpdateService_MergesIntoGlobalAndGameFollowers_AndReportsTheirState()
        {
            var item = WriteItem("1.0.0", Style(showHeader: false, background: Image("bg", 1)), null);
            _apply.ApplyToSettings(Global(false), item, _live);
            var game = Guid.NewGuid();
            var gameKey = LibraryTargetKeys.ToastGame(game);
            _updates.ApplyToGameTarget(gameKey, item);
            Assert.IsTrue(_customData.TryLoad(game, out var data));
            Assert.IsFalse(data.NotificationAppearanceOverride.Style.Toast.ShowHeader);
            StringAssert.Contains(data.NotificationAppearanceOverride.Style.ToastBackgroundImagePath, game.ToString("D"));

            _customData.Update(game, custom => custom.NotificationAppearanceOverride.Style.Toast.ShowIcon = false);
            var uses = _updates.UsesOf(item);
            Assert.AreEqual(2, uses.Count);
            Assert.IsTrue(uses.Single(use => use.TargetKey == gameKey).IsEdited);
            Assert.IsFalse(uses.Single(use => use.TargetKey == LibraryTargetKeys.ToastGlobal).IsEdited);
            Assert.IsTrue(uses.All(use => use.CanReset));

            var v2 = Style(showHeader: false, background: Image("bg", 2));
            v2.Toast.ShowGameName = false;
            WriteItem("1.1.0", v2, null);
            var report = _updates.MergeIntoTargets(item.Id, LibraryApplyMode.Merge);

            CollectionAssert.AreEquivalent(new[] { LibraryTargetKeys.ToastGlobal, gameKey }, report.UpdatedTargets);
            Assert.AreEqual(0, report.PendingTargets.Count);
            Assert.IsFalse(_live.NotificationStyle.Toast.ShowGameName);
            _customData.TryLoad(game, out data);
            Assert.IsFalse(data.NotificationAppearanceOverride.Style.Toast.ShowGameName);
            Assert.IsFalse(data.NotificationAppearanceOverride.Style.Toast.ShowIcon, "the game's edit stays");
            Assert.AreEqual("1.1.0", _gameLinks.Get(gameKey).AppliedVersion);

            Assert.IsTrue(_updates.Reset(gameKey));
            _customData.TryLoad(game, out data);
            Assert.IsTrue(data.NotificationAppearanceOverride.Style.Toast.ShowIcon);
        }

        // ---- helpers ----------------------------------------------------------------------------

        private ISettingsLibraryAdapter Global(bool isFrame) =>
            _targets.SettingsAdapter(LibraryTargetKeys.NotificationScope(isFrame, null, Guid.Empty));

        private ISettingsLibraryAdapter Provider(string providerKey, bool isFrame) =>
            _targets.SettingsAdapter(LibraryTargetKeys.NotificationScope(isFrame, providerKey, Guid.Empty));

        private static NotificationStyleSettings Style(bool showHeader, string background)
        {
            var style = NotificationStyleSettings.CreateDefault();
            style.Toast.ShowHeader = showHeader;
            style.ToastBackgroundImagePath = background;
            return style;
        }

        /// <summary>Writes the style as the Workshop toast item "glass" at a version.</summary>
        private LibraryItem WriteItem(string version, NotificationStyleSettings style, string template)
        {
            var package = Path.Combine(_root, "downloads", version, "glass" + NotificationStylePortableStore.ToastPackageFileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(package));
            _portable.ExportSurfacePackage(isFrame: false, style, package, template);
            return _updates.WriteWorkshopPart(
                new LibraryItem
                {
                    Id = "ws:glass",
                    Kind = LibraryItemKind.Toast,
                    Name = "Glass",
                    Origin = LibraryItemOrigin.Workshop,
                    WorkshopItemId = "glass",
                    Part = "toast",
                    Version = version
                },
                package,
                _toastFolder).Item;
        }

        /// <summary>A small PNG whose last byte makes its content distinct.</summary>
        private string Image(string name, byte variant)
        {
            var path = Path.Combine(_root, "sources", name + "_" + variant + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var bytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIW2NkYGD4DwABBAEAgh8sXQAAAABJRU5ErkJggg==")
                .Concat(new[] { variant })
                .ToArray();
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string Hash(string path) => LibraryStore.HashFile(path);
    }
}

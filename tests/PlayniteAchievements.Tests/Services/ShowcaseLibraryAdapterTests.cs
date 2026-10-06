using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Showcase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class ShowcaseLibraryAdapterTests
    {
        private string _root;
        private LibraryStore _library;
        private LibraryApplyService _apply;
        private PersistedSettings _live;
        private PersistedSettings _snapshot;
        private ShowcaseLibraryTargets _targets;
        private LibraryUpdateService _updates;
        private DirectoryPackageFolder _folder;

        // The author's side: the page the package is exported from.
        private ShowcaseSettings _author;
        private GridOptionsCatalog _authorGrids;
        private ShowcasePageSettings _authorPage;
        private ShowcaseWidgetInstanceSettings _authorRecent;
        private ShowcaseWidgetInstanceSettings _authorPie;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchShowcaseLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _library = new LibraryStore(_root);
            _apply = new LibraryApplyService(_library, new LibraryBaselineStore(_library.LibraryDirectory));
            _live = new PersistedSettings();
            _snapshot = new PersistedSettings();
            _targets = new ShowcaseLibraryTargets(() => _live, path => path);
            _updates = new LibraryUpdateService(
                _apply,
                new GameLinkStore(_library.LibraryDirectory),
                Enumerable.Empty<ISettingsLibraryAdapter>(),
                () => _live,
                update => update(_live),
                update => update(_live),
                new ILibraryTargetResolver[] { _targets });
            _folder = new DirectoryPackageFolder(
                Path.Combine(_root, LibraryStore.ShowcaseFolderName),
                ShowcasePagePortableStore.PackageFileExtension);

            _author = new ShowcaseSettings();
            _authorPage = ShowcaseLayoutService.AddPage(_author, ShowcasePageTemplate.Blank, "Trophies");
            _author.Pages.RemoveAll(page => page != _authorPage);
            _authorRecent = ShowcaseLayoutService.CreateWidget(_author, ShowcaseWidgetKind.RecentAchievements);
            _authorRecent.CustomTitle = "Latest";
            _authorRecent.SetOption("MaxPerGame", 3);
            _authorPie = ShowcaseLayoutService.CreateWidget(_author, ShowcaseWidgetKind.Pie);
            _authorPie.CustomTitle = "Rarity";
            var blocks = AuthorBlocks();
            Assert.IsTrue(ShowcaseLayoutService.PlaceWidget(_author, _authorPage.PageId, blocks[0].BlockId, _authorRecent.InstanceId));
            Assert.IsTrue(ShowcaseLayoutService.PlaceWidget(_author, _authorPage.PageId, blocks[1].BlockId, _authorPie.InstanceId));
            _authorGrids = new GridOptionsCatalog();
            _authorGrids.GetAchievement(ShowcaseGridSurfaces.ResolveWidgetSurface(_authorRecent.Kind, _authorRecent.InstanceId)).UseCoverImages = true;
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
        public void Install_ThenUntouched_IsNotEdited_AndEditingATitleIs()
        {
            var page = Install("1.0.0");
            var adapter = _targets.AdapterFor(page.PageId);

            var state = _apply.GetSettingsState(adapter, _live);
            Assert.IsTrue(state.IsFollowing);
            Assert.IsFalse(state.IsEdited);
            Assert.AreEqual(2, _live.GetLibraryLink(adapter.TargetKey).IdMap.Count);

            Local(page, ShowcaseWidgetKind.Pie).CustomTitle = "Mine";
            Assert.IsTrue(_apply.GetSettingsState(adapter, _live).IsEdited);
        }

        [TestMethod]
        public void Update_MergesPerWidget_KeepingTheUsersTitleAndOwnOptions()
        {
            var page = Install("1.0.0");
            var pie = Local(page, ShowcaseWidgetKind.Pie);
            var recent = Local(page, ShowcaseWidgetKind.RecentAchievements);
            pie.CustomTitle = "Mine";
            ShowcaseWidgetOptions.SetPinCollectionId(recent, "my-pins");

            _authorPie.CustomTitle = "Rarity spread";
            _authorRecent.SetOption("MaxPerGame", 9);
            _authorRecent.CustomTitle = "Newest";
            WritePackage("1.1.0");
            var report = _updates.MergeIntoTargets("ws:trophies", LibraryApplyMode.Merge);

            CollectionAssert.AreEqual(new[] { LibraryTargetKeys.Showcase(page.PageId) }, report.UpdatedTargets);
            Assert.AreEqual(1, report.KeptEdits);
            Assert.AreSame(pie, Local(page, ShowcaseWidgetKind.Pie), "the widgets keep their ids on the page");
            Assert.AreEqual("Mine", pie.CustomTitle, "the user's title stays");
            Assert.AreEqual("Newest", recent.CustomTitle);
            Assert.AreEqual(9, recent.GetOption("MaxPerGame", 0));
            Assert.AreEqual("my-pins", ShowcaseWidgetOptions.GetPinCollectionId(recent), "the user's own options stay");
            Assert.AreEqual(1, _live.Showcase.Pages.Count(candidate => candidate.Name.StartsWith("Trophies")), "no new page");
        }

        [TestMethod]
        public void Update_AddsANewWidget_WhenTheUserLeftTheLayoutAlone()
        {
            var page = Install("1.0.0");

            var games = ShowcaseLayoutService.CreateWidget(_author, ShowcaseWidgetKind.GameSummaries);
            Assert.IsTrue(ShowcaseLayoutService.PlaceWidget(_author, _authorPage.PageId, AuthorBlocks()[2].BlockId, games.InstanceId));
            WritePackage("1.1.0");
            _updates.MergeIntoTargets("ws:trophies", LibraryApplyMode.Merge);

            var added = Local(page, ShowcaseWidgetKind.GameSummaries);
            Assert.IsNotNull(added);
            Assert.AreNotEqual(games.InstanceId, added.InstanceId);
            Assert.AreEqual(_live.Showcase.DefaultGamePinCollectionId, ShowcaseWidgetOptions.GetPinCollectionId(added));
            var link = _live.GetLibraryLink(LibraryTargetKeys.Showcase(page.PageId));
            Assert.AreEqual(added.InstanceId, link.IdMap[games.InstanceId]);
            Assert.IsFalse(_apply.GetSettingsState(_targets.AdapterFor(page.PageId), _live).IsEdited);
        }

        [TestMethod]
        public void Update_KeepsAWidgetTheUserRemoved_Removed()
        {
            var page = Install("1.0.0");
            var pie = Local(page, ShowcaseWidgetKind.Pie);
            Assert.IsTrue(ShowcaseLayoutService.DeleteWidget(_live.Showcase, pie.InstanceId));

            _authorPie.CustomTitle = "Rarity spread";
            WritePackage("1.1.0");
            _updates.MergeIntoTargets("ws:trophies", LibraryApplyMode.Merge);

            Assert.IsNull(Local(page, ShowcaseWidgetKind.Pie));
            Assert.IsNotNull(Local(page, ShowcaseWidgetKind.RecentAchievements));
        }

        [TestMethod]
        public void Reset_TakesThePackageAsPublished_OnTheSameWidgets()
        {
            var page = Install("1.0.0");
            var pie = Local(page, ShowcaseWidgetKind.Pie);
            pie.CustomTitle = "Mine";

            Assert.IsTrue(_updates.Reset(LibraryTargetKeys.Showcase(page.PageId)));

            Assert.AreSame(pie, Local(page, ShowcaseWidgetKind.Pie));
            Assert.AreEqual("Rarity", pie.CustomTitle);
            Assert.IsFalse(_apply.GetSettingsState(_targets.AdapterFor(page.PageId), _live).IsEdited);
        }

        // ---- helpers ----------------------------------------------------------------------------

        private List<ShowcaseBlockSettings> AuthorBlocks()
        {
            return _authorPage.Blocks.OrderBy(block => block.Row).ThenBy(block => block.Column).ToList();
        }

        private LibraryItem WritePackage(string version)
        {
            var package = Path.Combine(_root, "downloads", version, "trophies" + ShowcasePagePortableStore.PackageFileExtension);
            ShowcasePagePortableStore.Write(package, ShowcasePagePortableStore.BuildPortable(_author, _authorGrids, _authorPage.PageId));
            return _updates.WriteWorkshopPart(
                new LibraryItem
                {
                    Id = "ws:trophies",
                    Kind = LibraryItemKind.ShowcasePage,
                    Name = "Trophies",
                    Origin = LibraryItemOrigin.Workshop,
                    WorkshopItemId = "trophies",
                    Version = version
                },
                package,
                _folder).Item;
        }

        /// <summary>Installs the package as a new page that follows the item, as the Workshop installer does.</summary>
        private ShowcasePageSettings Install(string version)
        {
            var item = WritePackage(version);
            var portable = ShowcasePagePortableStore.Read(_library.FullPath(item));
            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var page = ShowcasePagePortableStore.ApplyPortable(_live.Showcase, _live.GridOptions, portable, null, path => path, map);
                ShowcaseLayoutService.Normalize(_live.Showcase);
                ShowcaseLayoutService.PruneOrphanedWidgets(_live.Showcase);
                var adapter = _targets.AdapterFor(page.PageId);
                _live.SetLibraryLink(adapter.TargetKey, _apply.Link(adapter, item, _live, map));
                return page;
            }
            finally
            {
                ShowcasePagePortableStore.DeleteExtractedImages(portable);
            }
        }

        private ShowcaseWidgetInstanceSettings Local(ShowcasePageSettings page, ShowcaseWidgetKind kind)
        {
            return page.Blocks
                .Where(block => block.WidgetInstanceId != null)
                .Select(block => _live.Showcase.WidgetInstances.FirstOrDefault(widget => widget.InstanceId == block.WidgetInstanceId))
                .FirstOrDefault(widget => widget != null && widget.Kind == kind);
        }
    }
}

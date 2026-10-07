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
    public class GameDataLinkServiceTests
    {
        private string _root;
        private string _libraryDirectory;
        private string _baselineDirectory;
        private GameLinkStore _links;
        private readonly Dictionary<Guid, GameCustomDataFile> _data = new Dictionary<Guid, GameCustomDataFile>();
        private string _iconDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchGameDataLinks_" + Guid.NewGuid().ToString("N"));
            _libraryDirectory = Path.Combine(_root, "library");
            _baselineDirectory = Path.Combine(_root, "workshop", WorkshopBaselineStore.FolderName);
            _iconDirectory = Path.Combine(_root, "icons");
            Directory.CreateDirectory(_baselineDirectory);
            Directory.CreateDirectory(_iconDirectory);
            _links = new GameLinkStore(_libraryDirectory);
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
        public void Record_KeepsTheWholeRecordWithAPackageCopy()
        {
            var game = Guid.NewGuid();
            var baseline = Baseline("notes", game);

            var link = Service().Record(game, "notes", "Notes", "1.0.0", baseline, Package("v1"));

            Assert.AreEqual("ws:notes", link.LibraryItemId);
            Assert.AreEqual("Notes", link.Name);
            Assert.AreEqual("1.0.0", link.AppliedVersion);
            Assert.AreEqual(baseline, link.BaselineFile);
            var stored = _links.Get(LibraryTargetKeys.GameData(game));
            Assert.AreEqual(link.PackageFile, stored.PackageFile);
            var copy = Service().PackagePathOf(stored);
            Assert.IsNotNull(copy);
            StringAssert.StartsWith(copy, Path.Combine(_libraryDirectory, GameDataLinkService.FolderName));
            Assert.AreEqual("v1", File.ReadAllText(copy));
        }

        [TestMethod]
        public void Record_OverAnotherItem_DeletesTheBaselineTheOldRecordKept()
        {
            var game = Guid.NewGuid();
            var service = Service();
            var first = Baseline("notes", game);
            service.Record(game, "notes", "Notes", "1.0.0", first, Package("v1"));
            var second = Baseline("icons", game);

            var link = service.Record(game, "icons", "Icons", "2.0.0", second, Package("v2"));

            Assert.IsFalse(File.Exists(first));
            Assert.IsFalse(File.Exists(WorkshopBaselineStore.IconHashesPath(first)));
            Assert.IsTrue(File.Exists(second));
            Assert.AreEqual("v2", File.ReadAllText(service.PackagePathOf(link)));
            Assert.AreEqual(1, Directory.GetFiles(service.PackageDirectory).Length);
        }

        [TestMethod]
        public void Record_SameItemAgain_KeepsTheBaselineItRewrote()
        {
            var game = Guid.NewGuid();
            var service = Service();
            var baseline = Baseline("notes", game);
            service.Record(game, "notes", "Notes", "1.0.0", baseline, Package("v1"));

            service.Record(game, "notes", null, "1.1.0", baseline, Package("v2"));

            var link = service.Get(game);
            Assert.IsTrue(File.Exists(baseline));
            Assert.AreEqual("Notes", link.Name, "a record without a name keeps the earlier one");
            Assert.AreEqual("1.1.0", link.AppliedVersion);
        }

        [TestMethod]
        public void Unlink_DeletesTheCopyAndTheBaseline_AndTheGameKeepsItsData()
        {
            var game = Guid.NewGuid();
            var service = Service();
            var baseline = Baseline("notes", game);
            var link = service.Record(game, "notes", "Notes", "1.0.0", baseline, Package("v1"));
            var copy = service.PackagePathOf(link);
            _data[game] = Data("a");

            Assert.IsTrue(service.Unlink(game));

            Assert.IsNull(service.Get(game));
            Assert.IsFalse(File.Exists(copy));
            Assert.IsFalse(File.Exists(baseline));
            Assert.IsFalse(File.Exists(WorkshopBaselineStore.IconHashesPath(baseline)));
            Assert.IsNotNull(_data[game]);
            Assert.IsFalse(service.Unlink(game));
        }

        [TestMethod]
        public void Unlink_LeavesABaselineOutsideTheBaselinesFolder()
        {
            var game = Guid.NewGuid();
            var elsewhere = Path.Combine(_root, "elsewhere.json");
            File.WriteAllText(elsewhere, "{}");
            _links.Set(LibraryTargetKeys.GameData(game), new LibraryLink { LibraryItemId = "ws:notes", BaselineFile = elsewhere });

            Assert.IsTrue(Service().Unlink(game));

            Assert.IsTrue(File.Exists(elsewhere));
        }

        [TestMethod]
        public void Updates_AreJudgedPerGame()
        {
            var behind = new LibraryLink { LibraryItemId = "ws:notes", AppliedVersion = "1.0.0" };
            var current = new LibraryLink { LibraryItemId = "ws:notes", AppliedVersion = "2.0.0" };
            var other = new LibraryLink { LibraryItemId = "ws:icons", AppliedVersion = "1.0.0" };
            var notes = new WorkshopItem { Id = "notes", Version = "2.0.0" };
            var index = new WorkshopIndexFile { Items = new List<WorkshopItem> { notes, new WorkshopItem { Id = "icons", Version = "1.0.0" } } };

            Assert.IsTrue(GameDataLinkService.HasUpdate(behind, notes));
            Assert.IsFalse(GameDataLinkService.HasUpdate(current, notes));
            Assert.IsFalse(GameDataLinkService.HasUpdate(other, notes), "another item's version never counts");
            CollectionAssert.AreEqual(new[] { notes }, GameDataLinkService.FindUpdates(index, new[] { behind, current, other }).ToList());
            Assert.AreEqual(0, GameDataLinkService.FindUpdates(index, new[] { current }).Count);
            Assert.AreSame(notes, GameDataLinkService.IndexItemOf(index, behind));
            Assert.AreEqual("notes", GameDataLinkService.WorkshopItemIdOf(behind));
        }

        [TestMethod]
        public void DataDiffers_CountsCurationButNotProgress()
        {
            var baseline = Data("a");
            var progressOnly = Data("a");
            progressOnly.GoalAchievementApiNames = new List<string> { "ACH_1" };
            progressOnly.AchievementOrder = new List<string>();
            progressOnly.PlayniteGameId = Guid.NewGuid();
            var edited = Data("b");

            Assert.IsFalse(GameDataLinkService.DataDiffers(baseline, progressOnly));
            Assert.IsTrue(GameDataLinkService.DataDiffers(baseline, edited));
            Assert.IsTrue(GameDataLinkService.DataDiffers(baseline, null));
        }

        [TestMethod]
        public void IconsDiffer_SeesAddedRemovedAndChangedFiles()
        {
            var recorded = new Dictionary<string, string> { ["a.png"] = "1", ["b.png"] = "2" };

            Assert.IsFalse(GameDataLinkService.IconsDiffer(recorded, new Dictionary<string, string> { ["A.PNG"] = "1", ["b.png"] = "2" }));
            Assert.IsTrue(GameDataLinkService.IconsDiffer(recorded, new Dictionary<string, string> { ["a.png"] = "1", ["b.png"] = "3" }));
            Assert.IsTrue(GameDataLinkService.IconsDiffer(recorded, new Dictionary<string, string> { ["a.png"] = "1" }));
            Assert.IsTrue(GameDataLinkService.IconsDiffer(recorded, new Dictionary<string, string> { ["a.png"] = "1", ["c.png"] = "2" }));
            Assert.IsFalse(GameDataLinkService.IconsDiffer(null, new Dictionary<string, string> { ["a.png"] = "1" }), "no recorded hashes, nothing to compare");
        }

        [TestMethod]
        public void IsEdited_ComparesTheStoredDataAndIconsWithTheBaseline()
        {
            var game = Guid.NewGuid();
            File.WriteAllText(Path.Combine(_iconDirectory, "a.png"), "icon");
            var baseline = Path.Combine(_baselineDirectory, "notes-" + game.ToString("N") + ".json");
            File.WriteAllText(baseline, JsonConvert.SerializeObject(Data("a")));
            File.WriteAllText(WorkshopBaselineStore.IconHashesPath(baseline), JsonConvert.SerializeObject(WorkshopBaselineStore.HashIcons(_iconDirectory)));
            var link = new LibraryLink { LibraryItemId = "ws:notes", BaselineFile = baseline };
            var service = Service();
            _data[game] = Data("a");

            Assert.IsFalse(service.IsEdited(game, link));

            File.WriteAllText(Path.Combine(_iconDirectory, "a.png"), "swapped icon");
            Assert.IsTrue(service.IsEdited(game, link));

            File.WriteAllText(Path.Combine(_iconDirectory, "a.png"), "icon");
            _data[game] = Data("b");
            Assert.IsTrue(service.IsEdited(game, link));

            Assert.IsFalse(service.IsEdited(game, new LibraryLink { LibraryItemId = "ws:notes" }), "no baseline, nothing to tell");
        }

        [TestMethod]
        public void GroupByItem_MakesOneGroupPerItemWithTheHighestVersionAndItsName()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var c = Guid.NewGuid();
            var records = new Dictionary<Guid, LibraryLink>
            {
                [a] = new LibraryLink { LibraryItemId = "ws:notes", Name = "Notes", AppliedVersion = "1.0.0" },
                [b] = new LibraryLink { LibraryItemId = "ws:NOTES", Name = "Notes (renamed)", AppliedVersion = "1.2.0" },
                [c] = new LibraryLink { LibraryItemId = "ws:icons", AppliedVersion = "3.0.0" },
                [Guid.NewGuid()] = new LibraryLink { LibraryItemId = null, Name = "Nothing" }
            };

            var groups = GameDataLinkService.GroupByItem(records);

            Assert.AreEqual(2, groups.Count);
            var icons = groups.Single(group => group.WorkshopItemId == "icons");
            Assert.IsNull(icons.Name);
            Assert.AreEqual("3.0.0", icons.HighestVersion);
            CollectionAssert.AreEqual(new[] { c }, icons.Games.Select(pair => pair.Key).ToList());
            var notes = groups.Single(group => string.Equals(group.WorkshopItemId, "notes", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("Notes (renamed)", notes.Name);
            Assert.AreEqual("1.2.0", notes.HighestVersion);
            CollectionAssert.AreEquivalent(new[] { a, b }, notes.Games.Select(pair => pair.Key).ToList());
        }

        [TestMethod]
        public void GroupByItem_TakesAnyRecordsNameWhenTheHighestHasNone()
        {
            var records = new Dictionary<Guid, LibraryLink>
            {
                [Guid.NewGuid()] = new LibraryLink { LibraryItemId = "ws:notes", Name = "Notes", AppliedVersion = "1.0.0" },
                [Guid.NewGuid()] = new LibraryLink { LibraryItemId = "ws:notes", AppliedVersion = "2.0.0" }
            };

            var group = GameDataLinkService.GroupByItem(records).Single();

            Assert.AreEqual("Notes", group.Name);
            Assert.AreEqual("2.0.0", group.HighestVersion);
        }

        [TestMethod]
        public void GamesBehind_ListsOnlyTheGamesOnAnOlderVersionOfThatItem()
        {
            var behind = Guid.NewGuid();
            var current = Guid.NewGuid();
            var other = Guid.NewGuid();
            var records = new Dictionary<Guid, LibraryLink>
            {
                [behind] = new LibraryLink { LibraryItemId = "ws:notes", AppliedVersion = "1.0.0" },
                [current] = new LibraryLink { LibraryItemId = "ws:notes", AppliedVersion = "1.1.0" },
                [other] = new LibraryLink { LibraryItemId = "ws:icons", AppliedVersion = "0.1.0" }
            };

            var games = GameDataLinkService.GamesBehind(records, new WorkshopItem { Id = "notes", Version = "1.1.0" });

            CollectionAssert.AreEqual(new[] { behind }, games.ToList());
            Assert.AreEqual(0, GameDataLinkService.GamesBehind(records, null).Count);
        }

        private GameDataLinkService Service()
        {
            return new GameDataLinkService(
                _links,
                _libraryDirectory,
                _baselineDirectory,
                gameId => _data.TryGetValue(gameId, out var data) ? data : null,
                _ => _iconDirectory);
        }

        private string Baseline(string itemId, Guid game)
        {
            var path = Path.Combine(_baselineDirectory, itemId + "-" + game.ToString("N") + ".json");
            File.WriteAllText(path, "{}");
            File.WriteAllText(WorkshopBaselineStore.IconHashesPath(path), "{}");
            return path;
        }

        private string Package(string content)
        {
            var directory = Path.Combine(_root, "downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "notes.pa");
            File.WriteAllText(path, content);
            return path;
        }

        private static GameCustomDataFile Data(string category)
        {
            return new GameCustomDataFile
            {
                AchievementCategoryOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ACH_1"] = category },
                AchievementOrder = null
            };
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class LibraryFolderMigrationTests
    {
        private static readonly Guid GameId = new Guid("6f185bd9-c1ff-444f-880e-994e353ac5f2");

        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchLibraryFolders_" + Guid.NewGuid().ToString("N"));
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
        public void FreshLayout_IsLeftUntouched()
        {
            Write(Path.Combine(LibraryStore.FolderOf(LibraryItemKind.Colors), "Neon.pacolors"), "colors-1");
            Write(Path.Combine(LibraryStore.GameDataBaselinesFolder, "item-game.json"), "{}");
            WriteIndex(Item("a", LibraryItemKind.Colors, Path.Combine(LibraryStore.FolderOf(LibraryItemKind.Colors), "Neon.pacolors")));
            WriteLinks(Path.Combine(_root, LibraryStore.GameDataBaselinesFolder, "item-game.json"));
            Write(Path.Combine("custom_templates", "global", "AchievementToast.xaml"), "<x/>");
            var indexBefore = File.ReadAllText(IndexPath);
            var linksBefore = File.ReadAllText(LinksPath);

            var result = LibraryFolderMigration.Run(_root);

            Assert.IsFalse(result.HasChanges);
            Assert.AreEqual(indexBefore, File.ReadAllText(IndexPath));
            Assert.AreEqual(linksBefore, File.ReadAllText(LinksPath));
            Assert.IsFalse(File.Exists(JournalPath));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "custom_templates", "global", "AchievementToast.xaml")));
        }

        [TestMethod]
        public void OldLayout_MovesEveryFolder_AndRewritesIndexLinksAndInstalls()
        {
            Write(Path.Combine("color_presets", "Neon.pacolors"), "colors-1");
            Write(Path.Combine("unlock_sound_presets", "Chimes.pasounds"), "sounds-1");
            Write(Path.Combine("notification_style_presets", "toast", "Glass.panotif"), "toast-1");
            Write(Path.Combine("notification_style_presets", "frame", "Gold.paframe"), "frame-1");
            Write(Path.Combine("showcase_presets", "Stats.pashowcase"), "page-1");
            var oldBaseline = Path.Combine(_root, "workshop", "baselines", "item-" + GameId.ToString("N") + ".json");
            Write(oldBaseline, "{}");
            Write(WorkshopBaselineStore.IconHashesPath(oldBaseline), "{}");
            WriteIndex(
                Item("c", LibraryItemKind.Colors, Path.Combine("color_presets", "Neon.pacolors"), local: false),
                Item("s", LibraryItemKind.Sounds, Path.Combine("unlock_sound_presets", "Chimes.pasounds")),
                Item("t", LibraryItemKind.Toast, Path.Combine("notification_style_presets", "toast", "Glass.panotif")),
                Item("f", LibraryItemKind.Frame, Path.Combine("notification_style_presets", "frame", "Gold.paframe")),
                Item("p", LibraryItemKind.ShowcasePage, Path.Combine("showcase_presets", "Stats.pashowcase")));
            WriteLinks(oldBaseline);
            Write(
                Path.Combine("workshop", WorkshopIdentityStore.InstalledFileName),
                JsonConvert.SerializeObject(new[] { new { Id = "item", Kind = "GameCustomData", PlayniteGameId = GameId, BaselineFile = oldBaseline } }));

            var result = LibraryFolderMigration.Run(_root);

            Assert.AreEqual(7, result.Moved.Count);
            Assert.AreEqual(0, result.Failed);
            Assert.AreEqual(5, result.RewrittenItems);
            Assert.AreEqual(2, result.RewrittenBaselines, "the link and the recorded install");
            Assert.IsFalse(File.Exists(JournalPath));

            var expected = new Dictionary<string, string>
            {
                ["c"] = Path.Combine("library", "colors", "Neon.pacolors"),
                ["s"] = Path.Combine("library", "sounds", "Chimes.pasounds"),
                ["t"] = Path.Combine("library", "notifications", "Glass.panotif"),
                ["f"] = Path.Combine("library", "frames", "Gold.paframe"),
                ["p"] = Path.Combine("library", "showcase_pages", "Stats.pashowcase")
            };
            var index = ReadIndexPaths();
            foreach (var pair in expected)
            {
                Assert.AreEqual(pair.Value, index[pair.Key]);
                Assert.IsTrue(File.Exists(Path.Combine(_root, pair.Value)), pair.Value);
            }

            var newBaseline = Path.Combine(_root, "library", "gamedata", "baselines", Path.GetFileName(oldBaseline));
            Assert.IsTrue(File.Exists(newBaseline));
            Assert.IsTrue(File.Exists(WorkshopBaselineStore.IconHashesPath(newBaseline)));
            Assert.AreEqual(newBaseline, ReadLinkBaseline());
            var installs = JArray.Parse(File.ReadAllText(Path.Combine(_root, "workshop", WorkshopIdentityStore.InstalledFileName)));
            Assert.AreEqual(newBaseline, installs[0].Value<string>("BaselineFile"));

            foreach (var old in new[] { "color_presets", "unlock_sound_presets", "notification_style_presets", "showcase_presets", Path.Combine("workshop", "baselines") })
            {
                Assert.IsFalse(Directory.Exists(Path.Combine(_root, old)), old);
            }

            // The library then finds every item where the index says, keeping the ids.
            var store = new LibraryStore(_root);
            var reconcile = store.Reconcile();
            Assert.AreEqual(0, reconcile.DroppedIds.Count);
            Assert.AreEqual(0, reconcile.AddedIds.Count);
            CollectionAssert.AreEquivalent(expected.Keys.ToList(), store.Items.Select(item => item.Id).ToList());
        }

        [TestMethod]
        public void NameCollision_KeepsBoth_AndTheIndexFollowsTheMovedFile()
        {
            var colors = LibraryStore.FolderOf(LibraryItemKind.Colors);
            Write(Path.Combine(colors, "Neon.pacolors"), "already-here");
            Write(Path.Combine("color_presets", "Neon.pacolors"), "moved-in");
            WriteIndex(
                Item("here", LibraryItemKind.Colors, Path.Combine(colors, "Neon.pacolors")),
                Item("old", LibraryItemKind.Colors, Path.Combine("color_presets", "Neon.pacolors")));

            var result = LibraryFolderMigration.Run(_root);

            Assert.AreEqual(1, result.Renamed.Count);
            Assert.AreEqual("already-here", File.ReadAllText(Path.Combine(_root, colors, "Neon.pacolors")));
            Assert.AreEqual("moved-in", File.ReadAllText(Path.Combine(_root, colors, "Neon (2).pacolors")));
            var index = ReadIndex();
            Assert.AreEqual(Path.Combine(colors, "Neon.pacolors"), Find(index, "here").Value<string>("RelativePath"));
            Assert.AreEqual(Path.Combine(colors, "Neon (2).pacolors"), Find(index, "old").Value<string>("RelativePath"));
            Assert.AreEqual("Neon (2)", Find(index, "old").Value<string>("Name"), "a local item is named by its file");
        }

        [TestMethod]
        public void PartialRun_IsFinishedFromTheJournal()
        {
            // A crash after the journal and two of three moves, one of them to a free name.
            var colors = LibraryStore.FolderOf(LibraryItemKind.Colors);
            Write(Path.Combine(colors, "Neon.pacolors"), "already-here");
            Write(Path.Combine(colors, "Neon (2).pacolors"), "moved-neon");
            Write(Path.Combine(colors, "Sunset.pacolors"), "moved-sunset");
            Write(Path.Combine("color_presets", "Ocean.pacolors"), "not-moved");
            Write(
                Path.Combine(LibraryStore.DirectoryName, LibraryFolderMigration.JournalFileName),
                JsonConvert.SerializeObject(new
                {
                    SchemaVersion = 1,
                    Moves = new[]
                    {
                        new { From = Path.Combine("color_presets", "Neon.pacolors"), To = Path.Combine(colors, "Neon (2).pacolors") },
                        new { From = Path.Combine("color_presets", "Ocean.pacolors"), To = Path.Combine(colors, "Ocean.pacolors") },
                        new { From = Path.Combine("color_presets", "Sunset.pacolors"), To = Path.Combine(colors, "Sunset.pacolors") }
                    }
                }));
            WriteIndex(
                Item("neon", LibraryItemKind.Colors, Path.Combine("color_presets", "Neon.pacolors"), local: false),
                Item("ocean", LibraryItemKind.Colors, Path.Combine("color_presets", "Ocean.pacolors")),
                Item("sunset", LibraryItemKind.Colors, Path.Combine("color_presets", "Sunset.pacolors")));

            var result = LibraryFolderMigration.Run(_root);

            Assert.AreEqual(1, result.Moved.Count, "only the file left behind moves");
            Assert.AreEqual(3, result.RewrittenItems);
            var index = ReadIndexPaths();
            Assert.AreEqual(Path.Combine(colors, "Neon (2).pacolors"), index["neon"]);
            Assert.AreEqual(Path.Combine(colors, "Ocean.pacolors"), index["ocean"]);
            Assert.AreEqual(Path.Combine(colors, "Sunset.pacolors"), index["sunset"]);
            Assert.AreEqual("not-moved", File.ReadAllText(Path.Combine(_root, colors, "Ocean.pacolors")));
            Assert.IsFalse(File.Exists(JournalPath));
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "color_presets")));
        }

        [TestMethod]
        public void SecondRun_ChangesNothing()
        {
            Write(Path.Combine("color_presets", "Neon.pacolors"), "colors-1");
            WriteIndex(Item("c", LibraryItemKind.Colors, Path.Combine("color_presets", "Neon.pacolors")));
            Assert.IsTrue(LibraryFolderMigration.Run(_root).HasChanges);
            var indexAfterFirst = File.ReadAllText(IndexPath);

            var second = LibraryFolderMigration.Run(_root);

            Assert.IsFalse(second.HasChanges);
            Assert.AreEqual(indexAfterFirst, File.ReadAllText(IndexPath));
        }

        [TestMethod]
        public void EmptyFolders_AreRemoved_AndFoldersWithFilesAreKept()
        {
            Directory.CreateDirectory(Path.Combine(_root, "custom_templates", "global"));
            Directory.CreateDirectory(Path.Combine(_root, "showcase", "images"));
            Directory.CreateDirectory(Path.Combine(_root, "RecordingBuffer"));
            Directory.CreateDirectory(Path.Combine(_root, "color_presets"));
            Write(Path.Combine("fallback_icons", "locked.png"), "png");
            Write(Path.Combine("notification_style_presets", "readme.txt"), "kept");
            Directory.CreateDirectory(Path.Combine(_root, "notification_style_presets", "toast"));

            var result = LibraryFolderMigration.Run(_root);

            foreach (var removed in new[] { "custom_templates", "showcase", "RecordingBuffer", "color_presets", Path.Combine("notification_style_presets", "toast") })
            {
                Assert.IsFalse(Directory.Exists(Path.Combine(_root, removed)), removed);
                CollectionAssert.Contains(result.RemovedFolders, removed);
            }

            Assert.IsTrue(File.Exists(Path.Combine(_root, "fallback_icons", "locked.png")));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "notification_style_presets", "readme.txt")));
        }

        [TestMethod]
        public void RetiredInstalls_AreDeleted_AndAnUnmigratedFileIsKept()
        {
            Write(Path.Combine("workshop", WorkshopIdentityStore.RetiredInstalledFileName), "[]");
            Write(Path.Combine("workshop", WorkshopIdentityStore.InstalledFileName), "[]");
            Write(Path.Combine("workshop", "identity.json"), "{}");

            var result = LibraryFolderMigration.Run(_root);

            Assert.IsTrue(result.DeletedRetiredInstalls);
            Assert.IsFalse(File.Exists(Path.Combine(_root, "workshop", WorkshopIdentityStore.RetiredInstalledFileName)));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "workshop", WorkshopIdentityStore.InstalledFileName)));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "workshop", "identity.json")));
        }

        // ---- helpers ----------------------------------------------------------------------------

        private string IndexPath => Path.Combine(_root, LibraryStore.DirectoryName, LibraryStore.IndexFileName);

        private string LinksPath => Path.Combine(_root, LibraryStore.DirectoryName, GameLinkStore.FileName);

        private string JournalPath => Path.Combine(_root, LibraryStore.DirectoryName, LibraryFolderMigration.JournalFileName);

        private void Write(string path, string text)
        {
            var full = Path.IsPathRooted(path) ? path : Path.Combine(_root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }

        private object Item(string id, LibraryItemKind kind, string relativePath, bool local = true)
        {
            var full = Path.Combine(_root, relativePath);
            return new
            {
                Id = id,
                Kind = kind.ToString(),
                Name = Path.GetFileNameWithoutExtension(relativePath),
                RelativePath = relativePath,
                Origin = local ? "Local" : "Workshop",
                ContentHash = File.Exists(full) ? LibraryStore.HashFile(full) : null,
                AddedUtc = "2026-01-02T03:04:05.1234567Z"
            };
        }

        private void WriteIndex(params object[] items)
        {
            Write(IndexPath, JsonConvert.SerializeObject(new { SchemaVersion = 1, Items = items }, Formatting.Indented));
        }

        private void WriteLinks(string baselineFile)
        {
            Write(LinksPath, JsonConvert.SerializeObject(
                new
                {
                    SchemaVersion = 1,
                    Links = new Dictionary<string, object>
                    {
                        [LibraryTargetKeys.GameData(GameId)] = new { LibraryItemId = "ws:item", BaselineFile = baselineFile, PackageFile = GameId.ToString("N") + ".pa" }
                    }
                },
                Formatting.Indented));
        }

        private JArray ReadIndex()
        {
            return (JArray)JObject.Parse(File.ReadAllText(IndexPath))["Items"];
        }

        private static JObject Find(JArray items, string id)
        {
            return items.OfType<JObject>().Single(item => item.Value<string>("Id") == id);
        }

        private Dictionary<string, string> ReadIndexPaths()
        {
            return ReadIndex().OfType<JObject>().ToDictionary(item => item.Value<string>("Id"), item => item.Value<string>("RelativePath"));
        }

        private string ReadLinkBaseline()
        {
            var links = (JObject)JObject.Parse(File.ReadAllText(LinksPath))["Links"];
            return links[LibraryTargetKeys.GameData(GameId)].Value<string>("BaselineFile");
        }
    }
}

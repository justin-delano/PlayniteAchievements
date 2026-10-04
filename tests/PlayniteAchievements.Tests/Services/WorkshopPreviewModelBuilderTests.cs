using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class WorkshopPreviewModelBuilderTests
    {
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIW2NkYGD4DwABBAEAgh8sXQAAAABJRU5ErkJggg==";

        private string _tempDir;
        private WorkshopPreviewContext _context;

        [TestInitialize]
        public void Initialize()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            var diskImageService = new DiskImageService(logger: null, cacheRoot: Path.Combine(_tempDir, "images"));
            var styleStore = new NotificationStylePortableStore(new NotificationImageStore(diskImageService, logger: null), logger: null);
            var soundStore = new UnlockSoundPortableStore(Path.Combine(_tempDir, "userdata"));
            var colorStore = new ColorPackPortableStore();
            var gameStore = new GameCustomDataStore(Path.Combine(_tempDir, "store"));
            gameStore.AttachManagedCustomIconService(new ManagedCustomIconService(
                new DiskImageService(logger: null, cacheRoot: Path.Combine(_tempDir, "cache")),
                logger: null));

            _context = new WorkshopPreviewContext
            {
                ColorPackPortableStore = colorStore,
                NotificationStylePortableStore = styleStore,
                UnlockSoundPortableStore = soundStore,
                BundlePortableStore = new BundlePortableStore(styleStore, soundStore, colorStore),
                GameCustomDataStore = gameStore
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        [TestMethod]
        public void Colors_ReadsTheColorSet_AndDisposeRemovesScratch()
        {
            var persisted = new PersistedSettings();
            persisted.RarityColors = new RarityColorSettings { Common = "#123456" };
            var path = Path.Combine(_tempDir, "set.pacolors");
            _context.ColorPackPortableStore.Export(persisted, path);

            var model = (ColorsPreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.Colors, path, _context);

            Assert.AreEqual(WorkshopItemKind.Colors, model.Kind);
            Assert.AreEqual(path, model.PackagePath);
            Assert.AreEqual("#123456", model.Colors.RarityColors.Common);
            AssertScratchUnderLabel(model.ScratchDirectory);

            model.Dispose();
            Assert.IsFalse(Directory.Exists(model.ScratchDirectory));
            model.Dispose();
            Assert.IsTrue(model.IsDisposed);
        }

        [TestMethod]
        public void NotificationStyle_ReadsTheToastSurfaceAndItsTemplate()
        {
            var style = NotificationStyleSettings.CreateDefault();
            style.Toast.HeaderTexts.UnlockHeader = "Previewed";
            var path = Path.Combine(_tempDir, "look.panotif");
            _context.NotificationStylePortableStore.ExportSurfacePackage(isFrame: false, style, path, "<toast/>");

            using (var model = (NotificationStylePreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.NotificationStyle, path, _context))
            {
                Assert.IsFalse(model.IsFrame);
                Assert.AreEqual(WorkshopItemKind.NotificationStyle, model.Kind);
                Assert.AreEqual("<toast/>", model.TemplateXaml);
                Assert.AreEqual("Previewed", model.Style.Toast.HeaderTexts.UnlockHeader);
                Assert.IsTrue(model.Contents.HasToastStyle);
                Assert.IsTrue(model.Contents.HasToastTemplate);
            }
        }

        [TestMethod]
        public void ScreenshotFrame_ReadsTheFrameSurfaceAndItsTemplate()
        {
            var style = NotificationStyleSettings.CreateDefault();
            style.Frame.ShowUnlockTime = false;
            var path = Path.Combine(_tempDir, "look.paframe");
            _context.NotificationStylePortableStore.ExportSurfacePackage(isFrame: true, style, path, "<frame/>");

            using (var model = (NotificationStylePreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.ScreenshotFrame, path, _context))
            {
                Assert.IsTrue(model.IsFrame);
                Assert.AreEqual(WorkshopItemKind.ScreenshotFrame, model.Kind);
                Assert.AreEqual("<frame/>", model.TemplateXaml);
                Assert.IsFalse(model.Style.Frame.ShowUnlockTime);
                Assert.IsTrue(model.Contents.HasFrameStyle);
            }
        }

        [TestMethod]
        public void UnlockSounds_ListsSlotsInTierOrderWithTheirSizes()
        {
            var path = WriteSoundPack("sounds.pasounds", UnlockSoundTier.Rare, UnlockSoundTier.Common);

            using (var model = (UnlockSoundsPreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.UnlockSounds, path, _context))
            {
                CollectionAssert.AreEqual(
                    new[] { UnlockSoundTier.Common, UnlockSoundTier.Rare },
                    model.Slots.Select(slot => slot.Tier).ToArray());
                foreach (var slot in model.Slots)
                {
                    Assert.IsTrue(File.Exists(slot.FilePath));
                    StringAssert.StartsWith(slot.FilePath, model.ScratchDirectory);
                    Assert.AreEqual(64L, slot.SizeBytes);
                }
            }
        }

        [TestMethod]
        public void Bundle_ReadsEveryPartIntoChildFolders_AndDisposeRemovesThemAll()
        {
            var source = new PersistedSettings();
            source.RarityColors = new RarityColorSettings { Common = "#654321" };
            source.NotificationStyle.Toast.HeaderTexts.UnlockHeader = "Bundled";
            var wav = WriteWav(Path.Combine(_tempDir, "rare.wav"));
            var path = Path.Combine(_tempDir, "look.pabundle");
            _context.BundlePortableStore.Export(
                path,
                BundleParts.All,
                source,
                new[] { new ResolvedUnlockSound(UnlockSoundTier.Rare, UnlockSoundSource.Custom, wav) },
                toastTemplateXaml: "<x/>");

            var model = (BundlePreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.Bundle, path, _context);

            Assert.AreEqual(BundleParts.All, model.Parts);
            Assert.AreEqual("#654321", model.Colors.Colors.RarityColors.Common);
            Assert.AreEqual(UnlockSoundTier.Rare, model.Sounds.Slots.Single().Tier);
            Assert.IsFalse(model.Toast.IsFrame);
            Assert.AreEqual("<x/>", model.Toast.TemplateXaml);
            Assert.AreEqual("Bundled", model.Toast.Style.Toast.HeaderTexts.UnlockHeader);
            Assert.IsTrue(model.Frame.IsFrame);
            foreach (var child in new WorkshopPreviewModel[] { model.Colors, model.Sounds, model.Toast, model.Frame })
            {
                StringAssert.StartsWith(child.ScratchDirectory, model.ScratchDirectory + Path.DirectorySeparatorChar);
                Assert.IsTrue(Directory.Exists(child.ScratchDirectory));
            }

            model.Dispose();
            Assert.IsFalse(Directory.Exists(model.ScratchDirectory));
            Assert.IsTrue(model.Colors.IsDisposed && model.Sounds.IsDisposed && model.Toast.IsDisposed && model.Frame.IsDisposed);
        }

        [TestMethod]
        public void Bundle_WithoutAPart_LeavesThatChildNull()
        {
            var source = new PersistedSettings();
            var path = Path.Combine(_tempDir, "colors-only.pabundle");
            _context.BundlePortableStore.Export(path, BundleParts.Colors, source);

            using (var model = (BundlePreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.Bundle, path, _context))
            {
                Assert.AreEqual(BundleParts.Colors, model.Parts);
                Assert.IsNotNull(model.Colors);
                Assert.IsNull(model.Sounds);
                Assert.IsNull(model.Toast);
                Assert.IsNull(model.Frame);
            }
        }

        [TestMethod]
        public void ShowcasePage_JoinsBlocksToWidgets_AndDisposeRemovesExtractedImages()
        {
            var background = Path.Combine(_tempDir, "bg.png");
            File.WriteAllBytes(background, Convert.FromBase64String(PngBase64));
            var portable = new ShowcasePagePortableFile
            {
                Kind = ShowcasePagePortableFile.ShowcasePageKind,
                Version = ShowcasePagePortableStore.CurrentVersion,
                Page = new ShowcasePageSettings
                {
                    Name = "Shared page",
                    RowCount = 3,
                    ColumnCount = 0,
                    GridSize = 4,
                    Blocks = new List<ShowcaseBlockSettings>
                    {
                        new ShowcaseBlockSettings { Row = 0, Column = 0, RowSpan = 2, ColumnSpan = 1, WidgetInstanceId = "profile" },
                        new ShowcaseBlockSettings { Row = 0, Column = 1, RowSpan = 1, ColumnSpan = 3, WidgetInstanceId = "pie" },
                        new ShowcaseBlockSettings { Row = 2, Column = 0, WidgetInstanceId = "missing" }
                    }
                },
                Widgets = new List<ShowcaseWidgetInstanceSettings>
                {
                    new ShowcaseWidgetInstanceSettings
                    {
                        InstanceId = "profile",
                        Kind = ShowcaseWidgetKind.Profile,
                        Profile = new ShowcaseProfileSettings { BackgroundPath = "images/bg.png" }
                    },
                    new ShowcaseWidgetInstanceSettings { InstanceId = "pie", Kind = ShowcaseWidgetKind.Pie, CustomTitle = "My split" }
                }
            };
            portable.BundledImages["images/bg.png"] = background;
            var path = Path.Combine(_tempDir, "page.pashowcase");
            ShowcasePagePortableStore.Write(path, portable);

            var model = (ShowcasePagePreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.ShowcasePage, path, _context);

            Assert.AreEqual("Shared page", model.PageName);
            Assert.AreEqual(3, model.Rows);
            Assert.AreEqual(4, model.Columns, "an unset count falls back to the legacy grid size");
            Assert.AreEqual(2, model.Blocks.Count, "a block whose widget is not in the file is left out");
            Assert.AreEqual(ShowcaseWidgetKind.Profile, model.Blocks[0].WidgetKind);
            Assert.IsNull(model.Blocks[0].Title);
            Assert.AreEqual(2, model.Blocks[0].RowSpan);
            Assert.AreEqual(ShowcaseWidgetKind.Pie, model.Blocks[1].WidgetKind);
            Assert.AreEqual("My split", model.Blocks[1].Title);
            Assert.AreEqual(3, model.Blocks[1].ColumnSpan);
            var image = model.ImagePaths.Single();
            Assert.IsTrue(File.Exists(image));

            model.Dispose();
            Assert.IsFalse(File.Exists(image));
            Assert.IsFalse(Directory.Exists(model.ScratchDirectory));
        }

        [TestMethod]
        public void GameCustomData_WithoutSource_ListsThePackage()
        {
            var gameId = Guid.NewGuid();
            _context.GameCustomDataStore.Save(gameId, new GameCustomDataFile
            {
                PlayniteGameId = gameId,
                AchievementOverrides = new Dictionary<string, AchievementOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ach_one"] = new AchievementOverride { DisplayName = "Renamed", Note = "a note" }
                },
                CustomAchievements = new List<CustomAchievementDefinition>
                {
                    new CustomAchievementDefinition { Id = "bonus", DisplayName = "Bonus" }
                }
            });
            var path = Path.Combine(_tempDir, "game.pa");
            _context.GameCustomDataStore.ExportPortablePackage(gameId, path);

            using (var model = (GameCustomDataPreviewModel)WorkshopPreviewModelBuilder.Build(WorkshopItemKind.GameCustomData, path, _context))
            {
                Assert.AreEqual(GameCustomDataPackageShape.Manifest, model.Package.Shape);
                Assert.IsTrue(model.Diff.IsPackageOnly);
                Assert.AreEqual(2, model.Diff.Rows.Count);
                Assert.IsTrue(model.Diff.Rows.All(row => row.Before == null));
                Assert.AreEqual(1, model.Diff.CustomAchievementCount);
                Assert.AreEqual(1, model.Diff.NotesCount);
            }
        }

        [TestMethod]
        public void Failure_DeletesTheScratchDirectory()
        {
            var path = Path.Combine(_tempDir, "broken.pacolors");
            File.WriteAllText(path, "not a package");
            var root = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", WorkshopPreviewModelBuilder.ScratchFolderLabel);
            var before = ListDirectories(root);

            Assert.ThrowsException<InvalidOperationException>(
                () => WorkshopPreviewModelBuilder.Build(WorkshopItemKind.Colors, path, _context));

            CollectionAssert.AreEquivalent(before, ListDirectories(root));
        }

        private string WriteSoundPack(string fileName, params UnlockSoundTier[] tiers)
        {
            var resolved = tiers
                .Select(tier => new ResolvedUnlockSound(
                    tier,
                    UnlockSoundSource.Custom,
                    WriteWav(Path.Combine(_tempDir, tier + ".wav"))))
                .ToList();
            var path = Path.Combine(_tempDir, fileName);
            _context.UnlockSoundPortableStore.Export(resolved, path);
            return path;
        }

        private static string WriteWav(string path)
        {
            var bytes = new byte[64];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void AssertScratchUnderLabel(string scratch)
        {
            StringAssert.StartsWith(
                scratch,
                Path.Combine(Path.GetTempPath(), "PlayniteAchievements", WorkshopPreviewModelBuilder.ScratchFolderLabel));
            Assert.IsTrue(Directory.Exists(scratch));
        }

        private static List<string> ListDirectories(string root)
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }
    }
}

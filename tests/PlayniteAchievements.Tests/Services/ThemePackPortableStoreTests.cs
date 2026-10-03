using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class ThemePackPortableStoreTests
    {
        [TestMethod]
        public async Task Export_AllParts_ThenImport_AppliesEachPartToFreshSettings()
        {
            await WithTempAsync(async tempDir =>
            {
                var store = CreateStore(tempDir, out var soundStore);
                var source = new PersistedSettings();
                source.RarityColors = new RarityColorSettings { Common = "#112233", UltraRare = "#AABBCCDD" };
                source.ProviderColorOverrides = new Dictionary<string, string> { ["Steam"] = "#010203" };
                source.ResourceOverrides = new Dictionary<string, ResourceOverrideSetting>
                {
                    ["PlayAch.Brush.Text"] = new ResourceOverrideSetting { Mode = ResourceOverrideMode.Custom, CustomValue = "#FF0000" },
                    ["PlayAch.FontSize.Body"] = new ResourceOverrideSetting { Mode = ResourceOverrideMode.Custom, CustomValue = "15" },
                    ["Not.A.Known.Key"] = new ResourceOverrideSetting { Mode = ResourceOverrideMode.Custom, CustomValue = "#000000" }
                };
                source.NotificationStyle.Toast.ShowHeader = false;
                source.NotificationStyle.Toast.HeaderTexts.UnlockHeader = "Bundled!";
                source.NotificationStyle.Frame.ShowUnlockTime = false;

                var rareSound = WriteWav(Path.Combine(tempDir, "rare.wav"));
                var resolved = new[] { new ResolvedUnlockSound(UnlockSoundTier.Rare, UnlockSoundSource.Custom, rareSound) };

                var packagePath = Path.Combine(tempDir, "look.patheme");
                store.Export(packagePath, ThemePackParts.All, source, resolved, toastTemplateXaml: "<x/>");

                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var names = archive.Entries.Select(entry => entry.FullName).ToList();
                    CollectionAssert.Contains(names, ThemePackPortableStore.ManifestEntryName);
                    CollectionAssert.Contains(names, ThemePackPortableStore.ColorsEntryName);
                    CollectionAssert.Contains(names, ThemePackPortableStore.SoundsEntryName);
                    CollectionAssert.Contains(names, ThemePackPortableStore.ToastEntryName);
                    CollectionAssert.Contains(names, ThemePackPortableStore.FrameEntryName);
                    Assert.AreEqual(5, names.Count, "the manifest names parts; each part is an embedded standalone package");
                }

                Assert.AreEqual(ThemePackParts.All, store.Inspect(packagePath));

                var target = new PersistedSettings();
                target.UnlockSounds.Common = @"C:\keep\common.wav";
                string installedToastXaml = null;
                var applied = await store.ImportAsync(
                    packagePath,
                    ThemePackParts.All,
                    target,
                    (isFrame, xaml) => { if (!isFrame) installedToastXaml = xaml; },
                    CancellationToken.None);

                Assert.AreEqual(ThemePackParts.All, applied);
                Assert.AreEqual("#112233", target.RarityColors.Common);
                Assert.AreEqual("#AABBCCDD", target.RarityColors.UltraRare);
                Assert.AreEqual("#010203", target.ProviderColorOverrides["Steam"]);
                Assert.AreEqual("#FF0000", target.ResourceOverrides["PlayAch.Brush.Text"].CustomValue);
                Assert.AreEqual("15", target.ResourceOverrides["PlayAch.FontSize.Body"].CustomValue);
                Assert.IsFalse(target.ResourceOverrides.ContainsKey("Not.A.Known.Key"), "unknown resource keys are dropped");
                Assert.IsFalse(target.NotificationStyle.Toast.ShowHeader);
                Assert.AreEqual("Bundled!", target.NotificationStyle.Toast.HeaderTexts.UnlockHeader);
                Assert.IsFalse(target.NotificationStyle.Frame.ShowUnlockTime);
                Assert.AreEqual("<x/>", installedToastXaml);
                Assert.AreEqual(@"C:\keep\common.wav", target.UnlockSounds.Common, "tiers the pack does not carry are untouched");
                Assert.IsTrue(soundStore.IsManagedPath(target.UnlockSounds.Rare));
                Assert.IsTrue(File.Exists(target.UnlockSounds.Rare));
            });
        }

        [TestMethod]
        public async Task ExtractParts_WritesOnlyTheSelectedCarriedPartsAsStandalonePackages()
        {
            await WithTempAsync(async tempDir =>
            {
                var store = CreateStore(tempDir, out _);
                var source = new PersistedSettings();
                source.RarityColors = new RarityColorSettings { Common = "#445566" };
                var packagePath = Path.Combine(tempDir, "look.patheme");
                store.Export(packagePath, ThemePackParts.Colors | ThemePackParts.Toast | ThemePackParts.Frame, source, null, toastTemplateXaml: null);

                var outDir = Path.Combine(tempDir, "parts");
                var extracted = store.ExtractParts(packagePath, ThemePackParts.Colors | ThemePackParts.Sounds | ThemePackParts.Toast, outDir);

                CollectionAssert.AreEquivalent(
                    new[] { ThemePackParts.Colors, ThemePackParts.Toast },
                    extracted.Keys.ToArray(),
                    "sounds were not in the bundle and the frame was not selected");
                Assert.AreEqual(Path.Combine(outDir, "colors" + ColorPackPortableStore.PackageFileExtension), extracted[ThemePackParts.Colors]);
                Assert.AreEqual(Path.Combine(outDir, "toast" + NotificationStylePortableStore.ToastPackageFileExtension), extracted[ThemePackParts.Toast]);
                Assert.AreEqual("#445566", new ColorPackPortableStore().Read(extracted[ThemePackParts.Colors]).RarityColors.Common, "each part is a valid standalone package");
                await Task.CompletedTask;
            });
        }

        [TestMethod]
        public async Task Import_AppliesOnlySelectedParts()
        {
            await WithTempAsync(async tempDir =>
            {
                var store = CreateStore(tempDir, out _);
                var source = new PersistedSettings();
                source.RarityColors = new RarityColorSettings { Common = "#112233" };
                source.NotificationStyle.Toast.HeaderTexts.UnlockHeader = "Bundled!";

                var packagePath = Path.Combine(tempDir, "look.patheme");
                store.Export(packagePath, ThemePackParts.Colors | ThemePackParts.Toast, source);

                var target = new PersistedSettings();
                var originalHeader = target.NotificationStyle.Toast.HeaderTexts.UnlockHeader;
                var applied = await store.ImportAsync(packagePath, ThemePackParts.Colors, target, null, CancellationToken.None);

                Assert.AreEqual(ThemePackParts.Colors, applied);
                Assert.AreEqual("#112233", target.RarityColors.Common);
                Assert.AreEqual(originalHeader, target.NotificationStyle.Toast.HeaderTexts.UnlockHeader);
            });
        }

        [TestMethod]
        public void Export_SoundsPartWithNothingCustom_IsDroppedNotFatal()
        {
            WithTemp(tempDir =>
            {
                var store = CreateStore(tempDir, out _);
                var packagePath = Path.Combine(tempDir, "look.patheme");
                var onlyDefaults = new[] { new ResolvedUnlockSound(UnlockSoundTier.Common, UnlockSoundSource.Default, WriteWav(Path.Combine(tempDir, "d.wav"))) };

                store.Export(packagePath, ThemePackParts.Colors | ThemePackParts.Sounds, new PersistedSettings(), onlyDefaults);

                Assert.AreEqual(ThemePackParts.Colors, store.Inspect(packagePath));
            });
        }

        [TestMethod]
        public void Inspect_RejectsForeignKind_DeclaredPartWithoutPayload_AndNonZip()
        {
            WithTemp(tempDir =>
            {
                var store = CreateStore(tempDir, out _);

                var wrongKind = Path.Combine(tempDir, "kind.patheme");
                WriteManifestOnly(wrongKind, new ThemePackFile { Kind = "Other", Version = 1 });
                AssertThrows(() => store.Inspect(wrongKind), "not a Playnite Achievements theme");

                var missingPart = Path.Combine(tempDir, "missing.patheme");
                WriteManifestOnly(missingPart, new ThemePackFile
                {
                    Kind = ThemePackFile.ThemeKind,
                    Version = 1,
                    Parts = new List<string> { "Colors" }
                });
                AssertThrows(() => store.Inspect(missingPart), "missing its");

                var newer = Path.Combine(tempDir, "newer.patheme");
                WriteManifestOnly(newer, new ThemePackFile { Kind = ThemePackFile.ThemeKind, Version = ThemePackPortableStore.CurrentVersion + 1 });
                AssertThrows(() => store.Inspect(newer), "newer version");

                var notZip = Path.Combine(tempDir, "text.patheme");
                File.WriteAllText(notZip, "{}");
                AssertThrows(() => store.Inspect(notZip), "not a theme bundle");
            });
        }

        private static ThemePackPortableStore CreateStore(string tempDir, out UnlockSoundPortableStore soundStore)
        {
            var diskImageService = new DiskImageService(logger: null, cacheRoot: Path.Combine(tempDir, "images"));
            var imageStore = new NotificationImageStore(diskImageService, logger: null);
            var styleStore = new NotificationStylePortableStore(imageStore, logger: null);
            soundStore = new UnlockSoundPortableStore(Path.Combine(tempDir, "userdata"));
            return new ThemePackPortableStore(styleStore, soundStore, new ColorPackPortableStore());
        }

        private static void WriteManifestOnly(string path, ThemePackFile manifest)
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry(ThemePackPortableStore.ManifestEntryName).Open()))
            {
                writer.Write(JsonConvert.SerializeObject(manifest));
            }
        }

        private static string WriteWav(string path)
        {
            var bytes = new byte[64];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void AssertThrows(Action action, string expectedFragment)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, expectedFragment);
                return;
            }

            Assert.Fail($"Expected an InvalidOperationException containing '{expectedFragment}'.");
        }

        private static void WithTemp(Action<string> body)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                body(tempDir);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                await body(tempDir);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

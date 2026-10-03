using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class UnlockSoundPortableStoreTests
    {
        [TestMethod]
        public void Export_BundlesCustomAndThemeSounds_SkipsBundledDefaults()
        {
            WithTemp(tempDir =>
            {
                var custom = WriteWav(Path.Combine(tempDir, "mine.wav"));
                var theme = WriteWav(Path.Combine(tempDir, "theme.wav"));
                var bundled = WriteWav(Path.Combine(tempDir, "default.wav"));
                var resolved = new[]
                {
                    new ResolvedUnlockSound(UnlockSoundTier.Common, UnlockSoundSource.Custom, custom),
                    new ResolvedUnlockSound(UnlockSoundTier.Rare, UnlockSoundSource.Theme, theme),
                    new ResolvedUnlockSound(UnlockSoundTier.Capstone, UnlockSoundSource.Default, bundled),
                    new ResolvedUnlockSound(UnlockSoundTier.Hidden, UnlockSoundSource.None, null)
                };

                var packagePath = Path.Combine(tempDir, "pack.pasounds");
                new UnlockSoundPortableStore(Path.Combine(tempDir, "userdata")).Export(resolved, packagePath);

                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var names = archive.Entries.Select(entry => entry.FullName).ToList();
                    CollectionAssert.Contains(names, UnlockSoundPortableStore.ManifestEntryName);
                    CollectionAssert.Contains(names, "sounds/common.wav");
                    CollectionAssert.Contains(names, "sounds/rare.wav");
                    Assert.IsFalse(names.Any(name => name.Contains("capstone")), "bundled defaults travel with every install");
                    Assert.AreEqual(3, names.Count);

                    var manifest = ReadManifest(archive);
                    Assert.AreEqual(UnlockSoundPackFile.UnlockSoundsKind, manifest.Kind);
                    Assert.AreEqual(UnlockSoundPortableStore.CurrentVersion, manifest.Version);
                    Assert.AreEqual("sounds/common.wav", manifest.Slots["Common"]);
                    Assert.AreEqual("sounds/rare.wav", manifest.Slots["Rare"]);
                }
            });
        }

        [TestMethod]
        public void Import_CopiesIntoManagedStorage_AndLeavesUncarriedTiersAlone()
        {
            WithTemp(tempDir =>
            {
                var userData = Path.Combine(tempDir, "userdata");
                var store = new UnlockSoundPortableStore(userData);
                var packagePath = Path.Combine(tempDir, "pack.pasounds");
                store.Export(
                    new[] { new ResolvedUnlockSound(UnlockSoundTier.Rare, UnlockSoundSource.Custom, WriteWav(Path.Combine(tempDir, "r.wav"))) },
                    packagePath);

                var settings = new UnlockSoundSettings { Common = @"C:\keep\me.wav" };
                var imported = store.Import(packagePath, settings);

                CollectionAssert.AreEqual(new[] { UnlockSoundTier.Rare }, imported.ToArray());
                Assert.AreEqual(@"C:\keep\me.wav", settings.Common);
                Assert.IsTrue(store.IsManagedPath(settings.Rare), settings.Rare);
                Assert.IsTrue(File.Exists(settings.Rare));
                Assert.AreEqual("rare.wav", Path.GetFileName(settings.Rare));
                Assert.IsNull(settings.Capstone);
            });
        }

        [TestMethod]
        public void PruneUnreferenced_RemovesPackFoldersNoSlotPointsInto()
        {
            WithTemp(tempDir =>
            {
                var store = new UnlockSoundPortableStore(Path.Combine(tempDir, "userdata"));
                var packagePath = Path.Combine(tempDir, "pack.pasounds");
                store.Export(
                    new[] { new ResolvedUnlockSound(UnlockSoundTier.Common, UnlockSoundSource.Custom, WriteWav(Path.Combine(tempDir, "c.wav"))) },
                    packagePath);

                var settings = new UnlockSoundSettings();
                store.Import(packagePath, settings);
                var first = Path.GetDirectoryName(settings.Common);
                store.Import(packagePath, settings);
                var second = Path.GetDirectoryName(settings.Common);
                Assert.AreNotEqual(first, second);

                store.PruneUnreferenced(settings);

                Assert.IsFalse(Directory.Exists(first), "the replaced pack folder should be removed");
                Assert.IsTrue(Directory.Exists(second));
            });
        }

        [TestMethod]
        public void Import_RejectsTraversalAndNonAudioContent()
        {
            WithTemp(tempDir =>
            {
                var store = new UnlockSoundPortableStore(Path.Combine(tempDir, "userdata"));

                var traversal = Path.Combine(tempDir, "traversal.pasounds");
                WritePackage(traversal, new Dictionary<string, string> { ["Common"] = "sounds/../common.wav" },
                    new Dictionary<string, byte[]> { ["sounds/../common.wav"] = WavBytes() });
                AssertThrows(() => store.Import(traversal, new UnlockSoundSettings()), "Invalid bundled sound path");

                var fake = Path.Combine(tempDir, "fake.pasounds");
                WritePackage(fake, new Dictionary<string, string> { ["Common"] = "sounds/common.wav" },
                    new Dictionary<string, byte[]> { ["sounds/common.wav"] = Encoding.ASCII.GetBytes("MZ this is not audio") });
                AssertThrows(() => store.Import(fake, new UnlockSoundSettings()), "not a valid .wav");
                Assert.IsFalse(Directory.Exists(store.ManagedRoot) && Directory.GetDirectories(store.ManagedRoot).Length > 0,
                    "a failed import leaves no managed folder behind");

                var wrongKind = Path.Combine(tempDir, "kind.pasounds");
                WritePackage(wrongKind, new Dictionary<string, string>(), new Dictionary<string, byte[]>(), kind: "Other");
                AssertThrows(() => store.Import(wrongKind, new UnlockSoundSettings()), "not a Playnite Achievements sound pack");

                var notZip = Path.Combine(tempDir, "text.pasounds");
                File.WriteAllText(notZip, "{}");
                AssertThrows(() => store.Import(notZip, new UnlockSoundSettings()), "not a sound pack");
            });
        }

        [TestMethod]
        public void Export_WithNothingCustom_Throws()
        {
            WithTemp(tempDir =>
            {
                var store = new UnlockSoundPortableStore(Path.Combine(tempDir, "userdata"));
                AssertThrows(
                    () => store.Export(new[] { new ResolvedUnlockSound(UnlockSoundTier.Common, UnlockSoundSource.Default, WriteWav(Path.Combine(tempDir, "d.wav"))) },
                        Path.Combine(tempDir, "x.pasounds")),
                    "nothing to export");
            });
        }

        private static void WritePackage(
            string path,
            Dictionary<string, string> slots,
            Dictionary<string, byte[]> files,
            string kind = UnlockSoundPackFile.UnlockSoundsKind)
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifest = new UnlockSoundPackFile { Kind = kind, Version = UnlockSoundPortableStore.CurrentVersion, Slots = slots };
                using (var writer = new StreamWriter(archive.CreateEntry(UnlockSoundPortableStore.ManifestEntryName).Open()))
                {
                    writer.Write(JsonConvert.SerializeObject(manifest));
                }

                foreach (var pair in files)
                {
                    using (var stream = archive.CreateEntry(pair.Key).Open())
                    {
                        stream.Write(pair.Value, 0, pair.Value.Length);
                    }
                }
            }
        }

        private static UnlockSoundPackFile ReadManifest(ZipArchive archive)
        {
            using (var reader = new StreamReader(archive.GetEntry(UnlockSoundPortableStore.ManifestEntryName).Open()))
            {
                return JsonConvert.DeserializeObject<UnlockSoundPackFile>(reader.ReadToEnd());
            }
        }

        private static byte[] WavBytes()
        {
            // RIFF....WAVE plus a little padding: enough header for the content check.
            var bytes = new byte[64];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
            return bytes;
        }

        private static string WriteWav(string path)
        {
            File.WriteAllBytes(path, WavBytes());
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
    }
}

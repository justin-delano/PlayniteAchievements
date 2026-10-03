using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class ColorPackPortableStoreTests
    {
        [TestMethod]
        public void Export_ThenImport_RoundTripsColorsAndDropsUnknownResourceKeys()
        {
            WithTemp(tempDir =>
            {
                var store = new ColorPackPortableStore();
                var source = new PersistedSettings();
                source.RarityColors = new RarityColorSettings { Rare = "#ABCDEF", TrophyGold = "#FFD700" };
                source.ProviderColorOverrides = new Dictionary<string, string> { ["GOG"] = "#86328A" };
                source.ResourceOverrides = new Dictionary<string, ResourceOverrideSetting>
                {
                    ["PlayAch.FontFamily.Body"] = new ResourceOverrideSetting { Mode = ResourceOverrideMode.Custom, CustomValue = "Segoe UI" },
                    ["Made.Up"] = new ResourceOverrideSetting { Mode = ResourceOverrideMode.Custom, CustomValue = "#000000" }
                };

                var path = Path.Combine(tempDir, "set.pacolors");
                store.Export(source, path);

                using (var archive = ZipFile.OpenRead(path))
                {
                    Assert.IsNotNull(archive.GetEntry(ColorPackPortableStore.ManifestEntryName));
                    Assert.AreEqual(1, archive.Entries.Count);
                }

                var target = new PersistedSettings();
                store.Import(path, target);

                Assert.AreEqual("#ABCDEF", target.RarityColors.Rare);
                Assert.AreEqual("#FFD700", target.RarityColors.TrophyGold);
                Assert.AreEqual("#86328A", target.ProviderColorOverrides["GOG"]);
                Assert.AreEqual("Segoe UI", target.ResourceOverrides["PlayAch.FontFamily.Body"].CustomValue);
                Assert.IsFalse(target.ResourceOverrides.ContainsKey("Made.Up"));
            });
        }

        [TestMethod]
        public void Validate_RejectsNonHexValues_AndLeavesSettingsUntouched()
        {
            var target = new PersistedSettings();
            var before = target.RarityColors.Common;

            AssertThrows(() => ColorPackPortableStore.Apply(
                new ColorPackFile { Kind = ColorPackFile.ColorsKind, RarityColors = new RarityColorSettings { Common = "red" } },
                target), "not a #RRGGBB");
            Assert.AreEqual(before, target.RarityColors.Common);

            AssertThrows(() => ColorPackPortableStore.Apply(
                new ColorPackFile
                {
                    Kind = ColorPackFile.ColorsKind,
                    RarityColors = RarityColorSettings.CreateDefault(),
                    ProviderColorOverrides = new Dictionary<string, string> { ["Steam"] = "javascript:alert(1)" }
                },
                target), "provider color");

            AssertThrows(() => ColorPackPortableStore.Apply(
                new ColorPackFile
                {
                    Kind = ColorPackFile.ColorsKind,
                    RarityColors = RarityColorSettings.CreateDefault(),
                    ResourceOverrides = new Dictionary<string, ResourceOverrideSetting>
                    {
                        ["PlayAch.FontSize.Body"] = new ResourceOverrideSetting { Mode = ResourceOverrideMode.Custom, CustomValue = "-4" }
                    }
                },
                target), "font size");
        }

        [TestMethod]
        public void Read_RejectsForeignKind_NewerVersion_AndNonZip()
        {
            WithTemp(tempDir =>
            {
                var store = new ColorPackPortableStore();

                var wrongKind = Path.Combine(tempDir, "kind.pacolors");
                WriteManifest(wrongKind, new ColorPackFile { Kind = "Other", Version = 1 });
                AssertThrows(() => store.Read(wrongKind), "not a Playnite Achievements color pack");

                var newer = Path.Combine(tempDir, "newer.pacolors");
                WriteManifest(newer, new ColorPackFile { Kind = ColorPackFile.ColorsKind, Version = ColorPackPortableStore.CurrentVersion + 1 });
                AssertThrows(() => store.Read(newer), "newer version");

                var notZip = Path.Combine(tempDir, "text.pacolors");
                File.WriteAllText(notZip, "{}");
                AssertThrows(() => store.Read(notZip), "not a color pack");
            });
        }

        private static void WriteManifest(string path, ColorPackFile manifest)
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry(ColorPackPortableStore.ManifestEntryName).Open()))
            {
                writer.Write(JsonConvert.SerializeObject(manifest));
            }
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

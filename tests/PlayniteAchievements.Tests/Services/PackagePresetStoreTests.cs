using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Workshop;

namespace PlayniteAchievements.Tests.Services
{
    [TestClass]
    [TestCategory("Services")]
    public class PackagePresetStoreTests
    {
        private string _root;
        private string _packages;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", "presets-" + Guid.NewGuid().ToString("N"));
            _packages = Path.Combine(_root, "packages");
            Directory.CreateDirectory(_packages);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
            }
        }

        private PackagePresetStore CreateStore(Action<string> validate = null)
        {
            return new PackagePresetStore(_root, "test_presets", ".patest", validate);
        }

        private string WritePackage(string name, string content = "package")
        {
            var path = Path.Combine(_packages, name);
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void List_IsEmptyBeforeAnythingIsSaved()
        {
            var store = CreateStore();

            Assert.AreEqual(0, store.List().Count);
            Assert.AreEqual(0, store.Count());
            Assert.IsFalse(store.Exists("Anything"));
        }

        [TestMethod]
        public void SaveFrom_CopiesThePackageUnderTheSanitizedName()
        {
            var store = CreateStore();
            var source = WritePackage("neon.patest", "neon-bytes");

            var saved = store.SaveFrom("  Neon: Glow? ", source);

            Assert.AreEqual("Neon Glow", saved.Name);
            Assert.AreEqual(Path.Combine(_root, "test_presets", "Neon Glow.patest"), saved.FilePath);
            Assert.AreEqual("neon-bytes", File.ReadAllText(saved.FilePath));
            Assert.IsTrue(store.Exists("Neon Glow"));
            Assert.AreEqual("Neon Glow", store.Find("Neon Glow").Name);
        }

        [TestMethod]
        public void SaveFrom_ReplacesAPresetOfTheSameName()
        {
            var store = CreateStore();
            store.SaveFrom("Look", WritePackage("a.patest", "first"));

            store.SaveFrom("Look", WritePackage("b.patest", "second"));

            Assert.AreEqual(1, store.Count());
            Assert.AreEqual("second", File.ReadAllText(store.Find("Look").FilePath));
        }

        [TestMethod]
        public void SaveFrom_RunsTheValidatorBeforeCopying()
        {
            var store = CreateStore(path => throw new InvalidOperationException("bad package"));
            var source = WritePackage("bad.patest");

            var ex = Assert.ThrowsException<InvalidOperationException>(() => store.SaveFrom("Bad", source));

            Assert.AreEqual("bad package", ex.Message);
            Assert.AreEqual(0, store.Count());
        }

        [TestMethod]
        public void SaveFrom_RejectsAnEmptyNameAndAMissingFile()
        {
            var store = CreateStore();

            Assert.ThrowsException<ArgumentException>(() => store.SaveFrom("???", WritePackage("x.patest")));
            Assert.ThrowsException<FileNotFoundException>(() => store.SaveFrom("Fine", Path.Combine(_packages, "missing.patest")));
        }

        [TestMethod]
        public void UniqueName_AppendsACounterWhenTheNameIsTaken()
        {
            var store = CreateStore();
            store.SaveFrom("Look", WritePackage("a.patest"));

            Assert.AreEqual("Look (2)", store.UniqueName("Look"));

            store.SaveFrom("Look (2)", WritePackage("b.patest"));

            Assert.AreEqual("Look (3)", store.UniqueName("Look"));
            Assert.AreEqual("Other", store.UniqueName("Other"));
            Assert.AreEqual("Preset", store.UniqueName("   "));
        }

        [TestMethod]
        public void UniqueName_KeepsTheCounterWithinTheLengthCap()
        {
            var store = CreateStore();
            var longName = new string('a', PackagePresetStore.MaxNameLength);
            store.SaveFrom(longName, WritePackage("a.patest"));

            var unique = store.UniqueName(longName);

            Assert.IsTrue(unique.Length <= PackagePresetStore.MaxNameLength, unique);
            Assert.IsTrue(unique.EndsWith(" (2)", StringComparison.Ordinal), unique);
        }

        [TestMethod]
        public void Save_WritesThroughTheCallbackAtTheDestination()
        {
            var store = CreateStore();

            var saved = store.Save("Exported", path => File.WriteAllText(path, "from-settings"));

            Assert.AreEqual("from-settings", File.ReadAllText(saved.FilePath));
            CollectionAssert.AreEqual(new[] { "Exported" }, store.List().Select(p => p.Name).ToArray());
        }

        [TestMethod]
        public void List_IsSortedByNameAndIgnoresOtherExtensions()
        {
            var store = CreateStore();
            store.SaveFrom("zeta", WritePackage("1.patest"));
            store.SaveFrom("Alpha", WritePackage("2.patest"));
            File.WriteAllText(Path.Combine(store.DirectoryPath, "stray.txt"), "ignored");

            CollectionAssert.AreEqual(new[] { "Alpha", "zeta" }, store.List().Select(p => p.Name).ToArray());
        }

        [TestMethod]
        public void Delete_RemovesTheFileAndToleratesARepeat()
        {
            var store = CreateStore();
            var saved = store.SaveFrom("Gone", WritePackage("a.patest"));

            store.Delete(saved);
            store.Delete(saved);

            Assert.IsFalse(File.Exists(saved.FilePath));
            Assert.AreEqual(0, store.Count());
        }

        [TestMethod]
        public void SaveFrom_RefusesANewPresetPastTheCap_ButStillReplacesAnExistingOne()
        {
            var store = CreateStore();
            var source = WritePackage("a.patest");
            for (var i = 0; i < PackagePresetStore.MaxPresetCount; i++)
            {
                store.SaveFrom("Preset " + i, source);
            }

            Assert.ThrowsException<InvalidOperationException>(() => store.SaveFrom("One more", source));
            store.SaveFrom("Preset 0", WritePackage("b.patest", "replaced"));

            Assert.AreEqual(PackagePresetStore.MaxPresetCount, store.Count());
            Assert.AreEqual("replaced", File.ReadAllText(store.Find("Preset 0").FilePath));
        }

        [TestMethod]
        public void SanitizeName_StripsInvalidCharactersAndCapsLength()
        {
            Assert.AreEqual(string.Empty, PackagePresetStore.SanitizeName(null));
            Assert.AreEqual(string.Empty, PackagePresetStore.SanitizeName("<>:\"/\\|?*"));
            Assert.AreEqual("AB", PackagePresetStore.SanitizeName(" A/B "));
            Assert.AreEqual(PackagePresetStore.MaxNameLength, PackagePresetStore.SanitizeName(new string('x', 200)).Length);
        }
    }
}

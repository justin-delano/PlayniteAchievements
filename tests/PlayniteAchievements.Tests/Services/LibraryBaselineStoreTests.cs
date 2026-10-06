using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Services.Library;
using System;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class LibraryBaselineStoreTests
    {
        private string _libraryDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _libraryDirectory = Path.Combine(Path.GetTempPath(), "PlayAchBaselines_" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_libraryDirectory, recursive: true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void Write_CreatesANewFileEveryTime()
        {
            var store = new LibraryBaselineStore(_libraryDirectory);
            var projection = JToken.Parse("{ a: 1 }");

            var first = store.Write(projection);
            var second = store.Write(projection);

            Assert.AreNotEqual(first.File, second.File);
            Assert.AreEqual(first.Hash, second.Hash);
            Assert.AreEqual(2, Directory.GetFiles(store.Directory).Length);
            Assert.IsFalse(Path.IsPathRooted(first.File), "links store the file name");
        }

        [TestMethod]
        public void Read_ReturnsTheWrittenProjection()
        {
            var store = new LibraryBaselineStore(_libraryDirectory);
            var projection = JToken.Parse("{ colors: { accent: '#ff0000' }, list: [1, 2] }");

            var baseline = store.Write(projection);

            Assert.IsTrue(JToken.DeepEquals(projection, store.Read(baseline.File)));
            Assert.AreEqual(LibraryBaselineStore.HashProjection(store.Read(baseline.File)), baseline.Hash);
            Assert.IsNull(store.Read("missing.json"));
            Assert.IsNull(store.Read(null));
        }

        [TestMethod]
        public void HashProjection_DependsOnContent()
        {
            Assert.AreEqual(
                LibraryBaselineStore.HashProjection(JToken.Parse("{ a: 1 }")),
                LibraryBaselineStore.HashProjection(JToken.Parse("{\n  \"a\": 1\n}")));
            Assert.AreNotEqual(
                LibraryBaselineStore.HashProjection(JToken.Parse("{ a: 1 }")),
                LibraryBaselineStore.HashProjection(JToken.Parse("{ a: 2 }")));
        }

        [TestMethod]
        public void Sweep_RemovesOnlyUnreferencedFiles()
        {
            var store = new LibraryBaselineStore(_libraryDirectory);
            var kept = store.Write(JToken.Parse("{ a: 1 }"));
            var keptByPath = store.Write(JToken.Parse("{ a: 2 }"));
            var dropped = store.Write(JToken.Parse("{ a: 3 }"));
            var outside = Path.Combine(_libraryDirectory, "elsewhere.json");
            File.WriteAllText(outside, "{}");

            var deleted = store.Sweep(new[] { kept.File, store.Resolve(keptByPath.File), null, outside });

            Assert.AreEqual(1, deleted);
            Assert.IsNotNull(store.Read(kept.File));
            Assert.IsNotNull(store.Read(keptByPath.File));
            Assert.IsNull(store.Read(dropped.File));
            Assert.IsTrue(File.Exists(outside), "files outside the baselines folder are never touched");
        }

        [TestMethod]
        public void Sweep_WithoutFolder_DoesNothing()
        {
            Assert.AreEqual(0, new LibraryBaselineStore(_libraryDirectory).Sweep(Enumerable.Empty<string>()));
        }

        [TestMethod]
        public void Resolve_KeepsRootedPaths()
        {
            var store = new LibraryBaselineStore(_libraryDirectory);
            var rooted = Path.Combine(Path.GetTempPath(), "workshop", "baselines", "x.json");

            Assert.AreEqual(rooted, store.Resolve(rooted));
            Assert.AreEqual(Path.Combine(store.Directory, "x.json"), store.Resolve("x.json"));
        }
    }
}

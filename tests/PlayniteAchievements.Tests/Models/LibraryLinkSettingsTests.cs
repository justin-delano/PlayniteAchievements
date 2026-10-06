using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models.Tests
{
    [TestClass]
    public class LibraryLinkSettingsTests
    {
        private static readonly DateTime AppliedAt = new DateTime(2026, 10, 5, 8, 30, 0, DateTimeKind.Utc);

        [TestMethod]
        public void Default_IsAnEmptyCaseInsensitiveDictionary()
        {
            var settings = new PersistedSettings();

            Assert.IsNotNull(settings.LibraryLinks);
            Assert.AreEqual(0, settings.LibraryLinks.Count);
            settings.LibraryLinks["Colors"] = Link("item");
            Assert.IsNotNull(settings.GetLibraryLink("colors"));
        }

        [TestMethod]
        public void JsonRoundTrip_KeepsEveryLinkAndField()
        {
            var settings = new PersistedSettings();
            settings.SetLibraryLink("colors", Link("ws:neon"));
            settings.SetLibraryLink("toast:provider:Steam", Link("3f2a"));
            settings.SetLibraryLink("future:kind:unknown", Link("ws:later"));

            var loaded = JsonConvert.DeserializeObject<PersistedSettings>(JsonConvert.SerializeObject(settings));

            Assert.AreEqual(3, loaded.LibraryLinks.Count);
            AssertSameLink(Link("ws:neon"), loaded.GetLibraryLink("COLORS"));
            AssertSameLink(Link("3f2a"), loaded.GetLibraryLink("toast:provider:steam"));
            Assert.IsNotNull(loaded.GetLibraryLink("future:kind:unknown"), "keys this version does not know are kept");
        }

        [TestMethod]
        public void JsonWithoutLinks_LoadsEmpty()
        {
            var loaded = JsonConvert.DeserializeObject<PersistedSettings>("{}");
            var nulled = JsonConvert.DeserializeObject<PersistedSettings>("{ \"LibraryLinks\": null }");

            Assert.AreEqual(0, loaded.LibraryLinks.Count);
            Assert.IsNotNull(nulled.LibraryLinks);
            Assert.AreEqual(0, nulled.LibraryLinks.Count);
        }

        [TestMethod]
        public void Setter_DropsEmptyEntriesOnlyAndIgnoresCase()
        {
            var settings = new PersistedSettings
            {
                LibraryLinks = new Dictionary<string, LibraryLink>
                {
                    ["sounds"] = Link("a"),
                    ["mystery:key"] = Link("b"),
                    [" "] = Link("c"),
                    ["frame:global"] = null
                }
            };

            Assert.AreEqual(2, settings.LibraryLinks.Count);
            Assert.IsNotNull(settings.GetLibraryLink("SOUNDS"));
            Assert.IsNotNull(settings.GetLibraryLink("mystery:key"));

            settings.LibraryLinks = null;
            Assert.AreEqual(0, settings.LibraryLinks.Count);
        }

        [TestMethod]
        public void CloneAndCopyFrom_DeepCopyLinks()
        {
            var source = new PersistedSettings();
            source.SetLibraryLink("showcase:page-1", Link("ws:stats"));

            var clone = source.Clone();
            var copy = new PersistedSettings();
            copy.CopyFrom(source);

            foreach (var target in new[] { clone, copy })
            {
                AssertSameLink(Link("ws:stats"), target.GetLibraryLink("showcase:page-1"));
                Assert.AreNotSame(source.LibraryLinks, target.LibraryLinks);
                Assert.AreNotSame(source.GetLibraryLink("showcase:page-1"), target.GetLibraryLink("showcase:page-1"));
                target.GetLibraryLink("showcase:page-1").AppliedVersion = "changed";
                Assert.IsNotNull(target.GetLibraryLink("SHOWCASE:PAGE-1"), "the copy stays case-insensitive");
            }

            Assert.AreEqual("1.0.0", source.GetLibraryLink("showcase:page-1").AppliedVersion);
        }

        [TestMethod]
        public void SetLibraryLink_RaisesPropertyChangedAndRemoves()
        {
            var settings = new PersistedSettings();
            var raised = 0;
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PersistedSettings.LibraryLinks))
                {
                    raised++;
                }
            };

            settings.SetLibraryLink("colors", Link("a"));
            settings.SetLibraryLink("colors", null);
            settings.SetLibraryLink("colors", null);

            Assert.AreEqual(2, raised, "removing a missing link changes nothing");
            Assert.IsNull(settings.GetLibraryLink("colors"));
        }

        [TestMethod]
        public void Cancel_RestoresTheValueAndTheLinkTogether()
        {
            // SettingsViewModel.BeginEdit keeps a clone; CancelEdit puts a clone of it back.
            var live = new PersistedSettings();
            live.SetLibraryLink("colors", Link("old"));
            var snapshot = live.Clone();

            live.SetLibraryLink("colors", Link("new"));
            live.SetLibraryLink("sounds", Link("added"));
            live = snapshot.Clone();

            Assert.AreEqual("old", live.GetLibraryLink("colors").LibraryItemId);
            Assert.IsNull(live.GetLibraryLink("sounds"));
        }

        [TestMethod]
        public void Cancel_KeepsALinkWrittenIntoTheEditSnapshotToo()
        {
            // An update from outside the settings window writes live values and the snapshot
            // (UpdatePersistedIncludingEditSnapshot), so a Cancel does not undo it.
            var live = new PersistedSettings();
            var snapshot = live.Clone();

            foreach (var settings in new[] { live, snapshot })
            {
                settings.SetLibraryLink("toast:global", Link("ws:glass"));
            }

            var restored = new PersistedSettings();
            restored.CopyFrom(snapshot);

            AssertSameLink(Link("ws:glass"), restored.GetLibraryLink("toast:global"));
        }

        private static LibraryLink Link(string itemId)
        {
            return new LibraryLink
            {
                LibraryItemId = itemId,
                AppliedVersion = "1.0.0",
                BaselineFile = "0123.json",
                BaselineHash = "abcd",
                AppliedUtc = AppliedAt
            };
        }

        private static void AssertSameLink(LibraryLink expected, LibraryLink actual)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(expected.LibraryItemId, actual.LibraryItemId);
            Assert.AreEqual(expected.AppliedVersion, actual.AppliedVersion);
            Assert.AreEqual(expected.BaselineFile, actual.BaselineFile);
            Assert.AreEqual(expected.BaselineHash, actual.BaselineHash);
            Assert.AreEqual(expected.AppliedUtc, actual.AppliedUtc.ToUniversalTime());
        }
    }
}

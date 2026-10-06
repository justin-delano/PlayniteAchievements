using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class JsonThreeWayMergeTests
    {
        [TestMethod]
        public void WithoutBaseline_IncomingWins()
        {
            var current = J("{ notes: { a: 'mine' } }");
            var incoming = J("{ notes: { a: 'theirs' } }");

            var merged = JsonThreeWayMerge.Merge(null, current, incoming, out var kept);

            Assert.IsTrue(JToken.DeepEquals(incoming, merged));
            Assert.AreNotSame(incoming, merged);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void WithoutCurrent_IncomingWins()
        {
            var incoming = J("{ a: 1 }");

            var merged = JsonThreeWayMerge.Merge(J("{ a: 0 }"), null, incoming, out var kept);

            Assert.IsTrue(JToken.DeepEquals(incoming, merged));
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void NullIncoming_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => JsonThreeWayMerge.Merge(J("{}"), J("{}"), null, out _));
        }

        [TestMethod]
        public void UntouchedArray_TakesIncoming()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ order: ['a', 'b'] }"),
                J("{ order: ['a', 'b'] }"),
                J("{ order: ['b', 'a'] }"),
                out var kept);

            AssertJson("{ order: ['b', 'a'] }", merged);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void EditedArray_KeepsUserValueAsOneLeaf()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ order: ['a', 'b'] }"),
                J("{ order: ['b', 'a'] }"),
                J("{ order: ['a', 'b', 'c'] }"),
                out var kept);

            AssertJson("{ order: ['b', 'a'] }", merged);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void EditedScalar_KeepsUserValue()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ excluded: null }"),
                J("{ excluded: true }"),
                J("{ excluded: null }"),
                out var kept);

            Assert.AreEqual(true, (bool)merged["excluded"]);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void EditEqualToIncoming_IsNotCountedAsKept()
        {
            var merged = JsonThreeWayMerge.Merge(J("{ a: 1 }"), J("{ a: 2 }"), J("{ a: 2 }"), out var kept);

            AssertJson("{ a: 2 }", merged);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void Dictionary_MergesPerKey()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ overrides: { a: 'A', b: 'B', gone: 'G' } }"),
                J("{ overrides: { a: 'A edited', b: 'B', gone: 'G', mine: 'M' } }"),
                J("{ overrides: { a: 'A2', b: 'B2', new: 'N' } }"),
                out var kept);

            var overrides = (JObject)merged["overrides"];
            Assert.AreEqual("A edited", (string)overrides["a"], "the user's edit wins over the new value");
            Assert.AreEqual("B2", (string)overrides["b"], "an untouched key takes the new value");
            Assert.AreEqual("N", (string)overrides["new"], "new keys arrive");
            Assert.AreEqual("M", (string)overrides["mine"], "keys the user added stay");
            Assert.IsNull(overrides["gone"], "a key the new version dropped goes when the user left it alone");
            Assert.AreEqual(2, kept, "a edited and mine added");
        }

        [TestMethod]
        public void Dictionary_UserRemovalSurvivesIncomingChange()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ notes: { a: 'n1', b: 'n1' } }"),
                J("{ notes: { b: 'n1' } }"),
                J("{ notes: { a: 'n2', b: 'n2' } }"),
                out var kept);

            Assert.IsNull(merged["notes"]["a"]);
            Assert.AreEqual("n2", (string)merged["notes"]["b"]);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void Dictionary_KeysMatchWithoutRegardToCase()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ notes: { Key: 'n1' } }"),
                J("{ notes: { key: 'n1' } }"),
                J("{ notes: { KEY: 'n2' } }"),
                out var kept);

            var notes = (JObject)merged["notes"];
            Assert.AreEqual(1, notes.Count);
            Assert.AreEqual("n2", (string)notes.GetValue("key", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void NestedObjects_MergeDownToLeaves()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ style: { toast: { color: 'red', size: 10 }, frame: { width: 2 } } }"),
                J("{ style: { toast: { color: 'blue', size: 10 }, frame: { width: 2 } } }"),
                J("{ style: { toast: { color: 'green', size: 12 }, frame: { width: 3 } } }"),
                out var kept);

            AssertJson("{ style: { toast: { color: 'blue', size: 12 }, frame: { width: 3 } } }", merged);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void AtomicPath_MergesAsOneValue()
        {
            var baseline = J("{ overrides: { a: { name: 'A', icon: 'i1' } } }");
            var current = J("{ overrides: { a: { name: 'A edited', icon: 'i1' } } }");
            var incoming = J("{ overrides: { a: { name: 'A', icon: 'i2' } } }");

            var perLeaf = JsonThreeWayMerge.Merge(baseline, current, incoming, out var perLeafKept);
            var atomic = JsonThreeWayMerge.Merge(baseline, current, incoming, new[] { "Overrides.*" }, out var atomicKept);

            AssertJson("{ overrides: { a: { name: 'A edited', icon: 'i2' } } }", perLeaf);
            Assert.AreEqual(1, perLeafKept);
            AssertJson("{ overrides: { a: { name: 'A edited', icon: 'i1' } } }", atomic);
            Assert.AreEqual(1, atomicKept);
        }

        [TestMethod]
        public void EmptyAtomicPath_MakesTheDocumentOneLeaf()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ a: 1, b: 1 }"),
                J("{ a: 2, b: 1 }"),
                J("{ a: 1, b: 3 }"),
                new[] { string.Empty },
                out var kept);

            AssertJson("{ a: 2, b: 1 }", merged);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void EmptyCollection_CountsAsUntouched()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ filtered: null }"),
                J("{ filtered: [] }"),
                J("{ filtered: ['x'] }"),
                out var kept);

            AssertJson("{ filtered: ['x'] }", merged);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void MissingNullAndEmptyObject_AreTheSameValue()
        {
            Assert.IsTrue(JsonThreeWayMerge.SameValue(null, J("{}")));
            Assert.IsTrue(JsonThreeWayMerge.SameValue(JValue.CreateNull(), J("[]")));
            Assert.IsTrue(JsonThreeWayMerge.SameValue(J("{ a: null, b: [] }"), J("{}")));
            Assert.IsFalse(JsonThreeWayMerge.SameValue(J("{ a: 0 }"), J("{}")));
            Assert.IsFalse(JsonThreeWayMerge.SameValue(J("''"), null), "an empty string is a value");
        }

        [TestMethod]
        public void UserClearedDictionary_KeepsEveryKeyRemoved()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ notes: { a: 'n1', b: 'n1' } }"),
                J("{ notes: null }"),
                J("{ notes: { a: 'n2', b: 'n1', c: 'n3' } }"),
                out var kept);

            AssertJson("{ notes: { c: 'n3' } }", merged);
            Assert.AreEqual(2, kept, "a and b stay removed although the new version has them");
        }

        [TestMethod]
        public void ShapeChange_TreatsTheValueAsOneLeaf()
        {
            var merged = JsonThreeWayMerge.Merge(
                J("{ v: { a: 1 } }"),
                J("{ v: 'replaced' }"),
                J("{ v: { a: 2 } }"),
                out var kept);

            AssertJson("{ v: 'replaced' }", merged);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void Result_DoesNotShareTokensWithInputs()
        {
            var baseline = J("{ o: { a: 'A' }, keep: { x: 1 } }");
            var current = J("{ o: { a: 'A edited' }, keep: { x: 1 } }");
            var incoming = J("{ o: { a: 'A2' }, keep: { x: 1 } }");

            var merged = JsonThreeWayMerge.Merge(baseline, current, incoming, new[] { "o", "keep" }, out _);
            merged["o"]["a"] = "changed after merge";
            merged["keep"]["x"] = 5;

            Assert.AreEqual("A edited", (string)current["o"]["a"]);
            Assert.AreEqual(1, (int)incoming["keep"]["x"]);
        }

        [TestMethod]
        public void MatchesGameCustomDataMerge_OnTypedData()
        {
            var gameId = Guid.NewGuid();
            GameCustomDataFile File(Action<GameCustomDataFile> setup)
            {
                var file = new GameCustomDataFile { PlayniteGameId = gameId };
                setup(file);
                return file;
            }

            var baseline = File(f =>
            {
                f.AchievementOrder = new List<string> { "a", "b" };
                f.AchievementNotes = Map(("a", "n1"), ("b", "n1"));
                f.AchievementOverrides = Overrides(("a", "A"), ("b", "B"), ("gone", "G"));
            });
            var current = File(f =>
            {
                f.AchievementOrder = new List<string> { "b", "a" };
                f.AchievementNotes = Map(("b", "n1"));
                f.AchievementOverrides = Overrides(("a", "A edited"), ("b", "B"), ("gone", "G"), ("mine", "M"));
                f.ExcludedFromRefreshes = true;
            });
            var incoming = File(f =>
            {
                f.AchievementOrder = new List<string> { "a", "b", "c" };
                f.AchievementNotes = Map(("a", "n2"), ("b", "n2"));
                f.AchievementOverrides = Overrides(("a", "A2"), ("b", "B2"), ("new", "N"));
            });

            var typed = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var typedKept);
            var json = JsonThreeWayMerge.Merge(
                JToken.FromObject(baseline),
                JToken.FromObject(current),
                JToken.FromObject(incoming),
                new[] { "AchievementOverrides.*" },
                out var jsonKept);

            Assert.AreEqual(typedKept, jsonKept);
            Assert.IsTrue(
                JsonThreeWayMerge.SameValue(JToken.FromObject(typed), json),
                "typed: " + JToken.FromObject(typed) + Environment.NewLine + "json: " + json);
        }

        private static JToken J(string json) => JToken.Parse(json);

        private static void AssertJson(string expected, JToken actual)
        {
            Assert.IsTrue(JsonThreeWayMerge.SameValue(J(expected), actual), "expected " + J(expected) + " but was " + actual);
        }

        private static Dictionary<string, string> Map(params (string Key, string Value)[] entries)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                map[entry.Key] = entry.Value;
            }

            return map;
        }

        private static Dictionary<string, AchievementOverride> Overrides(params (string Key, string DisplayName)[] entries)
        {
            var map = new Dictionary<string, AchievementOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                map[entry.Key] = new AchievementOverride { DisplayName = entry.DisplayName };
            }

            return map;
        }
    }
}

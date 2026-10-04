using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class GameCustomDataThreeWayMergeTests
    {
        private static readonly Guid GameId = Guid.NewGuid();

        [TestMethod]
        public void WithoutBaseline_IncomingWins()
        {
            var current = File(f => f.AchievementNotes = Map(("a", "mine")));
            var incoming = File(f => f.AchievementNotes = Map(("a", "theirs")));

            var merged = GameCustomDataThreeWayMerge.Merge(null, current, incoming, out var kept);

            Assert.AreSame(incoming, merged);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void UntouchedField_TakesIncoming()
        {
            var baseline = File(f => f.AchievementOrder = new List<string> { "a", "b" });
            var current = File(f => f.AchievementOrder = new List<string> { "a", "b" });
            var incoming = File(f => f.AchievementOrder = new List<string> { "b", "a" });

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var kept);

            CollectionAssert.AreEqual(new[] { "b", "a" }, merged.AchievementOrder);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void EditedField_KeepsUserValue()
        {
            var baseline = File(f => f.AchievementOrder = new List<string> { "a", "b" });
            var current = File(f => f.AchievementOrder = new List<string> { "b", "a" });
            var incoming = File(f => f.AchievementOrder = new List<string> { "a", "b", "c" });

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var kept);

            CollectionAssert.AreEqual(new[] { "b", "a" }, merged.AchievementOrder);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void EditedScalar_KeepsUserValue()
        {
            var baseline = File(f => f.ExcludedFromRefreshes = null);
            var current = File(f => f.ExcludedFromRefreshes = true);
            var incoming = File(f => f.ExcludedFromRefreshes = null);

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var kept);

            Assert.AreEqual(true, merged.ExcludedFromRefreshes);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void Dictionary_MergesPerKey()
        {
            var baseline = File(f => f.AchievementOverrides = Overrides(("a", "A"), ("b", "B"), ("gone", "G")));
            var current = File(f => f.AchievementOverrides = Overrides(("a", "A edited"), ("b", "B"), ("gone", "G"), ("mine", "M")));
            var incoming = File(f => f.AchievementOverrides = Overrides(("a", "A2"), ("b", "B2"), ("new", "N")));

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var kept);

            Assert.AreEqual("A edited", merged.AchievementOverrides["a"].DisplayName, "the user's edit wins over the new value");
            Assert.AreEqual("B2", merged.AchievementOverrides["b"].DisplayName, "an untouched key takes the new value");
            Assert.AreEqual("N", merged.AchievementOverrides["new"].DisplayName, "new keys arrive");
            Assert.AreEqual("M", merged.AchievementOverrides["mine"].DisplayName, "keys the user added stay");
            Assert.IsFalse(merged.AchievementOverrides.ContainsKey("gone"), "a key the new version dropped goes when the user left it alone");
            Assert.AreEqual(2, kept, "a edited and mine added");
        }

        [TestMethod]
        public void Dictionary_UserRemovalSurvivesIncomingChange()
        {
            var baseline = File(f => f.AchievementNotes = Map(("a", "n1"), ("b", "n1")));
            var current = File(f => f.AchievementNotes = Map(("b", "n1")));
            var incoming = File(f => f.AchievementNotes = Map(("a", "n2"), ("b", "n2")));

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var kept);

            Assert.IsFalse(merged.AchievementNotes.ContainsKey("a"));
            Assert.AreEqual("n2", merged.AchievementNotes["b"]);
            Assert.AreEqual(1, kept);
        }

        [TestMethod]
        public void EmptyCollection_CountsAsUntouched()
        {
            var baseline = File(f => f.FilteredAchievementApiNames = null);
            var current = File(f => f.FilteredAchievementApiNames = new List<string>());
            var incoming = File(f => f.FilteredAchievementApiNames = new List<string> { "x" });

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out var kept);

            CollectionAssert.AreEqual(new[] { "x" }, merged.FilteredAchievementApiNames);
            Assert.AreEqual(0, kept);
        }

        [TestMethod]
        public void IdentityFields_ComeFromIncoming()
        {
            var baseline = File(f => f.SchemaVersion = 7);
            var current = File(f => f.SchemaVersion = 7);
            var incoming = File(f => f.SchemaVersion = 8);

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out _);

            Assert.AreEqual(8, merged.SchemaVersion);
            Assert.AreEqual(GameId, merged.PlayniteGameId);
        }

        [TestMethod]
        public void Result_DoesNotShareReferencesWithInputs()
        {
            var baseline = File(f => f.AchievementOverrides = Overrides(("a", "A")));
            var current = File(f => f.AchievementOverrides = Overrides(("a", "A edited")));
            var incoming = File(f => f.AchievementOverrides = Overrides(("a", "A2")));

            var merged = GameCustomDataThreeWayMerge.Merge(baseline, current, incoming, out _);
            merged.AchievementOverrides["a"].DisplayName = "changed after merge";

            Assert.AreEqual("A edited", current.AchievementOverrides["a"].DisplayName);
            Assert.AreEqual("A2", incoming.AchievementOverrides["a"].DisplayName);
        }

        private static GameCustomDataFile File(Action<GameCustomDataFile> setup)
        {
            var file = new GameCustomDataFile { PlayniteGameId = GameId };
            setup(file);
            return file;
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

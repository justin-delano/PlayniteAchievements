using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Database;

namespace PlayniteAchievements.SqlNado.Tests
{
    [TestClass]
    public class SqlNadoCacheBehaviorTests
    {
        [TestMethod]
        public void ComputeStaleDefinitionIds_ReturnsMissingOnly_CaseInsensitive()
        {
            var existing = new Dictionary<string, long>
            {
                ["Alpha"] = 1,
                ["Bravo"] = 2,
                ["Charlie"] = 3
            };

            var incoming = new[] { "alpha", " CHARLIE " };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, incoming);

            Assert.AreEqual(1, stale.Count);
            CollectionAssert.Contains(stale, 2L);
        }

        [TestMethod]
        public void ComputeStaleDefinitionIds_EmptyIncoming_DeletesAllValidIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["A"] = 10,
                ["B"] = 11
            };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, Array.Empty<string>());

            Assert.AreEqual(2, stale.Count);
            CollectionAssert.Contains(stale, 10L);
            CollectionAssert.Contains(stale, 11L);
        }

        [TestMethod]
        public void ComputeStaleDefinitionIds_NullIncoming_DeletesAllValidIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["A"] = 10,
                ["B"] = 11
            };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, null);

            Assert.AreEqual(2, stale.Count);
            CollectionAssert.Contains(stale, 10L);
            CollectionAssert.Contains(stale, 11L);
        }

        [TestMethod]
        public void ComputeStaleDefinitionIds_IgnoresBlankNamesAndNonPositiveIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["  "] = 1,
                ["Real"] = 0,
                ["Other"] = -5,
                ["Keep"] = 9
            };

            var incoming = new[] { "keep" };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, incoming);

            Assert.AreEqual(0, stale.Count);
        }

        [TestMethod]
        public void ComputeStaleDefinitionIds_DedupesByApiName_AndKeepsOnlyValidStaleIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["One"] = 100,
                ["Two"] = 200,
                ["Three"] = 300
            };

            var incoming = new[] { " two ", "TWO", "three" };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, incoming);

            Assert.AreEqual(1, stale.Count);
            Assert.AreEqual(100L, stale[0]);
        }

        [TestMethod]
        public void ComputeStaleDefinitionIds_NullExisting_ReturnsEmpty()
        {
            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(null, new[] { "A" });
            Assert.AreEqual(0, stale.Count);
        }

        [TestMethod]
        public void ComputeStaleDefinitionIds_EmptyExisting_ReturnsEmpty()
        {
            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(new Dictionary<string, long>(), new[] { "A" });
            Assert.AreEqual(0, stale.Count);
        }

        [DataTestMethod]
        [DataRow(0, 0, 0, true)]
        [DataRow(-1, 0, 0, true)]
        [DataRow(0, -1, 0, true)]
        [DataRow(0, 0, -1, true)]
        [DataRow(1, 0, 0, false)]
        [DataRow(0, 2, 0, false)]
        [DataRow(0, 0, 3, false)]
        [DataRow(1, 1, 1, false)]
        public void ShouldMarkLegacyImportDone_UsesFailureAndRemainingCounts(
            int parseFailedCount,
            int dbWriteFailedCount,
            int remainingFileCount,
            bool expected)
        {
            var actual = SqlNadoCacheBehavior.ShouldMarkLegacyImportDone(
                parseFailedCount,
                dbWriteFailedCount,
                remainingFileCount);
            Assert.AreEqual(expected, actual);
        }
    }
}

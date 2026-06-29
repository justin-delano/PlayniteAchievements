using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Database;
using SqlNado;

namespace PlayniteAchievements.SqlNado.Tests
{
    [TestClass]
    public class SqlNadoCacheBehaviorTests
    {
        [TestMethod]
        public void ExecuteScalar_WithNumericSqlParameters_UsesObjectArray()
        {
            var path = Path.Combine(Path.GetTempPath(), "playach-sqlnado-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var db = new SQLiteDatabase(
                    path,
                    SQLiteOpenOptions.SQLITE_OPEN_READWRITE |
                    SQLiteOpenOptions.SQLITE_OPEN_CREATE |
                    SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX))
                {
                    db.ExecuteNonQuery(
                        @"CREATE TABLE Probe (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            UserId INTEGER NOT NULL,
                            GameId INTEGER NOT NULL
                          );");
                    db.ExecuteNonQuery(
                        "INSERT INTO Probe (UserId, GameId) VALUES (?, ?);",
                        10L,
                        20L);

                    var count = db.ExecuteScalar<long>(
                        "SELECT COUNT(*) FROM Probe WHERE UserId = ? AND GameId = ?;",
                        new object[] { 10L, 20L });
                    var id = db.ExecuteScalar<long>(
                        "SELECT Id FROM Probe WHERE UserId = ? AND GameId = ? LIMIT 1;",
                        new object[] { 10L, 20L });

                    Assert.AreEqual(1L, count);
                    Assert.AreEqual(1L, id);
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

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

        [TestMethod]
        public void ShouldFallbackToProviderGameIdLookup_RetroAchievementsWithPlayniteId_ReturnsFalse()
        {
            var shouldFallback = SqlNadoCacheBehavior.ShouldFallbackToProviderGameIdLookup(
                "RetroAchievements",
                Guid.NewGuid().ToString(),
                12345);

            Assert.IsFalse(shouldFallback);
        }

        [TestMethod]
        public void ShouldFallbackToProviderGameIdLookup_RetroAchievementsWithoutPlayniteId_ReturnsTrue()
        {
            var shouldFallback = SqlNadoCacheBehavior.ShouldFallbackToProviderGameIdLookup(
                "RetroAchievements",
                null,
                12345);

            Assert.IsTrue(shouldFallback);
        }

        [TestMethod]
        public void ShouldFallbackToProviderGameIdLookup_NonRetroAchievementsWithPlayniteId_ReturnsFalse()
        {
            var shouldFallback = SqlNadoCacheBehavior.ShouldFallbackToProviderGameIdLookup(
                "Steam",
                Guid.NewGuid().ToString(),
                12345);

            Assert.IsFalse(shouldFallback);
        }

        [TestMethod]
        public void ShouldFallbackToProviderGameIdLookup_NonRetroAchievementsWithoutPlayniteId_ReturnsTrue()
        {
            var shouldFallback = SqlNadoCacheBehavior.ShouldFallbackToProviderGameIdLookup(
                "Steam",
                null,
                12345);

            Assert.IsTrue(shouldFallback);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow(0L)]
        [DataRow(-5L)]
        public void ShouldFallbackToProviderGameIdLookup_MissingOrInvalidProviderGameId_ReturnsFalse(long? providerGameId)
        {
            var shouldFallback = SqlNadoCacheBehavior.ShouldFallbackToProviderGameIdLookup(
                "Steam",
                Guid.NewGuid().ToString(),
                providerGameId);

            Assert.IsFalse(shouldFallback);
        }

        [DataTestMethod]
        [DataRow("RetroAchievements", true)]
        [DataRow(" retroachievements ", true)]
        [DataRow("Steam", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void IsRetroAchievementsProvider_DetectsExpectedProviders(string providerName, bool expected)
        {
            var actual = SqlNadoCacheBehavior.IsRetroAchievementsProvider(providerName);
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("Steam", true)]
        [DataRow("GOG", true)]
        [DataRow("Exophase", false)]
        [DataRow("Manual", false)]
        [DataRow("Unmapped", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void CanReclaimExophaseProxy_AllowsNativeProvidersOnly(
            string incomingProviderKey,
            bool expected)
        {
            var actual = SqlNadoCacheBehavior.CanReclaimExophaseProxy(incomingProviderKey);
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow(false, 1, 10, false, false)]
        [DataRow(true, 1, 10, false, true)]
        [DataRow(false, 10, 10, false, true)]
        [DataRow(false, 1, 10, true, true)]
        [DataRow(false, 0, 0, false, false)]
        public void ComputeIsCompleted_UsesProviderHundredPercentOrMarker(
            bool providerIsCompleted,
            int unlockedCount,
            int totalCount,
            bool markerUnlocked,
            bool expected)
        {
            var actual = SqlNadoCacheBehavior.ComputeIsCompleted(
                providerIsCompleted,
                unlockedCount,
                totalCount,
                markerUnlocked);

            Assert.AreEqual(expected, actual);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Friends;
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
        public void BuildDefinitionsFromFriendRows_SeedsFromRowsWithApiNameAndDisplayName()
        {
            var rows = new[]
            {
                new FriendAchievementRow { ApiName = "plat", DisplayName = "Platinum", Description = "All trophies", Unlocked = true },
                new FriendAchievementRow { ApiName = "story", DisplayName = "Story done", Description = "Finish", Unlocked = false }
            };

            var defs = SqlNadoCacheBehavior.BuildDefinitionsFromFriendRows(rows);

            Assert.AreEqual(2, defs.Count);
            Assert.AreEqual("plat", defs[0].ApiName);
            Assert.AreEqual("Platinum", defs[0].DisplayName);
            Assert.AreEqual("All trophies", defs[0].Description);
        }

        [TestMethod]
        public void BuildDefinitionsFromFriendRows_SkipsRowsMissingKeyOrName_AndDedupesByApiName()
        {
            var rows = new[]
            {
                new FriendAchievementRow { ApiName = "", DisplayName = "No key" },
                new FriendAchievementRow { ApiName = "only-key", DisplayName = "  " },
                new FriendAchievementRow { ApiName = "dup", DisplayName = "First" },
                new FriendAchievementRow { ApiName = " DUP ", DisplayName = "Second (dupe key)" },
                null
            };

            var defs = SqlNadoCacheBehavior.BuildDefinitionsFromFriendRows(rows);

            Assert.AreEqual(1, defs.Count);
            Assert.AreEqual("dup", defs[0].ApiName);
            Assert.AreEqual("First", defs[0].DisplayName);
        }

        [TestMethod]
        public void BuildDefinitionsFromFriendRows_NullInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, SqlNadoCacheBehavior.BuildDefinitionsFromFriendRows(null).Count);
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

        [TestMethod]
        public void CacheStore_FriendGameLinksLoadOwnershipPlaytimeAndLastPlayed()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));
            var models = File.ReadAllText(FindRepoFile("source", "Services", "Friends", "FriendCacheModels.cs"));

            StringAssert.Contains(models, "public long PlaytimeForeverMinutes { get; set; }");
            StringAssert.Contains(models, "public DateTime? LastPlayedUtc { get; set; }");
            StringAssert.Contains(store, "public long PlaytimeForeverMinutes { get; set; }");
            StringAssert.Contains(store, "public string LastPlayedUtc { get; set; }");
            StringAssert.Contains(store, "fo.PlaytimeForeverMinutes AS PlaytimeForeverMinutes");
            StringAssert.Contains(store, "fo.LastPlayedUtc AS LastPlayedUtc");
            StringAssert.Contains(store, "PlaytimeForeverMinutes = Math.Max(0, row.PlaytimeForeverMinutes)");
            StringAssert.Contains(store, "LastPlayedUtc = ParseUtc(row.LastPlayedUtc)");
        }

        [TestMethod]
        public void CacheStore_FriendIdentitiesExposeAbsoluteAvatarPath()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "AvatarPath = !string.IsNullOrWhiteSpace(row.AvatarPath)");
            StringAssert.Contains(store, "? MakeAbsolutePath(row.AvatarPath)");
            StringAssert.Contains(store, ": row.AvatarUrl");
        }

        [TestMethod]
        public void CacheStore_NormalFriendOwnershipCleanupPreservesProviderOnlyRows()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "if (options?.IncludeProviderOnlyGames != true)");
            StringAssert.Contains(store, "DeleteStaleSharedFriendOwnership(db, user.Id, seenSharedGameIds);");
            StringAssert.Contains(store, "private static void DeleteStaleSharedFriendOwnership");
            StringAssert.Contains(store, "g.PlayniteGameId IS NOT NULL");
            StringAssert.Contains(store, "TRIM(g.PlayniteGameId) <> ''");
        }

        [TestMethod]
        public void CacheStore_ExophaseStringKeysDoNotFallbackToStaleSharedMappings()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "ShouldUseSharedFriendGameFallback(providerKey, providerGameKey, playniteGameId)");
            StringAssert.Contains(store, "return !IsExophaseProvider(providerKey) || string.IsNullOrWhiteSpace(providerGameKey);");
        }

        [TestMethod]
        public void CacheStore_FriendGameAndAchievementRowsShareImageResolution()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "GameLogo = ResolveFriendGameIconPath(presentation, row.IconPath)");
            StringAssert.Contains(store, "GameCoverPath = ResolveFriendGameCoverPath(presentation, row.CoverPath)");
            StringAssert.Contains(store, "GameIconPath = ResolveFriendGameIconPath(presentation, row.IconPath)");
            StringAssert.Contains(store, "GameCoverPath = ResolveFriendGameCoverPath(presentation, row.CoverPath)");
        }

        [TestMethod]
        public void CacheStore_FriendAchievementRowsRequireCurrentOwnership()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "INNER JOIN FriendOwnership fo ON fo.UserId = u.Id AND fo.GameId = g.Id");
        }

        [TestMethod]
        public void CacheStore_RecentFriendCandidatesUsePlaytimeDeltaOnly()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, @"AND COALESCE(fo.Playtime2WeeksMinutes, 0) > 0");
            Assert.IsFalse(store.Contains("fo.LastScrapedUtc IS NULL"));
            Assert.IsFalse(store.Contains("fo.LastScrapedUtc < ?"));
        }

        [TestMethod]
        public void CacheStore_DeleteFriendData_PreserveFriendRecord_SkipsUsersDelete()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            // Clear keeps the friend registered; only Remove/Ignore hard-delete the Users row.
            StringAssert.Contains(store, "public FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId, bool preserveFriendRecord = false)");
            StringAssert.Contains(store, "if (!preserveFriendRecord)");
            StringAssert.Contains(store, "DELETE FROM Users WHERE Id = ? AND IsCurrentUser = 0;");
        }

        [TestMethod]
        public void ClearFriend_PreservesFriendRecord_WhileRemoveAndIgnoreDoNot()
        {
            var overview = File.ReadAllText(FindRepoFile("source", "Views", "FriendsOverviewControl.xaml.cs"));

            StringAssert.Contains(overview, "DeleteFriendData(friend.ProviderKey, friend.ExternalUserId, preserveFriendRecord: true)");
            // Ignore keeps the full delete (no preserve flag).
            StringAssert.Contains(overview, "DeleteFriendData(friend.ProviderKey, friend.ExternalUserId);");
        }

        [TestMethod]
        public void CacheStore_FriendGameSummary_UsesDisplayProviderKey()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "g.ProviderPlatformKey AS ProviderPlatformKey");
            StringAssert.Contains(store, "ResolveDisplayProviderKey(row.ProviderKey, row.ProviderPlatformKey)");
            StringAssert.Contains(store, "ProviderRegistry.TryResolveProviderVisuals(displayProviderKey");
        }

        [TestMethod]
        public void FriendGameImages_DownloadedToPaths_NotPersistedAsUrl()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));
            var runtime = File.ReadAllText(FindRepoFile("source", "Services", "Friends", "FriendsRefreshRuntime.cs"));

            // Header banner is downloaded and stored as local icon+cover paths.
            StringAssert.Contains(runtime, "DownloadDefinitionGameImageAsync(providerKey, providerGameKey, appId, definition.IconUrl, cancel)");
            StringAssert.Contains(runtime, "_friendCache.SaveProviderGameImagePaths(providerKey, providerGameKey, appId, localPath, localPath);");
            // The source URL is not persisted into the definition state.
            StringAssert.Contains(store, "// Image source URLs are not persisted; the header banner is downloaded to");
        }

        [TestMethod]
        public void ResolveFriendDefinition_PrefersStableApiName_OverLocalizedDisplayText()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            // The stable api name is tried before the display-name/description/icon fallbacks so a
            // friend's localized achievement text still matches the canonical definition.
            StringAssert.Contains(store, "var rowApiName = NormalizeMatchText(row.ApiName);");
            StringAssert.Contains(store, "string.Equals(NormalizeMatchText(def.ApiName), rowApiName, StringComparison.OrdinalIgnoreCase)");
            var apiNameIndex = store.IndexOf("var byApiName = definitions", StringComparison.Ordinal);
            var displayNameIndex = store.IndexOf("var exact = definitions", StringComparison.Ordinal);
            Assert.IsTrue(apiNameIndex > 0 && apiNameIndex < displayNameIndex,
                "ApiName match must precede the display-name match.");
        }

        [TestMethod]
        public void NormalizeMatchText_FoldsDiacritics()
        {
            var store = File.ReadAllText(FindRepoFile("source", "Services", "Database", "SqlNadoCacheStore.cs"));

            StringAssert.Contains(store, "NormalizationForm.FormD");
            StringAssert.Contains(store, "UnicodeCategory.NonSpacingMark");
        }

        private static string FindRepoFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not find " + Path.Combine(parts));
            return null;
        }
    }
}

using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Database.Rows;
using Playnite.SDK;
using SqlNado;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Database
{
    internal sealed class SqlNadoCacheStore
    {
        private sealed class CacheKeyRow
        {
            public string CacheKey { get; set; }
        }

        private sealed class ProgressGameJoinRow
        {
            public long UserGameProgressId { get; set; }
            public long GameId { get; set; }
            public long PlaytimeSeconds { get; set; }
            public long NoAchievements { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string ProviderName { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string LibrarySourceName { get; set; }
        }

        private sealed class AchievementJoinRow
        {
            public string ApiName { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string IconPath { get; set; }
            public long Hidden { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
        }

        private sealed class ResolvedUser
        {
            public string ProviderName { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public string FriendSource { get; set; }
        }

        private readonly object _sync = new object();
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly SqlNadoSchemaManager _schemaManager;
        private SQLiteDatabase _db;
        private bool _initialized;

        public string DatabasePath { get; }

        public SqlNadoCacheStore(PlayniteAchievementsPlugin plugin, ILogger logger, string baseDir)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _schemaManager = new SqlNadoSchemaManager(logger);
            DatabasePath = Path.Combine(baseDir ?? string.Empty, "achievement_cache.db");
        }

        public void EnsureInitialized()
        {
            lock (_sync)
            {
                EnsureInitializedLocked();
            }
        }

        public string GetMetadata(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return WithDb(db =>
            {
                var row = db.Load<CacheMetadataRow>(
                    "SELECT Key, Value FROM CacheMetadata WHERE Key = ? LIMIT 1;",
                    key.Trim()).FirstOrDefault();
                return row?.Value;
            });
        }

        public void SetMetadata(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            WithDb(db =>
            {
                db.ExecuteNonQuery(
                    "INSERT OR REPLACE INTO CacheMetadata (Key, Value) VALUES (?, ?);",
                    key.Trim(),
                    value ?? string.Empty);
            });
        }

        public bool HasAnyCurrentUserCacheRows()
        {
            return WithDb(db =>
            {
                var count = db.ExecuteScalar<long>(
                    @"SELECT COUNT(1)
                      FROM UserGameProgress ugp
                      INNER JOIN Users u ON u.Id = ugp.UserId
                      WHERE u.IsCurrentUser = 1;");
                return count > 0;
            });
        }

        public List<string> GetCachedGameIdsForCurrentUsers()
        {
            return WithDb(db =>
            {
                var rows = db.Load<CacheKeyRow>(
                    @"SELECT DISTINCT ugp.CacheKey AS CacheKey
                      FROM UserGameProgress ugp
                      INNER JOIN Users u ON u.Id = ugp.UserId
                      WHERE u.IsCurrentUser = 1
                      ORDER BY ugp.CacheKey;").ToList();

                return rows
                    .Select(a => a.CacheKey)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });
        }

        public GameAchievementData LoadCurrentUserGameData(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var cacheKey = key.Trim();
            return WithDb(db =>
            {
                var progress = db.Load<ProgressGameJoinRow>(
                    @"SELECT
                        ugp.Id AS UserGameProgressId,
                        ugp.GameId AS GameId,
                        ugp.PlaytimeSeconds AS PlaytimeSeconds,
                        ugp.NoAchievements AS NoAchievements,
                        ugp.LastUpdatedUtc AS LastUpdatedUtc,
                        g.ProviderName AS ProviderName,
                        g.ProviderGameId AS ProviderGameId,
                        g.PlayniteGameId AS PlayniteGameId,
                        g.GameName AS GameName,
                        g.LibrarySourceName AS LibrarySourceName
                      FROM UserGameProgress ugp
                      INNER JOIN Users u ON u.Id = ugp.UserId
                      INNER JOIN Games g ON g.Id = ugp.GameId
                      WHERE u.IsCurrentUser = 1
                        AND ugp.CacheKey = ?
                      ORDER BY ugp.LastUpdatedUtc DESC
                      LIMIT 1;",
                    cacheKey).FirstOrDefault();

                if (progress == null)
                {
                    return null;
                }

                var details = db.Load<AchievementJoinRow>(
                    @"SELECT
                        ad.ApiName AS ApiName,
                        ad.DisplayName AS DisplayName,
                        ad.Description AS Description,
                        ad.IconPath AS IconPath,
                        ad.Hidden AS Hidden,
                        ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                        ua.UnlockTimeUtc AS UnlockTimeUtc,
                        ua.ProgressNum AS ProgressNum,
                        ua.ProgressDenom AS ProgressDenom
                      FROM AchievementDefinitions ad
                      LEFT JOIN UserAchievements ua
                        ON ua.AchievementDefinitionId = ad.Id
                       AND ua.UserGameProgressId = ?
                      WHERE ad.GameId = ?
                      ORDER BY ad.Id;",
                    progress.UserGameProgressId,
                    progress.GameId).ToList();

                var model = new GameAchievementData
                {
                    LastUpdatedUtc = ParseUtc(progress.LastUpdatedUtc) ?? DateTime.UtcNow,
                    ProviderName = progress.ProviderName,
                    LibrarySourceName = progress.LibrarySourceName,
                    NoAchievements = progress.NoAchievements != 0,
                    PlaytimeSeconds = (ulong)Math.Max(0, progress.PlaytimeSeconds),
                    GameName = progress.GameName,
                    AppId = (int)Math.Max(0, progress.ProviderGameId ?? 0),
                    PlayniteGameId = ParseGuid(progress.PlayniteGameId),
                    Achievements = new List<AchievementDetail>()
                };

                foreach (var row in details)
                {
                    if (string.IsNullOrWhiteSpace(row?.ApiName))
                    {
                        continue;
                    }

                    model.Achievements.Add(new AchievementDetail
                    {
                        ApiName = row.ApiName,
                        DisplayName = row.DisplayName,
                        Description = row.Description,
                        IconPath = row.IconPath,
                        Hidden = row.Hidden != 0,
                        GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                        UnlockTimeUtc = ParseUtc(row.UnlockTimeUtc),
                        ProgressNum = row.ProgressNum,
                        ProgressDenom = row.ProgressDenom
                    });
                }

                if (model.PlayniteGameId == null && Guid.TryParse(cacheKey, out var parsed))
                {
                    model.PlayniteGameId = parsed;
                }

                return model;
            });
        }

        public void SaveCurrentUserGameData(string key, GameAchievementData data)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var cacheKey = key.Trim();
            var payload = data ?? new GameAchievementData();

            if (payload.LastUpdatedUtc == default(DateTime))
            {
                payload.LastUpdatedUtc = DateTime.UtcNow;
            }
            payload.LastUpdatedUtc = DateTimeUtilities.AsUtcKind(payload.LastUpdatedUtc);

            if (payload.PlayniteGameId == null && Guid.TryParse(cacheKey, out var parsedId))
            {
                payload.PlayniteGameId = parsedId;
            }

            var providerName = NormalizeProviderName(payload.ProviderName);
            var resolvedUser = ResolveCurrentUser(providerName);
            var nowIso = ToIso(DateTime.UtcNow);
            var updatedIso = ToIso(payload.LastUpdatedUtc);

            var achievements = payload.Achievements ?? new List<AchievementDetail>();
            var unlockedCount = achievements.Count(a => IsUnlocked(a?.UnlockTimeUtc));
            var totalCount = achievements.Count;
            var playtime = ClampPlaytime(payload.PlaytimeSeconds);

            WithDb(db =>
            {
                db.RunTransaction(() =>
                {
                    var userId = UpsertCurrentUser(db, resolvedUser, nowIso);
                    var gameId = UpsertGame(db, providerName, payload, nowIso, updatedIso);
                    var userProgressId = UpsertUserGameProgress(
                        db,
                        userId,
                        gameId,
                        cacheKey,
                        playtime,
                        payload.NoAchievements,
                        unlockedCount,
                        totalCount,
                        updatedIso,
                        nowIso);

                    var definitionIds = UpsertAchievementDefinitions(
                        db,
                        gameId,
                        achievements,
                        nowIso,
                        updatedIso);

                    db.ExecuteNonQuery("DELETE FROM UserAchievements WHERE UserGameProgressId = ?;", userProgressId);
                    foreach (var achievement in achievements)
                    {
                        if (achievement == null || string.IsNullOrWhiteSpace(achievement.ApiName))
                        {
                            continue;
                        }

                        if (!definitionIds.TryGetValue(achievement.ApiName.Trim(), out var definitionId))
                        {
                            continue;
                        }

                        var unlockTime = NormalizeUnlockTime(achievement.UnlockTimeUtc);
                        db.ExecuteNonQuery(
                            @"INSERT INTO UserAchievements
                                (UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, LastUpdatedUtc, CreatedUtc)
                              VALUES
                                (?, ?, ?, ?, ?, ?, ?, ?);",
                            userProgressId,
                            definitionId,
                            unlockTime.HasValue ? 1 : 0,
                            unlockTime.HasValue ? (object)ToIso(unlockTime.Value) : DBNull.Value,
                            achievement.ProgressNum.HasValue ? (object)achievement.ProgressNum.Value : DBNull.Value,
                            achievement.ProgressDenom.HasValue ? (object)achievement.ProgressDenom.Value : DBNull.Value,
                            updatedIso,
                            nowIso);
                    }
                });
            });
        }

        public void ClearCacheData()
        {
            WithDb(db =>
            {
                db.RunTransaction(() =>
                {
                    db.ExecuteNonQuery("DELETE FROM UserAchievements;");
                    db.ExecuteNonQuery("DELETE FROM UserGameProgress;");
                    db.ExecuteNonQuery("DELETE FROM AchievementDefinitions;");
                    db.ExecuteNonQuery("DELETE FROM Games;");
                });

                try
                {
                    db.Vacuum();
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "VACUUM failed after clearing cache.");
                }
            });
        }

        private void EnsureInitializedLocked()
        {
            if (_initialized)
            {
                return;
            }

            var parent = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            _db = new SQLiteDatabase(
                DatabasePath,
                SQLiteOpenOptions.SQLITE_OPEN_READWRITE |
                SQLiteOpenOptions.SQLITE_OPEN_CREATE |
                SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX);

            _db.EnableStatementsCache = true;
            _schemaManager.EnsureSchema(_db);
            _initialized = true;
        }

        private T WithDb<T>(Func<SQLiteDatabase, T> action)
        {
            lock (_sync)
            {
                EnsureInitializedLocked();
                return action(_db);
            }
        }

        private void WithDb(Action<SQLiteDatabase> action)
        {
            lock (_sync)
            {
                EnsureInitializedLocked();
                action(_db);
            }
        }

        private long UpsertCurrentUser(SQLiteDatabase db, ResolvedUser user, string nowIso)
        {
            db.ExecuteNonQuery(
                @"UPDATE Users
                  SET IsCurrentUser = CASE WHEN ExternalUserId = ? THEN 1 ELSE 0 END,
                      UpdatedUtc = ?
                  WHERE ProviderName = ?;",
                user.ExternalUserId,
                nowIso,
                user.ProviderName);

            var row = db.Load<UserRow>(
                @"SELECT Id, ProviderName, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, CreatedUtc, UpdatedUtc
                  FROM Users
                  WHERE ProviderName = ? AND ExternalUserId = ?
                  LIMIT 1;",
                user.ProviderName,
                user.ExternalUserId).FirstOrDefault();

            if (row != null)
            {
                db.ExecuteNonQuery(
                    @"UPDATE Users
                      SET DisplayName = ?,
                          IsCurrentUser = 1,
                          UpdatedUtc = ?
                      WHERE Id = ?;",
                    DbValue(user.DisplayName),
                    nowIso,
                    row.Id);
                return row.Id;
            }

            db.ExecuteNonQuery(
                @"INSERT INTO Users
                    (ProviderName, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, CreatedUtc, UpdatedUtc)
                  VALUES
                    (?, ?, ?, 1, ?, ?, ?);",
                user.ProviderName,
                user.ExternalUserId,
                DbValue(user.DisplayName),
                DbValue(user.FriendSource),
                nowIso,
                nowIso);

            return db.ExecuteScalar<long>("SELECT last_insert_rowid();");
        }

        private long UpsertGame(SQLiteDatabase db, string providerName, GameAchievementData data, string nowIso, string updatedIso)
        {
            var playniteGameId = data.PlayniteGameId?.ToString();
            long? providerGameId = data.AppId > 0 ? data.AppId : (long?)null;

            GameRow game = null;
            if (!string.IsNullOrWhiteSpace(playniteGameId))
            {
                game = db.Load<GameRow>(
                    @"SELECT Id, ProviderName, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderName = ? AND PlayniteGameId = ?
                      LIMIT 1;",
                    providerName,
                    playniteGameId).FirstOrDefault();
            }

            if (game == null && providerGameId.HasValue)
            {
                game = db.Load<GameRow>(
                    @"SELECT Id, ProviderName, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderName = ? AND ProviderGameId = ?
                      LIMIT 1;",
                    providerName,
                    providerGameId.Value).FirstOrDefault();
            }

            if (game == null)
            {
                db.ExecuteNonQuery(
                    @"INSERT INTO Games
                        (ProviderName, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?);",
                    providerName,
                    providerGameId.HasValue ? (object)providerGameId.Value : DBNull.Value,
                    DbValue(playniteGameId),
                    DbValue(data.GameName),
                    DbValue(data.LibrarySourceName),
                    nowIso,
                    updatedIso);
                return db.ExecuteScalar<long>("SELECT last_insert_rowid();");
            }

            db.ExecuteNonQuery(
                @"UPDATE Games
                  SET ProviderGameId = ?,
                      PlayniteGameId = ?,
                      GameName = ?,
                      LibrarySourceName = ?,
                      LastUpdatedUtc = ?
                  WHERE Id = ?;",
                providerGameId.HasValue ? (object)providerGameId.Value : DBNull.Value,
                DbValue(playniteGameId),
                DbValue(data.GameName),
                DbValue(data.LibrarySourceName),
                updatedIso,
                game.Id);

            return game.Id;
        }

        private long UpsertUserGameProgress(
            SQLiteDatabase db,
            long userId,
            long gameId,
            string cacheKey,
            long playtimeSeconds,
            bool noAchievements,
            int achievementsUnlocked,
            int totalAchievements,
            string updatedIso,
            string nowIso)
        {
            var existing = db.Load<UserGameProgressRow>(
                @"SELECT Id, UserId, GameId, CacheKey, PlaytimeSeconds, NoAchievements, AchievementsUnlocked, TotalAchievements, LastUpdatedUtc, CreatedUtc, UpdatedUtc
                  FROM UserGameProgress
                  WHERE UserId = ? AND CacheKey = ?
                  LIMIT 1;",
                userId,
                cacheKey).FirstOrDefault();

            if (existing == null)
            {
                existing = db.Load<UserGameProgressRow>(
                    @"SELECT Id, UserId, GameId, CacheKey, PlaytimeSeconds, NoAchievements, AchievementsUnlocked, TotalAchievements, LastUpdatedUtc, CreatedUtc, UpdatedUtc
                      FROM UserGameProgress
                      WHERE UserId = ? AND GameId = ?
                      LIMIT 1;",
                    userId,
                    gameId).FirstOrDefault();
            }

            if (existing == null)
            {
                db.ExecuteNonQuery(
                    @"INSERT INTO UserGameProgress
                        (UserId, GameId, CacheKey, PlaytimeSeconds, NoAchievements, AchievementsUnlocked, TotalAchievements, LastUpdatedUtc, CreatedUtc, UpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                    userId,
                    gameId,
                    cacheKey,
                    playtimeSeconds,
                    noAchievements ? 1 : 0,
                    achievementsUnlocked,
                    totalAchievements,
                    updatedIso,
                    nowIso,
                    nowIso);
                return db.ExecuteScalar<long>("SELECT last_insert_rowid();");
            }

            db.ExecuteNonQuery(
                @"UPDATE UserGameProgress
                  SET GameId = ?,
                      CacheKey = ?,
                      PlaytimeSeconds = ?,
                      NoAchievements = ?,
                      AchievementsUnlocked = ?,
                      TotalAchievements = ?,
                      LastUpdatedUtc = ?,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                gameId,
                cacheKey,
                playtimeSeconds,
                noAchievements ? 1 : 0,
                achievementsUnlocked,
                totalAchievements,
                updatedIso,
                nowIso,
                existing.Id);

            return existing.Id;
        }

        private Dictionary<string, long> UpsertAchievementDefinitions(
            SQLiteDatabase db,
            long gameId,
            IEnumerable<AchievementDetail> achievements,
            string nowIso,
            string updatedIso)
        {
            var idsByApiName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (achievements == null)
            {
                return idsByApiName;
            }

            foreach (var achievement in achievements)
            {
                if (achievement == null || string.IsNullOrWhiteSpace(achievement.ApiName))
                {
                    continue;
                }

                var apiName = achievement.ApiName.Trim();
                var existing = db.Load<AchievementDefinitionRow>(
                    @"SELECT Id, GameId, ApiName, DisplayName, Description, IconPath, Hidden, GlobalPercentUnlocked, ProgressMax, CreatedUtc, UpdatedUtc
                      FROM AchievementDefinitions
                      WHERE GameId = ? AND ApiName = ?
                      LIMIT 1;",
                    gameId,
                    apiName).FirstOrDefault();

                if (existing == null)
                {
                    db.ExecuteNonQuery(
                        @"INSERT INTO AchievementDefinitions
                            (GameId, ApiName, DisplayName, Description, IconPath, Hidden, GlobalPercentUnlocked, ProgressMax, CreatedUtc, UpdatedUtc)
                          VALUES
                            (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                        gameId,
                        apiName,
                        DbValue(achievement.DisplayName),
                        DbValue(achievement.Description),
                        DbValue(achievement.IconPath),
                        achievement.Hidden ? 1 : 0,
                        achievement.GlobalPercentUnlocked.HasValue ? (object)achievement.GlobalPercentUnlocked.Value : DBNull.Value,
                        achievement.ProgressDenom.HasValue ? (object)achievement.ProgressDenom.Value : DBNull.Value,
                        nowIso,
                        updatedIso);

                    idsByApiName[apiName] = db.ExecuteScalar<long>("SELECT last_insert_rowid();");
                    continue;
                }

                db.ExecuteNonQuery(
                    @"UPDATE AchievementDefinitions
                      SET DisplayName = ?,
                          Description = ?,
                          IconPath = ?,
                          Hidden = ?,
                          GlobalPercentUnlocked = ?,
                          ProgressMax = ?,
                          UpdatedUtc = ?
                      WHERE Id = ?;",
                    DbValue(achievement.DisplayName),
                    DbValue(achievement.Description),
                    DbValue(achievement.IconPath),
                    achievement.Hidden ? 1 : 0,
                    achievement.GlobalPercentUnlocked.HasValue ? (object)achievement.GlobalPercentUnlocked.Value : DBNull.Value,
                    achievement.ProgressDenom.HasValue ? (object)achievement.ProgressDenom.Value : DBNull.Value,
                    updatedIso,
                    existing.Id);

                idsByApiName[apiName] = existing.Id;
            }

            return idsByApiName;
        }

        private ResolvedUser ResolveCurrentUser(string providerName)
        {
            var settings = _plugin?.Settings?.Persisted;
            string externalId = null;
            string displayName = null;

            if (string.Equals(providerName, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                externalId = _plugin?.SteamSessionManager?.GetCachedSteamId64();
                if (string.IsNullOrWhiteSpace(externalId))
                {
                    externalId = settings?.SteamUserId;
                }
            }
            else if (string.Equals(providerName, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
            {
                externalId = settings?.RaUsername;
            }

            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = "legacy";
            }

            displayName = externalId;
            return new ResolvedUser
            {
                ProviderName = providerName,
                ExternalUserId = externalId.Trim(),
                DisplayName = displayName,
                FriendSource = null
            };
        }

        private static string NormalizeProviderName(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return "LegacyUnknown";
            }

            var value = providerName.Trim();
            if (value.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Steam";
            }

            if (value.IndexOf("retro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "RetroAchievements";
            }

            return value;
        }

        private static object DbValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value;
        }

        private static long ClampPlaytime(ulong seconds)
        {
            if (seconds > long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)seconds;
        }

        private static bool IsUnlocked(DateTime? unlockTimeUtc)
        {
            return NormalizeUnlockTime(unlockTimeUtc).HasValue;
        }

        private static DateTime? NormalizeUnlockTime(DateTime? unlockTimeUtc)
        {
            if (!unlockTimeUtc.HasValue)
            {
                return null;
            }

            var value = unlockTimeUtc.Value;
            if (value == DateTime.MinValue)
            {
                return null;
            }

            return DateTimeUtilities.AsUtcKind(value);
        }

        private static Guid? ParseGuid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (Guid.TryParse(value, out var guid))
            {
                return guid;
            }

            return null;
        }

        private static DateTime? ParseUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return DateTimeUtilities.AsUtcKind(parsed);
            }

            return null;
        }

        private static string ToIso(DateTime dateTime)
        {
            return DateTimeUtilities.AsUtcKind(dateTime).ToString("O", CultureInfo.InvariantCulture);
        }
    }
}

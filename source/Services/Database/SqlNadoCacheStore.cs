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
using System.Text;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Database
{
    internal sealed class SqlNadoCacheStore : IDisposable
    {
        private sealed class CacheKeyRow
        {
            public string CacheKey { get; set; }
        }

        private sealed class ProgressGameJoinRow
        {
            public long UserGameProgressId { get; set; }
            public long GameId { get; set; }
            public string CacheKey { get; set; }
            public long PlaytimeSeconds { get; set; }
            public long NoAchievements { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string ProviderName { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string LibrarySourceName { get; set; }
        }

        private sealed class ProgressAchievementJoinRow
        {
            public long UserGameProgressId { get; set; }
            public string ApiName { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string UnlockedIconPath { get; set; }
            public string LockedIconPath { get; set; }
            public int? Points { get; set; }
            public string Category { get; set; }
            public string TrophyType { get; set; }
            public long Hidden { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
        }

        private sealed class AchievementJoinRow
        {
            public string ApiName { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string UnlockedIconPath { get; set; }
            public string LockedIconPath { get; set; }
            public int? Points { get; set; }
            public string Category { get; set; }
            public string TrophyType { get; set; }
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

        private sealed class CachedCurrentUserState
        {
            public string ExternalUserId { get; set; }
            public long UserId { get; set; }
        }

        private readonly object _sync = new object();
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly SqlNadoSchemaManager _schemaManager;
        private readonly Dictionary<string, CachedCurrentUserState> _cachedCurrentUsersByProvider =
            new Dictionary<string, CachedCurrentUserState>(StringComparer.OrdinalIgnoreCase);
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
                var exists = db.ExecuteScalar<long>(
                    @"SELECT EXISTS(
                        SELECT 1
                        FROM UserGameProgress ugp
                        INNER JOIN Users u ON u.Id = ugp.UserId
                        WHERE u.IsCurrentUser = 1
                        LIMIT 1
                      );");
                return exists != 0;
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
                        ugp.CacheKey AS CacheKey,
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

                var model = CreateModel(progress);
                var details = db.Load<AchievementJoinRow>(
                    @"SELECT
                        ad.ApiName AS ApiName,
                        ad.DisplayName AS DisplayName,
                        ad.Description AS Description,
                        ad.UnlockedIconPath AS UnlockedIconPath,
                        ad.LockedIconPath AS LockedIconPath,
                        ad.Points AS Points,
                        ad.Category AS Category,
                        ad.TrophyType AS TrophyType,
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
                        UnlockedIconPath = row.UnlockedIconPath,
                        LockedIconPath = row.LockedIconPath,
                        Points = row.Points,
                        Category = row.Category,
                        TrophyType = row.TrophyType,
                        Hidden = row.Hidden != 0,
                        GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                        UnlockTimeUtc = ParseUtc(row.UnlockTimeUtc),
                        ProgressNum = row.ProgressNum,
                        ProgressDenom = row.ProgressDenom
                    });
                }

                BackfillPlayniteGameIdFromCacheKey(model, cacheKey);

                return model;
            });
        }

        public List<KeyValuePair<string, GameAchievementData>> LoadAllCurrentUserGameDataByCacheKey()
        {
            return WithDb(db =>
            {
                var progressRows = db.Load<ProgressGameJoinRow>(
                    @"WITH LatestProgress AS (
                        SELECT
                            ugp.Id AS UserGameProgressId,
                            ugp.GameId AS GameId,
                            TRIM(ugp.CacheKey) AS CacheKey,
                            ugp.PlaytimeSeconds AS PlaytimeSeconds,
                            ugp.NoAchievements AS NoAchievements,
                            ugp.LastUpdatedUtc AS LastUpdatedUtc,
                            g.ProviderName AS ProviderName,
                            g.ProviderGameId AS ProviderGameId,
                            g.PlayniteGameId AS PlayniteGameId,
                            g.GameName AS GameName,
                            g.LibrarySourceName AS LibrarySourceName,
                            ROW_NUMBER() OVER (
                                PARTITION BY ugp.CacheKey
                                ORDER BY ugp.LastUpdatedUtc DESC, ugp.Id DESC
                            ) AS RowNum
                        FROM UserGameProgress ugp
                        INNER JOIN Users u ON u.Id = ugp.UserId
                        INNER JOIN Games g ON g.Id = ugp.GameId
                        WHERE u.IsCurrentUser = 1
                          AND ugp.CacheKey IS NOT NULL
                          AND TRIM(ugp.CacheKey) <> ''
                    )
                    SELECT
                        UserGameProgressId,
                        GameId,
                        CacheKey,
                        PlaytimeSeconds,
                        NoAchievements,
                        LastUpdatedUtc,
                        ProviderName,
                        ProviderGameId,
                        PlayniteGameId,
                        GameName,
                        LibrarySourceName
                    FROM LatestProgress
                    WHERE RowNum = 1
                    ORDER BY LastUpdatedUtc DESC, UserGameProgressId DESC;").ToList();

                if (progressRows.Count == 0)
                {
                    return new List<KeyValuePair<string, GameAchievementData>>();
                }

                var selectedByProgressId = new Dictionary<long, ProgressGameJoinRow>(progressRows.Count);
                var selectedByCacheKey = new Dictionary<string, ProgressGameJoinRow>(StringComparer.OrdinalIgnoreCase);
                var modelsByProgressId = new Dictionary<long, GameAchievementData>(progressRows.Count);
                for (int i = 0; i < progressRows.Count; i++)
                {
                    var row = progressRows[i];
                    var cacheKey = row?.CacheKey?.Trim();
                    if (string.IsNullOrWhiteSpace(cacheKey) || row == null)
                    {
                        continue;
                    }

                    row.CacheKey = cacheKey;
                    selectedByCacheKey[cacheKey] = row;
                    selectedByProgressId[row.UserGameProgressId] = row;

                    var model = CreateModel(row);
                    BackfillPlayniteGameIdFromCacheKey(model, row.CacheKey);
                    modelsByProgressId[row.UserGameProgressId] = model;
                }

                var detailRows = db.Load<ProgressAchievementJoinRow>(
                    @"WITH LatestProgress AS (
                        SELECT
                            ugp.Id AS UserGameProgressId,
                            ugp.GameId AS GameId,
                            ROW_NUMBER() OVER (
                                PARTITION BY ugp.CacheKey
                                ORDER BY ugp.LastUpdatedUtc DESC, ugp.Id DESC
                            ) AS RowNum
                        FROM UserGameProgress ugp
                        INNER JOIN Users u ON u.Id = ugp.UserId
                        WHERE u.IsCurrentUser = 1
                          AND ugp.CacheKey IS NOT NULL
                          AND TRIM(ugp.CacheKey) <> ''
                    )
                    SELECT
                        lp.UserGameProgressId AS UserGameProgressId,
                        ad.ApiName AS ApiName,
                        ad.DisplayName AS DisplayName,
                        ad.Description AS Description,
                        ad.UnlockedIconPath AS UnlockedIconPath,
                        ad.LockedIconPath AS LockedIconPath,
                        ad.Points AS Points,
                        ad.Category AS Category,
                        ad.TrophyType AS TrophyType,
                        ad.Hidden AS Hidden,
                        ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                        ua.UnlockTimeUtc AS UnlockTimeUtc,
                        ua.ProgressNum AS ProgressNum,
                        ua.ProgressDenom AS ProgressDenom
                      FROM LatestProgress lp
                      INNER JOIN AchievementDefinitions ad ON ad.GameId = lp.GameId
                      LEFT JOIN UserAchievements ua
                        ON ua.AchievementDefinitionId = ad.Id
                       AND ua.UserGameProgressId = lp.UserGameProgressId
                      WHERE lp.RowNum = 1
                      ORDER BY lp.UserGameProgressId, ad.Id;").ToList();

                for (int i = 0; i < detailRows.Count; i++)
                {
                    var row = detailRows[i];
                    if (row == null ||
                        string.IsNullOrWhiteSpace(row.ApiName) ||
                        !modelsByProgressId.TryGetValue(row.UserGameProgressId, out var model))
                    {
                        continue;
                    }

                    model.Achievements.Add(new AchievementDetail
                    {
                        ApiName = row.ApiName,
                        DisplayName = row.DisplayName,
                        Description = row.Description,
                        UnlockedIconPath = row.UnlockedIconPath,
                        LockedIconPath = row.LockedIconPath,
                        Points = row.Points,
                        Category = row.Category,
                        TrophyType = row.TrophyType,
                        Hidden = row.Hidden != 0,
                        GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                        UnlockTimeUtc = ParseUtc(row.UnlockTimeUtc),
                        ProgressNum = row.ProgressNum,
                        ProgressDenom = row.ProgressDenom
                    });
                }

                return selectedByCacheKey
                    .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(a =>
                    {
                        return modelsByProgressId.TryGetValue(a.Value.UserGameProgressId, out var model)
                            ? new KeyValuePair<string, GameAchievementData>(a.Key, model)
                            : default(KeyValuePair<string, GameAchievementData>);
                    })
                    .Where(a => !string.IsNullOrWhiteSpace(a.Key) && a.Value != null)
                    .ToList();
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
                        false,
                        updatedIso,
                        nowIso);

                    var definitionIds = UpsertAchievementDefinitions(
                        db,
                        gameId,
                        achievements,
                        nowIso,
                        updatedIso);

                    var existingRows = db.Load<UserAchievementRow>(
                        @"SELECT Id, UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, LastUpdatedUtc, CreatedUtc
                          FROM UserAchievements
                          WHERE UserGameProgressId = ?;",
                        userProgressId)
                        .ToDictionary(a => a.AchievementDefinitionId);

                    var desiredRows = new Dictionary<long, AchievementDetail>();
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

                        desiredRows[definitionId] = achievement;
                    }

                    foreach (var desired in desiredRows)
                    {
                        var definitionId = desired.Key;
                        var achievement = desired.Value;

                        var unlockTime = NormalizeUnlockTime(achievement.UnlockTimeUtc);
                        var unlocked = unlockTime.HasValue;
                        var unlockIso = unlockTime.HasValue ? ToIso(unlockTime.Value) : null;
                        var progressNum = achievement.ProgressNum;
                        var progressDenom = achievement.ProgressDenom;

                        if (!existingRows.TryGetValue(definitionId, out var existing))
                        {
                            db.ExecuteNonQuery(
                                @"INSERT INTO UserAchievements
                                    (UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, LastUpdatedUtc, CreatedUtc)
                                  VALUES
                                    (?, ?, ?, ?, ?, ?, ?, ?);",
                                userProgressId,
                                definitionId,
                                unlocked,
                                unlockIso != null ? (object)unlockIso : DBNull.Value,
                                progressNum.HasValue ? (object)progressNum.Value : DBNull.Value,
                                progressDenom.HasValue ? (object)progressDenom.Value : DBNull.Value,
                                updatedIso,
                                nowIso);
                            continue;
                        }

                        existingRows.Remove(definitionId);

                        var existingUnlockIso = NormalizeStoredIso(existing.UnlockTimeUtc);
                        var changed = existing.Unlocked != unlocked ||
                                      !NullableEquals(existingUnlockIso, unlockIso) ||
                                      existing.ProgressNum != progressNum ||
                                      existing.ProgressDenom != progressDenom;

                        if (!changed)
                        {
                            continue;
                        }

                        db.ExecuteNonQuery(
                            @"UPDATE UserAchievements
                              SET Unlocked = ?,
                                  UnlockTimeUtc = ?,
                                  ProgressNum = ?,
                                  ProgressDenom = ?,
                                  LastUpdatedUtc = ?
                              WHERE Id = ?;",
                            unlocked,
                            unlockIso != null ? (object)unlockIso : DBNull.Value,
                            progressNum.HasValue ? (object)progressNum.Value : DBNull.Value,
                            progressDenom.HasValue ? (object)progressDenom.Value : DBNull.Value,
                            updatedIso,
                            existing.Id);
                    }

                    foreach (var stale in existingRows.Values)
                    {
                        db.ExecuteNonQuery(
                            @"DELETE FROM UserAchievements
                              WHERE Id = ?;",
                            stale.Id);
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

                _cachedCurrentUsersByProvider.Clear();

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

        public void RemoveGameData(Guid playniteGameId)
        {
            if (playniteGameId == Guid.Empty)
            {
                return;
            }

            var playniteGameIdText = playniteGameId.ToString();
            WithDb(db =>
            {
                db.RunTransaction(() =>
                {
                    db.ExecuteNonQuery(
                        @"DELETE FROM UserGameProgress
                          WHERE CacheKey = ?;",
                        playniteGameIdText);

                    db.ExecuteNonQuery(
                        @"DELETE FROM Games
                          WHERE PlayniteGameId = ?;",
                        playniteGameIdText);
                });
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
            if (_cachedCurrentUsersByProvider.TryGetValue(user.ProviderName, out var cachedUser) &&
                string.Equals(cachedUser.ExternalUserId, user.ExternalUserId, StringComparison.OrdinalIgnoreCase))
            {
                var cachedIdExists = db.ExecuteScalar<long>(
                    @"SELECT Id
                      FROM Users
                      WHERE Id = ?
                      LIMIT 1;",
                    cachedUser.UserId);
                if (cachedIdExists <= 0)
                {
                    _cachedCurrentUsersByProvider.Remove(user.ProviderName);
                }
                else
                {
                    db.ExecuteNonQuery(
                        @"UPDATE Users
                          SET DisplayName = ?,
                              FriendSource = ?,
                              IsCurrentUser = 1,
                              UpdatedUtc = ?
                          WHERE Id = ?;",
                        DbValue(user.DisplayName),
                        DbValue(user.FriendSource),
                        nowIso,
                        cachedUser.UserId);
                    return cachedUser.UserId;
                }
            }

            db.ExecuteNonQuery(
                @"UPDATE Users
                  SET IsCurrentUser = 0,
                      UpdatedUtc = ?
                  WHERE ProviderName = ?
                    AND IsCurrentUser = 1;",
                nowIso,
                user.ProviderName);

            db.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO Users
                    (ProviderName, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, CreatedUtc, UpdatedUtc)
                  VALUES
                    (?, ?, ?, 0, ?, ?, ?);",
                user.ProviderName,
                user.ExternalUserId,
                DbValue(user.DisplayName),
                DbValue(user.FriendSource),
                nowIso,
                nowIso);

            var userId = db.ExecuteScalar<long>(
                @"SELECT Id
                  FROM Users
                  WHERE ProviderName = ?
                    AND ExternalUserId = ?
                  LIMIT 1;",
                user.ProviderName,
                user.ExternalUserId);

            if (userId <= 0)
            {
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
                userId = db.ExecuteScalar<long>("SELECT last_insert_rowid();");
            }

            db.ExecuteNonQuery(
                @"UPDATE Users
                  SET DisplayName = ?,
                      FriendSource = ?,
                      IsCurrentUser = 1,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                DbValue(user.DisplayName),
                DbValue(user.FriendSource),
                nowIso,
                userId);

            _cachedCurrentUsersByProvider[user.ProviderName] = new CachedCurrentUserState
            {
                ExternalUserId = user.ExternalUserId,
                UserId = userId
            };

            return userId;
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
            bool isComplete,
            string updatedIso,
            string nowIso)
        {
            var existing = db.Load<UserGameProgressRow>(
                @"SELECT Id, UserId, GameId, CacheKey, PlaytimeSeconds, NoAchievements, AchievementsUnlocked, TotalAchievements, IsComplete, LastUpdatedUtc, CreatedUtc, UpdatedUtc
                  FROM UserGameProgress
                  WHERE UserId = ? AND CacheKey = ?
                  LIMIT 1;",
                userId,
                cacheKey).FirstOrDefault();

            if (existing == null)
            {
                existing = db.Load<UserGameProgressRow>(
                    @"SELECT Id, UserId, GameId, CacheKey, PlaytimeSeconds, NoAchievements, AchievementsUnlocked, TotalAchievements, IsComplete, LastUpdatedUtc, CreatedUtc, UpdatedUtc
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
                        (UserId, GameId, CacheKey, PlaytimeSeconds, NoAchievements, AchievementsUnlocked, TotalAchievements, IsComplete, LastUpdatedUtc, CreatedUtc, UpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                    userId,
                    gameId,
                    cacheKey,
                    playtimeSeconds,
                    noAchievements,
                    achievementsUnlocked,
                    totalAchievements,
                    isComplete,
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
                      IsComplete = ?,
                      LastUpdatedUtc = ?,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                gameId,
                cacheKey,
                playtimeSeconds,
                noAchievements,
                achievementsUnlocked,
                totalAchievements,
                isComplete,
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
                db.ExecuteNonQuery(
                    @"DELETE FROM AchievementDefinitions
                      WHERE GameId = ?;",
                    gameId);
                return idsByApiName;
            }

            var desiredApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var existingByApiName = db.Load<AchievementDefinitionRow>(
                    @"SELECT Id, GameId, ApiName, DisplayName, Description, UnlockedIconPath, LockedIconPath, Points, Category, TrophyType, Hidden, GlobalPercentUnlocked, ProgressMax, CreatedUtc, UpdatedUtc
                      FROM AchievementDefinitions
                      WHERE GameId = ?;",
                    gameId)
                .Where(a => !string.IsNullOrWhiteSpace(a?.ApiName))
                .ToDictionary(a => a.ApiName.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var achievement in achievements)
            {
                if (achievement == null || string.IsNullOrWhiteSpace(achievement.ApiName))
                {
                    continue;
                }

                var apiName = achievement.ApiName.Trim();
                desiredApiNames.Add(apiName);
                if (!existingByApiName.TryGetValue(apiName, out var existing))
                {
                    db.ExecuteNonQuery(
                        @"INSERT INTO AchievementDefinitions
                            (GameId, ApiName, DisplayName, Description, UnlockedIconPath, LockedIconPath, Points, Category, TrophyType, Hidden, GlobalPercentUnlocked, ProgressMax, CreatedUtc, UpdatedUtc)
                          VALUES
                            (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                        gameId,
                        apiName,
                        DbValue(achievement.DisplayName),
                        DbValue(achievement.Description),
                        DbValue(achievement.UnlockedIconPath),
                        DbValue(achievement.LockedIconPath),
                        achievement.Points.HasValue ? (object)achievement.Points.Value : DBNull.Value,
                        DbValue(achievement.Category),
                        DbValue(achievement.TrophyType),
                        achievement.Hidden,
                        achievement.GlobalPercentUnlocked.HasValue ? (object)achievement.GlobalPercentUnlocked.Value : DBNull.Value,
                        achievement.ProgressDenom.HasValue ? (object)achievement.ProgressDenom.Value : DBNull.Value,
                        nowIso,
                        updatedIso);

                    var definitionId = db.ExecuteScalar<long>("SELECT last_insert_rowid();");
                    existingByApiName[apiName] = new AchievementDefinitionRow
                    {
                        Id = definitionId,
                        GameId = gameId,
                        ApiName = apiName
                    };
                    idsByApiName[apiName] = definitionId;
                    continue;
                }

                var incomingDisplayName = NormalizeDbText(achievement.DisplayName);
                var incomingDescription = NormalizeDbText(achievement.Description);
                var incomingUnlockedIconPath = NormalizeDbText(achievement.UnlockedIconPath);
                var incomingLockedIconPath = NormalizeDbText(achievement.LockedIconPath);
                var incomingPoints = achievement.Points;
                var incomingCategory = NormalizeDbText(achievement.Category);
                var incomingTrophyType = NormalizeDbText(achievement.TrophyType);
                var incomingHidden = achievement.Hidden;
                var incomingGlobalPercent = achievement.GlobalPercentUnlocked;
                var incomingProgressMax = achievement.ProgressDenom;

                var changed = !NullableEquals(NormalizeDbText(existing.DisplayName), incomingDisplayName) ||
                              !NullableEquals(NormalizeDbText(existing.Description), incomingDescription) ||
                              !NullableEquals(NormalizeDbText(existing.UnlockedIconPath), incomingUnlockedIconPath) ||
                              !NullableEquals(NormalizeDbText(existing.LockedIconPath), incomingLockedIconPath) ||
                              existing.Points != incomingPoints ||
                              !NullableEquals(NormalizeDbText(existing.Category), incomingCategory) ||
                              !NullableEquals(NormalizeDbText(existing.TrophyType), incomingTrophyType) ||
                              existing.Hidden != incomingHidden ||
                              existing.GlobalPercentUnlocked != incomingGlobalPercent ||
                              existing.ProgressMax != incomingProgressMax;

                if (!changed)
                {
                    idsByApiName[apiName] = existing.Id;
                    continue;
                }

                db.ExecuteNonQuery(
                    @"UPDATE AchievementDefinitions
                      SET DisplayName = ?,
                          Description = ?,
                          UnlockedIconPath = ?,
                          LockedIconPath = ?,
                          Points = ?,
                          Category = ?,
                          TrophyType = ?,
                          Hidden = ?,
                          GlobalPercentUnlocked = ?,
                          ProgressMax = ?,
                          UpdatedUtc = ?
                      WHERE Id = ?;",
                                        incomingDisplayName != null ? (object)incomingDisplayName : DBNull.Value,
                                        incomingDescription != null ? (object)incomingDescription : DBNull.Value,
                                        incomingUnlockedIconPath != null ? (object)incomingUnlockedIconPath : DBNull.Value,
                                        incomingLockedIconPath != null ? (object)incomingLockedIconPath : DBNull.Value,
                                        incomingPoints.HasValue ? (object)incomingPoints.Value : DBNull.Value,
                                        incomingCategory != null ? (object)incomingCategory : DBNull.Value,
                                        incomingTrophyType != null ? (object)incomingTrophyType : DBNull.Value,
                                        incomingHidden,
                                        incomingGlobalPercent.HasValue ? (object)incomingGlobalPercent.Value : DBNull.Value,
                                        incomingProgressMax.HasValue ? (object)incomingProgressMax.Value : DBNull.Value,
                    updatedIso,
                    existing.Id);

                idsByApiName[apiName] = existing.Id;
            }

            var existingIdsByApiName = existingByApiName.ToDictionary(
                a => a.Key,
                a => a.Value?.Id ?? 0,
                StringComparer.OrdinalIgnoreCase);

            var staleDefinitionIds = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(
                existingIdsByApiName,
                desiredApiNames);

            for (int i = 0; i < staleDefinitionIds.Count; i++)
            {
                db.ExecuteNonQuery(
                    @"DELETE FROM AchievementDefinitions
                      WHERE Id = ?;",
                    staleDefinitionIds[i]);
            }

            return idsByApiName;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _cachedCurrentUsersByProvider.Clear();
                if (_db is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch
                    {
                    }
                }

                _db = null;
                _initialized = false;
            }
        }

        private static GameAchievementData CreateModel(ProgressGameJoinRow progress)
        {
            return new GameAchievementData
            {
                LastUpdatedUtc = ParseUtc(progress?.LastUpdatedUtc) ?? DateTime.UtcNow,
                ProviderName = progress?.ProviderName,
                LibrarySourceName = progress?.LibrarySourceName,
                NoAchievements = progress != null && progress.NoAchievements != 0,
                PlaytimeSeconds = (ulong)Math.Max(0, progress?.PlaytimeSeconds ?? 0),
                GameName = progress?.GameName,
                AppId = (int)Math.Max(0, progress?.ProviderGameId ?? 0),
                PlayniteGameId = ParseGuid(progress?.PlayniteGameId),
                Achievements = new List<AchievementDetail>()
            };
        }

        private static void BackfillPlayniteGameIdFromCacheKey(GameAchievementData model, string cacheKey)
        {
            if (model?.PlayniteGameId != null || string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            if (Guid.TryParse(cacheKey.Trim(), out var parsed))
            {
                model.PlayniteGameId = parsed;
            }
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

        private static string NormalizeDbText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string NormalizeStoredIso(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parsed = ParseUtc(value);
            return parsed.HasValue ? ToIso(parsed.Value) : value.Trim();
        }

        private static bool NullableEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
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

        /// <summary>
        /// Exports all database tables to CSV files in the specified directory.
        /// Returns the path to the directory containing the CSV files.
        /// </summary>
        public string ExportToCsv(string exportDirectory)
        {
            lock (_sync)
            {
                if (_db == null)
                {
                    throw new InvalidOperationException("Database not initialized.");
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var dir = Path.Combine(exportDirectory, $"achievement_export_{timestamp}");
                Directory.CreateDirectory(dir);

                _logger.Info($"Exporting database to CSV: {dir}");

                // Export each table using typed row classes
                ExportAchievementDefinitions(dir);
                ExportUserGameProgress(dir);
                ExportUserAchievements(dir);
                ExportGames(dir);
                ExportUsers(dir);
                ExportAchievementSummary(dir);

                _logger.Info($"Database export completed: {dir}");
                return dir;
            }
        }

        private void ExportAchievementDefinitions(string dir)
        {
            var filePath = Path.Combine(dir, "AchievementDefinitions.csv");
            var rows = _db.Load<AchievementDefinitionExportRow>(
                "SELECT Id, GameId, ApiName, DisplayName, Description, " +
                "UnlockedIconPath, LockedIconPath, Points, Category, TrophyType, Hidden, " +
                "GlobalPercentUnlocked, ProgressMax, CreatedUtc, UpdatedUtc " +
                "FROM AchievementDefinitions").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "GameId", "ApiName", "DisplayName", "Description",
                "UnlockedIconPath", "LockedIconPath", "Points", "Category", "TrophyType", "Hidden",
                "GlobalPercentUnlocked", "ProgressMax", "CreatedUtc", "UpdatedUtc"
            }, r => new[] {
                r.Id?.ToString(), r.GameId?.ToString(), r.ApiName, r.DisplayName, r.Description,
                r.UnlockedIconPath, r.LockedIconPath, r.Points?.ToString(), r.Category, r.TrophyType, r.Hidden?.ToString(),
                r.GlobalPercentUnlocked?.ToString(), r.ProgressMax?.ToString(), r.CreatedUtc, r.UpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportUserGameProgress(string dir)
        {
            var filePath = Path.Combine(dir, "UserGameProgress.csv");
            var rows = _db.Load<UserGameProgressExportRow>(
                "SELECT Id, UserId, GameId, CacheKey, PlaytimeSeconds, " +
                "NoAchievements, AchievementsUnlocked, TotalAchievements, " +
                "IsComplete, LastUpdatedUtc, CreatedUtc, UpdatedUtc " +
                "FROM UserGameProgress").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "UserId", "GameId", "CacheKey", "PlaytimeSeconds",
                "NoAchievements", "AchievementsUnlocked", "TotalAchievements",
                "IsComplete", "LastUpdatedUtc", "CreatedUtc", "UpdatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.UserId.ToString(), r.GameId.ToString(), r.CacheKey, r.PlaytimeSeconds.ToString(),
                r.NoAchievements.ToString(), r.AchievementsUnlocked.ToString(), r.TotalAchievements.ToString(),
                r.IsComplete.ToString(), r.LastUpdatedUtc, r.CreatedUtc, r.UpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportUserAchievements(string dir)
        {
            var filePath = Path.Combine(dir, "UserAchievements.csv");
            var rows = _db.Load<UserAchievementExportRow>(
                "SELECT Id, UserGameProgressId, AchievementDefinitionId, " +
                "Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, " +
                "LastUpdatedUtc, CreatedUtc " +
                "FROM UserAchievements").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "UserGameProgressId", "AchievementDefinitionId",
                "Unlocked", "UnlockTimeUtc", "ProgressNum", "ProgressDenom",
                "LastUpdatedUtc", "CreatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.UserGameProgressId.ToString(), r.AchievementDefinitionId.ToString(),
                r.Unlocked.ToString(), r.UnlockTimeUtc, r.ProgressNum?.ToString(), r.ProgressDenom?.ToString(),
                r.LastUpdatedUtc, r.CreatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportGames(string dir)
        {
            var filePath = Path.Combine(dir, "Games.csv");
            var rows = _db.Load<GameExportRow>(
                "SELECT Id, ProviderName, ProviderGameId, PlayniteGameId, GameName, " +
                "LibrarySourceName, FirstSeenUtc, LastUpdatedUtc " +
                "FROM Games").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "ProviderName", "ProviderGameId", "PlayniteGameId", "GameName",
                "LibrarySourceName", "FirstSeenUtc", "LastUpdatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.ProviderName, r.ProviderGameId?.ToString(), r.PlayniteGameId, r.GameName,
                r.LibrarySourceName, r.FirstSeenUtc, r.LastUpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportUsers(string dir)
        {
            var filePath = Path.Combine(dir, "Users.csv");
            var rows = _db.Load<UserExportRow>(
                "SELECT Id, ProviderName, ExternalUserId, DisplayName, IsCurrentUser, " +
                "FriendSource, CreatedUtc, UpdatedUtc " +
                "FROM Users").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "ProviderName", "ExternalUserId", "DisplayName", "IsCurrentUser",
                "FriendSource", "CreatedUtc", "UpdatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.ProviderName, r.ExternalUserId, r.DisplayName, r.IsCurrentUser.ToString(),
                r.FriendSource, r.CreatedUtc, r.UpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportAchievementSummary(string dir)
        {
            var filePath = Path.Combine(dir, "AchievementSummary.csv");
            var rows = _db.Load<AchievementSummaryExportRow>(
                "SELECT g.GameName, g.ProviderName, g.PlayniteGameId, " +
                "ad.ApiName, ad.DisplayName AS AchievementName, ad.Description, ad.Points, ad.Category, ad.TrophyType, " +
                "ad.GlobalPercentUnlocked, ad.Hidden, " +
                "ua.Unlocked, ua.UnlockTimeUtc, u.DisplayName AS UserName " +
                "FROM AchievementDefinitions ad " +
                "JOIN Games g ON ad.GameId = g.Id " +
                "LEFT JOIN UserAchievements ua ON ua.AchievementDefinitionId = ad.Id " +
                "LEFT JOIN UserGameProgress ugp ON ua.UserGameProgressId = ugp.Id " +
                "LEFT JOIN Users u ON ugp.UserId = u.Id " +
                "ORDER BY g.GameName, ad.DisplayName").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "GameName", "ProviderName", "PlayniteGameId",
                "ApiName", "AchievementName", "Description", "Points", "Category", "TrophyType",
                "GlobalPercentUnlocked", "Hidden",
                "Unlocked", "UnlockTimeUtc", "UserName"
            }, r => new[] {
                r.GameName, r.ProviderName, r.PlayniteGameId,
                r.ApiName, r.AchievementName, r.Description, r.Points?.ToString(), r.Category, r.TrophyType,
                r.GlobalPercentUnlocked?.ToString(), r.Hidden?.ToString(),
                r.Unlocked?.ToString(), r.UnlockTimeUtc, r.UserName
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private static void WriteCsv<T>(string filePath, List<T> rows, string[] headers, Func<T, string[]> getValues)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsvField)));
                foreach (var row in rows)
                {
                    var values = getValues(row);
                    writer.WriteLine(string.Join(",", values.Select(v => EscapeCsvField(v ?? ""))));
                }
            }
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return "";
            }

            if (field.Contains(",") || field.Contains("\n") || field.Contains("\r") || field.Contains("\""))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }

            return field;
        }

        // Export row DTOs
        private sealed class AchievementDefinitionExportRow
        {
            public long? Id { get; set; }
            public long? GameId { get; set; }
            public string ApiName { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string UnlockedIconPath { get; set; }
            public string LockedIconPath { get; set; }
            public int? Points { get; set; }
            public string Category { get; set; }
            public string TrophyType { get; set; }
            public bool? Hidden { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public int? ProgressMax { get; set; }
            public string CreatedUtc { get; set; }
            public string UpdatedUtc { get; set; }
        }

        private sealed class UserGameProgressExportRow
        {
            public long Id { get; set; }
            public long UserId { get; set; }
            public long GameId { get; set; }
            public string CacheKey { get; set; }
            public long PlaytimeSeconds { get; set; }
            public bool NoAchievements { get; set; }
            public long AchievementsUnlocked { get; set; }
            public long TotalAchievements { get; set; }
            public bool IsComplete { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string CreatedUtc { get; set; }
            public string UpdatedUtc { get; set; }
        }

        private sealed class UserAchievementExportRow
        {
            public long Id { get; set; }
            public long UserGameProgressId { get; set; }
            public long AchievementDefinitionId { get; set; }
            public bool Unlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string CreatedUtc { get; set; }
        }

        private sealed class GameExportRow
        {
            public long Id { get; set; }
            public string ProviderName { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string LibrarySourceName { get; set; }
            public string FirstSeenUtc { get; set; }
            public string LastUpdatedUtc { get; set; }
        }

        private sealed class UserExportRow
        {
            public long Id { get; set; }
            public string ProviderName { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public bool IsCurrentUser { get; set; }
            public string FriendSource { get; set; }
            public string CreatedUtc { get; set; }
            public string UpdatedUtc { get; set; }
        }

        private sealed class AchievementSummaryExportRow
        {
            public string GameName { get; set; }
            public string ProviderName { get; set; }
            public string PlayniteGameId { get; set; }
            public string ApiName { get; set; }
            public string AchievementName { get; set; }
            public string Description { get; set; }
            public int? Points { get; set; }
            public string Category { get; set; }
            public string TrophyType { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public bool? Hidden { get; set; }
            public bool? Unlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public string UserName { get; set; }
        }
    }
}

using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Achievements.Scoring;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.RetroAchievements;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Providers.Steam;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Database.Rows;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.ViewModels;
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
        private const string PercentNormalizationMetadataKey = "achievement_percent_normalization_v1";

        private sealed class CacheKeyRow
        {
            public string CacheKey { get; set; }
        }

        private sealed class ProgressGameJoinRow
        {
            public long UserGameProgressId { get; set; }
            public long GameId { get; set; }
            public string CacheKey { get; set; }
            public long HasAchievements { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string ProviderKey { get; set; }
            public string ProviderPlatformKey { get; set; }
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
            public int? ScaledPoints { get; set; }
            public string Category { get; set; }
            public string CategoryType { get; set; }
            public string TrophyType { get; set; }
            public long Hidden { get; set; }
            public long IsCapstone { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
            public long? Unlocked { get; set; }
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
            public int? ScaledPoints { get; set; }
            public string Category { get; set; }
            public string CategoryType { get; set; }
            public string TrophyType { get; set; }
            public long Hidden { get; set; }
            public long IsCapstone { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
            public long? Unlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
        }

        private sealed class AchievementPercentNormalizationRow
        {
            public long Id { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
        }

        private sealed class CachedGameSummaryRow
        {
            public string CacheKey { get; set; }
            public long HasAchievements { get; set; }
            public long AchievementsUnlocked { get; set; }
            public long TotalAchievements { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string ProviderKey { get; set; }
            public string ProviderPlatformKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public long CommonCount { get; set; }
            public long UncommonCount { get; set; }
            public long RareCount { get; set; }
            public long UltraRareCount { get; set; }
            public long TotalCommonPossible { get; set; }
            public long TotalUncommonPossible { get; set; }
            public long TotalRarePossible { get; set; }
            public long TotalUltraRarePossible { get; set; }
            public long TrophyPlatinumCount { get; set; }
            public long TrophyGoldCount { get; set; }
            public long TrophySilverCount { get; set; }
            public long TrophyBronzeCount { get; set; }
            public long TrophyPlatinumTotal { get; set; }
            public long TrophyGoldTotal { get; set; }
            public long TrophySilverTotal { get; set; }
            public long TrophyBronzeTotal { get; set; }
            public long HasUnlockedCapstone { get; set; }
        }

        private sealed class CachedRecentUnlockRow
        {
            public string CacheKey { get; set; }
            public string ProviderKey { get; set; }
            public string ProviderPlatformKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string ApiName { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string UnlockedIconPath { get; set; }
            public string LockedIconPath { get; set; }
            public int? Points { get; set; }
            public int? ScaledPoints { get; set; }
            public string Category { get; set; }
            public string CategoryType { get; set; }
            public string TrophyType { get; set; }
            public long Hidden { get; set; }
            public long IsCapstone { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
            public string UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
        }

        private sealed class CachedUnlockTimelineRow
        {
            public string CacheKey { get; set; }
            public string PlayniteGameId { get; set; }
            public string UnlockDateUtc { get; set; }
            public long UnlockCount { get; set; }
        }

        private sealed class CachedUnlockedScoreRow
        {
            public string CacheKey { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
        }

        private sealed class ResolvedUser
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public string FriendSource { get; set; }
        }

        private sealed class FriendRefreshCandidateRow
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public string AvatarUrl { get; set; }
            public string LastRefreshedUtc { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public int PlaytimeForeverMinutes { get; set; }
            public string LastPlayedUtc { get; set; }
            public string LastScrapedUtc { get; set; }
            public string LastScrapeStatus { get; set; }
        }

        private sealed class FriendSummaryRow
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public string AvatarUrl { get; set; }
            public long SharedGamesCount { get; set; }
            public long GamesWithUnlocksCount { get; set; }
            public long UnlockedAchievementsCount { get; set; }
            public long RecentUnlockCount { get; set; }
            public string LastUnlockUtc { get; set; }
            public string LastRefreshedUtc { get; set; }
            public long TotalPlaytimeMinutes { get; set; }
        }

        private sealed class FriendGameSummaryRow
        {
            public string ProviderKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public long FriendCount { get; set; }
            public long FriendsWithUnlocksCount { get; set; }
            public long UnlockedAchievementsCount { get; set; }
            public long UniqueUnlockedAchievementsCount { get; set; }
            public long TotalAchievements { get; set; }
            public string LastUnlockUtc { get; set; }
            public long TotalPlaytimeMinutes { get; set; }
            public long AveragePlaytimeMinutes { get; set; }
            public string LastPlayedUtc { get; set; }
            public string LastScrapedUtc { get; set; }
            public string LastScrapeStatus { get; set; }
        }

        private sealed class FriendGameLinkRow
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
        }

        private sealed class FriendRecentUnlockRow
        {
            public string ProviderKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string FriendExternalUserId { get; set; }
            public string FriendName { get; set; }
            public string FriendAvatarUrl { get; set; }
            public string ApiName { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string UnlockedIconPath { get; set; }
            public string LockedIconPath { get; set; }
            public int? Points { get; set; }
            public int? ScaledPoints { get; set; }
            public string Category { get; set; }
            public string CategoryType { get; set; }
            public string TrophyType { get; set; }
            public long Hidden { get; set; }
            public long IsCapstone { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
            public string UnlockTimeUtc { get; set; }
            public long? MyUnlocked { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
        }

        private sealed class GamePresentation
        {
            public string SortingName { get; set; }
            public string IconPath { get; set; }
            public string CoverPath { get; set; }
            public DateTime? LastPlayed { get; set; }
            public string PlatformText { get; set; }
            public IReadOnlyList<string> Platforms { get; set; } = Array.Empty<string>();
            public string RegionText { get; set; }
            public ulong PlaytimeSeconds { get; set; }
        }

        private sealed class FriendDefinitionMatch
        {
            public long DefinitionId { get; set; }
            public FriendAchievementRow Row { get; set; }
        }

        private sealed class CachedCurrentUserState
        {
            public string ExternalUserId { get; set; }
            public long UserId { get; set; }
        }

        private sealed class CurrentUserScopeRow
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
        }

        private readonly object _sync = new object();
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly SqlNadoSchemaManager _schemaManager;
        private readonly Dictionary<string, CachedCurrentUserState> _cachedCurrentUsersByProvider =
            new Dictionary<string, CachedCurrentUserState>(StringComparer.OrdinalIgnoreCase);
        private readonly string _pluginUserDataPath;
        private SQLiteDatabase _db;
        private bool _initialized;

        public string DatabasePath { get; }

        public SqlNadoCacheStore(PlayniteAchievementsPlugin plugin, ILogger logger, string baseDir)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _pluginUserDataPath = baseDir ?? string.Empty;
            DatabasePath = Path.Combine(_pluginUserDataPath, "achievement_cache.db");
            _schemaManager = new SqlNadoSchemaManager(logger, DatabasePath, baseDir);
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

        public DateTime? GetMostRecentLastUpdatedUtc()
        {
            return WithDb(db =>
            {
                var value = db.ExecuteScalar<string>(
                    @"SELECT MAX(ugp.LastUpdatedUtc)
                      FROM UserGameProgress ugp
                      INNER JOIN Users u ON u.Id = ugp.UserId
                      WHERE u.IsCurrentUser = 1;");
                return ParseUtc(value);
            });
        }

        public string GetCurrentUserScopeToken()
        {
            return WithDb(db =>
            {
                var rows = db.Load<CurrentUserScopeRow>(
                    @"SELECT ProviderKey, ExternalUserId
                      FROM Users
                      WHERE IsCurrentUser = 1
                      ORDER BY ProviderKey, ExternalUserId;").ToList();

                if (rows.Count == 0)
                {
                    return "none";
                }

                var parts = rows
                    .Select(a =>
                    {
                        var provider = a?.ProviderKey?.Trim().ToLowerInvariant();
                        var user = a?.ExternalUserId?.Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(user))
                        {
                            return null;
                        }

                        return $"{provider}:{user}";
                    })
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                return parts.Count == 0 ? "none" : string.Join("|", parts);
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
                        ugp.HasAchievements AS HasAchievements,
                        ugp.LastUpdatedUtc AS LastUpdatedUtc,
                        g.ProviderKey AS ProviderKey,
                        g.ProviderPlatformKey AS ProviderPlatformKey,
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
                        ad.ScaledPoints AS ScaledPoints,
                        ad.Category AS Category,
                        ad.CategoryType AS CategoryType,
                        ad.TrophyType AS TrophyType,
                        ad.Hidden AS Hidden,
                        ad.IsCapstone AS IsCapstone,
                        ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                        ad.Rarity AS Rarity,
                        ua.Unlocked AS Unlocked,
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

                    var detail = new AchievementDetail
                    {
                        ApiName = row.ApiName,
                        DisplayName = row.DisplayName,
                        Description = row.Description,
                        UnlockedIconPath = MakeAbsolutePath(row.UnlockedIconPath),
                        LockedIconPath = MakeAbsolutePath(row.LockedIconPath),
                        Points = row.Points,
                        ScaledPoints = row.ScaledPoints,
                        Category = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(row.Category),
                        CategoryType = AchievementCategoryTypeHelper.NormalizeOrDefault(row.CategoryType),
                        TrophyType = row.TrophyType,
                        Hidden = row.Hidden != 0,
                        IsCapstone = row.IsCapstone != 0,
                        GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                        Rarity = ParseStoredRarity(row.Rarity),
                        UnlockTimeUtc = ParseUtc(row.UnlockTimeUtc),
                        ProgressNum = row.ProgressNum,
                        ProgressDenom = row.ProgressDenom
                    };

                    if (row.Unlocked.HasValue)
                    {
                        detail.Unlocked = row.Unlocked.Value != 0;
                    }

                    model.Achievements.Add(detail);
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
                            ugp.HasAchievements AS HasAchievements,
                            ugp.LastUpdatedUtc AS LastUpdatedUtc,
                            g.ProviderKey AS ProviderKey,
                            g.ProviderPlatformKey AS ProviderPlatformKey,
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
                        HasAchievements,
                        LastUpdatedUtc,
                        ProviderKey,
                        ProviderPlatformKey,
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
                        ad.ScaledPoints AS ScaledPoints,
                        ad.Category AS Category,
                        ad.CategoryType AS CategoryType,
                        ad.TrophyType AS TrophyType,
                        ad.Hidden AS Hidden,
                        ad.IsCapstone AS IsCapstone,
                        ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                        ad.Rarity AS Rarity,
                        ua.Unlocked AS Unlocked,
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

                    var detail = new AchievementDetail
                    {
                        ApiName = row.ApiName,
                        DisplayName = row.DisplayName,
                        Description = row.Description,
                        UnlockedIconPath = MakeAbsolutePath(row.UnlockedIconPath),
                        LockedIconPath = MakeAbsolutePath(row.LockedIconPath),
                        Points = row.Points,
                        ScaledPoints = row.ScaledPoints,
                        Category = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(row.Category),
                        CategoryType = AchievementCategoryTypeHelper.NormalizeOrDefault(row.CategoryType),
                        TrophyType = row.TrophyType,
                        Hidden = row.Hidden != 0,
                        IsCapstone = row.IsCapstone != 0,
                        GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                        Rarity = ParseStoredRarity(row.Rarity),
                        UnlockTimeUtc = ParseUtc(row.UnlockTimeUtc),
                        ProgressNum = row.ProgressNum,
                        ProgressDenom = row.ProgressDenom
                    };

                    if (row.Unlocked.HasValue)
                    {
                        detail.Unlocked = row.Unlocked.Value != 0;
                    }

                    model.Achievements.Add(detail);
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

        public CachedSummaryData LoadCachedSummaryData(int recentAchievementDetailLimit = 0)
        {
            return WithDb(db =>
            {
                var gameRows = LoadCachedGameSummaryRows(db);
                var scoreTotalsByCacheKey = LoadCachedScoreTotals(db, unlockedOnly: true);
                var possibleScoreTotalsByCacheKey = LoadCachedScoreTotals(db, unlockedOnly: false);
                var timelineRows = LoadCachedUnlockTimelineRows(db);
                var requestedRecentLimit = recentAchievementDetailLimit > 0 ? recentAchievementDetailLimit : 0;
                var boundedRecentLimit = requestedRecentLimit > 0 ? requestedRecentLimit + 1 : 0;
                var recentRows = LoadCachedRecentUnlockRows(db, boundedRecentLimit);

                var result = new CachedSummaryData();

                for (var i = 0; i < gameRows.Count; i++)
                {
                    var row = gameRows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    var cacheKey = row.CacheKey?.Trim();
                    scoreTotalsByCacheKey.TryGetValue(cacheKey ?? string.Empty, out var scoreTotals);
                    possibleScoreTotalsByCacheKey.TryGetValue(cacheKey ?? string.Empty, out var possibleScoreTotals);
                    var playniteGameId = ResolveCachedPlayniteGameId(row.CacheKey, row.PlayniteGameId);
                    result.Games.Add(new CachedGameSummaryData
                    {
                        CacheKey = cacheKey,
                        PlayniteGameId = playniteGameId,
                        ProviderKey = row.ProviderKey,
                        ProviderPlatformKey = row.ProviderPlatformKey,
                        AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                        GameName = row.GameName,
                        HasAchievements = row.HasAchievements != 0,
                        LastUpdatedUtc = ParseUtc(row.LastUpdatedUtc) ?? DateTime.UtcNow,
                        TotalAchievements = (int)Math.Max(0, row.TotalAchievements),
                        UnlockedAchievements = (int)Math.Max(0, row.AchievementsUnlocked),
                        CollectionScore = scoreTotals.CollectionScore,
                        PrestigeScore = scoreTotals.PrestigeScore,
                        CollectionScoreTotal = possibleScoreTotals.CollectionScore,
                        PrestigeScoreTotal = possibleScoreTotals.PrestigeScore,
                        CommonCount = (int)Math.Max(0, row.CommonCount),
                        UncommonCount = (int)Math.Max(0, row.UncommonCount),
                        RareCount = (int)Math.Max(0, row.RareCount),
                        UltraRareCount = (int)Math.Max(0, row.UltraRareCount),
                        TotalCommonPossible = (int)Math.Max(0, row.TotalCommonPossible),
                        TotalUncommonPossible = (int)Math.Max(0, row.TotalUncommonPossible),
                        TotalRarePossible = (int)Math.Max(0, row.TotalRarePossible),
                        TotalUltraRarePossible = (int)Math.Max(0, row.TotalUltraRarePossible),
                        TrophyPlatinumCount = (int)Math.Max(0, row.TrophyPlatinumCount),
                        TrophyGoldCount = (int)Math.Max(0, row.TrophyGoldCount),
                        TrophySilverCount = (int)Math.Max(0, row.TrophySilverCount),
                        TrophyBronzeCount = (int)Math.Max(0, row.TrophyBronzeCount),
                        TrophyPlatinumTotal = (int)Math.Max(0, row.TrophyPlatinumTotal),
                        TrophyGoldTotal = (int)Math.Max(0, row.TrophyGoldTotal),
                        TrophySilverTotal = (int)Math.Max(0, row.TrophySilverTotal),
                        TrophyBronzeTotal = (int)Math.Max(0, row.TrophyBronzeTotal),
                        IsCompleted = ((int)Math.Max(0, row.TotalAchievements) > 0 &&
                            (int)Math.Max(0, row.AchievementsUnlocked) >= (int)Math.Max(0, row.TotalAchievements)) ||
                            row.HasUnlockedCapstone != 0
                    });
                }

                for (var i = 0; i < timelineRows.Count; i++)
                {
                    var row = timelineRows[i];
                    if (row == null || row.UnlockCount <= 0)
                    {
                        continue;
                    }

                    var unlockDate = ParseUtc(row.UnlockDateUtc)?.Date;
                    if (!unlockDate.HasValue)
                    {
                        continue;
                    }

                    Increment(result.GlobalUnlockCountsByDate, unlockDate.Value, (int)Math.Max(0, row.UnlockCount));

                    var playniteGameId = ResolveCachedPlayniteGameId(row.CacheKey, row.PlayniteGameId);
                    if (!playniteGameId.HasValue)
                    {
                        continue;
                    }

                    if (!result.UnlockCountsByDateByGame.TryGetValue(playniteGameId.Value, out var gameCounts))
                    {
                        gameCounts = new Dictionary<DateTime, int>();
                        result.UnlockCountsByDateByGame[playniteGameId.Value] = gameCounts;
                    }

                    Increment(gameCounts, unlockDate.Value, (int)Math.Max(0, row.UnlockCount));
                }

                if (requestedRecentLimit > 0 && recentRows.Count > requestedRecentLimit)
                {
                    result.HasMoreRecentUnlocks = true;
                    recentRows = recentRows.Take(requestedRecentLimit).ToList();
                }

                result.RecentUnlocks = MapRecentUnlocks(recentRows);
                return result;
            });
        }

        private static List<CachedGameSummaryRow> LoadCachedGameSummaryRows(SQLiteDatabase db)
        {
            return db.Load<CachedGameSummaryRow>(
                @"WITH LatestProgress AS (
                    SELECT
                        ugp.Id AS UserGameProgressId,
                        ugp.GameId AS GameId,
                        TRIM(ugp.CacheKey) AS CacheKey,
                        ugp.HasAchievements AS HasAchievements,
                        ugp.AchievementsUnlocked AS AchievementsUnlocked,
                        ugp.TotalAchievements AS TotalAchievements,
                        ugp.LastUpdatedUtc AS LastUpdatedUtc,
                        g.ProviderKey AS ProviderKey,
                        g.ProviderPlatformKey AS ProviderPlatformKey,
                        g.ProviderGameId AS ProviderGameId,
                        g.PlayniteGameId AS PlayniteGameId,
                        g.GameName AS GameName,
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
                    lp.CacheKey AS CacheKey,
                    lp.HasAchievements AS HasAchievements,
                    lp.AchievementsUnlocked AS AchievementsUnlocked,
                    lp.TotalAchievements AS TotalAchievements,
                    lp.LastUpdatedUtc AS LastUpdatedUtc,
                    lp.ProviderKey AS ProviderKey,
                    lp.ProviderPlatformKey AS ProviderPlatformKey,
                    lp.ProviderGameId AS ProviderGameId,
                    lp.PlayniteGameId AS PlayniteGameId,
                    lp.GameName AS GameName,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'common' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS CommonCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'uncommon' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS UncommonCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'rare' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS RareCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'ultrarare' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS UltraRareCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'common' THEN 1 ELSE 0 END) AS TotalCommonPossible,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'uncommon' THEN 1 ELSE 0 END) AS TotalUncommonPossible,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'rare' THEN 1 ELSE 0 END) AS TotalRarePossible,
                    SUM(CASE WHEN LOWER(COALESCE(ad.Rarity, '')) = 'ultrarare' THEN 1 ELSE 0 END) AS TotalUltraRarePossible,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'platinum' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS TrophyPlatinumCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'gold' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS TrophyGoldCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'silver' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS TrophySilverCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'bronze' AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS TrophyBronzeCount,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'platinum' THEN 1 ELSE 0 END) AS TrophyPlatinumTotal,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'gold' THEN 1 ELSE 0 END) AS TrophyGoldTotal,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'silver' THEN 1 ELSE 0 END) AS TrophySilverTotal,
                    SUM(CASE WHEN LOWER(COALESCE(ad.TrophyType, '')) = 'bronze' THEN 1 ELSE 0 END) AS TrophyBronzeTotal,
                    MAX(CASE WHEN ad.IsCapstone = 1 AND ua.Unlocked = 1 THEN 1 ELSE 0 END) AS HasUnlockedCapstone
                FROM LatestProgress lp
                LEFT JOIN AchievementDefinitions ad ON ad.GameId = lp.GameId
                LEFT JOIN UserAchievements ua
                    ON ua.AchievementDefinitionId = ad.Id
                   AND ua.UserGameProgressId = lp.UserGameProgressId
                WHERE lp.RowNum = 1
                GROUP BY
                    lp.CacheKey,
                    lp.HasAchievements,
                    lp.AchievementsUnlocked,
                    lp.TotalAchievements,
                    lp.LastUpdatedUtc,
                    lp.ProviderKey,
                    lp.ProviderPlatformKey,
                    lp.ProviderGameId,
                    lp.PlayniteGameId,
                    lp.GameName
                ORDER BY lp.LastUpdatedUtc DESC, lp.CacheKey;").ToList();
        }

        private static Dictionary<string, (int CollectionScore, int PrestigeScore)> LoadCachedScoreTotals(
            SQLiteDatabase db,
            bool unlockedOnly)
        {
            var userAchievementJoin = unlockedOnly
                ? @"INNER JOIN UserAchievements ua
                    ON ua.AchievementDefinitionId = ad.Id
                   AND ua.UserGameProgressId = lp.UserGameProgressId
                   AND ua.Unlocked = 1"
                : string.Empty;

            var rows = db.Load<CachedUnlockedScoreRow>(
                @"WITH LatestProgress AS (
                    SELECT
                        ugp.Id AS UserGameProgressId,
                        ugp.GameId AS GameId,
                        TRIM(ugp.CacheKey) AS CacheKey,
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
                    lp.CacheKey AS CacheKey,
                    ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                    ad.Rarity AS Rarity
                FROM LatestProgress lp
                INNER JOIN AchievementDefinitions ad ON ad.GameId = lp.GameId
                " + userAchievementJoin + @"
                WHERE lp.RowNum = 1
                ORDER BY lp.CacheKey;").ToList();

            var totals = new Dictionary<string, (int CollectionScore, int PrestigeScore)>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var cacheKey = row?.CacheKey?.Trim();
                if (string.IsNullOrWhiteSpace(cacheKey))
                {
                    continue;
                }

                totals.TryGetValue(cacheKey, out var current);
                var rarity = ParseStoredRarity(row.Rarity);
                totals[cacheKey] = (
                    AddClamped(current.CollectionScore, AchievementScoreCalculator.GetCollectionValue(rarity)),
                    AddClamped(current.PrestigeScore, AchievementScoreCalculator.GetPrestigeValue(row.GlobalPercentUnlocked, rarity)));
            }

            return totals;
        }

        private static List<CachedUnlockTimelineRow> LoadCachedUnlockTimelineRows(SQLiteDatabase db)
        {
            return db.Load<CachedUnlockTimelineRow>(
                @"WITH LatestProgress AS (
                    SELECT
                        ugp.Id AS UserGameProgressId,
                        TRIM(ugp.CacheKey) AS CacheKey,
                        g.PlayniteGameId AS PlayniteGameId,
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
                    lp.CacheKey AS CacheKey,
                    lp.PlayniteGameId AS PlayniteGameId,
                    date(ua.UnlockTimeUtc) AS UnlockDateUtc,
                    COUNT(*) AS UnlockCount
                FROM LatestProgress lp
                INNER JOIN UserAchievements ua
                    ON ua.UserGameProgressId = lp.UserGameProgressId
                   AND ua.Unlocked = 1
                   AND ua.UnlockTimeUtc IS NOT NULL
                WHERE lp.RowNum = 1
                GROUP BY
                    lp.CacheKey,
                    lp.PlayniteGameId,
                    date(ua.UnlockTimeUtc)
                ORDER BY UnlockDateUtc DESC, lp.CacheKey;").ToList();
        }

        private static List<CachedRecentUnlockRow> LoadCachedRecentUnlockRows(SQLiteDatabase db, int recentAchievementLimit)
        {
            var sql = new StringBuilder(
                @"WITH LatestProgress AS (
                    SELECT
                        ugp.Id AS UserGameProgressId,
                        TRIM(ugp.CacheKey) AS CacheKey,
                        g.ProviderKey AS ProviderKey,
                        g.ProviderPlatformKey AS ProviderPlatformKey,
                        g.ProviderGameId AS ProviderGameId,
                        g.PlayniteGameId AS PlayniteGameId,
                        g.GameName AS GameName,
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
                    lp.CacheKey AS CacheKey,
                    lp.ProviderKey AS ProviderKey,
                    lp.ProviderPlatformKey AS ProviderPlatformKey,
                    lp.ProviderGameId AS ProviderGameId,
                    lp.PlayniteGameId AS PlayniteGameId,
                    lp.GameName AS GameName,
                    ad.ApiName AS ApiName,
                    ad.DisplayName AS DisplayName,
                    ad.Description AS Description,
                    ad.UnlockedIconPath AS UnlockedIconPath,
                    ad.LockedIconPath AS LockedIconPath,
                    ad.Points AS Points,
                    ad.ScaledPoints AS ScaledPoints,
                    ad.Category AS Category,
                    ad.CategoryType AS CategoryType,
                    ad.TrophyType AS TrophyType,
                    ad.Hidden AS Hidden,
                    ad.IsCapstone AS IsCapstone,
                    ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                    ad.Rarity AS Rarity,
                    ua.UnlockTimeUtc AS UnlockTimeUtc,
                    ua.ProgressNum AS ProgressNum,
                    ua.ProgressDenom AS ProgressDenom
                FROM LatestProgress lp
                INNER JOIN UserAchievements ua
                    ON ua.UserGameProgressId = lp.UserGameProgressId
                   AND ua.Unlocked = 1
                   AND ua.UnlockTimeUtc IS NOT NULL
                INNER JOIN AchievementDefinitions ad ON ad.Id = ua.AchievementDefinitionId
                WHERE lp.RowNum = 1
                ORDER BY ua.UnlockTimeUtc DESC, lp.CacheKey, ad.Id");

            if (recentAchievementLimit > 0)
            {
                sql.Append(" LIMIT ?");
                sql.Append(';');
                return db.Load<CachedRecentUnlockRow>(sql.ToString(), recentAchievementLimit).ToList();
            }

            sql.Append(';');
            return db.Load<CachedRecentUnlockRow>(sql.ToString()).ToList();
        }

        private List<CachedRecentUnlockData> MapRecentUnlocks(
            IEnumerable<CachedRecentUnlockRow> rows)
        {
            var result = new List<CachedRecentUnlockData>();
            if (rows == null)
            {
                return result;
            }

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ApiName))
                {
                    continue;
                }

                result.Add(new CachedRecentUnlockData
                {
                    CacheKey = row.CacheKey?.Trim(),
                    PlayniteGameId = ResolveCachedPlayniteGameId(row.CacheKey, row.PlayniteGameId),
                    ProviderKey = row.ProviderKey,
                    ProviderPlatformKey = row.ProviderPlatformKey,
                    AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                    GameName = row.GameName,
                    ApiName = row.ApiName,
                    DisplayName = row.DisplayName,
                    Description = row.Description,
                    UnlockedIconPath = MakeAbsolutePath(row.UnlockedIconPath),
                    LockedIconPath = MakeAbsolutePath(row.LockedIconPath),
                    Points = row.Points,
                    ScaledPoints = row.ScaledPoints,
                    Category = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(row.Category),
                    CategoryType = AchievementCategoryTypeHelper.NormalizeOrDefault(row.CategoryType),
                    TrophyType = row.TrophyType,
                    Hidden = row.Hidden != 0,
                    IsCapstone = row.IsCapstone != 0,
                    GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                    Rarity = ParseStoredRarity(row.Rarity),
                    UnlockTimeUtc = ParseUtc(row.UnlockTimeUtc),
                    ProgressNum = row.ProgressNum,
                    ProgressDenom = row.ProgressDenom
                });
            }

            return result;
        }

        private static Guid? ResolveCachedPlayniteGameId(string cacheKey, string playniteGameId)
        {
            var resolved = ParseGuid(playniteGameId);
            if (resolved.HasValue)
            {
                return resolved;
            }

            if (Guid.TryParse((cacheKey ?? string.Empty).Trim(), out var parsedGameId))
            {
                return parsedGameId;
            }

            return null;
        }

        private GamePresentation ResolveGamePresentation(Guid? playniteGameId)
        {
            if (!playniteGameId.HasValue || playniteGameId.Value == Guid.Empty)
            {
                return new GamePresentation();
            }

            var playniteGame = _plugin?.PlayniteApi?.Database?.Games?.Get(playniteGameId.Value);
            return CreateGamePresentation(playniteGame);
        }

        private GamePresentation CreateGamePresentation(Playnite.SDK.Models.Game playniteGame)
        {
            return new GamePresentation
            {
                SortingName = playniteGame?.SortingName,
                IconPath = !string.IsNullOrWhiteSpace(playniteGame?.Icon)
                    ? ResolvePlayniteAssetPath(playniteGame.Icon)
                    : null,
                CoverPath = !string.IsNullOrWhiteSpace(playniteGame?.CoverImage)
                    ? ResolvePlayniteAssetPath(playniteGame.CoverImage)
                    : null,
                LastPlayed = playniteGame?.LastActivity,
                PlatformText = PlayniteGameMetadataFormatter.GetPlatformText(playniteGame),
                Platforms = PlayniteGameMetadataFormatter.GetPlatformNames(playniteGame),
                RegionText = PlayniteGameMetadataFormatter.GetRegionText(playniteGame),
                PlaytimeSeconds = playniteGame?.Playtime ?? 0
            };
        }

        private string ResolvePlayniteAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return _plugin?.PlayniteApi?.Database?.GetFullFilePath(path) ?? path;
            }
            catch
            {
                return path;
            }
        }

        private static void Increment(IDictionary<DateTime, int> counts, DateTime date, int amount)
        {
            if (counts == null || amount <= 0)
            {
                return;
            }

            if (counts.TryGetValue(date, out var existing))
            {
                counts[date] = existing + amount;
            }
            else
            {
                counts[date] = amount;
            }
        }

        private static int AddClamped(int current, int value)
        {
            if (value <= 0)
            {
                return current;
            }

            if (current > int.MaxValue - value)
            {
                return int.MaxValue;
            }

            return current + value;
        }

        public FriendCacheWriteResult SaveFriendList(string providerKey, IReadOnlyList<FriendIdentity> friends)
        {
            providerKey = NormalizeProviderKey(providerKey);
            var friendSource = GetFriendSource(providerKey);
            var now = DateTime.UtcNow;
            var nowIso = ToIso(now);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var incomingCount = friends?.Count(friend => !string.IsNullOrWhiteSpace(friend?.ExternalUserId)) ?? 0;

            try
            {
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        foreach (var friend in friends ?? Array.Empty<FriendIdentity>())
                        {
                            if (string.IsNullOrWhiteSpace(friend?.ExternalUserId))
                            {
                                continue;
                            }

                            friend.ProviderKey = providerKey;
                            UpsertFriendUser(db, providerKey, friendSource, friend, nowIso);
                            seen.Add(friend.ExternalUserId.Trim());
                        }

                        MarkMissingFriendsInactive(db, providerKey, friendSource, seen, nowIso);
                    });
                });

                return FriendCacheWriteResult.Ok(incomingCount, seen.Count, Math.Max(0, incomingCount - seen.Count));
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to save friend list for provider={providerKey}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        public FriendCacheWriteResult SaveFriendOwnership(
            string providerKey,
            string externalUserId,
            IReadOnlyList<FriendGameOwnership> ownership)
        {
            providerKey = NormalizeProviderKey(providerKey);
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return FriendCacheWriteResult.Failed("Friend id is missing.");
            }

            var nowIso = ToIso(DateTime.UtcNow);
            var validOwnership = (ownership ?? Array.Empty<FriendGameOwnership>())
                .Where(item => item?.AppId > 0)
                .ToList();
            var incomingCount = validOwnership.Count;
            var writtenCount = 0;
            var skippedCount = 0;

            try
            {
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        var user = LoadFriendUser(db, providerKey, externalUserId);
                        if (user == null)
                        {
                            skippedCount = incomingCount;
                            return;
                        }

                        foreach (var item in validOwnership)
                        {
                            var game = LoadSharedGameByAppId(db, providerKey, item.AppId);
                            if (game == null)
                            {
                                skippedCount++;
                                continue;
                            }

                            if (!UpsertFriendOwnership(db, user.Id, game.Id, item, nowIso))
                            {
                                throw new InvalidOperationException(
                                    $"Failed to upsert FriendOwnership for userId={user.Id}, gameId={game.Id}.");
                            }

                            writtenCount++;
                        }
                    });
                });

                return FriendCacheWriteResult.Ok(incomingCount, writtenCount, skippedCount);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to save friend ownership for provider={providerKey}, friend={externalUserId}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        public FriendCacheWriteResult SaveFriendGameAchievements(
            string providerKey,
            string externalUserId,
            int appId,
            FriendGameAchievements achievements)
        {
            providerKey = NormalizeProviderKey(providerKey);
            if (string.IsNullOrWhiteSpace(externalUserId) || appId <= 0)
            {
                return FriendCacheWriteResult.Failed("Friend id or app id is missing.");
            }

            var nowIso = ToIso(DateTime.UtcNow);
            var updatedIso = ToIso(achievements?.LastUpdatedUtc == default(DateTime)
                ? DateTime.UtcNow
                : achievements.LastUpdatedUtc);

            try
            {
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        var user = LoadFriendUser(db, providerKey, externalUserId);
                        var game = LoadSharedGameByAppId(db, providerKey, appId);
                        if (user == null || game == null)
                        {
                            return;
                        }

                        var status = ResolveFriendScrapeStatus(achievements);
                        var detail = achievements?.DetailCode.ToString();

                        UpdateFriendOwnershipScrapeState(db, user.Id, game.Id, status, detail, updatedIso, nowIso);

                        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        var definitions = LoadAchievementDefinitionsForGame(db, game.Id);
                        var totalAchievements = definitions.Count;
                        var incomingRows = achievements?.Rows ?? new List<FriendAchievementRow>();
                        var matchedRows = MapFriendRowsToDefinitions(definitions, incomingRows);

                        var cacheKey = BuildFriendCacheKey(providerKey, externalUserId, game);
                        var existingProgress = LoadUserGameProgress(db, user.Id, game.Id, cacheKey);
                        var userProgressId = UpsertUserGameProgress(
                            db,
                            existingProgress,
                            user.Id,
                            game.Id,
                            cacheKey,
                            totalAchievements > 0,
                            Math.Max((int)(existingProgress?.AchievementsUnlocked ?? 0), matchedRows.Count),
                            totalAchievements,
                            updatedIso,
                            nowIso);

                        UpsertFriendAchievementRows(db, userProgressId, matchedRows, updatedIso, nowIso);

                        var unlockedCount = db.ExecuteScalar<long>(
                            @"SELECT COUNT(*)
                              FROM UserAchievements
                              WHERE UserGameProgressId = ?
                                AND Unlocked = 1;",
                            new object[] { userProgressId });

                        db.ExecuteNonQuery(
                            @"UPDATE UserGameProgress
                              SET AchievementsUnlocked = ?,
                                  TotalAchievements = ?,
                                  HasAchievements = ?,
                                  LastUpdatedUtc = ?,
                                  UpdatedUtc = ?
                              WHERE Id = ?;",
                            Math.Max(0, unlockedCount),
                            totalAchievements,
                            totalAchievements > 0 ? 1 : 0,
                            updatedIso,
                            nowIso,
                            userProgressId);
                    });
                });

                return FriendCacheWriteResult.Ok();
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to save friend achievements for provider={providerKey}, friend={externalUserId}, appId={appId}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        public List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
            string providerKey,
            FriendRefreshOptions options)
        {
            providerKey = NormalizeProviderKey(providerKey);
            options = options?.Clone() ?? new FriendRefreshOptions();
            var ttl = options.RefreshTtl.GetValueOrDefault(TimeSpan.FromHours(24));
            var cutoffIso = ToIso(DateTime.UtcNow - (ttl <= TimeSpan.Zero ? TimeSpan.FromHours(24) : ttl));

            return WithDb(db =>
            {
                if (options.Scope == FriendRefreshScope.Full ||
                    options.Scope == FriendRefreshScope.Installed ||
                    (options.Scope == FriendRefreshScope.Custom && options.PlayniteGameIds?.Count > 0))
                {
                    return LoadCurrentUserFriendRefreshCandidates(db, providerKey, options);
                }

                return LoadSharedFriendRefreshCandidates(
                    db,
                    providerKey,
                    cutoffIso,
                    options.Scope == FriendRefreshScope.Recent);
            });
        }

        private static List<FriendRefreshCandidate> LoadSharedFriendRefreshCandidates(
            SQLiteDatabase db,
            string providerKey,
            string cutoffIso,
            bool recentOnly)
        {
                var sql =
                    @"SELECT
                        u.ProviderKey AS ProviderKey,
                        u.ExternalUserId AS ExternalUserId,
                        u.DisplayName AS DisplayName,
                        u.AvatarUrl AS AvatarUrl,
                        u.LastRefreshedUtc AS LastRefreshedUtc,
                        g.ProviderGameId AS ProviderGameId,
                        g.PlayniteGameId AS PlayniteGameId,
                        g.GameName AS GameName,
                        fo.PlaytimeForeverMinutes AS PlaytimeForeverMinutes,
                        fo.LastPlayedUtc AS LastPlayedUtc,
                        fo.LastScrapedUtc AS LastScrapedUtc,
                        fo.LastScrapeStatus AS LastScrapeStatus
                      FROM FriendOwnership fo
                      INNER JOIN Users u ON u.Id = fo.UserId
                      INNER JOIN Games g ON g.Id = fo.GameId
                      WHERE u.ProviderKey = ?
                        AND u.IsCurrentUser = 0
                        AND u.IsActiveFriend = 1
                        AND g.ProviderKey = ?
                        AND g.ProviderGameId IS NOT NULL
                        AND g.ProviderGameId > 0";

            List<FriendRefreshCandidateRow> rows;
            if (recentOnly)
            {
                rows = db.Load<FriendRefreshCandidateRow>(
                    sql + @" AND (
                            fo.LastScrapedUtc IS NULL
                            OR fo.LastScrapedUtc = ''
                            OR fo.LastScrapedUtc < ?
                            OR fo.LastScrapeStatus = 'transient'
                            OR COALESCE(fo.Playtime2WeeksMinutes, 0) > 0
                          )
                          ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, fo.PlaytimeForeverMinutes DESC, u.DisplayName, g.GameName;",
                    providerKey,
                    providerKey,
                    cutoffIso).ToList();
            }
            else
            {
                rows = db.Load<FriendRefreshCandidateRow>(
                    sql + " ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, fo.PlaytimeForeverMinutes DESC, u.DisplayName, g.GameName;",
                    providerKey,
                    providerKey).ToList();
            }

            return MapFriendRefreshCandidates(rows);
        }

        private static List<FriendRefreshCandidate> LoadCurrentUserFriendRefreshCandidates(
            SQLiteDatabase db,
            string providerKey,
            FriendRefreshOptions options)
        {
            var ids = options.PlayniteGameIds?
                .Where(id => id != Guid.Empty)
                .Select(id => id.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            var sql = new StringBuilder(
                @"WITH CurrentGames AS (
                    SELECT DISTINCT
                        g.Id AS Id,
                        g.ProviderKey AS ProviderKey,
                        g.ProviderPlatformKey AS ProviderPlatformKey,
                        g.ProviderGameId AS ProviderGameId,
                        g.PlayniteGameId AS PlayniteGameId,
                        g.GameName AS GameName,
                        g.LibrarySourceName AS LibrarySourceName,
                        g.FirstSeenUtc AS FirstSeenUtc,
                        g.LastUpdatedUtc AS LastUpdatedUtc
                    FROM Games g
                    INNER JOIN UserGameProgress ugp ON ugp.GameId = g.Id
                    INNER JOIN Users cu ON cu.Id = ugp.UserId
                    WHERE cu.ProviderKey = ?
                      AND cu.IsCurrentUser = 1
                      AND g.ProviderKey = ?
                      AND g.ProviderGameId IS NOT NULL
                      AND g.ProviderGameId > 0
                      AND g.PlayniteGameId IS NOT NULL
                      AND TRIM(g.PlayniteGameId) <> ''");

            var args = new List<object> { providerKey, providerKey };
            if (ids.Count > 0)
            {
                sql.Append(" AND g.PlayniteGameId IN (");
                sql.Append(string.Join(",", ids.Select(_ => "?")));
                sql.Append(')');
                args.AddRange(ids.Cast<object>());
            }

            sql.Append(
                @")
                  SELECT
                    u.ProviderKey AS ProviderKey,
                    u.ExternalUserId AS ExternalUserId,
                    u.DisplayName AS DisplayName,
                    u.AvatarUrl AS AvatarUrl,
                    u.LastRefreshedUtc AS LastRefreshedUtc,
                    g.ProviderGameId AS ProviderGameId,
                    g.PlayniteGameId AS PlayniteGameId,
                    g.GameName AS GameName,
                    COALESCE(fo.PlaytimeForeverMinutes, 0) AS PlaytimeForeverMinutes,
                    fo.LastPlayedUtc AS LastPlayedUtc,
                    fo.LastScrapedUtc AS LastScrapedUtc,
                    fo.LastScrapeStatus AS LastScrapeStatus
                  FROM Users u
                  CROSS JOIN CurrentGames g
                  LEFT JOIN FriendOwnership fo ON fo.UserId = u.Id AND fo.GameId = g.Id
                  WHERE u.ProviderKey = ?
                    AND u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                  ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, COALESCE(fo.PlaytimeForeverMinutes, 0) DESC, u.DisplayName, g.GameName;");

            args.Add(providerKey);
            return MapFriendRefreshCandidates(db.Load<FriendRefreshCandidateRow>(sql.ToString(), args.ToArray()).ToList());
        }

        private static List<FriendRefreshCandidate> MapFriendRefreshCandidates(IEnumerable<FriendRefreshCandidateRow> rows)
        {
            return (rows ?? Enumerable.Empty<FriendRefreshCandidateRow>())
                .Where(row => row?.ProviderGameId > 0)
                .Select(row => new FriendRefreshCandidate
                {
                    Friend = new FriendIdentity
                    {
                        ProviderKey = row.ProviderKey,
                        ExternalUserId = row.ExternalUserId,
                        DisplayName = row.DisplayName,
                        AvatarUrl = row.AvatarUrl,
                        LastRefreshedUtc = ParseUtc(row.LastRefreshedUtc)
                    },
                    AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                    PlayniteGameId = ParseGuid(row.PlayniteGameId),
                    GameName = row.GameName,
                    PlaytimeForeverMinutes = Math.Max(0, row.PlaytimeForeverMinutes),
                    LastPlayedUtc = ParseUtc(row.LastPlayedUtc),
                    LastScrapedUtc = ParseUtc(row.LastScrapedUtc),
                    LastScrapeStatus = row.LastScrapeStatus
                })
                .ToList();
        }

        public FriendsOverviewData LoadFriendsOverviewData(bool _, int recentLimit)
        {
            return WithDb(db =>
            {
                var data = new FriendsOverviewData();

                data.Friends = LoadFriendSummaryRows(db)
                    .Select(row => new FriendSummaryItem
                    {
                        ProviderKey = row.ProviderKey,
                        ExternalUserId = row.ExternalUserId,
                        DisplayName = row.DisplayName,
                        AvatarUrl = row.AvatarUrl,
                        SharedGamesCount = (int)Math.Max(0, row.SharedGamesCount),
                        GamesWithUnlocksCount = (int)Math.Max(0, row.GamesWithUnlocksCount),
                        UnlockedAchievementsCount = (int)Math.Max(0, row.UnlockedAchievementsCount),
                        RecentUnlockCount = (int)Math.Max(0, row.RecentUnlockCount),
                        LastUnlockUtc = ParseUtc(row.LastUnlockUtc),
                        LastRefreshedUtc = ParseUtc(row.LastRefreshedUtc),
                        TotalPlaytimeMinutes = Math.Max(0, row.TotalPlaytimeMinutes)
                    })
                    .ToList();

                data.Games = LoadFriendGameSummaryRows(db)
                    .Select(row =>
                    {
                        var playniteGameId = ResolveCachedPlayniteGameId(null, row.PlayniteGameId);
                        var presentation = ResolveGamePresentation(playniteGameId);
                        var providerName = ProviderRegistry.GetLocalizedName(row.ProviderKey);
                        var item = new FriendGameSummaryItem
                        {
                            ProviderKey = row.ProviderKey,
                            Provider = string.IsNullOrWhiteSpace(providerName) ? row.ProviderKey : providerName,
                            ProviderIconKey = string.IsNullOrWhiteSpace(row.ProviderKey) ? null : "ProviderIcon" + row.ProviderKey,
                            ProviderColorHex = "#888888",
                            AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                            PlayniteGameId = playniteGameId,
                            GameName = row.GameName,
                            SortingName = presentation.SortingName ?? row.GameName,
                            GameLogo = presentation.IconPath,
                            GameCoverPath = presentation.CoverPath,
                            PlatformText = presentation.PlatformText,
                            Platforms = presentation.Platforms,
                            RegionText = presentation.RegionText,
                            PlaytimeSeconds = presentation.PlaytimeSeconds,
                            LastPlayed = presentation.LastPlayed,
                            FriendCount = (int)Math.Max(0, row.FriendCount),
                            FriendsWithUnlocksCount = (int)Math.Max(0, row.FriendsWithUnlocksCount),
                            FriendUnlockedAchievementsCount = (int)Math.Max(0, row.UnlockedAchievementsCount),
                            UniqueFriendUnlockedAchievementsCount = (int)Math.Max(0, row.UniqueUnlockedAchievementsCount),
                            TotalAchievements = (int)Math.Max(0, row.TotalAchievements),
                            LastFriendUnlockUtc = ParseUtc(row.LastUnlockUtc),
                            TotalFriendPlaytimeMinutes = Math.Max(0, row.TotalPlaytimeMinutes),
                            AverageFriendPlaytimeMinutes = Math.Max(0, row.AveragePlaytimeMinutes),
                            LastFriendPlayedUtc = ParseUtc(row.LastPlayedUtc),
                            LastFriendScrapedUtc = ParseUtc(row.LastScrapedUtc),
                            LastFriendScrapeStatus = row.LastScrapeStatus
                        };

                        return item;
                    })
                    .ToList();

                data.FriendGameLinks = LoadFriendGameLinkRows(db)
                    .Select(row => new FriendGameLinkItem
                    {
                        ProviderKey = row.ProviderKey,
                        ExternalUserId = row.ExternalUserId,
                        AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                        PlayniteGameId = ResolveCachedPlayniteGameId(null, row.PlayniteGameId)
                    })
                    .ToList();

                data.RecentUnlocks = MapFriendRecentUnlocks(LoadFriendRecentUnlockRows(db, recentLimit));
                data.AllUnlockedAchievements = MapFriendRecentUnlocks(LoadFriendUnlockedAchievementRows(db));
                ApplyFriendSummaryScores(data.Friends, data.AllUnlockedAchievements);
                return data;
            });
        }

        private static void ApplyFriendSummaryScores(
            IEnumerable<FriendSummaryItem> friends,
            IEnumerable<FriendAchievementDisplayItem> unlockedAchievements)
        {
            var friendList = friends?.Where(friend => friend != null).ToList();
            if (friendList == null || friendList.Count == 0)
            {
                return;
            }

            var scoresByFriend = new Dictionary<string, (int CollectionScore, int PrestigeScore)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var achievement in unlockedAchievements ?? Enumerable.Empty<FriendAchievementDisplayItem>())
            {
                var key = BuildFriendScoreKey(achievement?.ProviderKey, achievement?.FriendExternalUserId);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                scoresByFriend.TryGetValue(key, out var current);
                scoresByFriend[key] = (
                    AddClamped(current.CollectionScore, achievement.CollectionScore),
                    AddClamped(current.PrestigeScore, achievement.PrestigeScore));
            }

            foreach (var friend in friendList)
            {
                if (scoresByFriend.TryGetValue(BuildFriendScoreKey(friend.ProviderKey, friend.ExternalUserId), out var scores))
                {
                    friend.CollectionScore = scores.CollectionScore;
                    friend.PrestigeScore = scores.PrestigeScore;
                }
            }
        }

        private static string BuildFriendScoreKey(string providerKey, string externalUserId)
        {
            if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(externalUserId))
            {
                return null;
            }

            return providerKey.Trim() + "\u001f" + externalUserId.Trim();
        }

        private static string GetFriendSource(string providerKey) =>
            string.IsNullOrWhiteSpace(providerKey) ? "Friends" : providerKey.Trim() + "Friends";

        private static string BuildFriendCacheKey(string providerKey, string externalUserId, GameRow game)
        {
            var gamePart = !string.IsNullOrWhiteSpace(game?.PlayniteGameId)
                ? game.PlayniteGameId.Trim()
                : (game?.ProviderGameId?.ToString(CultureInfo.InvariantCulture) ?? "unknown");
            return $"friend:{providerKey}:{externalUserId}:{gamePart}";
        }

        private long UpsertFriendUser(
            SQLiteDatabase db,
            string providerKey,
            string friendSource,
            FriendIdentity friend,
            string nowIso)
        {
            var externalUserId = friend.ExternalUserId.Trim();
            var displayName = string.IsNullOrWhiteSpace(friend.DisplayName)
                ? externalUserId
                : friend.DisplayName.Trim();
            var lastRefreshedIso = ToIso(friend.LastRefreshedUtc ?? DateTime.UtcNow);

            db.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO Users
                    (ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, AvatarUrl, LastRefreshedUtc, IsActiveFriend, CreatedUtc, UpdatedUtc)
                  VALUES
                    (?, ?, ?, 0, ?, ?, ?, 1, ?, ?);",
                providerKey,
                externalUserId,
                DbValue(displayName),
                DbValue(friendSource),
                DbValue(friend.AvatarUrl),
                lastRefreshedIso,
                nowIso,
                nowIso);

            var userId = db.ExecuteScalar<long>(
                @"SELECT Id
                  FROM Users
                  WHERE ProviderKey = ?
                    AND ExternalUserId = ?
                  LIMIT 1;",
                providerKey,
                externalUserId);

            if (userId <= 0)
            {
                return 0;
            }

            db.ExecuteNonQuery(
                @"UPDATE Users
                  SET DisplayName = ?,
                      FriendSource = ?,
                      AvatarUrl = ?,
                      LastRefreshedUtc = ?,
                      IsActiveFriend = CASE WHEN IsCurrentUser = 1 THEN IsActiveFriend ELSE 1 END,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                DbValue(displayName),
                DbValue(friendSource),
                DbValue(friend.AvatarUrl),
                lastRefreshedIso,
                nowIso,
                userId);

            return userId;
        }

        private static void MarkMissingFriendsInactive(
            SQLiteDatabase db,
            string providerKey,
            string friendSource,
            HashSet<string> seenExternalUserIds,
            string nowIso)
        {
            if (seenExternalUserIds == null || seenExternalUserIds.Count == 0)
            {
                db.ExecuteNonQuery(
                    @"UPDATE Users
                      SET IsActiveFriend = 0,
                          UpdatedUtc = ?
                      WHERE ProviderKey = ?
                        AND IsCurrentUser = 0
                        AND FriendSource = ?;",
                    nowIso,
                    providerKey,
                    friendSource);
                return;
            }

            var placeholders = string.Join(",", seenExternalUserIds.Select(_ => "?"));
            var args = new List<object> { nowIso, providerKey, friendSource };
            args.AddRange(seenExternalUserIds.Cast<object>());
            db.ExecuteNonQuery(
                @"UPDATE Users
                  SET IsActiveFriend = 0,
                      UpdatedUtc = ?
                  WHERE ProviderKey = ?
                    AND IsCurrentUser = 0
                    AND FriendSource = ?
                    AND ExternalUserId NOT IN (" + placeholders + ");",
                args.ToArray());
        }

        private static UserRow LoadFriendUser(SQLiteDatabase db, string providerKey, string externalUserId)
        {
            return db.Load<UserRow>(
                @"SELECT Id, ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, AvatarUrl, LastRefreshedUtc, IsActiveFriend, CreatedUtc, UpdatedUtc
                  FROM Users
                  WHERE ProviderKey = ?
                    AND ExternalUserId = ?
                    AND IsCurrentUser = 0
                    AND IsActiveFriend = 1
                  LIMIT 1;",
                providerKey,
                externalUserId.Trim()).FirstOrDefault();
        }

        private static GameRow LoadSharedGameByAppId(SQLiteDatabase db, string providerKey, int appId)
        {
            return db.Load<GameRow>(
                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                  FROM Games
                  WHERE ProviderKey = ?
                    AND ProviderGameId = ?
                    AND PlayniteGameId IS NOT NULL
                    AND TRIM(PlayniteGameId) <> ''
                  ORDER BY LastUpdatedUtc DESC, Id DESC
                  LIMIT 1;",
                providerKey,
                appId).FirstOrDefault();
        }

        private static bool UpsertFriendOwnership(
            SQLiteDatabase db,
            long userId,
            long gameId,
            FriendGameOwnership item,
            string nowIso)
        {
            var lastPlayedIso = item.LastPlayedUtc.HasValue ? ToIso(item.LastPlayedUtc.Value) : null;
            var existingId = db.ExecuteScalar<long>(
                @"SELECT Id
                  FROM FriendOwnership
                  WHERE UserId = ?
                    AND GameId = ?
                  LIMIT 1;",
                new object[] { userId, gameId });

            if (existingId <= 0)
            {
                db.ExecuteNonQuery(
                    @"INSERT INTO FriendOwnership
                        (UserId, GameId, PlaytimeForeverMinutes, Playtime2WeeksMinutes, LastPlayedUtc, LastOwnershipRefreshUtc, CreatedUtc, UpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?, ?);",
                    userId,
                    gameId,
                    Math.Max(0, item.PlaytimeForeverMinutes),
                    item.Playtime2WeeksMinutes.HasValue ? (object)Math.Max(0, item.Playtime2WeeksMinutes.Value) : DBNull.Value,
                    DbValue(lastPlayedIso),
                    nowIso,
                    nowIso,
                    nowIso);
                return FriendOwnershipExists(db, userId, gameId);
            }

            db.ExecuteNonQuery(
                @"UPDATE FriendOwnership
                  SET PlaytimeForeverMinutes = ?,
                      Playtime2WeeksMinutes = ?,
                      LastPlayedUtc = ?,
                      LastOwnershipRefreshUtc = ?,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                Math.Max(0, item.PlaytimeForeverMinutes),
                item.Playtime2WeeksMinutes.HasValue ? (object)Math.Max(0, item.Playtime2WeeksMinutes.Value) : DBNull.Value,
                DbValue(lastPlayedIso),
                nowIso,
                nowIso,
                existingId);

            return FriendOwnershipExists(db, userId, gameId);
        }

        private static void DeleteStaleFriendOwnership(SQLiteDatabase db, long userId, HashSet<long> seenGameIds)
        {
            if (seenGameIds == null || seenGameIds.Count == 0)
            {
                db.ExecuteNonQuery("DELETE FROM FriendOwnership WHERE UserId = ?;", userId);
                return;
            }

            var placeholders = string.Join(",", seenGameIds.Select(_ => "?"));
            var args = new List<object> { userId };
            args.AddRange(seenGameIds.Cast<object>());
            db.ExecuteNonQuery(
                "DELETE FROM FriendOwnership WHERE UserId = ? AND GameId NOT IN (" + placeholders + ");",
                args.ToArray());
        }

        private static int CountFriendOwnershipRows(SQLiteDatabase db, long userId)
        {
            if (db == null || userId <= 0)
            {
                return 0;
            }

            return (int)Math.Max(0, db.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM FriendOwnership WHERE UserId = ?;",
                new object[] { userId }));
        }

        private static bool FriendOwnershipExists(SQLiteDatabase db, long userId, long gameId)
        {
            if (db == null || userId <= 0 || gameId <= 0)
            {
                return false;
            }

            return db.ExecuteScalar<long>(
                @"SELECT EXISTS(
                    SELECT 1
                    FROM FriendOwnership
                    WHERE UserId = ?
                      AND GameId = ?
                    LIMIT 1
                  );",
                new object[] { userId, gameId }) != 0;
        }

        private static string ResolveFriendScrapeStatus(FriendGameAchievements achievements)
        {
            if (achievements == null || achievements.TransientFailure)
            {
                return "transient";
            }

            return achievements.StatsUnavailable ? "unavailable" : "ok";
        }

        private static void UpdateFriendOwnershipScrapeState(
            SQLiteDatabase db,
            long userId,
            long gameId,
            string status,
            string detail,
            string updatedIso,
            string nowIso)
        {
            var existingId = db.ExecuteScalar<long>(
                @"SELECT Id
                  FROM FriendOwnership
                  WHERE UserId = ?
                    AND GameId = ?
                  LIMIT 1;",
                new object[] { userId, gameId });

            if (existingId <= 0)
            {
                db.ExecuteNonQuery(
                    @"INSERT INTO FriendOwnership
                        (UserId, GameId, PlaytimeForeverMinutes, LastScrapedUtc, LastScrapeStatus, LastScrapeDetail, CreatedUtc, UpdatedUtc)
                      VALUES
                        (?, ?, 0, ?, ?, ?, ?, ?);",
                    userId,
                    gameId,
                    updatedIso,
                    DbValue(status),
                    DbValue(detail),
                    nowIso,
                    nowIso);
                return;
            }

            db.ExecuteNonQuery(
                @"UPDATE FriendOwnership
                  SET LastScrapedUtc = ?,
                      LastScrapeStatus = ?,
                      LastScrapeDetail = ?,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                updatedIso,
                DbValue(status),
                DbValue(detail),
                nowIso,
                existingId);
        }

        private static List<AchievementDefinitionRow> LoadAchievementDefinitionsForGame(SQLiteDatabase db, long gameId)
        {
            return db.Load<AchievementDefinitionRow>(
                @"SELECT Id, GameId, ApiName, DisplayName, Description, UnlockedIconPath, LockedIconPath,
                         Points, ScaledPoints, Category, CategoryType, TrophyType, Hidden, IsCapstone,
                         GlobalPercentUnlocked, Rarity, ProgressMax, CreatedUtc, UpdatedUtc
                  FROM AchievementDefinitions
                  WHERE GameId = ?
                  ORDER BY Id;",
                gameId).ToList();
        }

        private static List<FriendDefinitionMatch> MapFriendRowsToDefinitions(
            List<AchievementDefinitionRow> definitions,
            IEnumerable<FriendAchievementRow> rows)
        {
            var result = new List<FriendDefinitionMatch>();
            if (definitions == null || definitions.Count == 0 || rows == null)
            {
                return result;
            }

            var usedDefinitionIds = new HashSet<long>();
            foreach (var row in rows.Where(row => row?.Unlocked == true))
            {
                var definition = ResolveFriendDefinition(definitions, row, usedDefinitionIds);
                if (definition == null)
                {
                    continue;
                }

                usedDefinitionIds.Add(definition.Id);
                result.Add(new FriendDefinitionMatch
                {
                    DefinitionId = definition.Id,
                    Row = row
                });
            }

            return result;
        }

        private static AchievementDefinitionRow ResolveFriendDefinition(
            List<AchievementDefinitionRow> definitions,
            FriendAchievementRow row,
            HashSet<long> usedDefinitionIds)
        {
            var rowDisplay = NormalizeMatchText(row.DisplayName);
            var rowDescription = NormalizeMatchText(row.Description);

            var exact = definitions
                .Where(def => !usedDefinitionIds.Contains(def.Id) &&
                              string.Equals(NormalizeMatchText(def.DisplayName), rowDisplay, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(NormalizeMatchText(def.Description), rowDescription, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exact.Count == 1)
            {
                return exact[0];
            }

            var byName = definitions
                .Where(def => !usedDefinitionIds.Contains(def.Id) &&
                              string.Equals(NormalizeMatchText(def.DisplayName), rowDisplay, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }

            var iconFile = ExtractIconFilename(row.IconUrl);
            if (!string.IsNullOrWhiteSpace(iconFile))
            {
                var byIcon = definitions
                    .Where(def => !usedDefinitionIds.Contains(def.Id) &&
                                  (string.Equals(ExtractIconFilename(def.UnlockedIconPath), iconFile, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ExtractIconFilename(def.LockedIconPath), iconFile, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (byIcon.Count == 1)
                {
                    return byIcon[0];
                }
            }

            return null;
        }

        private static void UpsertFriendAchievementRows(
            SQLiteDatabase db,
            long userProgressId,
            List<FriendDefinitionMatch> matchedRows,
            string updatedIso,
            string nowIso)
        {
            if (matchedRows == null || matchedRows.Count == 0)
            {
                return;
            }

            var existingRows = db.Load<UserAchievementRow>(
                @"SELECT Id, UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, LastUpdatedUtc, CreatedUtc
                  FROM UserAchievements
                  WHERE UserGameProgressId = ?;",
                userProgressId)
                .ToDictionary(row => row.AchievementDefinitionId);

            foreach (var match in matchedRows)
            {
                var incoming = match.Row;
                var incomingTime = NormalizeUnlockTime(incoming.UnlockTimeUtc);
                var incomingIso = incomingTime.HasValue ? ToIso(incomingTime.Value) : null;
                var progressNum = incoming.ProgressNum;
                var progressDenom = incoming.ProgressDenom;

                if (!existingRows.TryGetValue(match.DefinitionId, out var existing))
                {
                    db.ExecuteNonQuery(
                        @"INSERT INTO UserAchievements
                            (UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, LastUpdatedUtc, CreatedUtc)
                          VALUES
                            (?, ?, 1, ?, ?, ?, ?, ?);",
                        userProgressId,
                        match.DefinitionId,
                        DbValue(incomingIso),
                        progressNum.HasValue ? (object)progressNum.Value : DBNull.Value,
                        progressDenom.HasValue ? (object)progressDenom.Value : DBNull.Value,
                        updatedIso,
                        nowIso);
                    continue;
                }

                var existingIso = NormalizeStoredIso(existing.UnlockTimeUtc);
                var resolvedIso = existingIso ?? incomingIso;
                var resolvedProgressNum = progressNum ?? existing.ProgressNum;
                var resolvedProgressDenom = progressDenom ?? existing.ProgressDenom;

                var changed = existing.Unlocked != 1 ||
                              !NullableEquals(existingIso, resolvedIso) ||
                              existing.ProgressNum != resolvedProgressNum ||
                              existing.ProgressDenom != resolvedProgressDenom;
                if (!changed)
                {
                    continue;
                }

                db.ExecuteNonQuery(
                    @"UPDATE UserAchievements
                      SET Unlocked = 1,
                          UnlockTimeUtc = ?,
                          ProgressNum = ?,
                          ProgressDenom = ?,
                          LastUpdatedUtc = ?
                      WHERE Id = ?;",
                    DbValue(resolvedIso),
                    resolvedProgressNum.HasValue ? (object)resolvedProgressNum.Value : DBNull.Value,
                    resolvedProgressDenom.HasValue ? (object)resolvedProgressDenom.Value : DBNull.Value,
                    updatedIso,
                    existing.Id);
            }
        }

        private static List<FriendSummaryRow> LoadFriendSummaryRows(SQLiteDatabase db)
        {
            var recentCutoffIso = ToIso(DateTime.UtcNow.AddDays(-30));
            return db.Load<FriendSummaryRow>(
                @"SELECT
                    u.ProviderKey AS ProviderKey,
                    u.ExternalUserId AS ExternalUserId,
                    u.DisplayName AS DisplayName,
                    u.AvatarUrl AS AvatarUrl,
                    (
                        SELECT COUNT(DISTINCT fo.GameId)
                        FROM FriendOwnership fo
                        WHERE fo.UserId = u.Id
                    ) AS SharedGamesCount,
                    (
                        SELECT COUNT(DISTINCT ugp.GameId)
                        FROM UserGameProgress ugp
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE ugp.UserId = u.Id
                    ) AS GamesWithUnlocksCount,
                    (
                        SELECT COUNT(DISTINCT ua.Id)
                        FROM UserGameProgress ugp
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE ugp.UserId = u.Id
                    ) AS UnlockedAchievementsCount,
                    (
                        SELECT COUNT(DISTINCT ua.Id)
                        FROM UserGameProgress ugp
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE ugp.UserId = u.Id
                          AND ua.UnlockTimeUtc IS NOT NULL
                          AND ua.UnlockTimeUtc >= ?
                    ) AS RecentUnlockCount,
                    (
                        SELECT MAX(ua.UnlockTimeUtc)
                        FROM UserGameProgress ugp
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE ugp.UserId = u.Id
                    ) AS LastUnlockUtc,
                    u.LastRefreshedUtc AS LastRefreshedUtc,
                    (
                        SELECT COALESCE(SUM(fo.PlaytimeForeverMinutes), 0)
                        FROM FriendOwnership fo
                        WHERE fo.UserId = u.Id
                    ) AS TotalPlaytimeMinutes
                  FROM Users u
                  WHERE u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                    AND u.FriendSource IS NOT NULL
                  ORDER BY LastUnlockUtc DESC, u.DisplayName;",
                recentCutoffIso).ToList();
        }

        private static List<FriendGameSummaryRow> LoadFriendGameSummaryRows(SQLiteDatabase db)
        {
            return db.Load<FriendGameSummaryRow>(
                @"SELECT
                    g.ProviderKey AS ProviderKey,
                    g.ProviderGameId AS ProviderGameId,
                    g.PlayniteGameId AS PlayniteGameId,
                    g.GameName AS GameName,
                    (
                        SELECT COUNT(DISTINCT u.Id)
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS FriendCount,
                    (
                        SELECT COUNT(DISTINCT u.Id)
                        FROM Users u
                        INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id AND ugp.GameId = g.Id
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS FriendsWithUnlocksCount,
                    (
                        SELECT COUNT(DISTINCT ua.Id)
                        FROM Users u
                        INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id AND ugp.GameId = g.Id
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS UnlockedAchievementsCount,
                    (
                        SELECT COUNT(DISTINCT ua.AchievementDefinitionId)
                        FROM Users u
                        INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id AND ugp.GameId = g.Id
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS UniqueUnlockedAchievementsCount,
                    (
                        SELECT COUNT(DISTINCT ad.Id)
                        FROM AchievementDefinitions ad
                        WHERE ad.GameId = g.Id
                    ) AS TotalAchievements,
                    (
                        SELECT MAX(ua.UnlockTimeUtc)
                        FROM Users u
                        INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id AND ugp.GameId = g.Id
                        INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                        WHERE u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS LastUnlockUtc,
                    (
                        SELECT COALESCE(SUM(fo.PlaytimeForeverMinutes), 0)
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS TotalPlaytimeMinutes,
                    (
                        SELECT COALESCE(AVG(fo.PlaytimeForeverMinutes), 0)
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS AveragePlaytimeMinutes,
                    (
                        SELECT MAX(fo.LastPlayedUtc)
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS LastPlayedUtc,
                    (
                        SELECT MAX(fo.LastScrapedUtc)
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    ) AS LastScrapedUtc,
                    (
                        SELECT fo.LastScrapeStatus
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                          AND fo.LastScrapeStatus IS NOT NULL
                        ORDER BY fo.LastScrapedUtc DESC
                        LIMIT 1
                    ) AS LastScrapeStatus
                  FROM Games g
                  WHERE EXISTS (
                        SELECT 1
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE fo.GameId = g.Id
                          AND u.IsCurrentUser = 0
                          AND u.IsActiveFriend = 1
                          AND u.FriendSource IS NOT NULL
                    )
                    AND g.PlayniteGameId IS NOT NULL
                    AND TRIM(g.PlayniteGameId) <> ''
                  ORDER BY LastUnlockUtc DESC, g.GameName;").ToList();
        }

        private static List<FriendGameLinkRow> LoadFriendGameLinkRows(SQLiteDatabase db)
        {
            return db.Load<FriendGameLinkRow>(
                @"SELECT DISTINCT
                    g.ProviderKey AS ProviderKey,
                    u.ExternalUserId AS ExternalUserId,
                    g.ProviderGameId AS ProviderGameId,
                    g.PlayniteGameId AS PlayniteGameId
                  FROM Users u
                  INNER JOIN FriendOwnership fo ON fo.UserId = u.Id
                  INNER JOIN Games g ON g.Id = fo.GameId
                  WHERE u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                    AND u.FriendSource IS NOT NULL
                    AND g.PlayniteGameId IS NOT NULL
                    AND TRIM(g.PlayniteGameId) <> ''
                  ORDER BY u.ExternalUserId, g.GameName;").ToList();
        }

        private static List<FriendRecentUnlockRow> LoadFriendRecentUnlockRows(
            SQLiteDatabase db,
            int recentLimit)
        {
            return LoadFriendAchievementRows(db, recentLimit, requireUnlockTime: true);
        }

        private static List<FriendRecentUnlockRow> LoadFriendUnlockedAchievementRows(SQLiteDatabase db)
        {
            return LoadFriendAchievementRows(db, 0, requireUnlockTime: false);
        }

        private static List<FriendRecentUnlockRow> LoadFriendAchievementRows(
            SQLiteDatabase db,
            int recentLimit,
            bool requireUnlockTime)
        {
            var sql = new StringBuilder(
                @"SELECT
                    g.ProviderKey AS ProviderKey,
                    g.ProviderGameId AS ProviderGameId,
                    g.PlayniteGameId AS PlayniteGameId,
                    g.GameName AS GameName,
                    u.ExternalUserId AS FriendExternalUserId,
                    u.DisplayName AS FriendName,
                    u.AvatarUrl AS FriendAvatarUrl,
                    ad.ApiName AS ApiName,
                    ad.DisplayName AS DisplayName,
                    ad.Description AS Description,
                    ad.UnlockedIconPath AS UnlockedIconPath,
                    ad.LockedIconPath AS LockedIconPath,
                    ad.Points AS Points,
                    ad.ScaledPoints AS ScaledPoints,
                    ad.Category AS Category,
                    ad.CategoryType AS CategoryType,
                    ad.TrophyType AS TrophyType,
                    ad.Hidden AS Hidden,
                    ad.IsCapstone AS IsCapstone,
                    ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                    ad.Rarity AS Rarity,
                    ua.UnlockTimeUtc AS UnlockTimeUtc,
                    COALESCE(sua.Unlocked, 0) AS MyUnlocked,
                    ua.ProgressNum AS ProgressNum,
                    ua.ProgressDenom AS ProgressDenom
                  FROM Users u
                  INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id
                  INNER JOIN Games g ON g.Id = ugp.GameId
                  INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                  INNER JOIN AchievementDefinitions ad ON ad.Id = ua.AchievementDefinitionId
                  LEFT JOIN Users su ON su.ProviderKey = g.ProviderKey AND su.IsCurrentUser = 1
                  LEFT JOIN UserGameProgress sugp ON sugp.UserId = su.Id AND sugp.GameId = g.Id
                  LEFT JOIN UserAchievements sua ON sua.UserGameProgressId = sugp.Id
                      AND sua.AchievementDefinitionId = ad.Id
                  WHERE u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                    AND u.FriendSource IS NOT NULL");

            if (requireUnlockTime)
            {
                sql.Append(" AND ua.UnlockTimeUtc IS NOT NULL");
            }

            sql.Append(" ORDER BY ua.UnlockTimeUtc DESC, u.DisplayName, g.GameName, ad.Id");
            if (recentLimit > 0)
            {
                sql.Append(" LIMIT ?");
                return db.Load<FriendRecentUnlockRow>(sql.ToString(), recentLimit).ToList();
            }

            return db.Load<FriendRecentUnlockRow>(sql.ToString()).ToList();
        }

        private List<FriendAchievementDisplayItem> MapFriendRecentUnlocks(IEnumerable<FriendRecentUnlockRow> rows)
        {
            var result = new List<FriendAchievementDisplayItem>();
            foreach (var row in rows ?? Enumerable.Empty<FriendRecentUnlockRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ApiName))
                {
                    continue;
                }

                var friendUnlockTimeUtc = ParseUtc(row.UnlockTimeUtc);
                var isUnlockedByMe = row.MyUnlocked.GetValueOrDefault() != 0;
                var detail = new AchievementDetail
                {
                    ApiName = row.ApiName,
                    DisplayName = row.DisplayName,
                    Description = row.Description,
                    UnlockedIconPath = MakeAbsolutePath(row.UnlockedIconPath),
                    LockedIconPath = MakeAbsolutePath(row.LockedIconPath),
                    Points = row.Points,
                    ScaledPoints = row.ScaledPoints,
                    Category = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(row.Category),
                    CategoryType = AchievementCategoryTypeHelper.NormalizeOrDefault(row.CategoryType),
                    TrophyType = row.TrophyType,
                    Hidden = row.Hidden != 0,
                    IsCapstone = row.IsCapstone != 0,
                    GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                    Rarity = ParseStoredRarity(row.Rarity),
                    UnlockTimeUtc = friendUnlockTimeUtc,
                    Unlocked = isUnlockedByMe,
                    ProgressNum = row.ProgressNum,
                    ProgressDenom = row.ProgressDenom
                };

                var playniteGameId = ResolveCachedPlayniteGameId(null, row.PlayniteGameId);
                var presentation = ResolveGamePresentation(playniteGameId);
                var item = new FriendAchievementDisplayItem
                {
                    ApiName = detail.ApiName,
                    DisplayName = detail.DisplayName,
                    Description = detail.Description,
                    UnlockedIconPath = detail.UnlockedIconPath,
                    LockedIconPath = detail.LockedIconPath,
                    PointsValue = detail.ScaledPoints ?? detail.Points,
                    CategoryLabel = detail.Category,
                    CategoryType = detail.CategoryType,
                    TrophyType = detail.TrophyType,
                    Hidden = detail.Hidden,
                    IsCapstone = detail.IsCapstone,
                    GlobalPercentUnlocked = detail.GlobalPercentUnlocked,
                    Rarity = detail.Rarity,
                    UnlockTimeUtc = friendUnlockTimeUtc,
                    Unlocked = isUnlockedByMe,
                    ProgressNum = detail.ProgressNum,
                    ProgressDenom = detail.ProgressDenom,
                    ProviderKey = row.ProviderKey,
                    AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                    GameName = row.GameName,
                    SortingName = presentation.SortingName ?? row.GameName,
                    PlayniteGameId = playniteGameId,
                    GameIconPath = presentation.IconPath,
                    GameCoverPath = presentation.CoverPath,
                    FriendName = row.FriendName,
                    FriendExternalUserId = row.FriendExternalUserId,
                    FriendAvatarUrl = row.FriendAvatarUrl
                };
                item.ApplyAppearanceSettings(_plugin?.Settings, playniteGameId);

                result.Add(item);
            }

            return result;
        }

        private static string NormalizeMatchText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string ExtractIconFilename(string iconUrl)
        {
            if (string.IsNullOrWhiteSpace(iconUrl))
            {
                return null;
            }

            var queryIndex = iconUrl.IndexOf('?');
            if (queryIndex > 0)
            {
                iconUrl = iconUrl.Substring(0, queryIndex);
            }

            var lastSlash = iconUrl.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash >= iconUrl.Length - 1)
            {
                lastSlash = iconUrl.LastIndexOf('\\');
            }

            if (lastSlash < 0 || lastSlash >= iconUrl.Length - 1)
            {
                return null;
            }

            return iconUrl.Substring(lastSlash + 1);
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

            var providerKey = NormalizeProviderKey(payload.ProviderKey);
            var resolvedUser = ResolveCurrentUser(providerKey);
            var nowIso = ToIso(DateTime.UtcNow);
            var updatedIso = ToIso(payload.LastUpdatedUtc);

            var achievements = payload.Achievements ?? new List<AchievementDetail>();
            NormalizeIncomingAchievements(achievements);
            var unlockedCount = achievements.Count(IsUnlocked);
            var totalCount = achievements.Count;

            WithDb(db =>
            {
                db.RunTransaction(() =>
                {
                    // If creating an Unmapped stub, check for existing real provider data and use that instead
                    string effectiveProviderKey = providerKey;
                    ResolvedUser effectiveUser = resolvedUser;
                    if (string.Equals(providerKey, "Unmapped", StringComparison.OrdinalIgnoreCase))
                    {
                        var existingRealProvider = FindExistingRealProviderGame(db, cacheKey);
                        if (existingRealProvider != null)
                        {
                            effectiveProviderKey = existingRealProvider.ProviderKey;
                            effectiveUser = ResolveCurrentUser(effectiveProviderKey);
                        }
                    }

                    var userId = UpsertCurrentUser(db, effectiveUser, nowIso);
                    var gameId = UpsertGame(db, effectiveProviderKey, payload, nowIso, updatedIso);
                    var existingProgress = LoadUserGameProgress(db, userId, gameId, cacheKey);
                    if (existingProgress == null)
                    {
                        // Reuse stale Exophase proxy progress when a native provider reclaims the same game.
                        existingProgress = LoadReclaimableExophaseProgress(db, cacheKey, effectiveProviderKey);
                    }
                    var previousGameId = existingProgress?.GameId;

                    // Use payload.HasAchievements directly - callers are responsible for setting it correctly
                    // Default is true; only false when a scan explicitly finds no achievements
                    var hasAchievements = payload.HasAchievements;

                    var userProgressId = UpsertUserGameProgress(
                        db,
                        existingProgress,
                        userId,
                        gameId,
                        cacheKey,
                        hasAchievements,
                        unlockedCount,
                        totalCount,
                        updatedIso,
                        nowIso);

                    if (previousGameId.HasValue && previousGameId.Value != gameId)
                    {
                        db.ExecuteNonQuery(
                            @"DELETE FROM Games
                              WHERE Id = ?
                                AND NOT EXISTS (SELECT 1 FROM UserGameProgress WHERE GameId = Games.Id);",
                            previousGameId.Value);
                    }

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
                        var unlocked = IsUnlocked(achievement) ? 1L : 0L;
                        var unlockIso = unlocked != 0 && unlockTime.HasValue ? ToIso(unlockTime.Value) : null;
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

                    // Deduplication: When saving real provider data, remove Unmapped stubs for the same game
                    if (!string.Equals(effectiveProviderKey, "Unmapped", StringComparison.OrdinalIgnoreCase))
                    {
                        RemoveUnmappedStubsForGame(db, cacheKey, userProgressId);
                    }
                });
            });
        }

        private GameRow FindExistingRealProviderGame(SQLiteDatabase db, string cacheKey)
        {
            // Find a Game entry for this CacheKey from a non-Unmapped provider
            return db.Load<GameRow>(
                                @"SELECT g.Id, g.ProviderKey, g.ProviderPlatformKey, g.ProviderGameId, g.PlayniteGameId, g.GameName, g.LibrarySourceName, g.FirstSeenUtc, g.LastUpdatedUtc
                  FROM Games g
                  WHERE g.PlayniteGameId = ?
                    AND g.ProviderKey <> 'Unmapped'
                    AND g.ProviderKey IS NOT NULL
                  ORDER BY g.LastUpdatedUtc DESC
                  LIMIT 1;",
                cacheKey).FirstOrDefault();
        }

        private void RemoveUnmappedStubsForGame(
            SQLiteDatabase db,
            string cacheKey,
            long realProgressId)
        {
            // Find Unmapped UserGameProgress entries for the same CacheKey
            var unmappedProgress = db.Load<UserGameProgressRow>(
                @"SELECT ugp.Id, ugp.UserId, ugp.GameId, ugp.CacheKey, ugp.HasAchievements,
                         ugp.AchievementsUnlocked, ugp.TotalAchievements,
                         ugp.LastUpdatedUtc, ugp.CreatedUtc, ugp.UpdatedUtc
                  FROM UserGameProgress ugp
                  INNER JOIN Users u ON ugp.UserId = u.Id
                  WHERE ugp.CacheKey = ?
                    AND u.ProviderKey = 'Unmapped'
                    AND ugp.Id <> ?;",
                cacheKey,
                realProgressId).ToList();

            if (unmappedProgress.Count == 0)
            {
                return;
            }

            foreach (var stub in unmappedProgress)
            {
                // Delete UserAchievements for the stub
                db.ExecuteNonQuery(
                    @"DELETE FROM UserAchievements
                      WHERE UserGameProgressId = ?;",
                    stub.Id);

                // Delete the stub's UserGameProgress
                db.ExecuteNonQuery(
                    @"DELETE FROM UserGameProgress
                      WHERE Id = ?;",
                    stub.Id);

                // Delete the stub's Game entry if no other progress references it
                db.ExecuteNonQuery(
                    @"DELETE FROM Games
                      WHERE Id = ?
                        AND NOT EXISTS (SELECT 1 FROM UserGameProgress WHERE GameId = Games.Id);",
                    stub.GameId);

                // Delete AchievementDefinitions for the stub's game if no other progress references them
                db.ExecuteNonQuery(
                    @"DELETE FROM AchievementDefinitions
                      WHERE GameId = ?
                        AND NOT EXISTS (
                            SELECT 1 FROM UserAchievements ua
                            INNER JOIN UserGameProgress ugp ON ua.UserGameProgressId = ugp.Id
                            WHERE ua.AchievementDefinitionId = AchievementDefinitions.Id
                        );",
                    stub.GameId);

                _logger?.Debug($"Removed Unmapped stub for CacheKey={cacheKey}, stubProgressId={stub.Id}, " +
                              $"replaced by realProgressId={realProgressId}");
            }
        }

        public void ClearCacheData()
        {
            lock (_sync)
            {
                // Close the database connection
                if (_db is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch { }
                }
                _db = null;
                _initialized = false;
                _cachedCurrentUsersByProvider.Clear();

                // Delete the database file and WAL-related files
                try
                {
                    var dbPath = DatabasePath;
                    var filesToDelete = new[]
                    {
                        dbPath,
                        dbPath + "-wal",
                        dbPath + "-shm"
                    };

                    foreach (var file in filesToDelete)
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            _logger?.Info($"Deleted database file: {file}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, $"Failed to delete database files: {DatabasePath}");
                }
            }
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

            try
            {
                _db = new SQLiteDatabase(
                    DatabasePath,
                    SQLiteOpenOptions.SQLITE_OPEN_READWRITE |
                    SQLiteOpenOptions.SQLITE_OPEN_CREATE |
                    SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX);

                _db.EnableStatementsCache = true;
                _schemaManager.EnsureSchema(_db);
                EnsureLegacyAchievementPercentNormalization(_db);
                _initialized = true;
            }
            catch
            {
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
                throw;
            }
        }

        private void EnsureLegacyAchievementPercentNormalization(SQLiteDatabase db)
        {
            var status = GetMetadataValue(db, PercentNormalizationMetadataKey);
            if (string.Equals(status, "done", StringComparison.Ordinal))
            {
                return;
            }

            db.RunTransaction(() =>
            {
                var rows = db.Load<AchievementPercentNormalizationRow>(
                    @"SELECT
                        ad.Id AS Id,
                        ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                        ad.Rarity AS Rarity
                      FROM AchievementDefinitions ad;").ToList();

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    var normalizedPercent = NormalizeStoredPercent(
                        row.GlobalPercentUnlocked,
                        convertLegacyRatio: true);
                    var storedRarity = ParseStoredRarity(row.Rarity);
                    var resolvedRarity = normalizedPercent.HasValue
                        ? PercentRarityHelper.GetRarityTier(normalizedPercent.Value)
                        : storedRarity;

                    if (row.GlobalPercentUnlocked == normalizedPercent &&
                        storedRarity == resolvedRarity)
                    {
                        continue;
                    }

                    db.ExecuteNonQuery(
                        @"UPDATE AchievementDefinitions
                          SET GlobalPercentUnlocked = ?,
                              Rarity = ?
                          WHERE Id = ?;",
                        normalizedPercent.HasValue ? (object)normalizedPercent.Value : DBNull.Value,
                        resolvedRarity.ToString(),
                        row.Id);
                }

                SetMetadataValue(db, PercentNormalizationMetadataKey, "done");
            });
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

        private static string GetMetadataValue(SQLiteDatabase db, string key)
        {
            if (db == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var row = db.Load<CacheMetadataRow>(
                "SELECT Key, Value FROM CacheMetadata WHERE Key = ? LIMIT 1;",
                key.Trim()).FirstOrDefault();
            return row?.Value;
        }

        private static void SetMetadataValue(SQLiteDatabase db, string key, string value)
        {
            if (db == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            db.ExecuteNonQuery(
                "INSERT OR REPLACE INTO CacheMetadata (Key, Value) VALUES (?, ?);",
                key.Trim(),
                value ?? string.Empty);
        }

        private long UpsertCurrentUser(SQLiteDatabase db, ResolvedUser user, string nowIso)
        {
            if (_cachedCurrentUsersByProvider.TryGetValue(user.ProviderKey, out var cachedUser) &&
                string.Equals(cachedUser.ExternalUserId, user.ExternalUserId, StringComparison.OrdinalIgnoreCase))
            {
                var cachedIdExists = db.ExecuteScalar<long>(
                    @"SELECT Id
                      FROM Users
                      WHERE Id = ?
                      LIMIT 1;",
                    new object[] { cachedUser.UserId });
                if (cachedIdExists <= 0)
                {
                    _cachedCurrentUsersByProvider.Remove(user.ProviderKey);
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
                  WHERE ProviderKey = ?
                    AND IsCurrentUser = 1;",
                nowIso,
                user.ProviderKey);

            db.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO Users
                    (ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, CreatedUtc, UpdatedUtc)
                  VALUES
                    (?, ?, ?, 0, ?, ?, ?);",
                user.ProviderKey,
                user.ExternalUserId,
                DbValue(user.DisplayName),
                DbValue(user.FriendSource),
                nowIso,
                nowIso);

            var userId = db.ExecuteScalar<long>(
                @"SELECT Id
                  FROM Users
                  WHERE ProviderKey = ?
                    AND ExternalUserId = ?
                  LIMIT 1;",
                user.ProviderKey,
                user.ExternalUserId);

            if (userId <= 0)
            {
                db.ExecuteNonQuery(
                    @"INSERT INTO Users
                        (ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, CreatedUtc, UpdatedUtc)
                      VALUES
                        (?, ?, ?, 1, ?, ?, ?);",
                    user.ProviderKey,
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

            _cachedCurrentUsersByProvider[user.ProviderKey] = new CachedCurrentUserState
            {
                ExternalUserId = user.ExternalUserId,
                UserId = userId
            };

            return userId;
        }

        private long UpsertGame(SQLiteDatabase db, string providerKey, GameAchievementData data, string nowIso, string updatedIso)
        {
            var playniteGameId = data.PlayniteGameId?.ToString();
            long? providerGameId = data.AppId > 0 ? data.AppId : (long?)null;
            var isRetroAchievements = SqlNadoCacheBehavior.IsRetroAchievementsProvider(providerKey);

            GameRow game = null;
            if (!string.IsNullOrWhiteSpace(playniteGameId))
            {
                game = db.Load<GameRow>(
                                        @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderKey = ? AND PlayniteGameId = ?
                      LIMIT 1;",
                    providerKey,
                    playniteGameId).FirstOrDefault();
            }

            if (game == null &&
                SqlNadoCacheBehavior.ShouldFallbackToProviderGameIdLookup(providerKey, playniteGameId, providerGameId))
            {
                game = db.Load<GameRow>(
                                        @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderKey = ? AND ProviderGameId = ?
                      LIMIT 1;",
                    providerKey,
                    providerGameId.Value).FirstOrDefault();
            }

            if (game == null)
            {
                if (isRetroAchievements && providerGameId.HasValue && !string.IsNullOrWhiteSpace(playniteGameId))
                {
                    var mirroredRows = db.Load<GameRow>(
                                                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                          FROM Games
                          WHERE ProviderKey = ?
                            AND ProviderGameId = ?
                            AND (PlayniteGameId IS NULL OR PlayniteGameId <> ?)
                          ORDER BY LastUpdatedUtc DESC, Id DESC
                          LIMIT 5;",
                        providerKey,
                        providerGameId.Value,
                        playniteGameId).ToList();

                    if (mirroredRows.Count > 0)
                    {
                        var existingIds = string.Join(
                            ",",
                            mirroredRows.Select(a => string.IsNullOrWhiteSpace(a?.PlayniteGameId) ? "(null)" : a.PlayniteGameId));
                        _logger?.Debug(
                            $"[Cache][RA] Mirroring providerGameId={providerGameId.Value} to playniteGameId={playniteGameId}; " +
                            $"existingPlayniteGameIds={existingIds}");
                    }
                }

                db.ExecuteNonQuery(
                    @"INSERT INTO Games
                        (ProviderKey, ProviderPlatformKey, ProviderGameId, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?, ?);",
                    providerKey,
                    DbValue(data.ProviderPlatformKey),
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
                  SET ProviderPlatformKey = ?,
                      ProviderGameId = ?,
                      PlayniteGameId = ?,
                      GameName = ?,
                      LibrarySourceName = ?,
                      LastUpdatedUtc = ?
                  WHERE Id = ?;",
                DbValue(data.ProviderPlatformKey),
                providerGameId.HasValue ? (object)providerGameId.Value : DBNull.Value,
                DbValue(playniteGameId),
                DbValue(data.GameName),
                DbValue(data.LibrarySourceName),
                updatedIso,
                game.Id);

            return game.Id;
        }

        private UserGameProgressRow LoadUserGameProgress(
            SQLiteDatabase db,
            long userId,
            long gameId,
            string cacheKey)
        {
            var existing = db.Load<UserGameProgressRow>(
                @"SELECT Id, UserId, GameId, CacheKey, HasAchievements, AchievementsUnlocked, TotalAchievements, LastUpdatedUtc, CreatedUtc, UpdatedUtc
                  FROM UserGameProgress
                  WHERE UserId = ? AND CacheKey = ?
                  LIMIT 1;",
                userId,
                cacheKey).FirstOrDefault();

            if (existing != null)
            {
                return existing;
            }

            return db.Load<UserGameProgressRow>(
                @"SELECT Id, UserId, GameId, CacheKey, HasAchievements, AchievementsUnlocked, TotalAchievements, LastUpdatedUtc, CreatedUtc, UpdatedUtc
                  FROM UserGameProgress
                  WHERE UserId = ? AND GameId = ?
                  LIMIT 1;",
                userId,
                gameId).FirstOrDefault();
        }

        private UserGameProgressRow LoadReclaimableExophaseProgress(
            SQLiteDatabase db,
            string cacheKey,
            string incomingProviderKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) ||
                !SqlNadoCacheBehavior.CanReclaimExophaseProxy(incomingProviderKey))
            {
                return null;
            }

            return db.Load<UserGameProgressRow>(
                @"SELECT ugp.Id, ugp.UserId, ugp.GameId, ugp.CacheKey, ugp.HasAchievements,
                         ugp.AchievementsUnlocked, ugp.TotalAchievements,
                         ugp.LastUpdatedUtc, ugp.CreatedUtc, ugp.UpdatedUtc
                  FROM UserGameProgress ugp
                  INNER JOIN Users u ON ugp.UserId = u.Id
                  INNER JOIN Games g ON ugp.GameId = g.Id
                  WHERE ugp.CacheKey = ?
                    AND u.IsCurrentUser = 1
                    AND g.ProviderKey = 'Exophase'
                    AND (
                        g.ProviderPlatformKey IS NULL
                        OR TRIM(g.ProviderPlatformKey) = ''
                        OR g.ProviderPlatformKey = 'Unknown'
                        OR g.ProviderPlatformKey = ?
                    )
                  ORDER BY ugp.LastUpdatedUtc DESC, ugp.Id DESC
                  LIMIT 1;",
                cacheKey,
                incomingProviderKey.Trim()).FirstOrDefault();
        }

        private long UpsertUserGameProgress(
            SQLiteDatabase db,
            UserGameProgressRow existing,
            long userId,
            long gameId,
            string cacheKey,
            bool hasAchievements,
            int achievementsUnlocked,
            int totalAchievements,
            string updatedIso,
            string nowIso)
        {
            if (existing == null)
            {
                db.ExecuteNonQuery(
                    @"INSERT INTO UserGameProgress
                        (UserId, GameId, CacheKey, HasAchievements, AchievementsUnlocked, TotalAchievements, LastUpdatedUtc, CreatedUtc, UpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?, ?, ?);",
                    userId,
                    gameId,
                    cacheKey,
                    hasAchievements ? 1 : 0,
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
                      HasAchievements = ?,
                      AchievementsUnlocked = ?,
                      TotalAchievements = ?,
                      LastUpdatedUtc = ?,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                gameId,
                cacheKey,
                hasAchievements ? 1 : 0,
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
                db.ExecuteNonQuery(
                    @"DELETE FROM AchievementDefinitions
                      WHERE GameId = ?;",
                    gameId);
                return idsByApiName;
            }

            var desiredApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var existingByApiName = db.Load<AchievementDefinitionRow>(
                    @"SELECT Id, GameId, ApiName, DisplayName, Description, UnlockedIconPath, LockedIconPath, Points, ScaledPoints, Category, CategoryType, TrophyType, Hidden, IsCapstone, GlobalPercentUnlocked, Rarity, ProgressMax, CreatedUtc, UpdatedUtc
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
                var incomingCategory = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(achievement.Category);
                var incomingCategoryType = AchievementCategoryTypeHelper.NormalizeOrDefault(achievement.CategoryType);
                var incomingGlobalPercent = NormalizeStoredPercent(achievement.GlobalPercentUnlocked);
                var incomingRarity = achievement.Rarity.ToString();

                // Compute IsCapstone: provider-set value or auto-detect platinum trophies.
                // Manual capstones from settings are applied on top at load time.
                var isCapstone = achievement.IsCapstone ||
                    string.Equals(achievement.TrophyType?.Trim(), "platinum", StringComparison.OrdinalIgnoreCase);

                if (!existingByApiName.TryGetValue(apiName, out var existing))
                {
                    db.ExecuteNonQuery(
                        @"INSERT INTO AchievementDefinitions
                            (GameId, ApiName, DisplayName, Description, UnlockedIconPath, LockedIconPath, Points, ScaledPoints, Category, CategoryType, TrophyType, Hidden, IsCapstone, GlobalPercentUnlocked, Rarity, ProgressMax, CreatedUtc, UpdatedUtc)
                          VALUES
                            (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                        gameId,
                        apiName,
                        DbValue(achievement.DisplayName),
                        DbValue(achievement.Description),
                        DbValue(MakeRelativePath(achievement.UnlockedIconPath)),
                        DbValue(MakeRelativePath(achievement.LockedIconPath)),
                        achievement.Points.HasValue ? (object)achievement.Points.Value : DBNull.Value,
                        achievement.ScaledPoints.HasValue ? (object)achievement.ScaledPoints.Value : DBNull.Value,
                        DbValue(incomingCategory),
                        DbValue(incomingCategoryType),
                        DbValue(achievement.TrophyType),
                        achievement.Hidden ? 1 : 0,
                        isCapstone ? 1 : 0,
                        incomingGlobalPercent.HasValue ? (object)incomingGlobalPercent.Value : DBNull.Value,
                        incomingRarity,
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
                var incomingUnlockedIconPath = MakeRelativePath(NormalizeDbText(achievement.UnlockedIconPath));
                var incomingLockedIconPath = MakeRelativePath(NormalizeDbText(achievement.LockedIconPath));
                var incomingPoints = achievement.Points;
                var incomingScaledPoints = achievement.ScaledPoints;
                var incomingTrophyType = NormalizeDbText(achievement.TrophyType);
                var incomingHidden = achievement.Hidden ? 1L : 0L;
                var incomingIsCapstone = isCapstone ? 1L : 0L;
                var incomingStoredRarity = incomingRarity;
                var incomingProgressMax = achievement.ProgressDenom;

                var changed = !NullableEquals(NormalizeDbText(existing.DisplayName), incomingDisplayName) ||
                              !NullableEquals(NormalizeDbText(existing.Description), incomingDescription) ||
                              !NullableEquals(NormalizeDbText(existing.UnlockedIconPath), incomingUnlockedIconPath) ||
                              !NullableEquals(NormalizeDbText(existing.LockedIconPath), incomingLockedIconPath) ||
                              existing.Points != incomingPoints ||
                              existing.ScaledPoints != incomingScaledPoints ||
                              !NullableEquals(NormalizeDbText(existing.Category), incomingCategory) ||
                              !NullableEquals(NormalizeDbText(existing.CategoryType), incomingCategoryType) ||
                              !NullableEquals(NormalizeDbText(existing.TrophyType), incomingTrophyType) ||
                              existing.Hidden != incomingHidden ||
                              existing.IsCapstone != incomingIsCapstone ||
                              existing.GlobalPercentUnlocked != incomingGlobalPercent ||
                              !NullableEquals(NormalizeDbText(existing.Rarity), incomingStoredRarity) ||
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
                          ScaledPoints = ?,
                          Category = ?,
                          CategoryType = ?,
                          TrophyType = ?,
                          Hidden = ?,
                          IsCapstone = ?,
                          GlobalPercentUnlocked = ?,
                          Rarity = ?,
                          ProgressMax = ?,
                          UpdatedUtc = ?
                      WHERE Id = ?;",
                                        incomingDisplayName != null ? (object)incomingDisplayName : DBNull.Value,
                                        incomingDescription != null ? (object)incomingDescription : DBNull.Value,
                                        incomingUnlockedIconPath != null ? (object)incomingUnlockedIconPath : DBNull.Value,
                                        incomingLockedIconPath != null ? (object)incomingLockedIconPath : DBNull.Value,
                                        incomingPoints.HasValue ? (object)incomingPoints.Value : DBNull.Value,
                                        incomingScaledPoints.HasValue ? (object)incomingScaledPoints.Value : DBNull.Value,
                                        incomingCategory != null ? (object)incomingCategory : DBNull.Value,
                                        incomingCategoryType != null ? (object)incomingCategoryType : DBNull.Value,
                                        incomingTrophyType != null ? (object)incomingTrophyType : DBNull.Value,
                                        incomingHidden,
                                        incomingIsCapstone,
                                        incomingGlobalPercent.HasValue ? (object)incomingGlobalPercent.Value : DBNull.Value,
                                        incomingStoredRarity,
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

            // Only delete stale definitions if we have incoming definitions to replace them.
            // This protects against API failures returning empty lists that would accidentally
            // delete all existing achievement data.
            if (desiredApiNames.Count > 0 && staleDefinitionIds.Count > 0)
            {
                for (int i = 0; i < staleDefinitionIds.Count; i++)
                {
                    db.ExecuteNonQuery(
                        @"DELETE FROM AchievementDefinitions
                          WHERE Id = ?;",
                        staleDefinitionIds[i]);
                }
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
                ProviderKey = progress?.ProviderKey,
                ProviderPlatformKey = progress?.ProviderPlatformKey,
                LibrarySourceName = progress?.LibrarySourceName,
                HasAchievements = progress != null && progress.HasAchievements != 0,
                // ExcludedByUser is populated by callers from settings
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

        private ResolvedUser ResolveCurrentUser(string providerKey)
        {
            var settings = _plugin?.Settings?.Persisted;
            string externalId = null;
            string displayName = null;

            if (string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                externalId = ProviderRegistry.Settings<SteamSettings>().SteamUserId;
            }
            else if (string.Equals(providerKey, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
            {
                externalId = ProviderRegistry.Settings<RetroAchievementsSettings>().RaUsername;
            }

            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = "unmapped";
            }

            displayName = externalId;
            return new ResolvedUser
            {
                ProviderKey = providerKey,
                ExternalUserId = externalId.Trim(),
                DisplayName = displayName,
                FriendSource = null
            };
        }

        private static string NormalizeProviderKey(string providerKey)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
            {
                return "Unmapped";
            }

            var normalized = providerKey.Trim();

            // Normalize to standard casing to match database values
            switch (normalized.ToLowerInvariant())
            {
                case "steam": return "Steam";
                case "epic": return "Epic";
                case "epic games": return "Epic";
                case "gog": return "GOG";
                case "battle.net": return "BattleNet";
                case "battlenet": return "BattleNet";
                case "ea": return "EA";
                case "origin": return "EA";
                case "xbox": return "Xbox";
                case "psn": return "PSN";
                case "playstation": return "PSN";
                case "retroachievements": return "RetroAchievements";
                case "rpcs3": return "RPCS3";
                case "shadps4": return "ShadPS4";
                case "manual": return "Manual";
                case "manuel": return "Manual";
                case "unmapped": return "Unmapped";
                default: return normalized;
            }
        }

        private static object DbValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value;
        }

        private static string NormalizeDbText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void NormalizeIncomingAchievements(IEnumerable<AchievementDetail> achievements)
        {
            if (achievements == null)
            {
                return;
            }

            foreach (var achievement in achievements)
            {
                if (achievement == null)
                {
                    continue;
                }

                achievement.GlobalPercentUnlocked = NormalizeStoredPercent(
                    achievement.GlobalPercentUnlocked);
            }
        }

        private static double? NormalizeStoredPercent(double? rawPercent, bool convertLegacyRatio = false)
        {
            if (!rawPercent.HasValue)
            {
                return null;
            }

            var value = rawPercent.Value;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return null;
            }

            if (convertLegacyRatio && value > 0 && value <= 1)
            {
                value *= 100.0;
            }

            if (value < 0)
            {
                return 0;
            }

            if (value > 100)
            {
                return 100;
            }

            return value;
        }

        private static RarityTier ParseStoredRarity(string value)
        {
            return RarityTierExtensions.TryParse(value, out var rarity)
                ? rarity
                : RarityTier.Common;
        }

        private static string NormalizeMarkerApiName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

        private static bool IsUnlocked(AchievementDetail achievement)
        {
            if (achievement == null)
            {
                return false;
            }

            return achievement.Unlocked;
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
        /// Convert an absolute cache path to a relative path for database storage.
        /// Returns the original path if it's already relative, a URL, or not under the plugin data path.
        /// </summary>
        private string MakeRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            // Already relative or URL - pass through unchanged
            if (!Path.IsPathRooted(absolutePath))
            {
                return absolutePath;
            }

            if (absolutePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath;
            }

            if (string.IsNullOrWhiteSpace(_pluginUserDataPath))
            {
                return absolutePath;
            }

            try
            {
                var fullBasePath = Path.GetFullPath(_pluginUserDataPath).TrimEnd(Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(absolutePath);

                if (fullPath.StartsWith(fullBasePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(fullBasePath.Length + 1);
                }
            }
            catch
            {
                // Path operations can fail with invalid characters - return original
            }

            return absolutePath;
        }

        /// <summary>
        /// Convert a relative path to an absolute path for runtime use.
        /// Returns the original path if it's already absolute or a URL.
        /// </summary>
        private string MakeAbsolutePath(string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            {
                return relativeOrAbsolutePath;
            }

            // Already absolute - pass through unchanged
            if (Path.IsPathRooted(relativeOrAbsolutePath))
            {
                return relativeOrAbsolutePath;
            }

            // URL - pass through unchanged
            if (relativeOrAbsolutePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return relativeOrAbsolutePath;
            }

            if (string.IsNullOrWhiteSpace(_pluginUserDataPath))
            {
                return relativeOrAbsolutePath;
            }

            try
            {
                return Path.Combine(_pluginUserDataPath, relativeOrAbsolutePath);
            }
            catch
            {
                // Path operations can fail with invalid characters - return original
                return relativeOrAbsolutePath;
            }
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
                "UnlockedIconPath, LockedIconPath, Points, Category, CategoryType, TrophyType, Hidden, IsCapstone, " +
                "GlobalPercentUnlocked, Rarity, ProgressMax, CreatedUtc, UpdatedUtc " +
                "FROM AchievementDefinitions").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "GameId", "ApiName", "DisplayName", "Description",
                "UnlockedIconPath", "LockedIconPath", "Points", "Category", "CategoryType", "TrophyType", "Hidden", "IsCapstone",
                "GlobalPercentUnlocked", "Rarity", "ProgressMax", "CreatedUtc", "UpdatedUtc"
            }, r => new[] {
                r.Id?.ToString(), r.GameId?.ToString(), r.ApiName, r.DisplayName, r.Description,
                r.UnlockedIconPath, r.LockedIconPath, r.Points?.ToString(), r.Category, r.CategoryType, r.TrophyType, r.Hidden?.ToString(), r.IsCapstone?.ToString(),
                r.GlobalPercentUnlocked?.ToString(), r.Rarity, r.ProgressMax?.ToString(), r.CreatedUtc, r.UpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportUserGameProgress(string dir)
        {
            var filePath = Path.Combine(dir, "UserGameProgress.csv");
            var rows = _db.Load<UserGameProgressExportRow>(
                "SELECT Id, UserId, GameId, CacheKey, " +
                "HasAchievements, AchievementsUnlocked, TotalAchievements, " +
                "LastUpdatedUtc, CreatedUtc, UpdatedUtc " +
                "FROM UserGameProgress").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "UserId", "GameId", "CacheKey",
                "HasAchievements", "AchievementsUnlocked", "TotalAchievements",
                "LastUpdatedUtc", "CreatedUtc", "UpdatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.UserId.ToString(), r.GameId.ToString(), r.CacheKey,
                r.HasAchievements.ToString(), r.AchievementsUnlocked.ToString(), r.TotalAchievements.ToString(),
                r.LastUpdatedUtc, r.CreatedUtc, r.UpdatedUtc
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
                "SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, PlayniteGameId, GameName, " +
                "LibrarySourceName, FirstSeenUtc, LastUpdatedUtc " +
                "FROM Games").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "ProviderKey", "ProviderPlatformKey", "ProviderGameId", "PlayniteGameId", "GameName",
                "LibrarySourceName", "FirstSeenUtc", "LastUpdatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.ProviderKey, r.ProviderPlatformKey, r.ProviderGameId?.ToString(), r.PlayniteGameId, r.GameName,
                r.LibrarySourceName, r.FirstSeenUtc, r.LastUpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportUsers(string dir)
        {
            var filePath = Path.Combine(dir, "Users.csv");
            var rows = _db.Load<UserExportRow>(
                "SELECT Id, ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, " +
                "FriendSource, CreatedUtc, UpdatedUtc " +
                "FROM Users").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "Id", "ProviderKey", "ExternalUserId", "DisplayName", "IsCurrentUser",
                "FriendSource", "CreatedUtc", "UpdatedUtc"
            }, r => new[] {
                r.Id.ToString(), r.ProviderKey, r.ExternalUserId, r.DisplayName, r.IsCurrentUser.ToString(),
                r.FriendSource, r.CreatedUtc, r.UpdatedUtc
            });
            _logger.Info($"Exported {rows.Count} rows to {filePath}");
        }

        private void ExportAchievementSummary(string dir)
        {
            var filePath = Path.Combine(dir, "AchievementSummary.csv");
            var rows = _db.Load<AchievementSummaryExportRow>(
                "SELECT g.GameName, g.ProviderKey, g.PlayniteGameId, " +
                "ad.ApiName, ad.DisplayName AS AchievementName, ad.Description, ad.Points, ad.Category, ad.CategoryType, ad.TrophyType, " +
                "ad.GlobalPercentUnlocked, ad.Rarity, ad.Hidden, " +
                "ua.Unlocked, ua.UnlockTimeUtc, u.DisplayName AS UserName " +
                "FROM AchievementDefinitions ad " +
                "JOIN Games g ON ad.GameId = g.Id " +
                "LEFT JOIN UserAchievements ua ON ua.AchievementDefinitionId = ad.Id " +
                "LEFT JOIN UserGameProgress ugp ON ua.UserGameProgressId = ugp.Id " +
                "LEFT JOIN Users u ON ugp.UserId = u.Id " +
                "ORDER BY g.GameName, ad.DisplayName").ToList();
            WriteCsv(filePath, rows, new[]
            {
                "GameName", "ProviderKey", "PlayniteGameId",
                "ApiName", "AchievementName", "Description", "Points", "Category", "CategoryType", "TrophyType",
                "GlobalPercentUnlocked", "Rarity", "Hidden",
                "Unlocked", "UnlockTimeUtc", "UserName"
            }, r => new[] {
                r.GameName, r.ProviderKey, r.PlayniteGameId,
                r.ApiName, r.AchievementName, r.Description, r.Points?.ToString(), r.Category, r.CategoryType, r.TrophyType,
                r.GlobalPercentUnlocked?.ToString(), r.Rarity, r.Hidden?.ToString(),
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
            public string CategoryType { get; set; }
            public string TrophyType { get; set; }
            public bool? Hidden { get; set; }
            public bool? IsCapstone { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
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
            public long HasAchievements { get; set; }
            public long AchievementsUnlocked { get; set; }
            public long TotalAchievements { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string CreatedUtc { get; set; }
            public string UpdatedUtc { get; set; }
        }

        private sealed class UserAchievementExportRow
        {
            public long Id { get; set; }
            public long UserGameProgressId { get; set; }
            public long AchievementDefinitionId { get; set; }
            public long Unlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string CreatedUtc { get; set; }
        }

        private sealed class GameExportRow
        {
            public long Id { get; set; }
            public string ProviderKey { get; set; }
            public string ProviderPlatformKey { get; set; }
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
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public long IsCurrentUser { get; set; }
            public string FriendSource { get; set; }
            public string CreatedUtc { get; set; }
            public string UpdatedUtc { get; set; }
        }

        private sealed class AchievementSummaryExportRow
        {
            public string GameName { get; set; }
            public string ProviderKey { get; set; }
            public string PlayniteGameId { get; set; }
            public string ApiName { get; set; }
            public string AchievementName { get; set; }
            public string Description { get; set; }
            public int? Points { get; set; }
            public string Category { get; set; }
            public string CategoryType { get; set; }
            public string TrophyType { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
            public bool? Hidden { get; set; }
            public bool? Unlocked { get; set; }
            public string UnlockTimeUtc { get; set; }
            public string UserName { get; set; }
        }
    }
}





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
            public string ProviderGameKey { get; set; }
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
            public string ProviderGameKey { get; set; }
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
            public string ProviderGameKey { get; set; }
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
            public int? Points { get; set; }
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
            public string ProviderGameKey { get; set; }
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
            public string AvatarPath { get; set; }
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
            public string ProviderPlatformKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string ProviderGameKey { get; set; }
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
            public string IconPath { get; set; }
            public string CoverPath { get; set; }
        }

        private sealed class FriendGameLinkRow
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public long? ProviderGameId { get; set; }
            public string ProviderGameKey { get; set; }
            public string PlayniteGameId { get; set; }
            public long PlaytimeForeverMinutes { get; set; }
            public string LastPlayedUtc { get; set; }
        }

        private sealed class ProviderGameDefinitionStateRow
        {
            public string ProviderKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string ProviderGameKey { get; set; }
            public string GameName { get; set; }
            public string IconUrl { get; set; }
            public string Status { get; set; }
            public string LastCheckedUtc { get; set; }
            public long DefinitionCount { get; set; }
        }

        private sealed class FriendRecentUnlockRow
        {
            public string ProviderKey { get; set; }
            public long? ProviderGameId { get; set; }
            public string ProviderGameKey { get; set; }
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string FriendExternalUserId { get; set; }
            public string FriendName { get; set; }
            public string FriendAvatarUrl { get; set; }
            public string FriendAvatarPath { get; set; }
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
            public long? Unlocked { get; set; }
            public long? MyUnlocked { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
            public string IconPath { get; set; }
            public string CoverPath { get; set; }
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
                        g.ProviderGameKey AS ProviderGameKey,
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
                            g.ProviderGameKey AS ProviderGameKey,
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
                        ProviderGameKey,
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
                        ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
                        GameName = row.GameName,
                        HasAchievements = row.HasAchievements != 0,
                        LastUpdatedUtc = ParseUtc(row.LastUpdatedUtc) ?? DateTime.UtcNow,
                        TotalAchievements = (int)Math.Max(0, row.TotalAchievements),
                        UnlockedAchievements = (int)Math.Max(0, row.AchievementsUnlocked),
                        CollectionScore = scoreTotals.CollectionScore,
                        PrestigeScore = scoreTotals.PrestigeScore,
                        CollectionScoreTotal = possibleScoreTotals.CollectionScore,
                        PrestigeScoreTotal = possibleScoreTotals.PrestigeScore,
                        Points = scoreTotals.Points,
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
                        g.ProviderGameKey AS ProviderGameKey,
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
                    lp.ProviderGameKey AS ProviderGameKey,
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
                    lp.ProviderGameKey,
                    lp.PlayniteGameId,
                    lp.GameName
                ORDER BY lp.LastUpdatedUtc DESC, lp.CacheKey;").ToList();
        }

        private static Dictionary<string, (int CollectionScore, int PrestigeScore, int Points)> LoadCachedScoreTotals(
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
                    ad.Rarity AS Rarity,
                    ad.Points AS Points
                FROM LatestProgress lp
                INNER JOIN AchievementDefinitions ad ON ad.GameId = lp.GameId
                " + userAchievementJoin + @"
                WHERE lp.RowNum = 1
                ORDER BY lp.CacheKey;").ToList();

            var totals = new Dictionary<string, (int CollectionScore, int PrestigeScore, int Points)>(StringComparer.OrdinalIgnoreCase);
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
                    AddClamped(current.PrestigeScore, AchievementScoreCalculator.GetPrestigeValue(row.GlobalPercentUnlocked, rarity)),
                    AddClamped(current.Points, row.Points ?? 0));
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
                        g.ProviderGameKey AS ProviderGameKey,
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
                    lp.ProviderGameKey AS ProviderGameKey,
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
                    ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
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

        // Memoized variant: many friend rows (games and, especially, individual achievements)
        // point at the same Playnite game, so resolving a game's presentation once per load
        // avoids repeated metadata-formatter work for every achievement of that game.
        private GamePresentation ResolveGamePresentation(
            Guid? playniteGameId,
            Dictionary<Guid, GamePresentation> cache)
        {
            if (cache == null)
            {
                return ResolveGamePresentation(playniteGameId);
            }

            var key = playniteGameId ?? Guid.Empty;
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var presentation = ResolveGamePresentation(playniteGameId);
            cache[key] = presentation;
            return presentation;
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
            IReadOnlyList<FriendGameOwnership> ownership,
            FriendOwnershipSaveOptions options = null)
        {
            providerKey = NormalizeProviderKey(providerKey);
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return FriendCacheWriteResult.Failed("Friend id is missing.");
            }

            var nowIso = ToIso(DateTime.UtcNow);
            var validOwnership = (ownership ?? Array.Empty<FriendGameOwnership>())
                .Where(item => HasProviderGameIdentity(item?.AppId ?? 0, item?.ProviderGameKey))
                .Select(item =>
                {
                    item.ProviderGameKey = NormalizeProviderGameKey(item.ProviderGameKey);
                    return item;
                })
                .ToList();
            var incomingCount = validOwnership.Count;
            var writtenCount = 0;
            var skippedCount = 0;
            var seenSharedGameIds = new HashSet<long>();

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
                            var game = LoadFriendGameByAppId(
                                db,
                                providerKey,
                                item.AppId,
                                item.ProviderGameKey,
                                item.ProviderPlatformKey,
                                item.PlayniteGameId,
                                options?.IncludeProviderOnlyGames == true,
                                item.GameName,
                                item.IconUrl,
                                nowIso);
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
                            seenSharedGameIds.Add(game.Id);
                        }

                        if (options?.IncludeProviderOnlyGames != true)
                        {
                            DeleteStaleSharedFriendOwnership(db, user.Id, seenSharedGameIds);
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

        public FriendCacheWriteResult SaveFriendGameDefinition(
            string providerKey,
            FriendGameDefinition definition)
        {
            providerKey = NormalizeProviderKey(providerKey);
            if (definition != null)
            {
                definition.ProviderGameKey = NormalizeProviderGameKey(definition.ProviderGameKey);
            }

            if (definition == null || !HasProviderGameIdentity(definition.AppId, definition.ProviderGameKey))
            {
                return FriendCacheWriteResult.Failed("Friend game definition is missing a provider game id.");
            }

            var now = DateTime.UtcNow;
            var nowIso = ToIso(now);
            var checkedIso = ToIso(definition.LastCheckedUtc == default(DateTime)
                ? now
                : definition.LastCheckedUtc);
            var status = ToDefinitionStatusStorage(definition.Status);

            try
            {
                var writtenCount = 0;
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        if (definition.Status == FriendGameDefinitionStatus.Ok &&
                            definition.Achievements != null &&
                            definition.Achievements.Count > 0)
                        {
                            var game = LoadAnyGameByAppId(db, providerKey, definition.AppId, definition.ProviderGameKey);
                            var gameId = game?.Id ?? EnsureProviderOnlyGame(
                                db,
                                providerKey,
                                definition.AppId,
                                definition.ProviderGameKey,
                                definition.ProviderPlatformKey,
                                definition.GameName,
                                nowIso);
                            if (gameId > 0)
                            {
                                UpsertAchievementDefinitions(
                                    db,
                                    gameId,
                                    definition.Achievements,
                                    nowIso,
                                    checkedIso);
                                writtenCount = definition.Achievements.Count;
                            }
                        }

                        // Image source URLs are not persisted; the header banner is downloaded to
                        // local IconPath/CoverPath during refresh instead.
                        UpsertProviderGameDefinitionState(
                            db,
                            providerKey,
                            definition.AppId,
                            definition.ProviderGameKey,
                            definition.GameName,
                            null,
                            status,
                            checkedIso,
                            nowIso);
                    });
                });

                return FriendCacheWriteResult.Ok(
                    incomingCount: definition.Achievements?.Count ?? 0,
                    writtenCount: writtenCount,
                    skippedCount: Math.Max(0, (definition.Achievements?.Count ?? 0) - writtenCount));
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to save friend game definition for provider={providerKey}, game={ToProviderGameLogKey(definition.AppId, definition.ProviderGameKey)}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        public Dictionary<string, FriendGameDefinitionState> LoadFriendGameDefinitionStates(
            string providerKey,
            IReadOnlyCollection<string> providerGameKeys)
        {
            providerKey = NormalizeProviderKey(providerKey);
            var keys = (providerGameKeys ?? Array.Empty<string>())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0)
            {
                return new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase);
            }

            return WithDb(db =>
            {
                var numericIds = keys
                    .Select(key => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                var slugKeys = keys
                    .Where(key => !int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    .ToList();

                var result = new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase);
                var clauses = new List<string>();
                var args = new List<object> { providerKey };

                if (numericIds.Count > 0)
                {
                    clauses.Add("ProviderGameId IN (" + string.Join(",", numericIds.Select(_ => "?")) + ")");
                    args.AddRange(numericIds.Cast<object>());
                }

                if (slugKeys.Count > 0)
                {
                    clauses.Add("ProviderGameKey IN (" + string.Join(",", slugKeys.Select(_ => "?")) + ")");
                    args.AddRange(slugKeys.Cast<object>());
                }

                if (clauses.Count == 0)
                {
                    return result;
                }

                var sql =
                    @"SELECT ProviderKey, ProviderGameId, ProviderGameKey, GameName, IconUrl, Status, LastCheckedUtc
                      FROM ProviderGameDefinitionState
                      WHERE ProviderKey = ?
                        AND (" + string.Join(" OR ", clauses) + ");";

                result = db.Load<ProviderGameDefinitionStateRow>(sql, args.ToArray())
                    .Where(row => row != null && HasProviderGameIdentity((int)Math.Max(0, row.ProviderGameId ?? 0), row.ProviderGameKey))
                    .GroupBy(row => ToProviderGameCacheKey((int)Math.Max(0, row.ProviderGameId ?? 0), row.ProviderGameKey), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var row = group.First();
                            return new FriendGameDefinitionState
                            {
                                ProviderKey = row.ProviderKey,
                                AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                                ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
                                GameName = row.GameName,
                                IconUrl = row.IconUrl,
                                Status = ParseDefinitionStatus(row.Status),
                                LastCheckedUtc = ParseUtc(row.LastCheckedUtc)
                            };
                        });

                var missingKeys = keys
                    .Where(key => !result.ContainsKey(key))
                    .ToList();
                if (missingKeys.Count == 0)
                {
                    return result;
                }

                var missingNumericIds = missingKeys
                    .Select(key => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                var missingSlugKeys = missingKeys
                    .Where(key => !int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    .ToList();
                var ownedClauses = new List<string>();
                var ownedArgs = new List<object> { providerKey };
                if (missingNumericIds.Count > 0)
                {
                    ownedClauses.Add("g.ProviderGameId IN (" + string.Join(",", missingNumericIds.Select(_ => "?")) + ")");
                    ownedArgs.AddRange(missingNumericIds.Cast<object>());
                }

                if (missingSlugKeys.Count > 0)
                {
                    ownedClauses.Add("g.ProviderGameKey IN (" + string.Join(",", missingSlugKeys.Select(_ => "?")) + ")");
                    ownedArgs.AddRange(missingSlugKeys.Cast<object>());
                }

                if (ownedClauses.Count == 0)
                {
                    return result;
                }

                var ownedSql =
                    @"SELECT
                        g.ProviderKey AS ProviderKey,
                        g.ProviderGameId AS ProviderGameId,
                        g.ProviderGameKey AS ProviderGameKey,
                        g.GameName AS GameName,
                        COUNT(ad.Id) AS DefinitionCount,
                        g.LastUpdatedUtc AS LastCheckedUtc
                      FROM Games g
                      LEFT JOIN AchievementDefinitions ad ON ad.GameId = g.Id
                      WHERE g.ProviderKey = ?
                        AND (" + string.Join(" OR ", ownedClauses) + @")
                        AND g.PlayniteGameId IS NOT NULL
                        AND TRIM(g.PlayniteGameId) <> ''
                      GROUP BY g.Id, g.ProviderKey, g.ProviderGameId, g.ProviderGameKey, g.GameName, g.LastUpdatedUtc;";

                foreach (var row in db.Load<ProviderGameDefinitionStateRow>(ownedSql, ownedArgs.ToArray())
                             .Where(row => row != null && HasProviderGameIdentity((int)Math.Max(0, row.ProviderGameId ?? 0), row.ProviderGameKey)))
                {
                    result[ToProviderGameCacheKey((int)Math.Max(0, row.ProviderGameId ?? 0), row.ProviderGameKey)] = new FriendGameDefinitionState
                    {
                        ProviderKey = row.ProviderKey,
                        AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                        ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
                        GameName = row.GameName,
                        Status = row.DefinitionCount > 0
                            ? FriendGameDefinitionStatus.Ok
                            : FriendGameDefinitionStatus.Unavailable,
                        LastCheckedUtc = DateTime.UtcNow
                    };
                }

                return result;
            });
        }

        public FriendCacheWriteResult SaveProviderGameImagePaths(
            string providerKey,
            string providerGameKey,
            int appId,
            string iconAbsolutePath,
            string coverAbsolutePath)
        {
            providerKey = NormalizeProviderKey(providerKey);
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            if (!HasProviderGameIdentity(appId, providerGameKey))
            {
                return FriendCacheWriteResult.Failed("Provider game id is missing.");
            }

            var iconPath = MakeRelativePath(iconAbsolutePath);
            var coverPath = MakeRelativePath(coverAbsolutePath);
            if (string.IsNullOrWhiteSpace(iconPath) && string.IsNullOrWhiteSpace(coverPath))
            {
                return FriendCacheWriteResult.Ok(incomingCount: 0, writtenCount: 0, skippedCount: 0);
            }

            try
            {
                var nowIso = ToIso(DateTime.UtcNow);
                WithDb(db =>
                {
                    if (!string.IsNullOrWhiteSpace(providerGameKey))
                    {
                        db.ExecuteNonQuery(
                            @"UPDATE Games
                          SET IconPath = COALESCE(?, IconPath),
                              CoverPath = COALESCE(?, CoverPath),
                              LastUpdatedUtc = ?
                          WHERE ProviderKey = ?
                            AND ProviderGameKey = ?
                            AND (PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = '');",
                            DbValue(iconPath),
                            DbValue(coverPath),
                            nowIso,
                            providerKey,
                            providerGameKey);
                    }
                    else
                    {
                        db.ExecuteNonQuery(
                            @"UPDATE Games
                              SET IconPath = COALESCE(?, IconPath),
                                  CoverPath = COALESCE(?, CoverPath),
                                  LastUpdatedUtc = ?
                              WHERE ProviderKey = ?
                                AND ProviderGameId = ?
                                AND (PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = '');",
                            DbValue(iconPath),
                            DbValue(coverPath),
                            nowIso,
                            providerKey,
                            appId);
                    }
                });

                return FriendCacheWriteResult.Ok(incomingCount: 1, writtenCount: 1, skippedCount: 0);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to save provider game image paths for {providerKey}/{ToProviderGameLogKey(appId, providerGameKey)}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        public FriendUnownedCacheStats GetUnownedFriendGameCacheStats()
        {
            return WithDb(db => LoadUnownedFriendGameCacheStats(db));
        }

        public FriendUnownedCacheClearResult ClearUnownedFriendGameData()
        {
            try
            {
                FriendUnownedCacheClearResult result = null;
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        var before = LoadUnownedFriendGameCacheStats(db);
                        db.ExecuteNonQuery(
                            @"DELETE FROM UserAchievements
                              WHERE UserGameProgressId IN (
                                  SELECT ugp.Id
                                  FROM UserGameProgress ugp
                                  INNER JOIN Games g ON g.Id = ugp.GameId
                                  INNER JOIN Users u ON u.Id = ugp.UserId
                                  WHERE u.IsCurrentUser = 0
                                    AND (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')
                              );");
                        db.ExecuteNonQuery(
                            @"DELETE FROM UserGameProgress
                              WHERE Id IN (
                                  SELECT ugp.Id
                                  FROM UserGameProgress ugp
                                  INNER JOIN Games g ON g.Id = ugp.GameId
                                  INNER JOIN Users u ON u.Id = ugp.UserId
                                  WHERE u.IsCurrentUser = 0
                                    AND (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')
                              );");
                        db.ExecuteNonQuery(
                            @"DELETE FROM FriendOwnership
                              WHERE GameId IN (
                                  SELECT Id
                                  FROM Games
                                  WHERE PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = ''
                              );");
                        db.ExecuteNonQuery(
                            @"DELETE FROM AchievementDefinitions
                              WHERE GameId IN (
                                  SELECT g.Id
                                  FROM Games g
                                  WHERE (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM UserGameProgress ugp
                                        INNER JOIN Users u ON u.Id = ugp.UserId
                                        WHERE ugp.GameId = g.Id
                                          AND u.IsCurrentUser = 1
                                    )
                              );");
                        db.ExecuteNonQuery(
                            @"DELETE FROM Games
                              WHERE (PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = '')
                                AND NOT EXISTS (
                                    SELECT 1
                                    FROM UserGameProgress ugp
                                    INNER JOIN Users u ON u.Id = ugp.UserId
                                    WHERE ugp.GameId = Games.Id
                                      AND u.IsCurrentUser = 1
                                );");
                        db.ExecuteNonQuery("DELETE FROM ProviderGameDefinitionState;");

                        result = new FriendUnownedCacheClearResult
                        {
                            Success = true,
                            Games = before.Games,
                            Definitions = before.Definitions,
                            OwnershipRows = before.OwnershipRows,
                            ProgressRows = before.ProgressRows,
                            AchievementRows = before.AchievementRows,
                            DefinitionStates = before.DefinitionStates
                        };
                    });
                });

                return result ?? new FriendUnownedCacheClearResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to clear unowned friend game data.");
                return FriendUnownedCacheClearResult.Failed(ex.Message);
            }
        }

        public FriendCacheWriteResult ClearUnownedFriendGame(string providerKey, int appId, string providerGameKey)
        {
            providerKey = NormalizeProviderKey(providerKey);
            var normalizedGameKey = NormalizeProviderGameKey(providerGameKey);
            if (!HasProviderGameIdentity(appId, normalizedGameKey))
            {
                return FriendCacheWriteResult.Failed("Provider game identity is missing.");
            }

            // Shared predicate selecting the single unowned (provider-only) Games row(s) for this
            // provider game. Matching either ProviderGameId or ProviderGameKey mirrors the provider
            // identity semantics used elsewhere; the OR is safe because DbValue(null) binds DBNull,
            // which never equals a stored key (and appId <= 0 never matches a real ProviderGameId).
            const string gameFilter =
                "g.ProviderKey = ? AND (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '') " +
                "AND (g.ProviderGameId = ? OR g.ProviderGameKey = ?)";

            try
            {
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        db.ExecuteNonQuery(
                            @"DELETE FROM UserAchievements
                              WHERE UserGameProgressId IN (
                                  SELECT ugp.Id
                                  FROM UserGameProgress ugp
                                  INNER JOIN Games g ON g.Id = ugp.GameId
                                  INNER JOIN Users u ON u.Id = ugp.UserId
                                  WHERE u.IsCurrentUser = 0 AND " + gameFilter + ");",
                            providerKey, appId, DbValue(normalizedGameKey));
                        db.ExecuteNonQuery(
                            @"DELETE FROM UserGameProgress
                              WHERE Id IN (
                                  SELECT ugp.Id
                                  FROM UserGameProgress ugp
                                  INNER JOIN Games g ON g.Id = ugp.GameId
                                  INNER JOIN Users u ON u.Id = ugp.UserId
                                  WHERE u.IsCurrentUser = 0 AND " + gameFilter + ");",
                            providerKey, appId, DbValue(normalizedGameKey));
                        db.ExecuteNonQuery(
                            @"DELETE FROM FriendOwnership
                              WHERE GameId IN (
                                  SELECT g.Id FROM Games g WHERE " + gameFilter + ");",
                            providerKey, appId, DbValue(normalizedGameKey));
                        db.ExecuteNonQuery(
                            @"DELETE FROM AchievementDefinitions
                              WHERE GameId IN (
                                  SELECT g.Id
                                  FROM Games g
                                  WHERE " + gameFilter + @"
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM UserGameProgress ugp
                                        INNER JOIN Users u ON u.Id = ugp.UserId
                                        WHERE ugp.GameId = g.Id
                                          AND u.IsCurrentUser = 1
                                    ));",
                            providerKey, appId, DbValue(normalizedGameKey));
                        db.ExecuteNonQuery(
                            @"DELETE FROM Games
                              WHERE Id IN (
                                  SELECT g.Id
                                  FROM Games g
                                  WHERE " + gameFilter + @"
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM UserGameProgress ugp
                                        INNER JOIN Users u ON u.Id = ugp.UserId
                                        WHERE ugp.GameId = g.Id
                                          AND u.IsCurrentUser = 1
                                    ));",
                            providerKey, appId, DbValue(normalizedGameKey));
                        db.ExecuteNonQuery(
                            @"DELETE FROM ProviderGameDefinitionState
                              WHERE ProviderKey = ?
                                AND (ProviderGameId = ? OR ProviderGameKey = ?);",
                            providerKey, appId, DbValue(normalizedGameKey));
                    });
                });

                return FriendCacheWriteResult.Ok(incomingCount: 1, writtenCount: 1, skippedCount: 0);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to clear unowned friend game data for {providerKey}/{ToProviderGameLogKey(appId, normalizedGameKey)}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        public FriendCacheWriteResult SaveFriendGameAchievements(
            string providerKey,
            string externalUserId,
            string providerGameKey,
            int appId,
            FriendGameAchievements achievements)
        {
            providerKey = NormalizeProviderKey(providerKey);
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            if (string.IsNullOrWhiteSpace(providerGameKey) && !string.IsNullOrWhiteSpace(achievements?.ProviderGameKey))
            {
                providerGameKey = NormalizeProviderGameKey(achievements.ProviderGameKey);
            }

            if (string.IsNullOrWhiteSpace(externalUserId) || !HasProviderGameIdentity(appId, providerGameKey))
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
                        var game = LoadAnyGameByAppId(db, providerKey, appId, providerGameKey);
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
                            Math.Max(0, matchedRows.Count(match => match?.Row?.Unlocked == true)),
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
                _logger?.Warn(ex, $"Failed to save friend achievements for provider={providerKey}, friend={externalUserId}, game={ToProviderGameLogKey(appId, providerGameKey)}.");
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
                if (options.ProviderGameKeys?.Count > 0)
                {
                    return LoadProviderGameKeyFriendRefreshCandidates(db, providerKey, options);
                }

                if (options.ProviderAppIds?.Count > 0)
                {
                    return LoadProviderAppFriendRefreshCandidates(db, providerKey, options);
                }

                if (options.Scope == FriendRefreshScope.Full ||
                    options.Scope == FriendRefreshScope.Installed ||
                    options.Scope == FriendRefreshScope.SelectedGame ||
                    (options.Scope == FriendRefreshScope.Custom && options.PlayniteGameIds?.Count > 0))
                {
                    var currentUserCandidates = LoadCurrentUserFriendRefreshCandidates(db, providerKey, options);
                    if (ShouldUseMappedFriendOwnershipCandidates(providerKey, options))
                    {
                        currentUserCandidates.AddRange(LoadSharedFriendRefreshCandidates(
                            db,
                            providerKey,
                            cutoffIso,
                            recentOnly: false,
                            includeProviderOnly: false,
                            providerOnlyOnly: false,
                            options));
                    }

                    if (options.Scope == FriendRefreshScope.Full &&
                        ShouldIncludeProviderOnlyFriendRows(options))
                    {
                        currentUserCandidates.AddRange(LoadSharedFriendRefreshCandidates(
                            db,
                            providerKey,
                            cutoffIso,
                            recentOnly: false,
                            includeProviderOnly: true,
                            providerOnlyOnly: true,
                            options));
                    }

                    return DeduplicateFriendRefreshCandidates(currentUserCandidates);
                }

                return LoadSharedFriendRefreshCandidates(
                    db,
                    providerKey,
                    cutoffIso,
                    options.Scope == FriendRefreshScope.Recent,
                    ShouldIncludeProviderOnlyFriendRows(options),
                    providerOnlyOnly: false,
                    options);
            });
        }

        public FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId, bool preserveFriendRecord = false)
        {
            providerKey = NormalizeProviderKey(providerKey);
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return FriendCacheWriteResult.Failed("Friend id is missing.");
            }

            var normalizedExternalUserId = externalUserId.Trim();

            try
            {
                var deletedUsers = 0;
                WithDb(db =>
                {
                    db.RunTransaction(() =>
                    {
                        var users = db.Load<UserRow>(
                            @"SELECT Id, ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, AvatarUrl, LastRefreshedUtc, IsActiveFriend, CreatedUtc, UpdatedUtc
                              FROM Users
                              WHERE ProviderKey = ?
                                AND ExternalUserId = ?
                                AND IsCurrentUser = 0;",
                            providerKey,
                            normalizedExternalUserId).ToList();

                        foreach (var user in users)
                        {
                            db.ExecuteNonQuery(
                                @"DELETE FROM UserAchievements
                                  WHERE UserGameProgressId IN (
                                      SELECT Id FROM UserGameProgress WHERE UserId = ?
                                  );",
                                user.Id);
                            db.ExecuteNonQuery("DELETE FROM UserGameProgress WHERE UserId = ?;", user.Id);
                            db.ExecuteNonQuery("DELETE FROM FriendOwnership WHERE UserId = ?;", user.Id);
                            if (!preserveFriendRecord)
                            {
                                // Removing the friend reference entirely (settings-side Remove / Ignore).
                                // "Clear Friend" preserves this row so the friend stays registered.
                                db.ExecuteNonQuery("DELETE FROM Users WHERE Id = ? AND IsCurrentUser = 0;", user.Id);
                            }
                            deletedUsers++;
                        }
                    });
                });

                return FriendCacheWriteResult.Ok(
                    incomingCount: 1,
                    writtenCount: deletedUsers,
                    skippedCount: deletedUsers > 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to delete friend data for provider={providerKey}, friend={normalizedExternalUserId}.");
                return FriendCacheWriteResult.Failed(ex.Message);
            }
        }

        private static List<FriendRefreshCandidate> LoadSharedFriendRefreshCandidates(
            SQLiteDatabase db,
            string providerKey,
            string cutoffIso,
            bool recentOnly,
            bool includeProviderOnly,
            bool providerOnlyOnly,
            FriendRefreshOptions options)
        {
            var sql = new StringBuilder(
                    @"SELECT
                        u.ProviderKey AS ProviderKey,
                        u.ExternalUserId AS ExternalUserId,
                        u.DisplayName AS DisplayName,
                        u.AvatarUrl AS AvatarUrl,
                        u.LastRefreshedUtc AS LastRefreshedUtc,
                        g.ProviderGameId AS ProviderGameId,
                        g.ProviderGameKey AS ProviderGameKey,
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
                        AND (
                            (g.ProviderGameId IS NOT NULL AND g.ProviderGameId > 0)
                            OR (g.ProviderGameKey IS NOT NULL AND TRIM(g.ProviderGameKey) <> '')
                        )");

            var args = new List<object> { providerKey, providerKey };

            if (providerOnlyOnly)
            {
                sql.Append(" AND (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')");
            }
            else if (!includeProviderOnly)
            {
                sql.Append(" AND g.PlayniteGameId IS NOT NULL AND TRIM(g.PlayniteGameId) <> ''");
            }

            var friendIds = NormalizeFriendFilterIds(options?.FriendExternalUserIds);
            if (friendIds.Count > 0)
            {
                sql.Append(" AND u.ExternalUserId IN (");
                sql.Append(string.Join(",", friendIds.Select(_ => "?")));
                sql.Append(')');
                args.AddRange(friendIds.Cast<object>());
            }

            var gameIds = NormalizeGameFilterIds(options?.PlayniteGameIds);
            if (gameIds.Count > 0)
            {
                sql.Append(" AND g.PlayniteGameId IN (");
                sql.Append(string.Join(",", gameIds.Select(_ => "?")));
                sql.Append(')');
                args.AddRange(gameIds.Cast<object>());
            }

            if (recentOnly)
            {
                sql.Append(
                    @" AND COALESCE(fo.Playtime2WeeksMinutes, 0) > 0");
            }

            sql.Append(" ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, fo.PlaytimeForeverMinutes DESC, u.DisplayName, g.GameName;");

            return MapFriendRefreshCandidates(
                db.Load<FriendRefreshCandidateRow>(sql.ToString(), args.ToArray()).ToList());
        }

        private static List<FriendRefreshCandidate> LoadProviderAppFriendRefreshCandidates(
            SQLiteDatabase db,
            string providerKey,
            FriendRefreshOptions options)
        {
            var appIds = options?.ProviderAppIds?
                .Where(id => id > 0)
                .Distinct()
                .ToList() ?? new List<int>();
            if (appIds.Count == 0)
            {
                return new List<FriendRefreshCandidate>();
            }

            var sql = new StringBuilder(
                    @"SELECT
                        u.ProviderKey AS ProviderKey,
                        u.ExternalUserId AS ExternalUserId,
                        u.DisplayName AS DisplayName,
                        u.AvatarUrl AS AvatarUrl,
                        u.LastRefreshedUtc AS LastRefreshedUtc,
                        g.ProviderGameId AS ProviderGameId,
                        g.ProviderGameKey AS ProviderGameKey,
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
                        AND g.ProviderGameId > 0
                        AND g.ProviderGameId IN (");
            sql.Append(string.Join(",", appIds.Select(_ => "?")));
            sql.Append(')');

            var args = new List<object> { providerKey, providerKey };
            args.AddRange(appIds.Cast<object>());

            var friendIds = NormalizeFriendFilterIds(options?.FriendExternalUserIds);
            if (friendIds.Count > 0)
            {
                sql.Append(" AND u.ExternalUserId IN (");
                sql.Append(string.Join(",", friendIds.Select(_ => "?")));
                sql.Append(')');
                args.AddRange(friendIds.Cast<object>());
            }

            sql.Append(" ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, fo.PlaytimeForeverMinutes DESC, u.DisplayName, g.GameName;");

            return MapFriendRefreshCandidates(
                db.Load<FriendRefreshCandidateRow>(sql.ToString(), args.ToArray()).ToList());
        }

        private static List<FriendRefreshCandidate> LoadProviderGameKeyFriendRefreshCandidates(
            SQLiteDatabase db,
            string providerKey,
            FriendRefreshOptions options)
        {
            var providerGameKeys = options?.ProviderGameKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (providerGameKeys.Count == 0)
            {
                return new List<FriendRefreshCandidate>();
            }

            var sql = new StringBuilder(
                    @"SELECT
                        u.ProviderKey AS ProviderKey,
                        u.ExternalUserId AS ExternalUserId,
                        u.DisplayName AS DisplayName,
                        u.AvatarUrl AS AvatarUrl,
                        u.LastRefreshedUtc AS LastRefreshedUtc,
                        g.ProviderGameId AS ProviderGameId,
                        g.ProviderGameKey AS ProviderGameKey,
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
                        AND g.ProviderGameKey IN (");
            sql.Append(string.Join(",", providerGameKeys.Select(_ => "?")));
            sql.Append(')');

            var args = new List<object> { providerKey, providerKey };
            args.AddRange(providerGameKeys.Cast<object>());

            var friendIds = NormalizeFriendFilterIds(options?.FriendExternalUserIds);
            if (friendIds.Count > 0)
            {
                sql.Append(" AND u.ExternalUserId IN (");
                sql.Append(string.Join(",", friendIds.Select(_ => "?")));
                sql.Append(')');
                args.AddRange(friendIds.Cast<object>());
            }

            sql.Append(" ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, fo.PlaytimeForeverMinutes DESC, u.DisplayName, g.GameName;");

            return MapFriendRefreshCandidates(
                db.Load<FriendRefreshCandidateRow>(sql.ToString(), args.ToArray()).ToList());
        }

        private static bool ShouldIncludeProviderOnlyFriendRows(FriendRefreshOptions options)
        {
            // Provider-only (unowned) rows are written only for friends who opted into the full
            // library, so include them only when the request-level policy permits full-library work.
            return options?.IncludesProviderOnlyGames() == true;
        }

        private static bool ShouldUseMappedFriendOwnershipCandidates(string providerKey, FriendRefreshOptions options)
        {
            return string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase) &&
                   (options?.Scope == FriendRefreshScope.Full ||
                    options?.Scope == FriendRefreshScope.Installed ||
                    options?.Scope == FriendRefreshScope.SelectedGame ||
                    (options?.Scope == FriendRefreshScope.Custom && options.PlayniteGameIds?.Count > 0));
        }

        private static List<FriendRefreshCandidate> DeduplicateFriendRefreshCandidates(
            IEnumerable<FriendRefreshCandidate> candidates)
        {
            return (candidates ?? Enumerable.Empty<FriendRefreshCandidate>())
                .Where(candidate => candidate?.Friend != null)
                .GroupBy(BuildFriendRefreshCandidateKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static string BuildFriendRefreshCandidateKey(FriendRefreshCandidate candidate)
        {
            var friendId = candidate?.Friend?.ExternalUserId?.Trim() ?? string.Empty;
            var gameKey = !string.IsNullOrWhiteSpace(candidate?.ProviderGameKey)
                ? "key:" + candidate.ProviderGameKey.Trim()
                : (candidate?.AppId > 0
                    ? "app:" + candidate.AppId.ToString(CultureInfo.InvariantCulture)
                    : "playnite:" + (candidate?.PlayniteGameId?.ToString() ?? string.Empty));
            return friendId + "\u001f" + gameKey;
        }

        private static List<string> NormalizeFriendFilterIds(IReadOnlyCollection<string> friendExternalUserIds)
        {
            return friendExternalUserIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        private static List<string> NormalizeGameFilterIds(IReadOnlyCollection<Guid> playniteGameIds)
        {
            return playniteGameIds?
                .Where(id => id != Guid.Empty)
                .Select(id => id.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
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
                        g.ProviderGameKey AS ProviderGameKey,
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
                      AND (
                          (g.ProviderGameId IS NOT NULL AND g.ProviderGameId > 0)
                          OR (g.ProviderGameKey IS NOT NULL AND TRIM(g.ProviderGameKey) <> '')
                      )
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
                    g.ProviderGameKey AS ProviderGameKey,
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
                    AND u.FriendSource IS NOT NULL");

            args.Add(providerKey);

            var friendIds = NormalizeFriendFilterIds(options.FriendExternalUserIds);
            if (friendIds.Count > 0)
            {
                sql.Append(" AND u.ExternalUserId IN (");
                sql.Append(string.Join(",", friendIds.Select(_ => "?")));
                sql.Append(')');
                args.AddRange(friendIds.Cast<object>());
            }

            sql.Append(" ORDER BY COALESCE(fo.LastPlayedUtc, '') DESC, COALESCE(fo.PlaytimeForeverMinutes, 0) DESC, u.DisplayName, g.GameName;");

            return MapFriendRefreshCandidates(db.Load<FriendRefreshCandidateRow>(sql.ToString(), args.ToArray()).ToList());
        }

        private static List<FriendRefreshCandidate> MapFriendRefreshCandidates(IEnumerable<FriendRefreshCandidateRow> rows)
        {
            return (rows ?? Enumerable.Empty<FriendRefreshCandidateRow>())
                .Where(row => row != null && HasProviderGameIdentity((int)Math.Max(0, row.ProviderGameId ?? 0), row.ProviderGameKey))
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
                    ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
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

                // Shared across the games list and both achievement lists so each game's
                // presentation is resolved at most once per load.
                var presentationCache = new Dictionary<Guid, GamePresentation>();

                using (PerfScope.Start(_logger, "Friends.LoadSummaryRows", thresholdMs: 15))
                data.Friends = LoadFriendSummaryRows(db)
                    .Select(row => new FriendSummaryItem
                    {
                        ProviderKey = row.ProviderKey,
                        ExternalUserId = row.ExternalUserId,
                        DisplayName = row.DisplayName,
                        AvatarPath = !string.IsNullOrWhiteSpace(row.AvatarPath)
                            ? MakeAbsolutePath(row.AvatarPath)
                            : row.AvatarUrl,
                        SharedGamesCount = (int)Math.Max(0, row.SharedGamesCount),
                        GamesWithUnlocksCount = (int)Math.Max(0, row.GamesWithUnlocksCount),
                        UnlockedAchievementsCount = (int)Math.Max(0, row.UnlockedAchievementsCount),
                        RecentUnlockCount = (int)Math.Max(0, row.RecentUnlockCount),
                        LastUnlockUtc = ParseUtc(row.LastUnlockUtc),
                        LastRefreshedUtc = ParseUtc(row.LastRefreshedUtc),
                        TotalPlaytimeMinutes = Math.Max(0, row.TotalPlaytimeMinutes)
                    })
                    .ToList();

                using (PerfScope.Start(_logger, "Friends.LoadGameSummaryRows", thresholdMs: 15))
                data.Games = LoadFriendGameSummaryRows(db)
                    .Select(row =>
                    {
                        var playniteGameId = ResolveCachedPlayniteGameId(null, row.PlayniteGameId);
                        var presentation = ResolveGamePresentation(playniteGameId, presentationCache);
                        // Display the underlying provider (e.g. EA) via the visual fields, but keep the
                        // raw aggregator ProviderKey (e.g. Exophase) for identity: game<->achievement and
                        // game<->friend matching (BuildGameUnlockKey/IsSameGame in FriendOverviewProjection)
                        // and refresh targeting compare ProviderKey against the achievement/friend rows,
                        // which retain the aggregator key. The grid icon binds to ProviderIconKey/Provider,
                        // not ProviderKey, so the display provider still renders.
                        var displayProviderKey = ResolveDisplayProviderKey(row.ProviderKey, row.ProviderPlatformKey);
                        var providerName = ProviderRegistry.GetLocalizedName(displayProviderKey);
                        if (!ProviderRegistry.TryResolveProviderVisuals(displayProviderKey, out var providerIconKey, out var providerColorHex))
                        {
                            providerIconKey = string.IsNullOrWhiteSpace(displayProviderKey) ? null : "ProviderIcon" + displayProviderKey;
                            providerColorHex = "#888888";
                        }
                        var item = new FriendGameSummaryItem
                        {
                            ProviderKey = string.IsNullOrWhiteSpace(row.ProviderKey) ? displayProviderKey : row.ProviderKey.Trim(),
                            Provider = string.IsNullOrWhiteSpace(providerName) ? displayProviderKey : providerName,
                            ProviderIconKey = providerIconKey,
                            ProviderColorHex = providerColorHex,
                            AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                            ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
                            PlayniteGameId = playniteGameId,
                            GameName = row.GameName,
                            SortingName = presentation.SortingName ?? row.GameName,
                            GameLogo = ResolveFriendGameIconPath(presentation, row.IconPath),
                            GameCoverPath = ResolveFriendGameCoverPath(presentation, row.CoverPath),
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

                using (PerfScope.Start(_logger, "Friends.LoadGameLinkRows", thresholdMs: 15))
                data.FriendGameLinks = LoadFriendGameLinkRows(db)
                    .Select(row => new FriendGameLinkItem
                    {
                        ProviderKey = row.ProviderKey,
                        ExternalUserId = row.ExternalUserId,
                        AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                        ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
                        PlayniteGameId = ResolveCachedPlayniteGameId(null, row.PlayniteGameId),
                        PlaytimeForeverMinutes = Math.Max(0, row.PlaytimeForeverMinutes),
                        LastPlayedUtc = ParseUtc(row.LastPlayedUtc)
                    })
                    .ToList();

                using (PerfScope.Start(_logger, "Friends.LoadAchievementRows", thresholdMs: 15))
                data.AllAchievements = MapFriendAchievementRows(LoadFriendAllAchievementRows(db), presentationCache);
                data.AllUnlockedAchievements = data.AllAchievements
                    .Where(item => item?.Unlocked == true)
                    .ToList();

                // Recent unlocks are the time-stamped subset of all unlocked achievements, already
                // ordered by unlock time DESC from the query - derive them in memory rather than
                // re-running the identical friend/achievement join a second time.
                var recentUnlocked = data.AllUnlockedAchievements
                    .Where(item => item.UnlockTimeUtc.HasValue)
                    .OrderByDescending(item => item.UnlockTimeUtc ?? DateTime.MinValue);
                data.RecentUnlocks = (recentLimit > 0 ? recentUnlocked.Take(recentLimit) : recentUnlocked).ToList();

                using (PerfScope.Start(_logger, "Friends.ApplySummaryScores", thresholdMs: 15))
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

                friend.CollectionLevel = GetDisplayLevel(AchievementLevelCalculator.CalculateModern(friend.CollectionScore));
                friend.PrestigeLevel = GetDisplayLevel(AchievementLevelCalculator.CalculateModern(friend.PrestigeScore));
            }
        }

        private static int GetDisplayLevel(AchievementLevelSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            return snapshot.DisplayLevel > 0 ? snapshot.DisplayLevel : snapshot.Level;
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

        // Prefer the underlying platform key (e.g. Steam) over the aggregator key (e.g. Exophase) so
        // friend game summaries render the display-provider icon, mirroring
        // OverviewDataBuilder.ResolveEffectiveProviderKey.
        private static string ResolveDisplayProviderKey(string providerKey, string providerPlatformKey)
        {
            var resolved = !string.IsNullOrWhiteSpace(providerPlatformKey)
                ? providerPlatformKey
                : providerKey;
            return string.IsNullOrWhiteSpace(resolved) ? "Unknown" : resolved.Trim();
        }

        private string ResolveFriendGameIconPath(GamePresentation presentation, string cachedIconPath)
        {
            return presentation?.IconPath ?? MakeAbsolutePath(cachedIconPath);
        }

        private string ResolveFriendGameCoverPath(GamePresentation presentation, string cachedCoverPath)
        {
            return presentation?.CoverPath ?? MakeAbsolutePath(cachedCoverPath);
        }

        private static string BuildFriendCacheKey(string providerKey, string externalUserId, GameRow game)
        {
            var gamePart = !string.IsNullOrWhiteSpace(game?.PlayniteGameId)
                ? game.PlayniteGameId.Trim()
                : (!string.IsNullOrWhiteSpace(game?.ProviderGameKey)
                    ? game.ProviderGameKey.Trim()
                    : (game?.ProviderGameId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"));
            return $"friend:{providerKey}:{externalUserId}:{gamePart}";
        }

        private static string NormalizeProviderGameKey(string providerGameKey)
        {
            return string.IsNullOrWhiteSpace(providerGameKey)
                ? null
                : providerGameKey.Trim();
        }

        private static bool IsExophaseProvider(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), "Exophase", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasProviderGameIdentity(int appId, string providerGameKey)
        {
            return appId > 0 || !string.IsNullOrWhiteSpace(providerGameKey);
        }

        private static string ToProviderGameCacheKey(int appId, string providerGameKey)
        {
            var normalized = NormalizeProviderGameKey(providerGameKey);
            return !string.IsNullOrWhiteSpace(normalized)
                ? normalized
                : (appId > 0 ? appId.ToString(CultureInfo.InvariantCulture) : null);
        }

        private static string ToProviderGameLogKey(int appId, string providerGameKey)
        {
            return ToProviderGameCacheKey(appId, providerGameKey) ?? "unknown";
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

            var avatarPath = MakeRelativePath(friend.AvatarPath);

            db.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO Users
                    (ProviderKey, ExternalUserId, DisplayName, IsCurrentUser, FriendSource, AvatarUrl, AvatarPath, LastRefreshedUtc, IsActiveFriend, CreatedUtc, UpdatedUtc)
                  VALUES
                    (?, ?, ?, 0, ?, ?, ?, ?, 1, ?, ?);",
                providerKey,
                externalUserId,
                DbValue(displayName),
                DbValue(friendSource),
                DbValue(friend.AvatarUrl),
                DbValue(avatarPath),
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
                      AvatarPath = COALESCE(?, AvatarPath),
                      LastRefreshedUtc = ?,
                      IsActiveFriend = CASE WHEN IsCurrentUser = 1 THEN IsActiveFriend ELSE 1 END,
                      UpdatedUtc = ?
                  WHERE Id = ?;",
                DbValue(displayName),
                DbValue(friendSource),
                DbValue(friend.AvatarUrl),
                DbValue(avatarPath),
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

        private static GameRow LoadSharedGameByAppId(SQLiteDatabase db, string providerKey, int appId, string providerGameKey)
        {
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            if (!string.IsNullOrWhiteSpace(providerGameKey))
            {
                return db.Load<GameRow>(
                    @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderKey = ?
                        AND ProviderGameKey = ?
                        AND PlayniteGameId IS NOT NULL
                        AND TRIM(PlayniteGameId) <> ''
                      ORDER BY LastUpdatedUtc DESC, Id DESC
                      LIMIT 1;",
                    providerKey,
                    providerGameKey).FirstOrDefault();
            }

            return db.Load<GameRow>(
                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
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

        private static GameRow LoadAnyGameByAppId(SQLiteDatabase db, string providerKey, int appId, string providerGameKey = null)
        {
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            if (!string.IsNullOrWhiteSpace(providerGameKey))
            {
                return db.Load<GameRow>(
                    @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderKey = ?
                        AND ProviderGameKey = ?
                      ORDER BY CASE WHEN PlayniteGameId IS NOT NULL AND TRIM(PlayniteGameId) <> '' THEN 0 ELSE 1 END,
                               LastUpdatedUtc DESC,
                               Id DESC
                      LIMIT 1;",
                    providerKey,
                    providerGameKey).FirstOrDefault();
            }

            return db.Load<GameRow>(
                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                  FROM Games
                  WHERE ProviderKey = ?
                    AND ProviderGameId = ?
                  ORDER BY CASE WHEN PlayniteGameId IS NOT NULL AND TRIM(PlayniteGameId) <> '' THEN 0 ELSE 1 END,
                           LastUpdatedUtc DESC,
                           Id DESC
                  LIMIT 1;",
                providerKey,
                appId).FirstOrDefault();
        }

        private static GameRow LoadGameByPlayniteId(SQLiteDatabase db, string providerKey, Guid playniteGameId)
        {
            if (playniteGameId == Guid.Empty)
            {
                return null;
            }

            return db.Load<GameRow>(
                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                  FROM Games
                  WHERE ProviderKey = ?
                    AND PlayniteGameId = ?
                  ORDER BY LastUpdatedUtc DESC, Id DESC
                  LIMIT 1;",
                providerKey,
                playniteGameId.ToString()).FirstOrDefault();
        }

        private static GameRow LoadProviderOnlyGameByAppId(SQLiteDatabase db, string providerKey, int appId, string providerGameKey = null)
        {
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            if (!string.IsNullOrWhiteSpace(providerGameKey))
            {
                return db.Load<GameRow>(
                    @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderKey = ?
                        AND ProviderGameKey = ?
                        AND (PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = '')
                      ORDER BY LastUpdatedUtc DESC, Id DESC
                      LIMIT 1;",
                    providerKey,
                    providerGameKey).FirstOrDefault();
            }

            return db.Load<GameRow>(
                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                  FROM Games
                  WHERE ProviderKey = ?
                    AND ProviderGameId = ?
                    AND (PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = '')
                  ORDER BY LastUpdatedUtc DESC, Id DESC
                  LIMIT 1;",
                providerKey,
                appId).FirstOrDefault();
        }

        private static GameRow LoadFriendGameByAppId(
            SQLiteDatabase db,
            string providerKey,
            int appId,
            string providerGameKey,
            string providerPlatformKey,
            Guid? playniteGameId,
            bool allowProviderOnly,
            string gameName,
            string iconUrl,
            string nowIso)
        {
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            if (playniteGameId.HasValue && playniteGameId.Value != Guid.Empty)
            {
                var mapped = LoadGameByPlayniteId(db, providerKey, playniteGameId.Value);
                if (mapped != null)
                {
                    UpdateMappedGameIdentity(db, mapped.Id, appId, providerGameKey, providerPlatformKey, gameName, nowIso);
                    mapped.ProviderGameId = appId > 0 ? appId : mapped.ProviderGameId;
                    mapped.ProviderGameKey = providerGameKey ?? mapped.ProviderGameKey;
                    return mapped;
                }

                var mappedId = EnsureMappedProviderGame(
                    db,
                    providerKey,
                    appId,
                    providerGameKey,
                    providerPlatformKey,
                    playniteGameId.Value,
                    gameName,
                    nowIso);
                if (mappedId > 0)
                {
                    return LoadGameByPlayniteId(db, providerKey, playniteGameId.Value);
                }
            }

            var shared = ShouldUseSharedFriendGameFallback(providerKey, providerGameKey, playniteGameId)
                ? LoadSharedGameByAppId(db, providerKey, appId, providerGameKey)
                : null;
            if (shared != null || !allowProviderOnly)
            {
                return shared;
            }

            var providerOnly = LoadProviderOnlyGameByAppId(db, providerKey, appId, providerGameKey);
            if (providerOnly != null)
            {
                if (!string.IsNullOrWhiteSpace(gameName) &&
                    !string.Equals(providerOnly.GameName, gameName.Trim(), StringComparison.Ordinal))
                {
                    db.ExecuteNonQuery(
                        @"UPDATE Games
                          SET GameName = ?,
                              LastUpdatedUtc = ?
                          WHERE Id = ?;",
                        DbValue(gameName.Trim()),
                        nowIso,
                        providerOnly.Id);
                    providerOnly.GameName = gameName.Trim();
                }

                return providerOnly;
            }

            var gameId = EnsureProviderOnlyGame(db, providerKey, appId, providerGameKey, providerPlatformKey, gameName, nowIso);
            return gameId > 0
                ? LoadProviderOnlyGameByAppId(db, providerKey, appId, providerGameKey)
                : null;
        }

        private static bool ShouldUseSharedFriendGameFallback(string providerKey, string providerGameKey, Guid? playniteGameId)
        {
            if (playniteGameId.HasValue && playniteGameId.Value != Guid.Empty)
            {
                return true;
            }

            // Steam/native integer app ids intentionally fall back to the current user's matching
            // shared library row. Exophase string keys are already platform-harmonized by the provider;
            // when it declines to emit a Playnite id, reusing an old mapped row would reintroduce
            // cross-platform merges (for example an Origin friend game landing on a Steam library row).
            return !IsExophaseProvider(providerKey) || string.IsNullOrWhiteSpace(providerGameKey);
        }

        private static long EnsureProviderOnlyGame(
            SQLiteDatabase db,
            string providerKey,
            int appId,
            string providerGameKey,
            string providerPlatformKey,
            string gameName,
            string nowIso)
        {
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            var existing = LoadAnyGameByAppId(db, providerKey, appId, providerGameKey);
            if (existing != null)
            {
                return existing.Id;
            }

            var name = string.IsNullOrWhiteSpace(gameName)
                ? $"{providerKey} Game {ToProviderGameLogKey(appId, providerGameKey)}"
                : gameName.Trim();
            db.ExecuteNonQuery(
                @"INSERT INTO Games
                    (ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc)
                  VALUES
                    (?, ?, ?, ?, NULL, ?, ?, ?, ?);",
                providerKey,
                DbValue(string.IsNullOrWhiteSpace(providerPlatformKey) ? providerKey : providerPlatformKey.Trim()),
                appId > 0 ? (object)appId : DBNull.Value,
                DbValue(providerGameKey),
                DbValue(name),
                DbValue(providerKey),
                nowIso,
                nowIso);
            return db.ExecuteScalar<long>("SELECT last_insert_rowid();");
        }

        private static long EnsureMappedProviderGame(
            SQLiteDatabase db,
            string providerKey,
            int appId,
            string providerGameKey,
            string providerPlatformKey,
            Guid playniteGameId,
            string gameName,
            string nowIso)
        {
            if (playniteGameId == Guid.Empty)
            {
                return 0;
            }

            var existing = LoadGameByPlayniteId(db, providerKey, playniteGameId);
            if (existing != null)
            {
                UpdateMappedGameIdentity(db, existing.Id, appId, providerGameKey, providerPlatformKey, gameName, nowIso);
                return existing.Id;
            }

            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            var name = string.IsNullOrWhiteSpace(gameName)
                ? $"{providerKey} Game {ToProviderGameLogKey(appId, providerGameKey)}"
                : gameName.Trim();
            db.ExecuteNonQuery(
                @"INSERT INTO Games
                    (ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc)
                  VALUES
                    (?, ?, ?, ?, ?, ?, ?, ?, ?);",
                providerKey,
                DbValue(string.IsNullOrWhiteSpace(providerPlatformKey) ? providerKey : providerPlatformKey.Trim()),
                appId > 0 ? (object)appId : DBNull.Value,
                DbValue(providerGameKey),
                playniteGameId.ToString(),
                DbValue(name),
                DbValue(providerKey),
                nowIso,
                nowIso);
            return db.ExecuteScalar<long>("SELECT last_insert_rowid();");
        }

        private static void UpdateMappedGameIdentity(
            SQLiteDatabase db,
            long gameId,
            int appId,
            string providerGameKey,
            string providerPlatformKey,
            string gameName,
            string nowIso)
        {
            if (gameId <= 0)
            {
                return;
            }

            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            db.ExecuteNonQuery(
                @"UPDATE Games
                  SET ProviderPlatformKey = COALESCE(?, ProviderPlatformKey),
                      ProviderGameId = COALESCE(?, ProviderGameId),
                      ProviderGameKey = COALESCE(?, ProviderGameKey),
                      GameName = COALESCE(?, GameName),
                      LastUpdatedUtc = ?
                  WHERE Id = ?;",
                DbValue(string.IsNullOrWhiteSpace(providerPlatformKey) ? null : providerPlatformKey.Trim()),
                appId > 0 ? (object)appId : DBNull.Value,
                DbValue(providerGameKey),
                DbValue(string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim()),
                nowIso,
                gameId);
        }

        private static void UpsertProviderGameDefinitionState(
            SQLiteDatabase db,
            string providerKey,
            int appId,
            string providerGameKey,
            string gameName,
            string iconUrl,
            string status,
            string checkedIso,
            string nowIso)
        {
            providerGameKey = NormalizeProviderGameKey(providerGameKey);
            db.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO ProviderGameDefinitionState
                    (ProviderKey, ProviderGameId, ProviderGameKey, GameName, IconUrl, Status, LastCheckedUtc, CreatedUtc, UpdatedUtc)
                  VALUES
                    (?, ?, ?, ?, ?, ?, ?, ?, ?);",
                providerKey,
                appId > 0 ? (object)appId : DBNull.Value,
                DbValue(providerGameKey),
                DbValue(gameName),
                DbValue(iconUrl),
                DbValue(status),
                checkedIso,
                nowIso,
                nowIso);
            if (!string.IsNullOrWhiteSpace(providerGameKey))
            {
                db.ExecuteNonQuery(
                    @"UPDATE ProviderGameDefinitionState
                  SET GameName = ?,
                      IconUrl = ?,
                      Status = ?,
                      LastCheckedUtc = ?,
                      UpdatedUtc = ?
                  WHERE ProviderKey = ?
                    AND ProviderGameKey = ?;",
                    DbValue(gameName),
                    DbValue(iconUrl),
                    DbValue(status),
                    checkedIso,
                    nowIso,
                    providerKey,
                    providerGameKey);
            }
            else
            {
                db.ExecuteNonQuery(
                    @"UPDATE ProviderGameDefinitionState
                      SET GameName = ?,
                          IconUrl = ?,
                          Status = ?,
                          LastCheckedUtc = ?,
                          UpdatedUtc = ?
                      WHERE ProviderKey = ?
                        AND ProviderGameId = ?;",
                    DbValue(gameName),
                    DbValue(iconUrl),
                    DbValue(status),
                    checkedIso,
                    nowIso,
                    providerKey,
                    appId);
            }
        }

        private static FriendUnownedCacheStats LoadUnownedFriendGameCacheStats(SQLiteDatabase db)
        {
            return new FriendUnownedCacheStats
            {
                Games = (int)Math.Max(0, db.ExecuteScalar<long>(
                    @"SELECT COUNT(*)
                      FROM Games g
                      WHERE (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')
                        AND NOT EXISTS (
                            SELECT 1
                            FROM UserGameProgress ugp
                            INNER JOIN Users u ON u.Id = ugp.UserId
                            WHERE ugp.GameId = g.Id
                              AND u.IsCurrentUser = 1
                        );")),
                Definitions = (int)Math.Max(0, db.ExecuteScalar<long>(
                    @"SELECT COUNT(*)
                      FROM AchievementDefinitions
                      WHERE GameId IN (
                          SELECT g.Id
                          FROM Games g
                          WHERE (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')
                            AND NOT EXISTS (
                                SELECT 1
                                FROM UserGameProgress ugp
                                INNER JOIN Users u ON u.Id = ugp.UserId
                                WHERE ugp.GameId = g.Id
                                  AND u.IsCurrentUser = 1
                            )
                      );")),
                OwnershipRows = (int)Math.Max(0, db.ExecuteScalar<long>(
                    @"SELECT COUNT(*)
                      FROM FriendOwnership
                      WHERE GameId IN (
                          SELECT Id FROM Games WHERE PlayniteGameId IS NULL OR TRIM(PlayniteGameId) = ''
                      );")),
                ProgressRows = (int)Math.Max(0, db.ExecuteScalar<long>(
                    @"SELECT COUNT(*)
                      FROM UserGameProgress ugp
                      INNER JOIN Games g ON g.Id = ugp.GameId
                      INNER JOIN Users u ON u.Id = ugp.UserId
                      WHERE u.IsCurrentUser = 0
                        AND (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '');")),
                AchievementRows = (int)Math.Max(0, db.ExecuteScalar<long>(
                    @"SELECT COUNT(*)
                      FROM UserAchievements
                      WHERE UserGameProgressId IN (
                          SELECT ugp.Id
                          FROM UserGameProgress ugp
                          INNER JOIN Games g ON g.Id = ugp.GameId
                          INNER JOIN Users u ON u.Id = ugp.UserId
                          WHERE u.IsCurrentUser = 0
                            AND (g.PlayniteGameId IS NULL OR TRIM(g.PlayniteGameId) = '')
                      );")),
                DefinitionStates = (int)Math.Max(0, db.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM ProviderGameDefinitionState;"))
            };
        }

        private static string ToDefinitionStatusStorage(FriendGameDefinitionStatus status)
        {
            switch (status)
            {
                case FriendGameDefinitionStatus.Ok:
                    return "ok";
                case FriendGameDefinitionStatus.NoAchievements:
                    return "no_achievements";
                case FriendGameDefinitionStatus.Transient:
                    return "transient";
                default:
                    return "unavailable";
            }
        }

        private static FriendGameDefinitionStatus ParseDefinitionStatus(string status)
        {
            if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return FriendGameDefinitionStatus.Ok;
            }

            if (string.Equals(status, "no_achievements", StringComparison.OrdinalIgnoreCase))
            {
                return FriendGameDefinitionStatus.NoAchievements;
            }

            if (string.Equals(status, "transient", StringComparison.OrdinalIgnoreCase))
            {
                return FriendGameDefinitionStatus.Transient;
            }

            return FriendGameDefinitionStatus.Unavailable;
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

        private static void DeleteStaleSharedFriendOwnership(SQLiteDatabase db, long userId, HashSet<long> seenGameIds)
        {
            if (seenGameIds == null || seenGameIds.Count == 0)
            {
                db.ExecuteNonQuery(
                    @"DELETE FROM FriendOwnership
                      WHERE UserId = ?
                        AND EXISTS (
                            SELECT 1
                            FROM Games g
                            WHERE g.Id = FriendOwnership.GameId
                              AND g.PlayniteGameId IS NOT NULL
                              AND TRIM(g.PlayniteGameId) <> ''
                        );",
                    userId);
                return;
            }

            var placeholders = string.Join(",", seenGameIds.Select(_ => "?"));
            var args = new List<object> { userId };
            args.AddRange(seenGameIds.Cast<object>());
            db.ExecuteNonQuery(
                @"DELETE FROM FriendOwnership
                  WHERE UserId = ?
                    AND GameId NOT IN (" + placeholders + @")
                    AND EXISTS (
                        SELECT 1
                        FROM Games g
                        WHERE g.Id = FriendOwnership.GameId
                          AND g.PlayniteGameId IS NOT NULL
                          AND TRIM(g.PlayniteGameId) <> ''
                    );",
                args.ToArray());
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
            foreach (var row in rows.Where(row => row != null))
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
                var incomingUnlocked = incoming?.Unlocked == true ? 1L : 0L;
                var incomingTime = incomingUnlocked != 0 ? NormalizeUnlockTime(incoming.UnlockTimeUtc) : null;
                var incomingIso = incomingTime.HasValue ? ToIso(incomingTime.Value) : null;
                var progressNum = incoming?.ProgressNum;
                var progressDenom = incoming?.ProgressDenom;

                if (!existingRows.TryGetValue(match.DefinitionId, out var existing))
                {
                    db.ExecuteNonQuery(
                        @"INSERT INTO UserAchievements
                            (UserGameProgressId, AchievementDefinitionId, Unlocked, UnlockTimeUtc, ProgressNum, ProgressDenom, LastUpdatedUtc, CreatedUtc)
                          VALUES
                            (?, ?, ?, ?, ?, ?, ?, ?);",
                        userProgressId,
                        match.DefinitionId,
                        incomingUnlocked,
                        DbValue(incomingIso),
                        progressNum.HasValue ? (object)progressNum.Value : DBNull.Value,
                        progressDenom.HasValue ? (object)progressDenom.Value : DBNull.Value,
                        updatedIso,
                        nowIso);
                    continue;
                }

                var existingIso = NormalizeStoredIso(existing.UnlockTimeUtc);
                var resolvedIso = incomingUnlocked != 0
                    ? incomingIso ?? existingIso
                    : null;
                var resolvedProgressNum = progressNum ?? existing.ProgressNum;
                var resolvedProgressDenom = progressDenom ?? existing.ProgressDenom;

                var changed = existing.Unlocked != incomingUnlocked ||
                              !NullableEquals(existingIso, resolvedIso) ||
                              existing.ProgressNum != resolvedProgressNum ||
                              existing.ProgressDenom != resolvedProgressDenom;
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
                    incomingUnlocked,
                    DbValue(resolvedIso),
                    resolvedProgressNum.HasValue ? (object)resolvedProgressNum.Value : DBNull.Value,
                    resolvedProgressDenom.HasValue ? (object)resolvedProgressDenom.Value : DBNull.Value,
                    updatedIso,
                    existing.Id);
            }
        }

        /// <summary>
        /// Loads the active (non-ignored) friend roster for a provider as lightweight identities, for
        /// display in provider settings. Ignored friends are tracked separately in provider settings.
        /// </summary>
        public List<FriendIdentity> LoadFriendIdentities(string providerKey)
        {
            providerKey = NormalizeProviderKey(providerKey);
            try
            {
                return WithDb(db =>
                    db.Load<FriendIdentityRow>(
                        @"SELECT
                            u.ProviderKey AS ProviderKey,
                            u.ExternalUserId AS ExternalUserId,
                            u.DisplayName AS DisplayName,
                            u.AvatarUrl AS AvatarUrl,
                            u.AvatarPath AS AvatarPath
                          FROM Users u
                          WHERE u.ProviderKey = ?
                            AND u.IsCurrentUser = 0
                            AND u.IsActiveFriend = 1
                            AND u.FriendSource IS NOT NULL
                          ORDER BY u.DisplayName;",
                        providerKey)
                    .Select(row => new FriendIdentity
                    {
                        ProviderKey = row.ProviderKey,
                        ExternalUserId = row.ExternalUserId,
                        DisplayName = row.DisplayName,
                        AvatarUrl = row.AvatarUrl,
                        AvatarPath = !string.IsNullOrWhiteSpace(row.AvatarPath)
                            ? MakeAbsolutePath(row.AvatarPath)
                            : row.AvatarUrl
                    })
                    .ToList());
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to load friend identities for provider={providerKey}.");
                return new List<FriendIdentity>();
            }
        }

        private sealed class FriendIdentityRow
        {
            public string ProviderKey { get; set; }
            public string ExternalUserId { get; set; }
            public string DisplayName { get; set; }
            public string AvatarUrl { get; set; }
            public string AvatarPath { get; set; }
        }

        private static List<FriendSummaryRow> LoadFriendSummaryRows(SQLiteDatabase db)
        {
            var recentCutoffIso = ToIso(DateTime.UtcNow.AddDays(-30));

            // Per-friend stats are pre-aggregated once (grouped by UserId) and joined back to
            // the friend rows, replacing the previous six correlated subqueries per friend.
            return db.Load<FriendSummaryRow>(
                @"WITH ownership AS (
                    SELECT fo.UserId AS UserId,
                           COUNT(DISTINCT fo.GameId) AS SharedGamesCount,
                           COALESCE(SUM(fo.PlaytimeForeverMinutes), 0) AS TotalPlaytimeMinutes
                    FROM FriendOwnership fo
                    INNER JOIN Users u ON u.Id = fo.UserId
                    WHERE u.IsCurrentUser = 0 AND u.IsActiveFriend = 1 AND u.FriendSource IS NOT NULL
                    GROUP BY fo.UserId
                ),
                unlocks AS (
                    SELECT ugp.UserId AS UserId,
                           COUNT(DISTINCT ugp.GameId) AS GamesWithUnlocksCount,
                           COUNT(ua.Id) AS UnlockedAchievementsCount,
                           COUNT(CASE WHEN ua.UnlockTimeUtc IS NOT NULL AND ua.UnlockTimeUtc >= ? THEN ua.Id END) AS RecentUnlockCount,
                           MAX(ua.UnlockTimeUtc) AS LastUnlockUtc
                    FROM UserGameProgress ugp
                    INNER JOIN Users u ON u.Id = ugp.UserId
                    INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                    WHERE u.IsCurrentUser = 0 AND u.IsActiveFriend = 1 AND u.FriendSource IS NOT NULL
                    GROUP BY ugp.UserId
                )
                SELECT
                    u.ProviderKey AS ProviderKey,
                    u.ExternalUserId AS ExternalUserId,
                    u.DisplayName AS DisplayName,
                    u.AvatarUrl AS AvatarUrl,
                    u.AvatarPath AS AvatarPath,
                    COALESCE(o.SharedGamesCount, 0) AS SharedGamesCount,
                    COALESCE(un.GamesWithUnlocksCount, 0) AS GamesWithUnlocksCount,
                    COALESCE(un.UnlockedAchievementsCount, 0) AS UnlockedAchievementsCount,
                    COALESCE(un.RecentUnlockCount, 0) AS RecentUnlockCount,
                    un.LastUnlockUtc AS LastUnlockUtc,
                    u.LastRefreshedUtc AS LastRefreshedUtc,
                    COALESCE(o.TotalPlaytimeMinutes, 0) AS TotalPlaytimeMinutes
                  FROM Users u
                  LEFT JOIN ownership o ON o.UserId = u.Id
                  LEFT JOIN unlocks un ON un.UserId = u.Id
                  WHERE u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                    AND u.FriendSource IS NOT NULL
                  ORDER BY un.LastUnlockUtc DESC, u.DisplayName;",
                recentCutoffIso).ToList();
        }

        private static List<FriendGameSummaryRow> LoadFriendGameSummaryRows(SQLiteDatabase db)
        {
            // Per-game friend stats are pre-aggregated once (grouped by GameId) across three
            // source CTEs and joined back to the game rows, replacing the previous eleven
            // correlated subqueries per game. Ownership is the driving set, so only games a
            // friend owns appear (matching the previous EXISTS filter).
            return db.Load<FriendGameSummaryRow>(
                @"WITH ownership AS (
                    SELECT fo.GameId AS GameId,
                           COUNT(DISTINCT u.Id) AS FriendCount,
                           COALESCE(SUM(fo.PlaytimeForeverMinutes), 0) AS TotalPlaytimeMinutes,
                           COALESCE(AVG(fo.PlaytimeForeverMinutes), 0) AS AveragePlaytimeMinutes,
                           MAX(fo.LastPlayedUtc) AS LastPlayedUtc,
                           MAX(fo.LastScrapedUtc) AS LastScrapedUtc
                    FROM FriendOwnership fo
                    INNER JOIN Users u ON u.Id = fo.UserId
                    WHERE u.IsCurrentUser = 0 AND u.IsActiveFriend = 1 AND u.FriendSource IS NOT NULL
                    GROUP BY fo.GameId
                ),
                unlocks AS (
                    SELECT ugp.GameId AS GameId,
                           COUNT(DISTINCT u.Id) AS FriendsWithUnlocksCount,
                           COUNT(ua.Id) AS UnlockedAchievementsCount,
                           COUNT(DISTINCT ua.AchievementDefinitionId) AS UniqueUnlockedAchievementsCount,
                           MAX(ua.UnlockTimeUtc) AS LastUnlockUtc
                    FROM Users u
                    INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id
                    INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id AND ua.Unlocked = 1
                    WHERE u.IsCurrentUser = 0 AND u.IsActiveFriend = 1 AND u.FriendSource IS NOT NULL
                    GROUP BY ugp.GameId
                ),
                totals AS (
                    SELECT ad.GameId AS GameId, COUNT(*) AS TotalAchievements
                    FROM AchievementDefinitions ad
                    GROUP BY ad.GameId
                ),
                scrapeStatus AS (
                    SELECT GameId, LastScrapeStatus
                    FROM (
                        SELECT fo.GameId AS GameId,
                               fo.LastScrapeStatus AS LastScrapeStatus,
                               ROW_NUMBER() OVER (PARTITION BY fo.GameId ORDER BY fo.LastScrapedUtc DESC) AS rn
                        FROM FriendOwnership fo
                        INNER JOIN Users u ON u.Id = fo.UserId
                        WHERE u.IsCurrentUser = 0 AND u.IsActiveFriend = 1 AND u.FriendSource IS NOT NULL
                          AND fo.LastScrapeStatus IS NOT NULL
                    )
                    WHERE rn = 1
                )
                SELECT
                    g.ProviderKey AS ProviderKey,
                    g.ProviderPlatformKey AS ProviderPlatformKey,
                    g.ProviderGameId AS ProviderGameId,
                    g.ProviderGameKey AS ProviderGameKey,
                    g.PlayniteGameId AS PlayniteGameId,
                    g.GameName AS GameName,
                    o.FriendCount AS FriendCount,
                    COALESCE(un.FriendsWithUnlocksCount, 0) AS FriendsWithUnlocksCount,
                    COALESCE(un.UnlockedAchievementsCount, 0) AS UnlockedAchievementsCount,
                    COALESCE(un.UniqueUnlockedAchievementsCount, 0) AS UniqueUnlockedAchievementsCount,
                    COALESCE(t.TotalAchievements, 0) AS TotalAchievements,
                    un.LastUnlockUtc AS LastUnlockUtc,
                    o.TotalPlaytimeMinutes AS TotalPlaytimeMinutes,
                    o.AveragePlaytimeMinutes AS AveragePlaytimeMinutes,
                    o.LastPlayedUtc AS LastPlayedUtc,
                    o.LastScrapedUtc AS LastScrapedUtc,
                    ss.LastScrapeStatus AS LastScrapeStatus,
                    g.IconPath AS IconPath,
                    g.CoverPath AS CoverPath
                  FROM Games g
                  INNER JOIN ownership o ON o.GameId = g.Id
                  LEFT JOIN unlocks un ON un.GameId = g.Id
                  LEFT JOIN totals t ON t.GameId = g.Id
                  LEFT JOIN scrapeStatus ss ON ss.GameId = g.Id
                  ORDER BY un.LastUnlockUtc DESC, g.GameName;").ToList();
        }

        private static List<FriendGameLinkRow> LoadFriendGameLinkRows(SQLiteDatabase db)
        {
            return db.Load<FriendGameLinkRow>(
                @"SELECT DISTINCT
                    g.ProviderKey AS ProviderKey,
                    u.ExternalUserId AS ExternalUserId,
                    g.ProviderGameId AS ProviderGameId,
                    g.ProviderGameKey AS ProviderGameKey,
                    g.PlayniteGameId AS PlayniteGameId,
                    fo.PlaytimeForeverMinutes AS PlaytimeForeverMinutes,
                    fo.LastPlayedUtc AS LastPlayedUtc
                  FROM Users u
                  INNER JOIN FriendOwnership fo ON fo.UserId = u.Id
                  INNER JOIN Games g ON g.Id = fo.GameId
                  WHERE u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                    AND u.FriendSource IS NOT NULL
                  ORDER BY u.ExternalUserId, g.GameName;").ToList();
        }

        private static List<FriendRecentUnlockRow> LoadFriendAllAchievementRows(SQLiteDatabase db)
        {
            return LoadFriendAchievementRows(db, 0, requireUnlockTime: false, unlockedOnly: false);
        }

        private static List<FriendRecentUnlockRow> LoadFriendUnlockedAchievementRows(SQLiteDatabase db)
        {
            return LoadFriendAchievementRows(db, 0, requireUnlockTime: false, unlockedOnly: true);
        }

        private static List<FriendRecentUnlockRow> LoadFriendAchievementRows(
            SQLiteDatabase db,
            int recentLimit,
            bool requireUnlockTime,
            bool unlockedOnly)
        {
            var sql = new StringBuilder(
                @"SELECT
                    g.ProviderKey AS ProviderKey,
                    g.ProviderGameId AS ProviderGameId,
                    g.ProviderGameKey AS ProviderGameKey,
                    g.PlayniteGameId AS PlayniteGameId,
                    g.GameName AS GameName,
                    u.ExternalUserId AS FriendExternalUserId,
                    u.DisplayName AS FriendName,
                    u.AvatarUrl AS FriendAvatarUrl,
                    u.AvatarPath AS FriendAvatarPath,
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
                    ua.Unlocked AS Unlocked,
                    COALESCE(sua.Unlocked, 0) AS MyUnlocked,
                    ua.ProgressNum AS ProgressNum,
                    ua.ProgressDenom AS ProgressDenom,
                    g.IconPath AS IconPath,
                    g.CoverPath AS CoverPath
                  FROM Users u
                  INNER JOIN UserGameProgress ugp ON ugp.UserId = u.Id
                  INNER JOIN Games g ON g.Id = ugp.GameId
                  INNER JOIN FriendOwnership fo ON fo.UserId = u.Id AND fo.GameId = g.Id
                  INNER JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id
                  INNER JOIN AchievementDefinitions ad ON ad.Id = ua.AchievementDefinitionId
                  LEFT JOIN Users su ON su.ProviderKey = g.ProviderKey AND su.IsCurrentUser = 1
                  LEFT JOIN UserGameProgress sugp ON sugp.UserId = su.Id AND sugp.GameId = g.Id
                  LEFT JOIN UserAchievements sua ON sua.UserGameProgressId = sugp.Id
                      AND sua.AchievementDefinitionId = ad.Id
                  WHERE u.IsCurrentUser = 0
                    AND u.IsActiveFriend = 1
                    AND u.FriendSource IS NOT NULL");

            if (unlockedOnly)
            {
                sql.Append(" AND ua.Unlocked = 1");
            }

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

        private List<FriendAchievementDisplayItem> MapFriendAchievementRows(
            IEnumerable<FriendRecentUnlockRow> rows,
            Dictionary<Guid, GamePresentation> presentationCache = null)
        {
            var result = new List<FriendAchievementDisplayItem>();
            foreach (var row in rows ?? Enumerable.Empty<FriendRecentUnlockRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ApiName))
                {
                    continue;
                }

                var friendUnlockTimeUtc = ParseUtc(row.UnlockTimeUtc);
                var isUnlockedByFriend = row.Unlocked.GetValueOrDefault() != 0;
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
                var presentation = ResolveGamePresentation(playniteGameId, presentationCache);
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
                    Unlocked = isUnlockedByFriend,
                    ProgressNum = detail.ProgressNum,
                    ProgressDenom = detail.ProgressDenom,
                    ProviderKey = row.ProviderKey,
                    AppId = (int)Math.Max(0, row.ProviderGameId ?? 0),
                    ProviderGameKey = NormalizeProviderGameKey(row.ProviderGameKey),
                    GameName = row.GameName,
                    SortingName = presentation.SortingName ?? row.GameName,
                    PlayniteGameId = playniteGameId,
                    GameIconPath = ResolveFriendGameIconPath(presentation, row.IconPath),
                    GameCoverPath = ResolveFriendGameCoverPath(presentation, row.CoverPath),
                    FriendName = row.FriendName,
                    FriendExternalUserId = row.FriendExternalUserId,
                    FriendAvatarPath = !string.IsNullOrWhiteSpace(row.FriendAvatarPath)
                        ? MakeAbsolutePath(row.FriendAvatarPath)
                        : row.FriendAvatarUrl
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
                                @"SELECT g.Id, g.ProviderKey, g.ProviderPlatformKey, g.ProviderGameId, g.ProviderGameKey, g.PlayniteGameId, g.GameName, g.LibrarySourceName, g.FirstSeenUtc, g.LastUpdatedUtc
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
            var providerGameKey = NormalizeProviderGameKey(data.ProviderGameKey);
            var isRetroAchievements = SqlNadoCacheBehavior.IsRetroAchievementsProvider(providerKey);

            GameRow game = null;
            if (!string.IsNullOrWhiteSpace(playniteGameId))
            {
                game = db.Load<GameRow>(
                                        @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
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
                                        @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
                      FROM Games
                      WHERE ProviderKey = ? AND ProviderGameId = ?
                      LIMIT 1;",
                    providerKey,
                    providerGameId.Value).FirstOrDefault();
            }

            if (game == null &&
                !string.IsNullOrWhiteSpace(providerGameKey) &&
                !string.IsNullOrWhiteSpace(playniteGameId))
            {
                game = LoadProviderOnlyGameByAppId(db, providerKey, 0, providerGameKey);
            }

            if (game == null &&
                providerGameId.HasValue &&
                providerGameId.Value > 0 &&
                !string.IsNullOrWhiteSpace(playniteGameId))
            {
                game = LoadProviderOnlyGameByAppId(db, providerKey, (int)providerGameId.Value);
            }

            if (game == null)
            {
                if (isRetroAchievements && providerGameId.HasValue && !string.IsNullOrWhiteSpace(playniteGameId))
                {
                    var mirroredRows = db.Load<GameRow>(
                                                @"SELECT Id, ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc
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
                        (ProviderKey, ProviderPlatformKey, ProviderGameId, ProviderGameKey, PlayniteGameId, GameName, LibrarySourceName, FirstSeenUtc, LastUpdatedUtc)
                      VALUES
                        (?, ?, ?, ?, ?, ?, ?, ?, ?);",
                    providerKey,
                    DbValue(data.ProviderPlatformKey),
                    providerGameId.HasValue ? (object)providerGameId.Value : DBNull.Value,
                    DbValue(providerGameKey),
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
                      ProviderGameKey = ?,
                      PlayniteGameId = ?,
                      GameName = ?,
                      LibrarySourceName = ?,
                      LastUpdatedUtc = ?
                  WHERE Id = ?;",
                DbValue(data.ProviderPlatformKey),
                providerGameId.HasValue ? (object)providerGameId.Value : DBNull.Value,
                DbValue(providerGameKey),
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
                ProviderGameKey = NormalizeProviderGameKey(progress?.ProviderGameKey),
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





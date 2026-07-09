using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.ViewModels;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Friends
{
    internal sealed class FriendCacheWriteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int IncomingCount { get; set; }
        public int WrittenCount { get; set; }
        public int SkippedCount { get; set; }

        public static FriendCacheWriteResult Ok(int incomingCount = 0, int writtenCount = 0, int skippedCount = 0) =>
            new FriendCacheWriteResult
            {
                Success = true,
                IncomingCount = Math.Max(0, incomingCount),
                WrittenCount = Math.Max(0, writtenCount),
                SkippedCount = Math.Max(0, skippedCount)
            };

        public static FriendCacheWriteResult Failed(string message) =>
            new FriendCacheWriteResult { Success = false, ErrorMessage = message };
    }

    internal sealed class FriendRefreshCandidate
    {
        public FriendIdentity Friend { get; set; }
        public int AppId { get; set; }
        public string ProviderGameKey { get; set; }
        public Guid? PlayniteGameId { get; set; }
        public string GameName { get; set; }
        public int PlaytimeForeverMinutes { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
        public DateTime? LastScrapedUtc { get; set; }
        public string LastScrapeStatus { get; set; }
    }

    internal sealed class FriendOwnershipSaveOptions
    {
        public bool IncludeProviderOnlyGames { get; set; }
    }

    /// <summary>
    /// A provider game in the current user's Playnite library: its provider identity plus the mapped
    /// Playnite game id. Used by the game-centric friend candidate builder to classify a friend's
    /// freshly-scraped ownership as mapped (library) vs provider-only and to intersect with the
    /// installed-game set, in a single cache read rather than per-game lookups.
    /// </summary>
    internal sealed class FriendGameMapping
    {
        public int AppId { get; set; }
        public string ProviderGameKey { get; set; }
        public Guid PlayniteGameId { get; set; }
    }

    /// <summary>
    /// Pre-overwrite snapshot of a friend's cached ownership row, used to decide whether the
    /// freshly-fetched owned-games data represents new activity since the last successful scrape.
    /// </summary>
    internal sealed class FriendOwnershipRecency
    {
        public int PlaytimeForeverMinutes { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
        public DateTime? LastScrapedUtc { get; set; }
        public string LastScrapeStatus { get; set; }
    }

    internal sealed class FriendGameDefinitionState
    {
        public string ProviderKey { get; set; }
        public int AppId { get; set; }
        public string ProviderGameKey { get; set; }
        public string GameName { get; set; }
        public string IconUrl { get; set; }
        public FriendGameDefinitionStatus Status { get; set; } = FriendGameDefinitionStatus.Unavailable;
        public DateTime? LastCheckedUtc { get; set; }
    }

    internal class FriendUnownedCacheStats
    {
        public int Games { get; set; }
        public int Definitions { get; set; }
        public int OwnershipRows { get; set; }
        public int ProgressRows { get; set; }
        public int AchievementRows { get; set; }
        public int DefinitionStates { get; set; }
    }

    internal sealed class FriendUnownedCacheClearResult : FriendUnownedCacheStats
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public static FriendUnownedCacheClearResult Failed(string message) =>
            new FriendUnownedCacheClearResult { Success = false, ErrorMessage = message };
    }

    internal sealed class FriendsOverviewData
    {
        public List<FriendSummaryItem> Friends { get; set; } = new List<FriendSummaryItem>();
        public List<FriendGameSummaryItem> Games { get; set; } = new List<FriendGameSummaryItem>();
        public List<FriendAchievementDisplayItem> RecentUnlocks { get; set; } = new List<FriendAchievementDisplayItem>();
        public List<FriendAchievementDisplayItem> AllAchievements { get; set; } = new List<FriendAchievementDisplayItem>();
        public List<FriendAchievementDisplayItem> AllUnlockedAchievements { get; set; } = new List<FriendAchievementDisplayItem>();
        public List<FriendGameLinkItem> FriendGameLinks { get; set; } = new List<FriendGameLinkItem>();
    }

    internal sealed class FriendGameLinkItem
    {
        public string ProviderKey { get; set; }
        public string ExternalUserId { get; set; }
        public string FriendGroupId { get; set; }
        public int AppId { get; set; }
        public string ProviderGameKey { get; set; }
        public Guid? PlayniteGameId { get; set; }
        public long PlaytimeForeverMinutes { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
    }

    internal interface IFriendCacheManager
    {
        FriendCacheWriteResult SaveFriendList(string providerKey, IReadOnlyList<FriendIdentity> friends);

        FriendCacheWriteResult SaveFriendOwnership(
            string providerKey,
            string externalUserId,
            IReadOnlyList<FriendGameOwnership> ownership,
            FriendOwnershipSaveOptions options = null);

        FriendCacheWriteResult SaveFriendGameDefinition(
            string providerKey,
            FriendGameDefinition definition);

        FriendCacheWriteResult SaveProviderGameImagePaths(
            string providerKey,
            string providerGameKey,
            int appId,
            string iconAbsolutePath,
            string coverAbsolutePath);

        Dictionary<string, FriendGameDefinitionState> LoadFriendGameDefinitionStates(
            string providerKey,
            IReadOnlyCollection<string> providerGameKeys);

        FriendUnownedCacheStats GetUnownedFriendGameCacheStats();

        FriendUnownedCacheClearResult ClearUnownedFriendGameData();

        FriendCacheWriteResult ClearUnownedFriendGame(string providerKey, int appId, string providerGameKey);

        bool IsProviderGameMappedToPlayniteLibrary(string providerKey, int appId, string providerGameKey);

        // All provider games mapped to the current user's Playnite library for this provider (one row
        // per mapped game). The game-centric candidate builder loads this once per provider to classify
        // freshly-scraped friend ownership as mapped vs provider-only and to resolve the Playnite game id
        // for the Installed-scope intersection, avoiding a per-game cache query.
        IReadOnlyList<FriendGameMapping> LoadFriendGameMappings(string providerKey);

        FriendCacheWriteResult PromoteProviderOnlyGameToPlayniteBacked(
            string providerKey,
            int appId,
            string providerGameKey,
            Guid playniteGameId);

        FriendCacheWriteResult SaveFriendGameAchievements(
            string providerKey,
            string externalUserId,
            string providerGameKey,
            int appId,
            FriendGameAchievements achievements);

        // When preserveFriendRecord is true, cached achievement/game/ownership data is cleared but the
        // friend's Users record is kept so the friend stays registered (used by "Clear Friend").
        FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId, bool preserveFriendRecord = false);

        List<FriendIdentity> LoadFriendIdentities(string providerKey);

        List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
            string providerKey,
            FriendRefreshOptions options);

        // Pre-save snapshot of a friend's cached ownership rows (playtime / last-played / last-scrape),
        // keyed by provider-game cache key, used by the Recent-scope recency gate.
        IReadOnlyDictionary<string, FriendOwnershipRecency> LoadFriendOwnershipRecency(
            string providerKey,
            string externalUserId);

        FriendsOverviewData LoadFriendsOverviewData(bool hideSpoilers, int recentLimit);

        // Current-user games (with the servicing provider label stored at scan time) used to resolve a
        // friend's games against the local library without re-deriving platform from Source/Platform.
        IReadOnlyList<CurrentUserGameLabel> LoadCurrentUserGameLabels();
    }
}

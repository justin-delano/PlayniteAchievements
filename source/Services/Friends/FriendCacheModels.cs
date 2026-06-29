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
        public Guid? PlayniteGameId { get; set; }
        public string GameName { get; set; }
        public int PlaytimeForeverMinutes { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
        public DateTime? LastScrapedUtc { get; set; }
        public string LastScrapeStatus { get; set; }
    }

    internal sealed class FriendsOverviewData
    {
        public List<FriendSummaryItem> Friends { get; set; } = new List<FriendSummaryItem>();
        public List<FriendGameSummaryItem> Games { get; set; } = new List<FriendGameSummaryItem>();
        public List<FriendAchievementDisplayItem> RecentUnlocks { get; set; } = new List<FriendAchievementDisplayItem>();
        public List<FriendAchievementDisplayItem> AllUnlockedAchievements { get; set; } = new List<FriendAchievementDisplayItem>();
        public List<FriendGameLinkItem> FriendGameLinks { get; set; } = new List<FriendGameLinkItem>();
    }

    internal sealed class FriendGameLinkItem
    {
        public string ProviderKey { get; set; }
        public string ExternalUserId { get; set; }
        public int AppId { get; set; }
        public Guid? PlayniteGameId { get; set; }
    }

    internal interface IFriendCacheManager
    {
        FriendCacheWriteResult SaveFriendList(string providerKey, IReadOnlyList<FriendIdentity> friends);

        FriendCacheWriteResult SaveFriendOwnership(
            string providerKey,
            string externalUserId,
            IReadOnlyList<FriendGameOwnership> ownership);

        FriendCacheWriteResult SaveFriendGameAchievements(
            string providerKey,
            string externalUserId,
            int appId,
            FriendGameAchievements achievements);

        List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
            string providerKey,
            FriendRefreshOptions options);

        FriendsOverviewData LoadFriendsOverviewData(bool hideSpoilers, int recentLimit);
    }
}

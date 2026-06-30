using PlayniteAchievements.Providers.Steam.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Models.Friends
{
    public enum FriendRefreshScope
    {
        Recent,
        Full,
        Shared,
        Installed,
        SelectedGame,
        Custom
    }

    public sealed class FriendRefreshOptions
    {
        public FriendRefreshScope Scope { get; set; } = FriendRefreshScope.Recent;
        public IReadOnlyCollection<Guid> PlayniteGameIds { get; set; }
        public IReadOnlyCollection<string> FriendExternalUserIds { get; set; }
        public TimeSpan? RefreshTtl { get; set; }

        public FriendRefreshOptions Clone()
        {
            return new FriendRefreshOptions
            {
                Scope = Scope,
                PlayniteGameIds = PlayniteGameIds?
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList(),
                FriendExternalUserIds = FriendExternalUserIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RefreshTtl = RefreshTtl
            };
        }
    }

    public sealed class FriendCustomRefreshOptions
    {
        public IReadOnlyCollection<string> ProviderKeys { get; set; }
        public FriendRefreshScope Scope { get; set; } = FriendRefreshScope.Recent;
        public IReadOnlyCollection<Guid> PlayniteGameIds { get; set; }
        public IReadOnlyCollection<string> FriendExternalUserIds { get; set; }
        public TimeSpan? RefreshTtl { get; set; }

        public FriendCustomRefreshOptions Clone()
        {
            return new FriendCustomRefreshOptions
            {
                ProviderKeys = ProviderKeys?
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => key.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Scope = Scope,
                PlayniteGameIds = PlayniteGameIds?
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList(),
                FriendExternalUserIds = FriendExternalUserIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RefreshTtl = RefreshTtl
            };
        }
    }

    public sealed class FriendsProviderResult<T>
    {
        public bool Success { get; set; }
        public bool AuthRequired { get; set; }
        public bool TransientFailure { get; set; }
        public string ErrorMessage { get; set; }
        public T Data { get; set; }

        public static FriendsProviderResult<T> FromData(T data) =>
            new FriendsProviderResult<T> { Success = true, Data = data };

        public static FriendsProviderResult<T> Failed(string message, bool authRequired = false, bool transientFailure = false) =>
            new FriendsProviderResult<T>
            {
                Success = false,
                AuthRequired = authRequired,
                TransientFailure = transientFailure,
                ErrorMessage = message
            };
    }

    public sealed class FriendIdentity
    {
        public string ProviderKey { get; set; }
        public string ExternalUserId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public DateTime? LastRefreshedUtc { get; set; }
    }

    public sealed class FriendGameOwnership
    {
        public string ProviderKey { get; set; }
        public string ExternalUserId { get; set; }
        public int AppId { get; set; }
        public int PlaytimeForeverMinutes { get; set; }
        public int? Playtime2WeeksMinutes { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
    }

    public sealed class FriendAchievementRow
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
        public bool Unlocked { get; set; }
        public DateTime? UnlockTimeUtc { get; set; }
        public int? ProgressNum { get; set; }
        public int? ProgressDenom { get; set; }
    }

    public sealed class FriendGameAchievements
    {
        public FriendIdentity Friend { get; set; }
        public int AppId { get; set; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public bool StatsUnavailable { get; set; }
        public bool TransientFailure { get; set; }
        public SteamScrapeDetail DetailCode { get; set; }
        public List<FriendAchievementRow> Rows { get; set; } = new List<FriendAchievementRow>();
    }

    public sealed class FriendsRefreshPreparation
    {
        public bool CanRefreshAchievements { get; set; } = true;
    }

    public interface IFriendsProvider
    {
        string ProviderKey { get; }

        Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel);

        void EndRefresh();

        Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel);

        Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetOwnedGamesAsync(
            FriendIdentity friend,
            CancellationToken cancel);

        Task<FriendsProviderResult<FriendGameAchievements>> GetFriendGameAchievementsAsync(
            FriendIdentity friend,
            int appId,
            string gameName,
            CancellationToken cancel);
    }
}

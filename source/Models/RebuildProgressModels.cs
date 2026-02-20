using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models
{
public enum RebuildUpdateKind
    {
        Stage,
        UserProgress,
        UserCompleted,
        Completed,
        AuthRequired
    }

    public enum RebuildStage
    {
        NotConfigured,
        LoadingOwnedGames,
        LoadingExistingCache,
        RefreshingUserAchievements,
        Completed
    }

    /// <summary>
    /// Simplified progress update from a data provider.
    /// </summary>
    public sealed class ProviderRefreshUpdate
    {
        public int CurrentIndex { get; set; }
        public int TotalItems { get; set; }
        public string CurrentGameName { get; set; }
        public bool AuthRequired { get; set; }
    }

    /// <summary>
    /// Progress update information for cache rebuild operations.
    /// </summary>
    public sealed class RebuildUpdate
    {
        public RebuildUpdateKind Kind { get; set; }
        public RebuildStage Stage { get; set; }

        public int UserAppIndex { get; set; }
        public int UserAppCount { get; set; }

        public int CurrentAppId { get; set; }
        public string CurrentGameName { get; set; }

        public int OverallIndex { get; set; }
        public int OverallCount { get; set; }
    }

    /// <summary>
    /// Summary information after a cache rebuild operation completes.
    /// </summary>
    public sealed class RebuildSummary
    {
        public int GamesRefreshed { get; set; }
        public int GamesWithAchievements { get; set; }
        public int GamesWithoutAchievements { get; set; }
    }

    /// <summary>
    /// Payload information for cache rebuild events.
    /// </summary>
    public sealed class RebuildPayload
    {
        public RebuildSummary Summary { get; set; } = new RebuildSummary();
    }
}
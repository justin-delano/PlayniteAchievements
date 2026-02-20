using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Options for user achievement cache refreshing (user-only, no friends).
    /// </summary>
    public sealed class CacheRefreshOptions
    {
        public IReadOnlyCollection<Guid> PlayniteGameIds { get; set; }
        public IReadOnlyCollection<int> SteamAppIds { get; set; }

        /// <summary>
        /// Quick refresh mode: refresh N most recently played games with achievements.
        /// Uses rtime_last_played from Steam API to determine recency.
        /// </summary>
        public bool QuickRefreshMode { get; set; } = false;

        /// <summary>
        /// Number of recent games to refresh in quick refresh mode (default: 10).
        /// </summary>
        public int QuickRefreshRecentGamesCount { get; set; } = 10;

        /// <summary>
        /// When true, games with zero playtime on Steam are included.
        /// </summary>
        public bool IncludeUnplayedGames { get; set; } = true;

        /// <summary>
        /// When true, skip games that are already cached with HasAchievements = false or ExcludedByUser = true.
        /// Default is true to avoid unnecessary API calls for games without achievements.
        /// </summary>
        public bool SkipNoAchievementsGames { get; set; } = true;
    }

    public class UserUnlockedAchievements
    {
        public int AppId { get; set; }
        public HashSet<string> UnlockedApiNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DateTime> UnlockTimesUtc { get; set; } = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int?> ProgressNum { get; set; } = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int?> ProgressDenom { get; set; } = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        public DateTime LastUpdatedUtc { get; set; }
    }
}

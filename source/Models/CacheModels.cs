using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Options for user achievement cache scanning (user-only, no friends).
    /// </summary>
    public sealed class CacheScanOptions
    {
        public IReadOnlyCollection<Guid> PlayniteGameIds { get; set; }
        public IReadOnlyCollection<int> SteamAppIds { get; set; }

        /// <summary>
        /// Quick refresh mode: scan N most recently played games with achievements.
        /// Uses rtime_last_played from Steam API to determine recency.
        /// </summary>
        public bool QuickRefreshMode { get; set; } = false;

        /// <summary>
        /// Number of recent games to scan in quick refresh mode (default: 10).
        /// </summary>
        public int QuickRefreshRecentGamesCount { get; set; } = 10;

        /// <summary>
        /// When true, games with zero playtime on Steam should be skipped.
        /// </summary>
        public bool IgnoreUnplayedGames { get; set; } = false;
    }

    public class UserUnlockedAchievements
    {
        public int AppId { get; set; }
        public Dictionary<string, DateTime> UnlockTimesUtc { get; set; } = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public DateTime LastUpdatedUtc { get; set; }
        public ulong PlaytimeSeconds { get; set; }
    }
}

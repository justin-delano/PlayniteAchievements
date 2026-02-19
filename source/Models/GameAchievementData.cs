using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models
{
    using Achievements;

    /// <summary>
    /// User achievement data for a single game, combining schema metadata with user progress.
    /// </summary>
    public sealed class GameAchievementData
    {
        public DateTime LastUpdatedUtc { get; set; }

        /// <summary>
        /// Name of the provider that produced this entry (e.g. Steam, RetroAchievements).
        /// Used for diagnostics and future multi-provider caching.
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// Playnite library source name for the game at scan time (e.g. Steam, GOG).
        /// Best-effort metadata; may be empty for some entries.
        /// </summary>
        public string LibrarySourceName { get; set; }

        public bool NoAchievements { get; set; }

        public bool IsCompleted { get; set; }

        public ulong PlaytimeSeconds { get; set; }

        public string GameName { get; set; }

        public int AppId { get; set; }

        public Guid? PlayniteGameId { get; set; }

        public List<AchievementDetail> Achievements { get; set; } = new List<AchievementDetail>();
    }
}


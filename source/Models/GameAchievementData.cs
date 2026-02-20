using System;
using System.Collections.Generic;
using System.Linq;

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
        /// Playnite library source name for the game at refresh time (e.g. Steam, GOG).
        /// Best-effort metadata; may be empty for some entries.
        /// </summary>
        public string LibrarySourceName { get; set; }

        public bool NoAchievements { get; set; }

        /// <summary>
        /// Computed completion status based on all achievements unlocked or capstone.
        /// </summary>
        public bool IsCompleted =>
            (Achievements?.Count > 0 && Achievements.All(a => a?.Unlocked == true)) ||
            IsCapstoneUnlocked();

        private bool IsCapstoneUnlocked()
        {
            if (Achievements == null || Achievements.Count == 0)
                return false;
            return Achievements.Any(a => a?.IsCapstone == true && a.Unlocked);
        }

        public ulong PlaytimeSeconds { get; set; }

        public string GameName { get; set; }

        public int AppId { get; set; }

        public Guid? PlayniteGameId { get; set; }

        public List<AchievementDetail> Achievements { get; set; } = new List<AchievementDetail>();
    }
}


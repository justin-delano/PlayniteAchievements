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
        /// Playnite library source name for the game at scan time (e.g. Steam, GOG).
        /// Best-effort metadata; may be empty for some entries.
        /// </summary>
        public string LibrarySourceName { get; set; }

        public bool NoAchievements { get; set; }

        /// <summary>
        /// Computed completion status based on provider signal, all achievements unlocked, or completion marker.
        /// </summary>
        public bool IsCompleted =>
            ProviderIsCompleted ||
            (Achievements?.Count > 0 && Achievements.All(a => a?.Unlocked == true)) ||
            IsMarkerUnlocked();

        private bool IsMarkerUnlocked()
        {
            if (string.IsNullOrWhiteSpace(CompletedMarkerApiName) || Achievements == null)
                return false;

            return Achievements.Any(a =>
                string.Equals(a?.ApiName?.Trim(), CompletedMarkerApiName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                a.Unlocked);
        }

        /// <summary>
        /// Provider-native completion signal (for example, PSN platinum unlocked).
        /// Persisted separately from computed completion so manual marker logic can
        /// recompute IsCompleted strictly from current marker state.
        /// </summary>
        public bool ProviderIsCompleted { get; set; }

        /// <summary>
        /// Optional per-game achievement ApiName selected as manual completion marker.
        /// When set, IsCompleted becomes true if this achievement is unlocked.
        /// </summary>
        public string CompletedMarkerApiName { get; set; }

        public ulong PlaytimeSeconds { get; set; }

        public string GameName { get; set; }

        public int AppId { get; set; }

        public Guid? PlayniteGameId { get; set; }

        public List<AchievementDetail> Achievements { get; set; } = new List<AchievementDetail>();
    }
}


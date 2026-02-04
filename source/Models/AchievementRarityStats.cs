using System;

namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Statistics for achievement rarity tiers.
    /// Matches SuccessStory.AchRaretyStats structure for theme compatibility.
    /// </summary>
    public class AchievementRarityStats
    {
        /// <summary>
        /// Total number of achievements in this rarity tier.
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Number of unlocked achievements in this rarity tier.
        /// </summary>
        public int Unlocked { get; set; }

        /// <summary>
        /// Alias for SuccessStory/Aniki themes expecting "UnLocked" naming.
        /// </summary>
        public int UnLocked => Unlocked;

        /// <summary>
        /// Number of locked achievements in this rarity tier.
        /// </summary>
        public int Locked { get; set; }

        /// <summary>
        /// String representation of the statistics (e.g., "5 / 10").
        /// </summary>
        public string Stats => $"{Unlocked} / {Total}";
    }
}

using System;
using System.Windows.Input;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Summary of achievement progress for a single game, used in all-games overview displays.
    /// Represents a "trophy card" showing progress, trophy counts, and metadata for one game.
    /// </summary>
    public sealed class GameAchievementSummary
    {
        /// <summary>
        /// Unique identifier for the game.
        /// </summary>
        public Guid GameId { get; }

        /// <summary>
        /// Game name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Platform/source name (e.g., "Steam", "GOG").
        /// </summary>
        public string Platform { get; }

        /// <summary>
        /// Full path to the game's cover image.
        /// </summary>
        public string CoverImagePath { get; }

        /// <summary>
        /// Achievement completion percentage (0-100).
        /// </summary>
        public int Progress { get; }

        /// <summary>
        /// Number of ultra-rare achievements unlocked (gold trophy equivalent).
        /// </summary>
        public int GoldCount { get; }

        /// <summary>
        /// Number of uncommon achievements unlocked (silver trophy equivalent).
        /// </summary>
        public int SilverCount { get; }

        /// <summary>
        /// Number of common achievements unlocked (bronze trophy equivalent).
        /// </summary>
        public int BronzeCount { get; }

        /// <summary>
        /// Date of the most recent achievement unlock.
        /// </summary>
        public DateTime LastUnlockDate { get; }

        /// <summary>
        /// Command to open the detailed achievement view for this game.
        /// </summary>
        public ICommand OpenAchievementWindow { get; }

        #region Legacy Aliases for Aniki ReMake/Helper Compatibility

        /// <summary>
        /// Legacy alias for GoldCount (Aniki ReMake compatibility).
        /// </summary>
        public int GS90Count => GoldCount;

        /// <summary>
        /// Legacy alias for SilverCount (Aniki ReMake compatibility).
        /// </summary>
        public int GS30Count => SilverCount;

        /// <summary>
        /// Legacy alias for BronzeCount (Aniki ReMake compatibility).
        /// </summary>
        public int GS15Count => BronzeCount;

        /// <summary>
        /// Legacy alias for LastUnlockDate (Aniki ReMake compatibility).
        /// </summary>
        public DateTime LatestUnlocked => LastUnlockDate;

        #endregion

        public GameAchievementSummary(
            Guid gameId,
            string name,
            string platform,
            string coverImagePath,
            int progress,
            int goldCount,
            int silverCount,
            int bronzeCount,
            DateTime lastUnlockDate,
            ICommand openAchievementWindow)
        {
            GameId = gameId;
            Name = name ?? string.Empty;
            Platform = platform ?? "Unknown";
            CoverImagePath = coverImagePath ?? string.Empty;
            Progress = Math.Max(0, Math.Min(100, progress));
            GoldCount = Math.Max(0, goldCount);
            SilverCount = Math.Max(0, silverCount);
            BronzeCount = Math.Max(0, bronzeCount);
            LastUnlockDate = lastUnlockDate;
            OpenAchievementWindow = openAchievementWindow;
        }
    }
}

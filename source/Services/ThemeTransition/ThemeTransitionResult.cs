using System;

namespace PlayniteAchievements.Services.ThemeTransition
{
    /// <summary>
    /// Result of a theme transition operation.
    /// </summary>
    public class TransitionResult
    {
        /// <summary>
        /// Gets or sets whether the transition was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the result message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the number of files processed.
        /// </summary>
        public int FilesProcessed { get; set; }

        /// <summary>
        /// Gets or sets the number of replacements made.
        /// </summary>
        public int ReplacementsMade { get; set; }

        /// <summary>
        /// Gets or sets the path to the backup folder.
        /// </summary>
        public string BackupPath { get; set; }
    }
}

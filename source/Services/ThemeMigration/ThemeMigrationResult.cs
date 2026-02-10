using System;

namespace PlayniteAchievements.Services.ThemeMigration
{
    /// <summary>
    /// Result of a theme migration operation.
    /// </summary>
    public class MigrationResult
    {
        /// <summary>
        /// Gets or sets whether the migration was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the result message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the number of files backed up.
        /// </summary>
        public int FilesBackedUp { get; set; }

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

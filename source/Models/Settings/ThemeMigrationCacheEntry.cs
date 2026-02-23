using System;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Persisted cache entry tracking the theme version last migrated by the plugin.
    /// Used to detect theme upgrades that may require re-migration.
    /// </summary>
    public class ThemeMigrationCacheEntry
    {
        public string ThemeName { get; set; }
        public string ThemePath { get; set; }
        public string MigratedThemeVersion { get; set; }
        public DateTime MigratedAtUtc { get; set; }
    }
}


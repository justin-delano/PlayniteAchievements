using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.ThemeMigration
{
    /// <summary>
    /// Service for discovering themes that need to be migrated from SuccessStory.
    /// </summary>
    public sealed class ThemeDiscoveryService
    {
        private readonly ILogger _logger;
        private readonly IPlayniteAPI _playniteApi;
        private const string BackupFolderName = "PlayniteAchievements_backup";

        public ThemeDiscoveryService(ILogger logger, IPlayniteAPI playniteApi)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
        }

        /// <summary>
        /// Represents a discovered theme.
        /// </summary>
        public class ThemeInfo
        {
            /// <summary>
            /// Display name from theme.yaml Name field.
            /// </summary>
            public string DisplayName { get; set; }

            /// <summary>
            /// Theme type (Desktop or Fullscreen).
            /// </summary>
            public string ThemeType { get; set; }

            /// <summary>
            /// Directory-based name (e.g., "Desktop/ThemeName").
            /// Used as fallback when DisplayName is not available.
            /// </summary>
            public string Name { get; set; }
            public string Path { get; set; }
            public bool HasBackup { get; set; }
            public bool NeedsMigration { get; set; }
            public bool CouldNotScan { get; set; }
            public string CurrentThemeVersion { get; set; }
            public string CachedMigratedThemeVersion { get; set; }
            public bool UpgradedSinceLastMigration { get; set; }

            /// <summary>
            /// Gets the best available name for display purposes.
            /// Combines ThemeType with DisplayName from theme.yaml if available,
            /// otherwise falls back to Name.
            /// </summary>
            public string BestDisplayName
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(ThemeType))
                    {
                        return $"{ThemeType}/{DisplayName}";
                    }
                    return Name;
                }
            }
        }

        /// <summary>
        /// Discovers all themes in the Playnite themes directory.
        /// </summary>
        /// <param name="themesRootPath">Root path to the themes directory.</param>
        /// <param name="themeMigrationVersionCache">
        /// Optional cache mapping ThemePath -> last migrated theme.yaml Version.
        /// When provided, discovery will flag themes that have upgraded versions since migration.
        /// </param>
        /// <returns>List of discovered themes.</returns>
        public List<ThemeInfo> DiscoverThemes(string themesRootPath, IReadOnlyDictionary<string, ThemeMigrationCacheEntry> themeMigrationVersionCache = null)
        {
            var themes = new List<ThemeInfo>();

            if (string.IsNullOrWhiteSpace(themesRootPath))
            {
                _logger.Warn("Themes root path is empty or null.");
                return themes;
            }

            if (!Directory.Exists(themesRootPath))
            {
                _logger.Warn($"Themes directory does not exist: {themesRootPath}");
                return themes;
            }

            try
            {
                // Playnite themes are organized in Desktop and Fullscreen subdirectories
                var subDirectories = new[] { "Desktop", "Fullscreen" };

                foreach (var subDir in subDirectories)
                {
                    var subDirPath = Path.Combine(themesRootPath, subDir);
                    if (!Directory.Exists(subDirPath))
                    {
                        _logger.Debug($"Theme subdirectory does not exist: {subDirPath}");
                        continue;
                    }

                    var themeDirectories = Directory.GetDirectories(subDirPath);
                    _logger.Info($"Found {themeDirectories.Length} themes in {subDir}");

                    foreach (var themeDir in themeDirectories)
                    {
                        var dirInfo = new DirectoryInfo(themeDir);
                        // Strip ID suffix from directory name (e.g., "Stellar_ab1234" -> "Stellar")
                        var cleanDirName = StripThemeIdSuffix(dirInfo.Name);
                        var themeName = $"{subDir}/{cleanDirName}";

                        _logger.Debug($"Processing theme: {themeName} at {themeDir}");

                        var backupPath = Path.Combine(themeDir, BackupFolderName);
                        var hasBackup = Directory.Exists(backupPath);
                        _logger.Debug($"Theme has backup: {hasBackup}");

                        // Check if theme contains SuccessStory references
                        var (needsMigration, couldNotScan) = CheckIfNeedsMigration(themeDir);

                        // Read theme.yaml version (optional)
                        string currentVersion = null;
                        ThemeYamlVersionReader.TryReadThemeVersion(themeDir, out currentVersion);

                        // Read theme.yaml Name (optional, for display purposes)
                        string displayName = null;
                        ThemeYamlVersionReader.TryReadThemeName(themeDir, out displayName);

                        // Compare against cached migrated version (optional)
                        string cachedVersion = null;
                        bool upgradedSinceLastMigration = false;
                        if (themeMigrationVersionCache != null &&
                            themeMigrationVersionCache.TryGetValue(themeDir, out var cached) &&
                            cached != null &&
                            !string.IsNullOrWhiteSpace(cached.MigratedThemeVersion) &&
                            !string.IsNullOrWhiteSpace(currentVersion))
                        {
                            cachedVersion = cached.MigratedThemeVersion;
                            upgradedSinceLastMigration = !string.Equals(cachedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
                        }

                        var themeInfo = new ThemeInfo
                        {
                            DisplayName = displayName,
                            ThemeType = subDir,
                            Name = themeName,
                            Path = themeDir,
                            HasBackup = hasBackup,
                            NeedsMigration = !hasBackup && needsMigration,
                            CouldNotScan = couldNotScan,
                            CurrentThemeVersion = currentVersion,
                            CachedMigratedThemeVersion = cachedVersion,
                            UpgradedSinceLastMigration = upgradedSinceLastMigration
                        };

                        themes.Add(themeInfo);

                        _logger.Info($"Discovered theme: {themeName}, NeedsMigration: {themeInfo.NeedsMigration}, HasBackup: {hasBackup}, CouldNotScan: {couldNotScan}");
                    }
                }

                _logger.Info($"Discovered {themes.Count} themes, {themes.Count(t => t.NeedsMigration)} need migration.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to discover themes in: {themesRootPath}");
            }

            return themes;
        }

        /// <summary>
        /// Strips the ID suffix from a theme directory name.
        /// Playnite theme directories often have IDs like "Stellar_ab1234" where
        /// "_ab1234" is an ID suffix that should be removed for display purposes.
        /// </summary>
        private static string StripThemeIdSuffix(string directoryName)
        {
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return directoryName;
            }

            // Pattern: underscore followed by 6+ alphanumeric characters at the end
            // e.g., "Stellar_ab1234" -> "Stellar"
            var lastUnderscoreIndex = directoryName.LastIndexOf('_');
            if (lastUnderscoreIndex > 0 && lastUnderscoreIndex < directoryName.Length - 1)
            {
                var suffix = directoryName.Substring(lastUnderscoreIndex + 1);
                // Check if suffix looks like an ID (alphanumeric, reasonable length)
                if (suffix.Length >= 6 && suffix.Length <= 12 && suffix.All(char.IsLetterOrDigit))
                {
                    return directoryName.Substring(0, lastUnderscoreIndex);
                }
            }

            return directoryName;
        }

        /// <summary>
        /// Gets the default Playnite themes directory path.
        /// </summary>
        /// <returns>Path to the themes directory, or null if not found.</returns>
        public string GetDefaultThemesPath()
        {
            try
            {
                // Playnite stores themes in the configuration directory
                var configPath = _playniteApi.Paths.ConfigurationPath;
                var themesPath = Path.Combine(configPath, "Themes");
                return Directory.Exists(themesPath) ? themesPath : null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get default themes path.");
                return null;
            }
        }

        /// <summary>
        /// Checks if a theme directory contains SuccessStory references.
        /// </summary>
        private (bool needsMigration, bool couldNotScan) CheckIfNeedsMigration(string themePath)
        {
            int filesRead = 0;
            int filesSkipped = 0;
            bool foundSuccessStory = false;
            bool foundPlayniteAchievements = false;

            try
            {
                var dirInfo = new DirectoryInfo(themePath);

                // Search recursively for XAML, PS1, and CS files
                var filesToCheck = new List<string>();

                try
                {
                    filesToCheck.AddRange(dirInfo.GetFiles("*.xaml", SearchOption.AllDirectories).Select(f => f.FullName));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not search for XAML files in {themePath}");
                }

                try
                {
                    filesToCheck.AddRange(dirInfo.GetFiles("*.ps1", SearchOption.AllDirectories).Select(f => f.FullName));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not search for PS1 files in {themePath}");
                }

                try
                {
                    filesToCheck.AddRange(dirInfo.GetFiles("*.cs", SearchOption.AllDirectories).Select(f => f.FullName));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not search for CS files in {themePath}");
                }

                // Remove duplicates and filter out backup folder
                filesToCheck = filesToCheck
                    .Where(f => !f.Contains(BackupFolderName))
                    .Distinct()
                    .ToList();

                _logger.Debug($"Found {filesToCheck.Count} files to check in theme: {themePath}");

                foreach (var file in filesToCheck.Take(100))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        filesRead++;

                        // Check if theme already uses PlayniteAchievements (already migrated)
                        if (content.IndexOf("PlayniteAchievements", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundPlayniteAchievements = true;
                            _logger.Debug($"Found PlayniteAchievements reference in: {file} - theme already migrated");
                            break;
                        }

                        if (content.IndexOf("SuccessStory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundSuccessStory = true;
                            _logger.Debug($"Found SuccessStory reference in: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        filesSkipped++;
                        _logger.Debug(ex, $"Could not read file while checking theme: {file}");
                    }

                    // Exit early if we found PlayniteAchievements (already migrated)
                    if (foundPlayniteAchievements)
                    {
                        break;
                    }
                }

                // If theme already uses PlayniteAchievements, it doesn't need migration
                if (foundPlayniteAchievements)
                {
                    _logger.Info($"Theme {Path.GetFileName(themePath)} already uses PlayniteAchievements - skipping");
                    return (false, false);
                }

                _logger.Info($"Theme scan for {Path.GetFileName(themePath)}: {filesRead} files read, {filesSkipped} files skipped, found SuccessStory: {foundSuccessStory}");

                // If we couldn't read ANY files, conservatively assume it needs migration
                // This handles the case where the theme is currently running and files are locked
                if (filesRead == 0 && filesSkipped > 0)
                {
                    _logger.Warn($"Could not read any files in theme (likely currently running): {themePath}");
                    return (true, true); // Conservative: assume it needs migration, mark as could not scan
                }

                return (foundSuccessStory, false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check if theme needs migration: {themePath}");
                return (true, true); // Conservative: assume it needs migration on error, mark as could not scan
            }
        }
    }
}

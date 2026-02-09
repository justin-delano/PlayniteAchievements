using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;

namespace PlayniteAchievements.Services.ThemeTransition
{
    /// <summary>
    /// Service for discovering themes that need to be transitioned from SuccessStory.
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
            public string Name { get; set; }
            public string Path { get; set; }
            public bool HasBackup { get; set; }
            public bool NeedsTransition { get; set; }
            public bool CouldNotScan { get; set; }
        }

        /// <summary>
        /// Discovers all themes in the Playnite themes directory.
        /// </summary>
        /// <param name="themesRootPath">Root path to the themes directory.</param>
        /// <returns>List of discovered themes.</returns>
        public List<ThemeInfo> DiscoverThemes(string themesRootPath)
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

                    foreach (var themeDir in themeDirectories)
                    {
                        var dirInfo = new DirectoryInfo(themeDir);
                        var themeName = $"{subDir}/{dirInfo.Name}";

                        var backupPath = Path.Combine(themeDir, BackupFolderName);
                        var hasBackup = Directory.Exists(backupPath);

                        // Check if theme contains SuccessStory references
                        var (needsTransition, couldNotScan) = CheckIfNeedsTransition(themeDir);

                        var themeInfo = new ThemeInfo
                        {
                            Name = themeName,
                            Path = themeDir,
                            HasBackup = hasBackup,
                            NeedsTransition = !hasBackup && needsTransition,
                            CouldNotScan = couldNotScan
                        };

                        themes.Add(themeInfo);

                        _logger.Debug($"Discovered theme: {themeName}, NeedsTransition: {themeInfo.NeedsTransition}, HasBackup: {hasBackup}, CouldNotScan: {couldNotScan}");
                    }
                }

                _logger.Info($"Discovered {themes.Count} themes, {themes.Count(t => t.NeedsTransition)} need transition.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to discover themes in: {themesRootPath}");
            }

            return themes;
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
        private (bool needsTransition, bool couldNotScan) CheckIfNeedsTransition(string themePath)
        {
            int filesRead = 0;
            int filesSkipped = 0;
            bool foundSuccessStory = false;

            try
            {
                var dirInfo = new DirectoryInfo(themePath);

                // Check for common theme files that might contain SuccessStory references
                var filesToCheck = new List<string>();
                filesToCheck.AddRange(dirInfo.GetFiles("*.xaml", SearchOption.TopDirectoryOnly).Select(f => f.FullName));
                filesToCheck.AddRange(dirInfo.GetFiles("*.ps1", SearchOption.TopDirectoryOnly).Select(f => f.FullName));
                filesToCheck.AddRange(dirInfo.GetFiles("*.cs", SearchOption.TopDirectoryOnly).Select(f => f.FullName));

                // Also check in subdirectories (limit depth for performance)
                foreach (var subDir in dirInfo.GetDirectories())
                {
                    if (subDir.Name == BackupFolderName || subDir.Name.StartsWith("."))
                    {
                        continue;
                    }

                    try
                    {
                        filesToCheck.AddRange(subDir.GetFiles("*.xaml", SearchOption.TopDirectoryOnly).Select(f => f.FullName));
                    }
                    catch { }
                }

                foreach (var file in filesToCheck.Take(50))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        filesRead++;
                        if (content.IndexOf("SuccessStory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundSuccessStory = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        filesSkipped++;
                        _logger.Debug(ex, $"Could not read file while checking theme: {file}");
                    }
                }

                _logger.Debug($"Theme scan for {themePath}: {filesRead} files read, {filesSkipped} files skipped, found SuccessStory: {foundSuccessStory}");

                // If we couldn't read ANY files, conservatively assume it needs transition
                // This handles the case where the theme is currently running and files are locked
                if (filesRead == 0 && filesSkipped > 0)
                {
                    _logger.Warn($"Could not read any files in theme (likely currently running): {themePath}");
                    return (true, true); // Conservative: assume it needs transition, mark as could not scan
                }

                return (foundSuccessStory, false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check if theme needs transition: {themePath}");
                return (true, true); // Conservative: assume it needs transition on error, mark as could not scan
            }
        }
    }
}

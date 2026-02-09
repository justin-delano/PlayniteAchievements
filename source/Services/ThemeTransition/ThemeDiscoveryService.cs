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
                    _logger.Info($"Found {themeDirectories.Length} themes in {subDir}");

                    foreach (var themeDir in themeDirectories)
                    {
                        var dirInfo = new DirectoryInfo(themeDir);
                        var themeName = $"{subDir}/{dirInfo.Name}";

                        _logger.Debug($"Processing theme: {themeName} at {themeDir}");

                        var backupPath = Path.Combine(themeDir, BackupFolderName);
                        var hasBackup = Directory.Exists(backupPath);
                        _logger.Debug($"Theme has backup: {hasBackup}");

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

                        _logger.Info($"Discovered theme: {themeName}, NeedsTransition: {themeInfo.NeedsTransition}, HasBackup: {hasBackup}, CouldNotScan: {couldNotScan}");
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
                        if (content.IndexOf("SuccessStory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundSuccessStory = true;
                            _logger.Debug($"Found SuccessStory reference in: {file}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        filesSkipped++;
                        _logger.Debug(ex, $"Could not read file while checking theme: {file}");
                    }
                }

                _logger.Info($"Theme scan for {Path.GetFileName(themePath)}: {filesRead} files read, {filesSkipped} files skipped, found SuccessStory: {foundSuccessStory}");

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

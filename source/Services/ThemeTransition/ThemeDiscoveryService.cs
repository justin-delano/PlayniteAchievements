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
        private const string BackupFolderName = "PlayniteAchievements_backup";

        public ThemeDiscoveryService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                var themeDirectories = Directory.GetDirectories(themesRootPath);

                foreach (var themeDir in themeDirectories)
                {
                    var dirInfo = new DirectoryInfo(themeDir);
                    var themeName = dirInfo.Name;

                    var backupPath = Path.Combine(themeDir, BackupFolderName);
                    var hasBackup = Directory.Exists(backupPath);

                    // Check if theme contains SuccessStory references
                    bool needsTransition = CheckIfNeedsTransition(themeDir);

                    var themeInfo = new ThemeInfo
                    {
                        Name = themeName,
                        Path = themeDir,
                        HasBackup = hasBackup,
                        NeedsTransition = !hasBackup && needsTransition
                    };

                    themes.Add(themeInfo);

                    _logger.Debug($"Discovered theme: {themeName}, NeedsTransition: {themeInfo.NeedsTransition}, HasBackup: {hasBackup}");
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
                var playnitePath = Path.GetDirectoryName(
                    System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);

                if (string.IsNullOrEmpty(playnitePath))
                {
                    return null;
                }

                var themesPath = Path.Combine(playnitePath, "Themes");
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
        private bool CheckIfNeedsTransition(string themePath)
        {
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
                        if (content.IndexOf("SuccessStory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Failed to check if theme needs transition: {themePath}");
                return false;
            }
        }
    }
}

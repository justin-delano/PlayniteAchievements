using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Playnite.SDK;

namespace PlayniteAchievements.Services.ThemeTransition
{
    /// <summary>
    /// Service for transitioning themes from SuccessStory to PlayniteAchievements.
    /// Creates a selective backup of only files that were changed.
    /// </summary>
    public sealed class ThemeTransitionService
    {
        private readonly ILogger _logger;
        private const string BackupFolderName = "PlayniteAchievements_backup";
        private const string ManifestFileName = "backup_manifest.txt";

        public ThemeTransitionService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Transitions a theme from SuccessStory to PlayniteAchievements.
        /// </summary>
        /// <param name="themePath">Full path to the theme directory.</param>
        /// <returns>Transition result with status and details.</returns>
        public async Task<TransitionResult> TransitionThemeAsync(string themePath)
        {
            if (string.IsNullOrWhiteSpace(themePath))
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = "Theme path cannot be empty."
                };
            }

            if (!Directory.Exists(themePath))
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = $"Theme directory does not exist: {themePath}"
                };
            }

            _logger.Info($"Starting theme transition for: {themePath}");

            try
            {
                var backupPath = Path.Combine(themePath, BackupFolderName);

                if (Directory.Exists(backupPath))
                {
                    _logger.Info($"Backup already exists at: {backupPath}");
                    return new TransitionResult
                    {
                        Success = false,
                        Message = $"Backup folder '{BackupFolderName}' already exists in theme directory. Please remove it first."
                    };
                }

                var result = await Task.Run(() =>
                {
                    return PerformTransition(themePath, backupPath);
                });

                // If no files needed changes, report success but note no backup was created
                if (result.FilesBackedUp == 0)
                {
                    _logger.Info($"No files contained SuccessStory references - no changes needed for: {themePath}");
                    return new TransitionResult
                    {
                        Success = true,
                        Message = "Theme already compatible - no SuccessStory references found. No backup created.",
                        FilesProcessed = 0,
                        ReplacementsMade = 0,
                        FilesBackedUp = 0
                    };
                }

                _logger.Info($"Theme transition completed successfully for: {themePath}");

                return new TransitionResult
                {
                    Success = true,
                    Message = $"Theme transitioned successfully. {result.FilesBackedUp} files backed up, {result.FilesProcessed} files modified.",
                    BackupPath = backupPath,
                    FilesProcessed = result.FilesProcessed,
                    ReplacementsMade = result.ReplacementsMade,
                    FilesBackedUp = result.FilesBackedUp
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to transition theme at: {themePath}");
                return new TransitionResult
                {
                    Success = false,
                    Message = $"Transition failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Performs the transition: backs up modified files and applies replacements.
        /// </summary>
        private TransitionResult PerformTransition(string themePath, string backupPath)
        {
            var backedUpFiles = new List<string>();
            int filesProcessed = 0;
            int replacementsMade = 0;
            int filesSkipped = 0;

            var themeDir = new DirectoryInfo(themePath);

            _logger.Info($"Scanning theme for SuccessStory references: {themePath}");

            // First pass: find files that need replacement and back them up
            foreach (var file in themeDir.GetFiles("*.*", SearchOption.AllDirectories))
            {
                if (file.FullName.StartsWith(backupPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string extension = file.Extension.ToLowerInvariant();
                if (!IsProcessableFile(extension))
                {
                    continue;
                }

                var (needsReplacement, replacementCount) = CheckIfNeedsReplacement(file);
                if (!needsReplacement)
                {
                    filesSkipped++;
                    _logger.Debug($"Skipped file (no SuccessStory): {GetRelativePath(file.FullName, themePath)}");
                    continue;
                }

                // Back up the file
                BackupFile(file, themePath, backupPath, backedUpFiles);
                _logger.Info($"Backing up file with {replacementCount} SuccessStory references: {GetRelativePath(file.FullName, themePath)}");

                // Apply replacements
                var (processed, count) = ProcessFile(file);
                if (processed)
                {
                    filesProcessed++;
                    replacementsMade += count;
                }
            }

            // Only create backup folder and manifest if we actually backed up files
            if (backedUpFiles.Count > 0)
            {
                WriteManifest(backupPath, backedUpFiles);
            }
            else
            {
                _logger.Info("No files contained SuccessStory references - no backup created");
            }

            _logger.Info($"Transition complete: {filesProcessed} files modified, {filesSkipped} files skipped, {replacementsMade} replacements made");

            return new TransitionResult
            {
                FilesBackedUp = backedUpFiles.Count,
                FilesProcessed = filesProcessed,
                ReplacementsMade = replacementsMade
            };
        }

        /// <summary>
        /// Gets the relative path from themePath to fullPath.
        /// </summary>
        private string GetRelativePath(string fullPath, string themePath)
        {
            if (fullPath.StartsWith(themePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(themePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return fullPath;
        }

        /// <summary>
        /// Checks if a file contains SuccessStory references.
        /// </summary>
        private (bool needsReplacement, int count) CheckIfNeedsReplacement(FileInfo file)
        {
            try
            {
                var content = File.ReadAllText(file.FullName, Encoding.UTF8);
                int fullscreenHelperCount = CountOccurrences(content, "SuccessStoryFullscreenHelper");
                int pluginIdCount = CountOccurrences(content, "playnite-successstory-plugin");
                int helperCount = CountOccurrences(content, "SSHelper");
                int successStoryCount = CountOccurrences(content, "SuccessStory");
                int totalCount = fullscreenHelperCount + pluginIdCount + helperCount + successStoryCount;

                return (totalCount > 0, totalCount);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Could not check file for replacement: {file.FullName}");
                return (false, 0);
            }
        }

        /// <summary>
        /// Backs up a single file, preserving directory structure.
        /// </summary>
        private void BackupFile(FileInfo file, string themePath, string backupPath, List<string> backedUpFiles)
        {
            string relativePath = file.FullName.Substring(themePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destFile = Path.Combine(backupPath, relativePath);
            string destDir = Path.GetDirectoryName(destFile);

            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            file.CopyTo(destFile, overwrite: false);
            backedUpFiles.Add(relativePath);
        }

        /// <summary>
        /// Writes the backup manifest file.
        /// </summary>
        private void WriteManifest(string backupPath, List<string> backedUpFiles)
        {
            string manifestPath = Path.Combine(backupPath, ManifestFileName);
            File.WriteAllLines(manifestPath, backedUpFiles);
            _logger.Info($"Wrote manifest with {backedUpFiles.Count} entries to: {manifestPath}");
        }

        /// <summary>
        /// Reads the backup manifest file.
        /// </summary>
        private List<string> ReadManifest(string backupPath)
        {
            string manifestPath = Path.Combine(backupPath, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                _logger.Warn($"Manifest file not found: {manifestPath}");
                return new List<string>();
            }

            var files = File.ReadAllLines(manifestPath).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            _logger.Info($"Read manifest with {files.Count} entries from: {manifestPath}");
            return files;
        }

        /// <summary>
        /// Processes a single file, performing replacements.
        /// </summary>
        private (bool processed, int replacements) ProcessFile(FileInfo file)
        {
            string content;
            try
            {
                content = File.ReadAllText(file.FullName, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to read file: {file.FullName}");
                return (false, 0);
            }

            string originalContent = content;
            int replacements = 0;

            // Replace SuccessStoryFullscreenHelper first (most specific - fullscreen installation checks)
            content = content.Replace("SuccessStoryFullscreenHelper", "PlayniteAchievements");
            replacements += CountOccurrences(originalContent, "SuccessStoryFullscreenHelper");

            // Replace playnite-successstory-plugin second (installation checks)
            content = content.Replace("playnite-successstory-plugin", "PlayniteAchievements");
            replacements += CountOccurrences(originalContent, "playnite-successstory-plugin");

            // Replace SSHelper third (class references)
            content = content.Replace("SSHelper", "PlayniteAchievements");
            replacements += CountOccurrences(originalContent, "SSHelper");

            // Then replace SuccessStory (most general - matches all above)
            content = content.Replace("SuccessStory", "PlayniteAchievements");
            replacements += CountOccurrences(originalContent, "SuccessStory");

            if (content != originalContent)
            {
                try
                {
                    File.WriteAllText(file.FullName, content, Encoding.UTF8);
                    _logger.Debug($"Replaced {replacements} occurrences in: {file.Name}");
                    return (true, replacements);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to write file: {file.FullName}");
                    return (false, 0);
                }
            }

            return (false, 0);
        }

        /// <summary>
        /// Determines if a file type should be processed for replacements.
        /// </summary>
        private bool IsProcessableFile(string extension)
        {
            return extension == ".xaml";
        }

        /// <summary>
        /// Counts non-overlapping occurrences of a substring in text.
        /// </summary>
        private int CountOccurrences(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            int patternLength = pattern.Length;

            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += patternLength;
            }

            return count;
        }

        /// <summary>
        /// Reverts a theme to its original state from backup.
        /// </summary>
        /// <param name="themePath">Full path to the theme directory.</param>
        /// <returns>Transition result with status and details.</returns>
        public async Task<TransitionResult> RevertThemeAsync(string themePath)
        {
            if (string.IsNullOrWhiteSpace(themePath))
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = "Theme path cannot be empty."
                };
            }

            if (!Directory.Exists(themePath))
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = $"Theme directory does not exist: {themePath}"
                };
            }

            var backupPath = Path.Combine(themePath, BackupFolderName);

            if (!Directory.Exists(backupPath))
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = $"No backup folder found at: {BackupFolderName}"
                };
            }

            _logger.Info($"Starting theme revert for: {themePath}");

            try
            {
                var filesRestored = await Task.Run(() =>
                {
                    return RestoreFromBackup(themePath, backupPath);
                });

                _logger.Info($"Theme revert completed successfully for: {themePath}");

                return new TransitionResult
                {
                    Success = true,
                    Message = filesRestored == 0
                        ? "No files needed restoring (backup was empty). Backup folder deleted."
                        : $"Theme reverted successfully. {filesRestored} files restored. Backup folder deleted."
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to revert theme at: {themePath}");
                return new TransitionResult
                {
                    Success = false,
                    Message = $"Revert failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Restores theme files from the backup directory using the manifest.
        /// </summary>
        private int RestoreFromBackup(string themePath, string backupPath)
        {
            _logger.Info($"Restoring from backup: {backupPath}");

            var backedUpFiles = ReadManifest(backupPath);
            int filesRestored = 0;

            foreach (var relativePath in backedUpFiles)
            {
                string backupFile = Path.Combine(backupPath, relativePath);
                string themeFile = Path.Combine(themePath, relativePath);

                if (!File.Exists(backupFile))
                {
                    _logger.Warn($"Backup file not found: {backupFile}");
                    continue;
                }

                try
                {
                    // Ensure target directory exists
                    string targetDir = Path.GetDirectoryName(themeFile);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(backupFile, themeFile, overwrite: true);
                    filesRestored++;
                    _logger.Debug($"Restored file: {relativePath}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to restore file: {relativePath}");
                }
            }

            // Delete the backup folder
            try
            {
                Directory.Delete(backupPath, recursive: true);
                _logger.Info($"Deleted backup folder: {backupPath}");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Could not delete backup folder: {backupPath}");
            }

            _logger.Info($"Restore completed: {filesRestored} files restored");
            return filesRestored;
        }

        /// <summary>
        /// Checks if a theme has a backup folder.
        /// </summary>
        /// <param name="themePath">Full path to the theme directory.</param>
        /// <returns>True if backup folder exists.</returns>
        public bool HasBackup(string themePath)
        {
            if (string.IsNullOrWhiteSpace(themePath))
            {
                return false;
            }

            var backupPath = Path.Combine(themePath, BackupFolderName);
            return Directory.Exists(backupPath);
        }
    }
}

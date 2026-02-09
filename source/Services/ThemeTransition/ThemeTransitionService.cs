using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Playnite.SDK;

namespace PlayniteAchievements.Services.ThemeTransition
{
    /// <summary>
    /// Service for transitioning themes from SuccessStory to PlayniteAchievements.
    /// Creates a backup of the theme and replaces SuccessStory references.
    /// </summary>
    public sealed class ThemeTransitionService
    {
        private readonly ILogger _logger;
        private const string BackupFolderName = "PlayniteAchievements_backup";

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

                await Task.Run(() =>
                {
                    CreateBackup(themePath, backupPath);
                    PerformReplacements(themePath, backupPath);
                });

                _logger.Info($"Theme transition completed successfully for: {themePath}");

                return new TransitionResult
                {
                    Success = true,
                    Message = $"Theme transitioned successfully. Backup created at: {BackupFolderName}",
                    BackupPath = backupPath
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
        /// Creates a full backup of the theme directory.
        /// </summary>
        private void CreateBackup(string sourcePath, string backupPath)
        {
            _logger.Info($"Creating backup at: {backupPath}");

            var sourceDir = new DirectoryInfo(sourcePath);
            var backupDir = Directory.CreateDirectory(backupPath);

            foreach (var file in sourceDir.GetFiles())
            {
                string destFile = Path.Combine(backupPath, file.Name);
                file.CopyTo(destFile, overwrite: true);
                _logger.Debug($"Copied file: {file.Name} -> {destFile}");
            }

            foreach (var dir in sourceDir.GetDirectories())
            {
                if (dir.Name == BackupFolderName)
                {
                    continue;
                }

                string destDir = Path.Combine(backupPath, dir.Name);
                CopyDirectoryRecursive(dir.FullName, destDir);
            }

            _logger.Info($"Backup created with {backupDir.GetFiles().Length} files and {backupDir.GetDirectories().Length} directories");
        }

        /// <summary>
        /// Recursively copies a directory.
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            var sourceInfo = new DirectoryInfo(sourceDir);

            foreach (var file in sourceInfo.GetFiles())
            {
                string destFile = Path.Combine(destDir, file.Name);
                file.CopyTo(destFile, overwrite: true);
            }

            foreach (var dir in sourceInfo.GetDirectories())
            {
                string nestedDestDir = Path.Combine(destDir, dir.Name);
                CopyDirectoryRecursive(dir.FullName, nestedDestDir);
            }
        }

        /// <summary>
        /// Performs SuccessStory to PlayniteAchievements replacements in all theme files.
        /// </summary>
        private void PerformReplacements(string themePath, string backupPath)
        {
            _logger.Info("Performing string replacements in theme files");

            int filesProcessed = 0;
            int replacementsMade = 0;

            var themeDir = new DirectoryInfo(themePath);

            foreach (var file in themeDir.GetFiles("*.*", SearchOption.AllDirectories))
            {
                if (file.FullName.StartsWith(backupPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string extension = file.Extension.ToLowerInvariant();
                if (!IsProcessableFile(extension))
                {
                    _logger.Debug($"Skipping file with extension {extension}: {file.Name}");
                    continue;
                }

                try
                {
                    var (processed, count) = ProcessFile(file);
                    if (processed)
                    {
                        filesProcessed++;
                        replacementsMade += count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to process file: {file.FullName}");
                }
            }

            _logger.Info($"Replacements complete: {filesProcessed} files processed, {replacementsMade} replacements made");
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

            content = content.Replace("SuccessStory", "PlayniteAchievements");
            replacements += CountOccurrences(originalContent, "SuccessStory");

            content = content.Replace("SuccessStoryFullscreenHelper", "PlayniteAchievements");
            replacements += CountOccurrences(originalContent, "SuccessStoryFullscreenHelper");

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
            // return extension == ".xaml" ||
            //        extension == ".ps1" ||
            //        extension == ".cs" ||
            //        extension == ".txt" ||
            //        extension == ".md" ||
            //        extension == ".json" ||
            //        extension == ".xml" ||
            //        extension == ".yaml" ||
            //        extension == ".yml";
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
                await Task.Run(() =>
                {
                    RestoreFromBackup(themePath, backupPath);
                });

                _logger.Info($"Theme revert completed successfully for: {themePath}");

                return new TransitionResult
                {
                    Success = true,
                    Message = $"Theme reverted successfully. Backup folder preserved at: {BackupFolderName}"
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
        /// Restores theme files from the backup directory.
        /// </summary>
        private void RestoreFromBackup(string themePath, string backupPath)
        {
            _logger.Info($"Restoring from backup: {backupPath}");

            var backupDir = new DirectoryInfo(backupPath);

            // First, delete all files and directories in the theme root (except backup)
            var themeDir = new DirectoryInfo(themePath);

            foreach (var file in themeDir.GetFiles())
            {
                try
                {
                    file.Delete();
                    _logger.Debug($"Deleted file: {file.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to delete file: {file.Name}");
                }
            }

            foreach (var dir in themeDir.GetDirectories())
            {
                if (dir.Name == BackupFolderName)
                {
                    continue;
                }

                try
                {
                    Directory.Delete(dir.FullName, recursive: true);
                    _logger.Debug($"Deleted directory: {dir.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to delete directory: {dir.Name}");
                }
            }

            // Now copy everything from backup to theme root
            foreach (var file in backupDir.GetFiles())
            {
                string destFile = Path.Combine(themePath, file.Name);
                file.CopyTo(destFile, overwrite: true);
                _logger.Debug($"Restored file: {file.Name}");
            }

            foreach (var dir in backupDir.GetDirectories())
            {
                string destDir = Path.Combine(themePath, dir.Name);
                CopyDirectoryRecursive(dir.FullName, destDir);
                _logger.Debug($"Restored directory: {dir.Name}");
            }

            _logger.Info("Restore completed");
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

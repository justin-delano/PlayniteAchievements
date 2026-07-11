using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Images;

namespace PlayniteAchievements.Views.Settings.General
{
    /// <summary>
    /// General settings: Maintenance section. Hosts cached data clearing, icon cache clearing,
    /// and utility actions (reset first-time setup, database export, open data folder).
    /// </summary>
    public partial class MaintenanceSettingsSection : UserControl
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        public MaintenanceSettingsSection()
        {
            InitializeComponent();
        }

        internal MaintenanceSettingsSection(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger)
            : this()
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
        }

        // -----------------------------
        // Cache actions
        // -----------------------------

        private void WipeCache_Click(object sender, RoutedEventArgs e)
        {
            string message = null;
            var image = MessageBoxImage.Information;
            Exception operationError = null;
            var progressText = L(
                "LOCPlayAch_Settings_Cache_ProgressClearing",
                "Clearing cached achievement data...");

            RunMaintenanceProgress(
                progressText,
                isIndeterminate: true,
                operation: progress =>
                {
                    try
                    {
                        _plugin.RefreshRuntime.Cache.ClearCache();
                        message = L("LOCPlayAch_Status_Succeeded", "Success!");
                        image = MessageBoxImage.Information;
                    }
                    catch (Exception ex)
                    {
                        operationError = ex;
                    }
                });

            if (operationError != null)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", operationError.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _plugin.PlayniteApi.Dialogs.ShowMessage(
                message ?? L("LOCPlayAch_Status_Succeeded", "Success!"),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                image);
        }

        private void ClearUnownedFriendGameData_Click(object sender, RoutedEventArgs e)
        {
            var friendCache = _plugin?.RefreshRuntime?.Cache as IFriendCacheManager;
            if (friendCache == null)
            {
                return;
            }

            try
            {
                var stats = friendCache.GetUnownedFriendGameCacheStats() ?? new FriendUnownedCacheStats();
                if (stats.Games <= 0 &&
                    stats.DefinitionStates <= 0 &&
                    stats.OwnershipRows <= 0 &&
                    stats.ProgressRows <= 0 &&
                    stats.AchievementRows <= 0 &&
                    stats.Definitions <= 0)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        L("LOCPlayAch_FriendsOverview_ClearUnowned_None", "No unowned friend game data is cached."),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var message = LF(
                    "LOCPlayAch_FriendsOverview_ClearUnowned_Confirm",
                    "Clear unowned friend game data?\n\nThis will delete {0:N0} provider-only games, {1:N0} achievement definitions, {2:N0} friend ownership rows, {3:N0} friend progress rows, {4:N0} achievement rows, and {5:N0} definition-state rows. Owned/shared friend data and your current-user cache will be kept.",
                    stats.Games,
                    stats.Definitions,
                    stats.OwnershipRows,
                    stats.ProgressRows,
                    stats.AchievementRows,
                    stats.DefinitionStates);

                if (_plugin.PlayniteApi.Dialogs.ShowMessage(
                        message,
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = friendCache.ClearUnownedFriendGameData();
                if (result?.Success != true)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        LF("LOCPlayAch_Status_Failed", "Error: {0}", result?.ErrorMessage ?? "unknown"),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Remove every cached unowned cover/icon file in one pass.
                _plugin.ImageService?.ClearGameCache(FriendImageCacheFolders.Games);

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF(
                        "LOCPlayAch_FriendsOverview_ClearUnowned_Done",
                        "Cleared {0:N0} provider-only games and {1:N0} friend progress rows.",
                        result.Games,
                        result.ProgressRows),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to clear unowned friend game data.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearAllIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.All);

        private void ClearCompressedIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.CompressedOnly);

        private void ClearFullResolutionIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.FullResolutionOnly);

        private void ClearLockedIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.LockedOnly);

        private void ClearIconCache(IconCacheClearScope scope)
        {
            var fileLabel = ResourceProvider.GetString(GetIconCacheFileLabelResourceKey(scope));
            var scanningText = LF(
                "LOCPlayAch_Settings_IconCache_ProgressScanning",
                "Scanning cached {0} files...",
                fileLabel);
            var deletingTextFormat = L(
                "LOCPlayAch_Settings_IconCache_ProgressDeletingCount",
                "Deleting cached {0} files... ({1}/{2})");
            var deletedCount = 0;
            Exception operationError = null;

            RunMaintenanceProgress(
                scanningText,
                isIndeterminate: false,
                operation: progress =>
                {
                    try
                    {
                        UpdateMaintenanceProgress(progress, current: 0, max: 1);

                        IEnumerable<string> additionalPaths = null;
                        if (scope == IconCacheClearScope.LockedOnly)
                        {
                            additionalPaths = GetExplicitLockedIconCachePaths(progress);
                        }

                        _plugin.ImageService?.Clear();
                        deletedCount = _plugin.ImageService?.ClearDiskCache(
                            scope,
                            additionalPaths,
                            (processed, total) =>
                            {
                                var safeTotal = Math.Max(1, total);
                                var safeProcessed = total <= 0
                                    ? 1
                                    : Math.Max(0, Math.Min(total, processed));

                                var progressText = total <= 0
                                    ? LF(
                                        "LOCPlayAch_Settings_IconCache_ProgressNoFiles",
                                        "No cached {0} files were found.",
                                        fileLabel)
                                    : string.Format(
                                        deletingTextFormat,
                                        fileLabel,
                                        safeProcessed,
                                        total);

                                UpdateMaintenanceProgress(
                                    progress,
                                    text: progressText,
                                    current: safeProcessed,
                                    max: safeTotal);
                            }) ?? 0;
                    }
                    catch (Exception ex)
                    {
                        operationError = ex;
                    }
                });

            if (operationError != null)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", operationError.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var message = L("LOCPlayAch_Status_Succeeded", "Success!");

            _plugin.PlayniteApi.Dialogs.ShowMessage(
                message,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private string GetIconCacheFileLabelResourceKey(IconCacheClearScope scope)
        {
            switch (scope)
            {
                case IconCacheClearScope.CompressedOnly:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_Compressed";
                case IconCacheClearScope.FullResolutionOnly:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_FullResolution";
                case IconCacheClearScope.LockedOnly:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_Locked";
                default:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_All";
            }
        }

        private void RunMaintenanceProgress(
            string initialText,
            bool isIndeterminate,
            Action<GlobalProgressActionArgs> operation)
        {
            var progressOptions = new GlobalProgressOptions(initialText)
            {
                Cancelable = false,
                IsIndeterminate = isIndeterminate
            };

            _plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(async progress =>
            {
                UpdateMaintenanceProgress(progress, text: initialText, isIndeterminate: isIndeterminate);
                await Task.Run(() => operation?.Invoke(progress)).ConfigureAwait(false);
            }, progressOptions);
        }

        private void UpdateMaintenanceProgress(
            GlobalProgressActionArgs progress,
            string text = null,
            int? current = null,
            int? max = null,
            bool? isIndeterminate = null)
        {
            if (progress == null)
            {
                return;
            }

            Action update = () =>
            {
                if (max.HasValue)
                {
                    progress.ProgressMaxValue = max.Value;
                }

                if (current.HasValue)
                {
                    progress.CurrentProgressValue = current.Value;
                }

                if (isIndeterminate.HasValue)
                {
                    progress.IsIndeterminate = isIndeterminate.Value;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    progress.Text = text;
                }
            };

            if (progress.MainDispatcher != null)
            {
                progress.MainDispatcher.InvokeIfNeeded(update);
            }
            else
            {
                update();
            }
        }

        private IEnumerable<string> GetExplicitLockedIconCachePaths(GlobalProgressActionArgs progress = null)
        {
            var dataService = _plugin?.AchievementDataService;
            var cachedGameIds = dataService?.GetCachedGameIds();
            if (cachedGameIds == null || cachedGameIds.Count == 0)
            {
                if (progress != null)
                {
                    UpdateMaintenanceProgress(
                        progress,
                        text: L(
                            "LOCPlayAch_Settings_IconCache_ProgressNoLockedReferences",
                            "No cached locked icon references were found."),
                        current: 1,
                        max: 1);
                }

                return Array.Empty<string>();
            }

            var lockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (progress != null)
            {
                UpdateMaintenanceProgress(progress, current: 0, max: cachedGameIds.Count);
            }

            for (var i = 0; i < cachedGameIds.Count; i++)
            {
                var gameId = cachedGameIds[i];
                if (progress != null)
                {
                    UpdateMaintenanceProgress(
                        progress,
                        text: LF(
                            "LOCPlayAch_Settings_IconCache_ProgressScanningLockedReferences",
                            "Scanning cached locked icon references... ({0}/{1})",
                            i + 1,
                            cachedGameIds.Count),
                        current: i + 1,
                        max: cachedGameIds.Count);
                }

                var gameData = dataService?.GetRawGameAchievementData(gameId);
                var achievements = gameData?.Achievements;
                if (achievements == null)
                {
                    continue;
                }

                foreach (var achievement in achievements)
                {
                    var lockedPath = achievement?.LockedIconPath;
                    if (!DiskImageService.IsLocalIconPath(lockedPath))
                    {
                        continue;
                    }

                    var unlockedPath = achievement?.UnlockedIconPath;
                    if (!string.IsNullOrWhiteSpace(unlockedPath) &&
                        string.Equals(lockedPath.Trim(), unlockedPath.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    lockedPaths.Add(lockedPath);
                }
            }

            return lockedPaths;
        }

        private void ResetFirstTimeSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger?.Info($"Resetting FirstTimeSetupCompleted. Current value before: {_settings.Persisted.FirstTimeSetupCompleted}");

                _settings.Persisted.FirstTimeSetupCompleted = false;

                _logger?.Info($"Value after setting to false: {_settings.Persisted.FirstTimeSetupCompleted}");

                _plugin.SavePluginSettings(_settings);

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Status_Succeeded", "Success!"),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to reset first-time setup.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportDatabase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exportBaseDir = _plugin.GetPluginUserDataPath();
                var exportDir = _plugin.RefreshRuntime.Cache.ExportDatabaseToCsv(exportBaseDir);

                _logger?.Info($"Database exported to: {exportDir}");

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Status_Succeeded", "Success!") + "\n" + exportDir,
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to export database.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataPath = _plugin.GetPluginUserDataPath();

                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = dataPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to open extension data folder.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string LF(string key, string fallbackFormat, params object[] args)
        {
            return string.Format(L(key, fallbackFormat), args);
        }
    }
}

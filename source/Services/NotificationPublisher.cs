using System;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Services
{
    public class NotificationPublisher
    {
        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;

        public NotificationPublisher(IPlayniteAPI api, PlayniteAchievementsSettings settings, ILogger logger)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
        }

        public void ShowPeriodicStatus(string status)
        {
            if (_settings?.Persisted?.EnableNotifications != true || !_settings.Persisted.NotifyPeriodicUpdates)
                return;

            var title = ResourceProvider.GetString("LOCPlayAch_Title_PluginName");
            var text = string.IsNullOrWhiteSpace(status)
                ? ResourceProvider.GetString("LOCPlayAch_Rebuild_Completed")
                : status;

            try
            {
                _api.Notifications.Add(new NotificationMessage(
                    $"PlayniteAchievements-Periodic-{Guid.NewGuid()}",
                    $"{title}\n{text}",
                    NotificationType.Info));
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to show periodic notification.");
            }
        }

        public void ShowThemeAutoMigrated(string themeName)
        {
            if (_settings?.Persisted?.EnableNotifications != true)
            {
                return;
            }

            var title = ResourceProvider.GetString("LOCPlayAch_ThemeMigration_AutoMigratedTitle");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Theme Auto-Migrated";
            }

            var displayName = string.IsNullOrWhiteSpace(themeName) ? "Theme" : themeName;

            var message = string.Format(
                ResourceProvider.GetString("LOCPlayAch_ThemeMigration_AutoMigratedMessage"),
                displayName);

            var restart = ResourceProvider.GetString("LOCPlayAch_ThemeMigration_AutoMigratedRestart");

            var text = $"{message}\n{restart}";

            try
            {
                _api.Notifications.Add(new NotificationMessage(
                    $"PlayniteAchievements-ThemeAutoMigrated-{Guid.NewGuid()}",
                    $"{title}\n{text}",
                    NotificationType.Info));
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to show theme auto-migrated notification.");
            }
        }
    }
}

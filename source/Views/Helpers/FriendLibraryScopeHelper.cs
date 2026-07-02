using System;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Shared logic for the per-friend library scope toggle (Full vs Shared), reused by the friend
    /// summaries grid context menu and central Friends settings tab. Individual choices live in
    /// central friend settings; absence means the shared (default) scope.
    /// </summary>
    internal static class FriendLibraryScopeHelper
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(FriendLibraryScopeHelper));

        public static bool CanConfigureFriend(string providerKey, string externalUserId)
        {
            return !string.IsNullOrWhiteSpace(providerKey) &&
                   !string.IsNullOrWhiteSpace(externalUserId);
        }

        /// <summary>
        /// Returns true when the friend currently uses the full library scope.
        /// </summary>
        public static bool IsFullLibrary(string providerKey, string externalUserId)
        {
            if (!CanConfigureFriend(providerKey, externalUserId))
            {
                return false;
            }

            return PlayniteAchievementsPlugin.Instance?.Settings?.Persisted
                ?.GetFriendSetting(providerKey, externalUserId)
                ?.LibraryScope == FriendLibraryScope.Full;
        }

        /// <summary>
        /// Sets the friend's library scope. Enabling the full scope shows the one-time warning the first
        /// time it is enabled for any friend; if the user declines, the friend stays on the shared scope.
        /// Returns the resulting state (true = full library).
        /// </summary>
        public static bool SetFullLibrary(
            string providerKey,
            string externalUserId,
            string displayName,
            string avatarUrl,
            bool enable)
        {
            if (!CanConfigureFriend(providerKey, externalUserId))
            {
                return false;
            }

            if (enable && !ConfirmFullLibraryEnable())
            {
                return false;
            }

            var plugin = PlayniteAchievementsPlugin.Instance;
            var persisted = plugin?.Settings?.Persisted;
            if (plugin == null || persisted == null)
            {
                return false;
            }

            var entry = persisted.AddOrUpdateFriend(
                providerKey,
                externalUserId,
                displayName,
                avatarUrl,
                null,
                FriendSettingsSource.AutoDiscovered,
                enable ? FriendLibraryScope.Full : FriendLibraryScope.Shared);
            if (entry == null)
            {
                return false;
            }

            FriendSettingsSyncService.SyncConfiguredFriendsToCache(
                persisted,
                plugin.RefreshRuntime?.Cache as IFriendCacheManager,
                Logger,
                providerKey);
            plugin.PersistSettingsForUi();
            plugin.ThemeIntegrationService?.RequestUpdate(null, forceRefresh: true);
            return enable;
        }

        /// <summary>
        /// Shows the one-time warning the first time the full library scope is enabled for any friend
        /// and records acceptance. Returns true if the caller may proceed with enabling. Exposed so
        /// callers that mutate a settings edit session directly (e.g. the provider settings view) can
        /// reuse the same gate without going through <see cref="SetFullLibrary"/>.
        /// </summary>
        public static bool ConfirmFullLibraryEnable()
        {
            var persisted = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted;
            if (persisted == null || persisted.FullLibraryWarningAccepted)
            {
                return true;
            }

            var api = PlayniteAchievementsPlugin.Instance?.PlayniteApi;
            var message = ResourceProvider.GetString("LOCPlayAch_FriendLibrary_FullWarning") ??
                "Using the full library discovers this friend's games outside your Playnite library and may cache a large amount of provider-only achievement data.\n\nEnable this?";
            var title = ResourceProvider.GetString("LOCPlayAch_Title_PluginName") ?? "Playnite Achievements";

            var result = api?.Dialogs?.ShowMessage(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                ?? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            persisted.FullLibraryWarningAccepted = true;
            PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
            return true;
        }
    }
}

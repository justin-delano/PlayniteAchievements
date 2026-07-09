using System;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Steam;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Shared logic for the per-friend library scope toggle (Full vs Shared), reused by the friend
    /// summaries grid context menu and the Steam provider settings friends table. The opt-in set lives
    /// in <see cref="SteamSettings.FullLibraryFriends"/>; absence means the shared (default) scope.
    /// </summary>
    internal static class FriendLibraryScopeHelper
    {
        public static bool IsSteamFriend(string providerKey)
        {
            return string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when the friend currently uses the full library scope.
        /// </summary>
        public static bool IsFullLibrary(string providerKey, string externalUserId)
        {
            if (!IsSteamFriend(providerKey) || string.IsNullOrWhiteSpace(externalUserId))
            {
                return false;
            }

            return ProviderRegistry.Settings<SteamSettings>()?.IsFullLibraryFriend(externalUserId) == true;
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
            if (!IsSteamFriend(providerKey) || string.IsNullOrWhiteSpace(externalUserId))
            {
                return false;
            }

            if (enable && !ConfirmFullLibraryEnable())
            {
                return false;
            }

            var settings = ProviderRegistry.Settings<SteamSettings>();
            if (settings == null)
            {
                return false;
            }

            settings.SetFullLibraryFriend(externalUserId, displayName, avatarUrl, enable);
            ProviderRegistry.Write(settings, persistToDisk: true);
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

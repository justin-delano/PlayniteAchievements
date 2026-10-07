using System;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>Where a link target is configured.</summary>
    public enum LibraryTargetDestination
    {
        None,
        Settings,
        ManageAchievements,
        Showcase,

        /// <summary>A game's Manage Achievements on the Overview tab, where its Workshop game data is managed.</summary>
        GameData
    }

    /// <summary>
    /// The place a link target is configured, read from its key: a settings page (colors,
    /// sounds, the global and per-platform notification and frame scopes), a game's Manage
    /// Achievements Notifications tab (a game's notification and frame scope), a showcase page,
    /// or a game's Manage Achievements Overview tab (a game's Workshop game data).
    /// </summary>
    public sealed class LibraryTargetNavigation
    {
        private LibraryTargetNavigation(LibraryTargetDestination destination)
        {
            Destination = destination;
        }

        public static readonly LibraryTargetNavigation None = new LibraryTargetNavigation(LibraryTargetDestination.None);

        public LibraryTargetDestination Destination { get; }

        /// <summary>The settings place, for <see cref="LibraryTargetDestination.Settings"/>.</summary>
        public SettingsNavigationRequest Settings { get; private set; }

        /// <summary>The game, for <see cref="LibraryTargetDestination.ManageAchievements"/> and <see cref="LibraryTargetDestination.GameData"/>.</summary>
        public Guid GameId { get; private set; }

        /// <summary>The surface of a game's scope, for <see cref="LibraryTargetDestination.ManageAchievements"/>.</summary>
        public NotificationSurface Surface { get; private set; }

        /// <summary>The page, for <see cref="LibraryTargetDestination.Showcase"/>.</summary>
        public string ShowcasePageId { get; private set; }

        public static LibraryTargetNavigation Resolve(string targetKey)
        {
            if (LibraryTargetKeys.IsGameData(targetKey) && LibraryTargetKeys.TryGetGameId(targetKey, out var gameDataGameId))
            {
                return new LibraryTargetNavigation(LibraryTargetDestination.GameData) { GameId = gameDataGameId };
            }

            if (string.Equals(targetKey, LibraryTargetKeys.Colors, StringComparison.OrdinalIgnoreCase))
            {
                return ForSettings(new SettingsNavigationRequest(SettingsNavigationRequest.DisplayTab, SettingsNavigationRequest.ColorsPage));
            }

            if (LibraryTargetKeys.TryParseSoundsScope(targetKey, out var soundsProviderKey, out var soundsGameId))
            {
                return ForStyles(NotificationSurface.Sounds, soundsProviderKey, soundsGameId);
            }

            if (LibraryTargetKeys.TryParseNotificationScope(targetKey, out var isFrame, out var providerKey, out var gameId))
            {
                return ForStyles(isFrame ? NotificationSurface.Frame : NotificationSurface.Toast, providerKey, gameId);
            }

            if (LibraryTargetKeys.TryGetShowcasePageId(targetKey, out var pageId))
            {
                return new LibraryTargetNavigation(LibraryTargetDestination.Showcase) { ShowcasePageId = pageId };
            }

            return None;
        }

        /// <summary>A surface of a scope: a game's in its Manage Achievements, else the Styles page on the platform.</summary>
        private static LibraryTargetNavigation ForStyles(NotificationSurface surface, string providerKey, Guid gameId)
        {
            if (gameId != Guid.Empty)
            {
                return new LibraryTargetNavigation(LibraryTargetDestination.ManageAchievements)
                {
                    GameId = gameId,
                    Surface = surface
                };
            }

            return ForSettings(new SettingsNavigationRequest(SettingsNavigationRequest.NotificationsTab, SettingsNavigationRequest.AppearancePage)
            {
                ProviderKey = providerKey,
                Surface = surface
            });
        }

        private static LibraryTargetNavigation ForSettings(SettingsNavigationRequest request)
        {
            return new LibraryTargetNavigation(LibraryTargetDestination.Settings) { Settings = request };
        }
    }
}

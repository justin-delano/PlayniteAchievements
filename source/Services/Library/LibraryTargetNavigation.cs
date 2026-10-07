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
        Showcase
    }

    /// <summary>
    /// The place a link target is configured, read from its key: a settings page (colors,
    /// sounds, the global and per-platform notification and frame scopes), a game's Manage
    /// Achievements Notifications tab (a game's notification and frame scope), or a showcase page.
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

        /// <summary>The game, for <see cref="LibraryTargetDestination.ManageAchievements"/>.</summary>
        public Guid GameId { get; private set; }

        /// <summary>The surface of a game's scope, for <see cref="LibraryTargetDestination.ManageAchievements"/>.</summary>
        public bool IsFrame { get; private set; }

        /// <summary>The page, for <see cref="LibraryTargetDestination.Showcase"/>.</summary>
        public string ShowcasePageId { get; private set; }

        public static LibraryTargetNavigation Resolve(string targetKey)
        {
            if (string.Equals(targetKey, LibraryTargetKeys.Colors, StringComparison.OrdinalIgnoreCase))
            {
                return ForSettings(new SettingsNavigationRequest(SettingsNavigationRequest.DisplayTab, SettingsNavigationRequest.ColorsPage));
            }

            if (string.Equals(targetKey, LibraryTargetKeys.Sounds, StringComparison.OrdinalIgnoreCase))
            {
                return ForSettings(new SettingsNavigationRequest(SettingsNavigationRequest.NotificationsTab, SettingsNavigationRequest.BehaviorPage)
                {
                    ShowSounds = true
                });
            }

            if (LibraryTargetKeys.TryParseNotificationScope(targetKey, out var isFrame, out var providerKey, out var gameId))
            {
                if (gameId != Guid.Empty)
                {
                    return new LibraryTargetNavigation(LibraryTargetDestination.ManageAchievements)
                    {
                        GameId = gameId,
                        IsFrame = isFrame
                    };
                }

                return ForSettings(new SettingsNavigationRequest(SettingsNavigationRequest.NotificationsTab, SettingsNavigationRequest.AppearancePage)
                {
                    ProviderKey = providerKey,
                    IsFrame = isFrame
                });
            }

            if (LibraryTargetKeys.TryGetShowcasePageId(targetKey, out var pageId))
            {
                return new LibraryTargetNavigation(LibraryTargetDestination.Showcase) { ShowcasePageId = pageId };
            }

            return None;
        }

        private static LibraryTargetNavigation ForSettings(SettingsNavigationRequest request)
        {
            return new LibraryTargetNavigation(LibraryTargetDestination.Settings) { Settings = request };
        }
    }
}

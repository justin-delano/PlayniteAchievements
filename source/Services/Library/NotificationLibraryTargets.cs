using System;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Notifications;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The notification and frame scopes as library targets: the global and platform scopes live
    /// in the settings, a game's scope in its custom data.
    /// </summary>
    public sealed class NotificationLibraryTargets : ILibraryTargetResolver
    {
        private readonly NotificationStyleLibraryAdapter _toast;
        private readonly NotificationStyleLibraryAdapter _frame;
        private readonly Func<GameCustomDataStore> _customData;
        private readonly Func<PersistedSettings> _live;
        private readonly Func<Guid, string> _gameProviderKey;

        /// <param name="toast">The notification surface adapter.</param>
        /// <param name="frame">The screenshot frame surface adapter.</param>
        /// <param name="customData">The per-game custom data, where a game's own style lives.</param>
        /// <param name="live">The live settings a game without its own style inherits from.</param>
        /// <param name="gameProviderKey">The platform of a game, whose style it inherits; may be null.</param>
        public NotificationLibraryTargets(
            NotificationStyleLibraryAdapter toast,
            NotificationStyleLibraryAdapter frame,
            Func<GameCustomDataStore> customData,
            Func<PersistedSettings> live,
            Func<Guid, string> gameProviderKey = null)
        {
            _toast = toast ?? throw new ArgumentNullException(nameof(toast));
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _customData = customData ?? throw new ArgumentNullException(nameof(customData));
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _gameProviderKey = gameProviderKey;
        }

        public NotificationStyleLibraryAdapter AdapterFor(bool isFrame) => isFrame ? _frame : _toast;

        /// <summary>A game's scope, or null when the custom data is not available.</summary>
        public NotificationStyleScope GameScope(Guid gameId)
        {
            var store = _customData();
            return store == null || gameId == Guid.Empty
                ? null
                : NotificationStyleScope.ForGame(store, gameId, _live, () => _gameProviderKey?.Invoke(gameId));
        }

        public ISettingsLibraryAdapter SettingsAdapter(string targetKey)
        {
            if (!LibraryTargetKeys.TryParseNotificationScope(targetKey, out var isFrame, out var providerKey, out var gameId)
                || gameId != Guid.Empty)
            {
                return null;
            }

            return new SettingsLibraryAdapter<NotificationStyleScope>(
                LibraryTargetKeys.NotificationScope(isFrame, providerKey, Guid.Empty),
                AdapterFor(isFrame),
                settings => NotificationStyleScope.ForSettings(settings, providerKey));
        }

        public ILibraryGameTarget GameTarget(string targetKey)
        {
            if (!LibraryTargetKeys.TryParseNotificationScope(targetKey, out var isFrame, out _, out var gameId)
                || gameId == Guid.Empty)
            {
                return null;
            }

            var scope = GameScope(gameId);
            return scope == null ? null : new LibraryGameTarget<NotificationStyleScope>(AdapterFor(isFrame), scope);
        }
    }
}

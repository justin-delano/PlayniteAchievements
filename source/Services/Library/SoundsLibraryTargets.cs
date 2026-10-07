using System;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Sound;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The platform and game sound scopes as library targets: a platform's pack lives in the
    /// settings, a game's in its custom data. The global pack is the settings adapter's own
    /// <see cref="LibraryTargetKeys.Sounds"/> target.
    /// </summary>
    public sealed class SoundsLibraryTargets : ILibraryTargetResolver
    {
        private readonly SoundsLibraryAdapter _adapter;
        private readonly Func<GameCustomDataStore> _customData;
        private readonly Func<PersistedSettings> _live;
        private readonly Func<Guid, string> _gameProviderKey;

        /// <param name="adapter">The sound pack adapter.</param>
        /// <param name="customData">The per-game custom data, where a game's own pack lives.</param>
        /// <param name="live">The live settings a game without its own pack inherits from.</param>
        /// <param name="gameProviderKey">The platform of a game, whose pack it inherits; may be null.</param>
        public SoundsLibraryTargets(
            SoundsLibraryAdapter adapter,
            Func<GameCustomDataStore> customData,
            Func<PersistedSettings> live,
            Func<Guid, string> gameProviderKey = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _customData = customData ?? throw new ArgumentNullException(nameof(customData));
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _gameProviderKey = gameProviderKey;
        }

        public SoundsLibraryAdapter Adapter => _adapter;

        /// <summary>A game's scope, or null when the custom data is not available.</summary>
        public UnlockSoundScope GameScope(Guid gameId)
        {
            var store = _customData();
            return store == null || gameId == Guid.Empty
                ? null
                : UnlockSoundScope.ForGame(store, gameId, _live, () => _gameProviderKey?.Invoke(gameId));
        }

        public ISettingsLibraryAdapter SettingsAdapter(string targetKey)
        {
            if (!LibraryTargetKeys.TryParseSoundsScope(targetKey, out var providerKey, out var gameId)
                || gameId != Guid.Empty)
            {
                return null;
            }

            return providerKey == null
                ? (ISettingsLibraryAdapter)_adapter
                : new SettingsLibraryAdapter<UnlockSoundScope>(
                    LibraryTargetKeys.SoundsProvider(providerKey),
                    _adapter,
                    settings => UnlockSoundScope.ForSettings(settings, providerKey));
        }

        public ILibraryGameTarget GameTarget(string targetKey)
        {
            if (!LibraryTargetKeys.TryParseSoundsScope(targetKey, out _, out var gameId)
                || gameId == Guid.Empty)
            {
                return null;
            }

            var scope = GameScope(gameId);
            return scope == null ? null : new LibraryGameTarget<UnlockSoundScope>(_adapter, scope);
        }
    }
}

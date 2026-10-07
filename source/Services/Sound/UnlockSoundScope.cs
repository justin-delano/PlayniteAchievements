using System;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;

namespace PlayniteAchievements.Services.Sound
{
    /// <summary>
    /// One unlock sound scope: the global pack, a platform's own pack, or a game's own pack. A
    /// platform or game without a pack of its own plays the pack it inherits; writing one gives it
    /// its own. Layering is whole-pack: an owned pack's blank tier falls to the theme and bundled
    /// sounds, never to the parent scope's file.
    /// </summary>
    public abstract class UnlockSoundScope
    {
        protected UnlockSoundScope(string providerKey, Guid gameId)
        {
            ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim();
            GameId = gameId;
        }

        /// <summary>The platform of a platform scope; null for the global and game scopes.</summary>
        public string ProviderKey { get; }

        /// <summary>The game of a game scope; empty otherwise.</summary>
        public Guid GameId { get; }

        public bool IsGame => GameId != Guid.Empty;

        /// <summary>The scope's own pack, or null when it follows an inherited one.</summary>
        public abstract UnlockSoundSettings OwnSounds { get; }

        /// <summary>The pack the scope plays: its own, or the one it inherits.</summary>
        public abstract UnlockSoundSettings EffectiveSounds { get; }

        /// <summary>Stores <paramref name="sounds"/> as the scope's own pack; null makes it follow again.</summary>
        public abstract void Write(UnlockSoundSettings sounds);

        /// <summary>The global scope (null <paramref name="providerKey"/>) or a platform's scope in <paramref name="settings"/>.</summary>
        public static UnlockSoundScope ForSettings(PersistedSettings settings, string providerKey)
        {
            return new SettingsScope(settings ?? throw new ArgumentNullException(nameof(settings)), providerKey);
        }

        /// <summary>A game's scope, stored in its custom data.</summary>
        /// <param name="settings">The settings a game without its own pack inherits from.</param>
        /// <param name="providerKey">The game's platform, whose pack it inherits when it has one.</param>
        public static UnlockSoundScope ForGame(
            GameCustomDataStore store,
            Guid gameId,
            Func<PersistedSettings> settings,
            Func<string> providerKey)
        {
            if (gameId == Guid.Empty)
            {
                throw new ArgumentException("A game id is required.", nameof(gameId));
            }

            return new GameScope(
                store ?? throw new ArgumentNullException(nameof(store)),
                gameId,
                settings ?? throw new ArgumentNullException(nameof(settings)),
                providerKey);
        }

        /// <summary>
        /// The pack an unlock plays: the game's own, else its platform's, else the global pack.
        /// A missing store or game id skips the game step.
        /// </summary>
        public static UnlockSoundSettings Resolve(
            PersistedSettings settings,
            GameCustomDataStore store,
            string providerKey,
            Guid gameId)
        {
            if (store != null && gameId != Guid.Empty &&
                store.TryLoad(gameId, out var data) && data?.UnlockSounds != null)
            {
                return data.UnlockSounds;
            }

            return ResolveSettings(settings, providerKey);
        }

        private static UnlockSoundSettings ResolveSettings(PersistedSettings settings, string providerKey)
        {
            if (settings == null)
            {
                return null;
            }

            return (string.IsNullOrWhiteSpace(providerKey) ? null : settings.GetProviderUnlockSounds(providerKey))
                   ?? settings.UnlockSounds;
        }

        private sealed class SettingsScope : UnlockSoundScope
        {
            private readonly PersistedSettings _settings;

            public SettingsScope(PersistedSettings settings, string providerKey)
                : base(providerKey, Guid.Empty)
            {
                _settings = settings;
            }

            public override UnlockSoundSettings OwnSounds =>
                ProviderKey == null ? _settings.UnlockSounds : _settings.GetProviderUnlockSounds(ProviderKey);

            public override UnlockSoundSettings EffectiveSounds => OwnSounds ?? _settings.UnlockSounds;

            public override void Write(UnlockSoundSettings sounds)
            {
                if (ProviderKey == null)
                {
                    _settings.UnlockSounds = sounds;
                }
                else
                {
                    _settings.SetProviderUnlockSounds(ProviderKey, sounds);
                }
            }
        }

        private sealed class GameScope : UnlockSoundScope
        {
            private readonly GameCustomDataStore _store;
            private readonly Func<PersistedSettings> _settings;
            private readonly Func<string> _providerKey;

            public GameScope(GameCustomDataStore store, Guid gameId, Func<PersistedSettings> settings, Func<string> providerKey)
                : base(null, gameId)
            {
                _store = store;
                _settings = settings;
                _providerKey = providerKey;
            }

            public override UnlockSoundSettings OwnSounds =>
                _store.TryLoad(GameId, out var data) ? data?.UnlockSounds : null;

            public override UnlockSoundSettings EffectiveSounds =>
                OwnSounds ?? ResolveSettings(_settings(), _providerKey?.Invoke()) ?? UnlockSoundSettings.CreateDefault();

            public override void Write(UnlockSoundSettings sounds)
            {
                var copy = sounds?.Clone();
                _store.Update(GameId, data => data.UnlockSounds = copy);
            }
        }
    }
}

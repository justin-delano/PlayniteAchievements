using System;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Services.Notifications
{
    /// <summary>
    /// One notification appearance scope: the global style, a platform's own style, or a game's
    /// own style. A platform or game that has no style of its own shows the style it inherits;
    /// writing one gives it its own. Image slots and custom templates are kept per scope.
    /// </summary>
    public abstract class NotificationStyleScope
    {
        protected NotificationStyleScope(string providerKey, Guid gameId)
        {
            ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim();
            GameId = gameId;
        }

        /// <summary>The platform of a platform scope; null for the global and game scopes.</summary>
        public string ProviderKey { get; }

        /// <summary>The game of a game scope; empty otherwise.</summary>
        public Guid GameId { get; }

        public bool IsGame => GameId != Guid.Empty;

        /// <summary>The owner of the scope's managed image slots.</summary>
        public NotificationImageOwner ImageOwner =>
            IsGame ? NotificationImageOwner.ForGame(GameId) : NotificationImageOwner.ForProvider(ProviderKey);

        /// <summary>The platform key the scope's custom templates live under (null for global and game scopes).</summary>
        public string TemplateProviderKey => IsGame ? null : ProviderKey;

        /// <summary>The scope's own style, or null when it follows an inherited one.</summary>
        public abstract NotificationStyleSettings OwnStyle { get; }

        /// <summary>The style the scope shows: its own, or the one it inherits.</summary>
        public abstract NotificationStyleSettings EffectiveStyle { get; }

        /// <summary>Stores <paramref name="style"/> as the scope's own style.</summary>
        public abstract void Write(NotificationStyleSettings style);

        /// <summary>The global scope (null <paramref name="providerKey"/>) or a platform's scope in <paramref name="settings"/>.</summary>
        public static NotificationStyleScope ForSettings(PersistedSettings settings, string providerKey)
        {
            return new SettingsScope(settings ?? throw new ArgumentNullException(nameof(settings)), providerKey);
        }

        /// <summary>A game's scope, stored in its custom data.</summary>
        /// <param name="settings">The settings a game without its own style inherits from.</param>
        /// <param name="providerKey">The game's platform, whose style it inherits when it has one.</param>
        public static NotificationStyleScope ForGame(
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

        private sealed class SettingsScope : NotificationStyleScope
        {
            private readonly PersistedSettings _settings;

            public SettingsScope(PersistedSettings settings, string providerKey)
                : base(providerKey, Guid.Empty)
            {
                _settings = settings;
            }

            public override NotificationStyleSettings OwnStyle =>
                ProviderKey == null ? _settings.NotificationStyle : _settings.GetProviderNotificationStyle(ProviderKey);

            public override NotificationStyleSettings EffectiveStyle => OwnStyle ?? _settings.NotificationStyle;

            public override void Write(NotificationStyleSettings style)
            {
                if (ProviderKey == null)
                {
                    _settings.NotificationStyle = style;
                }
                else
                {
                    _settings.SetProviderNotificationStyle(ProviderKey, style);
                }
            }
        }

        private sealed class GameScope : NotificationStyleScope
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

            public override NotificationStyleSettings OwnStyle =>
                _store.TryLoad(GameId, out var data) ? data?.NotificationAppearanceOverride?.Style : null;

            public override NotificationStyleSettings EffectiveStyle =>
                OwnStyle ?? NotificationStyleResolver.Resolve(_settings(), _providerKey?.Invoke()) ?? NotificationStyleSettings.CreateDefault();

            /// <summary>Gives the game its own style; a game styled for the first time takes the current theme design choices.</summary>
            public override void Write(NotificationStyleSettings style)
            {
                var settings = _settings();
                _store.Update(GameId, data =>
                {
                    var existing = data.NotificationAppearanceOverride;
                    data.NotificationAppearanceOverride = new GameNotificationAppearanceOverride
                    {
                        Style = style,
                        ToastUseThemeStyling = existing?.ToastUseThemeStyling ?? settings?.ToastUseThemeStyling ?? true,
                        FrameUseThemeStyling = existing?.FrameUseThemeStyling ?? settings?.FrameUseThemeStyling ?? true
                    };
                });
            }
        }
    }
}

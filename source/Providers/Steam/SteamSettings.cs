using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Steam
{
    /// <summary>
    /// Steam-specific provider settings.
    /// </summary>
    public class SteamSettings : ProviderSettingsBase
    {
        private string _steamUserId;
        private string _steamApiKey;

        /// <inheritdoc />
        public override string ProviderKey => "Steam";

        /// <summary>
        /// Gets or sets the last successfully probed Steam user ID.
        /// This is derived auth state, not user-editable configuration.
        /// </summary>
        public string SteamUserId
        {
            get => _steamUserId;
            set => SetValue(ref _steamUserId, value);
        }

        /// <summary>
        /// Gets or sets the Steam Web API key.
        /// </summary>
        public string SteamApiKey
        {
            get => _steamApiKey;
            set => SetValue(ref _steamApiKey, value);
        }
    }
}

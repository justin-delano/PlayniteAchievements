using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    /// <summary>
    /// RetroAchievements provider settings.
    /// </summary>
    public class RetroAchievementsSettings : ProviderSettingsBase
    {
        private string _raUsername;
        private string _raWebApiKey;

        /// <inheritdoc />
        public override string ProviderKey => "RetroAchievements";

        /// <summary>
        /// Gets or sets the RetroAchievements username.
        /// </summary>
        public string RaUsername
        {
            get => _raUsername;
            set => SetValue(ref _raUsername, value);
        }

        /// <summary>
        /// Gets or sets the RetroAchievements web API key.
        /// </summary>
        public string RaWebApiKey
        {
            get => _raWebApiKey;
            set => SetValue(ref _raWebApiKey, value);
        }

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new RetroAchievementsSettings
            {
                IsEnabled = IsEnabled,
                RaUsername = RaUsername,
                RaWebApiKey = RaWebApiKey
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is RetroAchievementsSettings other)
            {
                IsEnabled = other.IsEnabled;
                RaUsername = other.RaUsername;
                RaWebApiKey = other.RaWebApiKey;
            }
        }
    }
}

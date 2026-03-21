using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Xbox
{
    /// <summary>
    /// Xbox provider settings. Authentication is handled via session manager.
    /// </summary>
    public class XboxSettings : ProviderSettingsBase
    {
        /// <inheritdoc />
        public override string ProviderKey => "Xbox";

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new XboxSettings
            {
                IsEnabled = IsEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is XboxSettings other)
            {
                IsEnabled = other.IsEnabled;
            }
        }
    }
}

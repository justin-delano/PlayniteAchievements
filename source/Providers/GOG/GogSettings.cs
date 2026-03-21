using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.GOG
{
    /// <summary>
    /// GOG provider settings. Authentication is handled via session manager.
    /// </summary>
    public class GogSettings : ProviderSettingsBase
    {
        /// <inheritdoc />
        public override string ProviderKey => "GOG";

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new GogSettings
            {
                IsEnabled = IsEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is GogSettings other)
            {
                IsEnabled = other.IsEnabled;
            }
        }
    }
}

using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.PSN
{
    /// <summary>
    /// PlayStation Network provider settings. Authentication is handled via session manager.
    /// </summary>
    public class PsnSettings : ProviderSettingsBase
    {
        /// <inheritdoc />
        public override string ProviderKey => "PSN";

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new PsnSettings
            {
                IsEnabled = IsEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is PsnSettings other)
            {
                IsEnabled = other.IsEnabled;
            }
        }
    }
}

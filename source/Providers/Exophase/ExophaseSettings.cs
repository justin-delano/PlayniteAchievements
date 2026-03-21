using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Exophase
{
    /// <summary>
    /// Exophase provider settings. Authentication is handled via session manager.
    /// </summary>
    public class ExophaseSettings : ProviderSettingsBase
    {
        /// <inheritdoc />
        public override string ProviderKey => "Exophase";

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new ExophaseSettings
            {
                IsEnabled = IsEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is ExophaseSettings other)
            {
                IsEnabled = other.IsEnabled;
            }
        }
    }
}

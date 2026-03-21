using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Epic
{
    /// <summary>
    /// Epic Games provider settings. Authentication is handled via session manager.
    /// </summary>
    public class EpicSettings : ProviderSettingsBase
    {
        /// <inheritdoc />
        public override string ProviderKey => "Epic";

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new EpicSettings
            {
                IsEnabled = IsEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is EpicSettings other)
            {
                IsEnabled = other.IsEnabled;
            }
        }
    }
}

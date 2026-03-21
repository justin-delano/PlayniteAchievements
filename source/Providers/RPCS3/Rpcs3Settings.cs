using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// RPCS3 emulator provider settings.
    /// </summary>
    public class Rpcs3Settings : ProviderSettingsBase
    {
        /// <inheritdoc />
        public override string ProviderKey => "RPCS3";

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new Rpcs3Settings
            {
                IsEnabled = IsEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is Rpcs3Settings other)
            {
                IsEnabled = other.IsEnabled;
            }
        }
    }
}

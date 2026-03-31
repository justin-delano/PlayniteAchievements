using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Local
{
    public class LocalSettings : ProviderSettingsBase
    {
        public override string ProviderKey => "Local";

        public string ExtraLocalPaths { get; set; } = string.Empty;

        public LocalSettings()
        {
            IsEnabled = true;
        }
    }
}

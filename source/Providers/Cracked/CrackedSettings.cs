using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Cracked
{
    public class CrackedSettings : ProviderSettingsBase
    {
        public override string ProviderKey => "Cracked";

        public string ExtraCrackedPaths { get; set; } = string.Empty;

        public CrackedSettings()
        {
            IsEnabled = true;
        }
    }
}

using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Hoyoverse
{
    public sealed class HoyoverseSettings : ProviderSettingsBase
    {
        private bool _enableGenshinImpact = true;
        private bool _enableHonkaiStarRail = true;
        private bool _enableZenlessZoneZero = true;
        private string _genshinExportPath;
        private string _honkaiStarRailExportPath;
        private string _zenlessZoneZeroExportPath;

        public override string ProviderKey => HoyoverseConstants.ProviderKey;

        public bool EnableGenshinImpact
        {
            get => _enableGenshinImpact;
            set => SetValue(ref _enableGenshinImpact, value);
        }

        public bool EnableHonkaiStarRail
        {
            get => _enableHonkaiStarRail;
            set => SetValue(ref _enableHonkaiStarRail, value);
        }

        public bool EnableZenlessZoneZero
        {
            get => _enableZenlessZoneZero;
            set => SetValue(ref _enableZenlessZoneZero, value);
        }

        public string GenshinExportPath
        {
            get => _genshinExportPath;
            set => SetValue(ref _genshinExportPath, value);
        }

        public string HonkaiStarRailExportPath
        {
            get => _honkaiStarRailExportPath;
            set => SetValue(ref _honkaiStarRailExportPath, value);
        }

        public string ZenlessZoneZeroExportPath
        {
            get => _zenlessZoneZeroExportPath;
            set => SetValue(ref _zenlessZoneZeroExportPath, value);
        }
    }
}

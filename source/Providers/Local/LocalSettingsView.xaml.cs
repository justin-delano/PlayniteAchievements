using System;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Local
{
    public partial class LocalSettingsView : ProviderSettingsViewBase
    {
        private LocalSettings _localSettings;

        public new LocalSettings Settings => _localSettings;

        public LocalSettingsView()
        {
            InitializeComponent();
        }

        public override void Initialize(IProviderSettings settings)
        {
            _localSettings = settings as LocalSettings;
            base.Initialize(settings);
        }
    }
}

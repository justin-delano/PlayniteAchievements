using System;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Cracked
{
    public partial class CrackedSettingsView : ProviderSettingsViewBase
    {
        private CrackedSettings _crackedSettings;

        public new CrackedSettings Settings => _crackedSettings;

        public CrackedSettingsView()
        {
            InitializeComponent();
        }

        public override void Initialize(IProviderSettings settings)
        {
            _crackedSettings = settings as CrackedSettings;
            base.Initialize(settings);
        }
    }
}

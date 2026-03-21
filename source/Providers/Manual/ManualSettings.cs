using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Manual
{
    /// <summary>
    /// Manual achievement tracking provider settings.
    /// </summary>
    public class ManualSettings : ProviderSettingsBase
    {
        private bool _manualTrackingOverrideEnabled;

        /// <inheritdoc />
        public override string ProviderKey => "Manual";

        /// <summary>
        /// Gets or sets whether manual tracking override is enabled.
        /// </summary>
        public bool ManualTrackingOverrideEnabled
        {
            get => _manualTrackingOverrideEnabled;
            set => SetValue(ref _manualTrackingOverrideEnabled, value);
        }

        /// <inheritdoc />
        public override IProviderSettings Clone()
        {
            return new ManualSettings
            {
                IsEnabled = IsEnabled,
                ManualTrackingOverrideEnabled = ManualTrackingOverrideEnabled
            };
        }

        /// <inheritdoc />
        public override void CopyFrom(IProviderSettings source)
        {
            if (source is ManualSettings other)
            {
                IsEnabled = other.IsEnabled;
                ManualTrackingOverrideEnabled = other.ManualTrackingOverrideEnabled;
            }
        }
    }
}

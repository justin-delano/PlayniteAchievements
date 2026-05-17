using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Providers.BattleNet.Models;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Providers.BattleNet
{
    public partial class BattleNetSettingsView : ProviderSettingsViewBase
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(BattleNetSettingsView));
        private readonly BattleNetApiClient _apiClient;
        private readonly ILogger _logger;
        private BattleNetSettings _battleNetSettings;

        public new BattleNetSettings Settings => _battleNetSettings;

        public BattleNetSettingsView(BattleNetApiClient apiClient, ILogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? Logger;
            InitializeComponent();
            _logger.Debug("[BattleNet/Settings] Settings view constructed.");
        }

        public override void Initialize(IProviderSettings settings)
        {
            if (_battleNetSettings != null)
            {
                _battleNetSettings.PropertyChanged -= BattleNetSettings_PropertyChanged;
            }

            _battleNetSettings = settings as BattleNetSettings;
            if (_battleNetSettings == null)
            {
                _logger.Warn($"[BattleNet/Settings] Initialized with incompatible settings object: {settings?.GetType().FullName ?? "<null>"}");
            }
            else
            {
                _logger.Info($"[BattleNet/Settings] Initializing settings view. {SettingsSummary(_battleNetSettings)}");
            }

            base.Initialize(settings);
            if (_battleNetSettings != null)
            {
                _battleNetSettings.PropertyChanged += BattleNetSettings_PropertyChanged;
                ClientSecretBox.Password = _battleNetSettings.BattleNetClientSecret ?? string.Empty;
            }

            LoadWowRegions();
            UpdateSc2Status();
        }

        private void BattleNetSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(BattleNetSettings.BattleNetClientId):
                case nameof(BattleNetSettings.BattleNetClientSecret):
                case nameof(BattleNetSettings.Sc2RegionId):
                case nameof(BattleNetSettings.Sc2RealmId):
                case nameof(BattleNetSettings.Sc2ProfileId):
                    UpdateSc2Status();
                    break;
            }
        }

        private void UpdateSc2Status()
        {
            if (_battleNetSettings == null)
            {
                return;
            }

            if (BattleNetGameSupport.HasConfiguredSc2(_battleNetSettings))
            {
                Sc2StatusText.Text =
                    ResourceProvider.GetString("LOCPlayAch_Settings_BattleNet_Status_Sc2Detected")
                    + $" (Region {_battleNetSettings.Sc2RegionId}, Realm {_battleNetSettings.Sc2RealmId})";
            }
            else
            {
                Sc2StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Settings_BattleNet_Status_Sc2Incomplete");
            }

            _logger.Debug($"[BattleNet/Settings] SC2 status text updated. configured={Bool(BattleNetGameSupport.HasConfiguredSc2(_battleNetSettings))}, region={_battleNetSettings.Sc2RegionId}, realm={_battleNetSettings.Sc2RealmId}, profileId={(_battleNetSettings.Sc2ProfileId > 0 ? MaskId(_battleNetSettings.Sc2ProfileId.ToString()) : "<none>")}, apiCredentials={Bool(BattleNetGameSupport.HasApiCredentials(_battleNetSettings))}");
        }

        private void ClientSecret_Changed(object sender, RoutedEventArgs e)
        {
            if (_battleNetSettings == null)
            {
                return;
            }

            _battleNetSettings.BattleNetClientSecret = ClientSecretBox.Password;
        }

        private void LoadWowRegions()
        {
            _logger.Debug("[BattleNet/Settings] Loading WoW region choices.");
            WowRegionCombo.Items.Clear();
            WowRegionCombo.Items.Add("us");
            WowRegionCombo.Items.Add("eu");
            WowRegionCombo.Items.Add("kr");

            var settings = _battleNetSettings;
            if (!string.IsNullOrEmpty(settings?.WowRegion))
            {
                WowRegionCombo.SelectedItem = settings.WowRegion;
            }
            else
            {
                WowRegionCombo.SelectedIndex = 0;
            }

            _logger.Debug($"[BattleNet/Settings] WoW region selected. region={WowRegionCombo.SelectedItem ?? "<none>"}");
        }

        private async void WowRegion_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var region = WowRegionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(region))
            {
                return;
            }

            var settings = _battleNetSettings;
            if (settings == null)
            {
                return;
            }

            var regionChanged = !string.Equals(settings.WowRegion, region, StringComparison.OrdinalIgnoreCase);
            _logger.Info($"[BattleNet/Settings] WoW region changed. selected={region}, previous={settings.WowRegion ?? "<none>"}, changed={Bool(regionChanged)}");
            settings.WowRegion = region;
            if (regionChanged)
            {
                settings.WowRealmSlug = null;
                _logger.Debug("[BattleNet/Settings] Cleared saved WoW realm slug because region changed.");
            }

            try
            {
                _logger.Debug($"[BattleNet/Settings] Loading WoW realms. region={region}");
                var realms = await _apiClient.GetWowRealmsAsync(region, CancellationToken.None);
                WowRealmCombo.Items.Clear();
                foreach (var realm in realms)
                {
                    WowRealmCombo.Items.Add(realm);
                }
                _logger.Info($"[BattleNet/Settings] Loaded WoW realms. region={region}, count={realms.Count}");

                if (!string.IsNullOrEmpty(settings.WowRealmSlug))
                {
                    var selectedRealm = realms.Find(r => r.Slug == settings.WowRealmSlug);
                    WowRealmCombo.SelectedItem = selectedRealm;
                    _logger.Debug($"[BattleNet/Settings] Restored saved WoW realm selection. slug={settings.WowRealmSlug}, found={Bool(selectedRealm != null)}");
                    if (selectedRealm == null)
                    {
                        settings.WowRealmSlug = null;
                        _logger.Warn($"[BattleNet/Settings] Saved WoW realm slug was not present in loaded realm list for region '{region}'. Cleared stale slug.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"[BattleNet/Settings] Failed to load realms for region {region}.");
            }
        }

        private void WowRealm_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_battleNetSettings == null)
            {
                return;
            }

            if (WowRealmCombo.SelectedItem is WowRealm realm)
            {
                _battleNetSettings.WowRealmSlug = realm.Slug;
                _logger.Info($"[BattleNet/Settings] WoW realm selected. name={realm.Name ?? "<unnamed>"}, slug={realm.Slug ?? "<none>"}");
            }
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string MaskId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= 4)
            {
                return "****";
            }

            return $"{new string('*', Math.Min(8, trimmed.Length - 4))}{trimmed.Substring(trimmed.Length - 4)}";
        }

        private static string Presence(string value) => string.IsNullOrWhiteSpace(value) ? "missing" : "set";

        private static string SettingsSummary(BattleNetSettings settings)
        {
            if (settings == null)
            {
                return "<null settings>";
            }

            return string.Format(
                "enabled={0}, apiClientId={1}, apiClientSecret={2}, sc2Region={3}, sc2Realm={4}, sc2Profile={5}, wowRegion={6}, wowRealmSlug={7}, wowCharacter={8}",
                Bool(settings.IsEnabled),
                Presence(settings.BattleNetClientId),
                Presence(settings.BattleNetClientSecret),
                settings.Sc2RegionId,
                settings.Sc2RealmId,
                settings.Sc2ProfileId > 0 ? MaskId(settings.Sc2ProfileId.ToString()) : "<none>",
                string.IsNullOrWhiteSpace(settings.WowRegion) ? "<none>" : settings.WowRegion,
                string.IsNullOrWhiteSpace(settings.WowRealmSlug) ? "<none>" : settings.WowRealmSlug,
                Presence(settings.WowCharacter));
        }
    }
}

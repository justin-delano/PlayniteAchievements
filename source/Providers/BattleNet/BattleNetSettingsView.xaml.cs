using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Providers.BattleNet.Models;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Providers.BattleNet
{
    public partial class BattleNetSettingsView : ProviderSettingsViewBase, IAuthRefreshable
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(BattleNetSettingsView));
        private readonly BattleNetSessionManager _sessionManager;
        private readonly BattleNetApiClient _apiClient;
        private readonly ILogger _logger;
        private BattleNetSettings _battleNetSettings;

        #region DependencyProperties

        public static readonly DependencyProperty AuthBusyProperty =
            DependencyProperty.Register(nameof(AuthBusy), typeof(bool), typeof(BattleNetSettingsView), new PropertyMetadata(false));

        public bool AuthBusy
        {
            get => (bool)GetValue(AuthBusyProperty);
            set => SetValue(AuthBusyProperty, value);
        }

        public static readonly DependencyProperty IsAuthenticatedProperty =
            DependencyProperty.Register(nameof(IsAuthenticated), typeof(bool), typeof(BattleNetSettingsView), new PropertyMetadata(false));

        public bool IsAuthenticated
        {
            get => (bool)GetValue(IsAuthenticatedProperty);
            set => SetValue(IsAuthenticatedProperty, value);
        }

        public static readonly DependencyProperty AuthStatusProperty =
            DependencyProperty.Register(nameof(AuthStatus), typeof(string), typeof(BattleNetSettingsView), new PropertyMetadata(string.Empty));

        public string AuthStatus
        {
            get => (string)GetValue(AuthStatusProperty);
            set => SetValue(AuthStatusProperty, value);
        }

        #endregion

        public new BattleNetSettings Settings => _battleNetSettings;

        public BattleNetSettingsView(BattleNetSessionManager sessionManager, BattleNetApiClient apiClient, ILogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? Logger;
            InitializeComponent();

            ConnectionLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderConnection"),
                ResourceProvider.GetString("LOCPlayAch_Provider_BattleNet"));
            AuthLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderAuth"),
                ResourceProvider.GetString("LOCPlayAch_Provider_BattleNet"));
            _logger.Debug("[BattleNet/Settings] Settings view constructed.");
        }

        public override void Initialize(IProviderSettings settings)
        {
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
            LoadWowRegions();
            _ = RefreshAuthStatusAsync();
        }

        private void UpdateAuthStatus(AuthProbeResult result)
        {
            var isAuthenticated = result?.IsSuccess ?? false;
            IsAuthenticated = isAuthenticated;
            _logger.Debug($"[BattleNet/Settings] Auth status updated. authenticated={Bool(isAuthenticated)}, outcome={result?.Outcome}");

            if (isAuthenticated)
            {
                AuthStatus = ResourceProvider.GetString("LOCPlayAch_Auth_Authenticated");
            }
            else
            {
                AuthStatus = ResourceProvider.GetString("LOCPlayAch_Common_NotAuthenticated");
            }
        }

        public async Task RefreshAuthStatusAsync()
        {
            AuthProbeResult result;
            _logger.Debug("[BattleNet/Settings] Refreshing auth status.");
            try
            {
                result = await _sessionManager.ProbeAuthStateAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "[BattleNet/Settings] Auth probe failed during settings refresh.");
                result = AuthProbeResult.ProbeFailed();
            }

            UpdateAuthStatus(result);
            UpdateSc2Status();
            _logger.Debug($"[BattleNet/Settings] Auth status refresh complete. authenticated={Bool(IsAuthenticated)}, sc2Profile={_battleNetSettings?.Sc2ProfileId ?? 0}");
        }

        private async void LoginWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info("[BattleNet/Settings] Login requested from settings UI.");
                SetAuthBusy(true);
                var result = await _sessionManager.AuthenticateInteractiveAsync(forceInteractive: true, CancellationToken.None);
                if (result.IsSuccess)
                {
                    _logger.Info($"[BattleNet/Settings] Login succeeded. userId={MaskId(result.UserId)}");
                    await RefreshAuthStatusAsync();
                    PlayniteAchievementsPlugin.NotifySettingsSaved();
                }
                else
                {
                    _logger.Warn($"[BattleNet/Settings] Login did not complete successfully. outcome={result.Outcome}, windowOpened={Bool(result.WindowOpened)}");
                    UpdateAuthStatus(result);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[BattleNet/Settings] Web login failed.");
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info("[BattleNet/Settings] Logout requested from settings UI.");
                SetAuthBusy(true);
                _sessionManager.ClearSession();
                await RefreshAuthStatusAsync();
                PlayniteAchievementsPlugin.NotifySettingsSaved();
                _logger.Info("[BattleNet/Settings] Logout completed.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[BattleNet/Settings] Logout failed.");
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        private void UpdateSc2Status()
        {
            if (_battleNetSettings == null) return;

            if (IsAuthenticated && _battleNetSettings.Sc2ProfileId > 0)
            {
                Sc2StatusText.Text =
                    ResourceProvider.GetString("LOCPlayAch_Settings_BattleNet_Status_Sc2Detected")
                    + $" (Region {_battleNetSettings.Sc2RegionId})";
            }
            else if (IsAuthenticated)
            {
                Sc2StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Settings_BattleNet_Status_NoSc2Profile");
            }
            else
            {
                Sc2StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Common_NotAuthenticated");
            }

            _logger.Debug($"[BattleNet/Settings] SC2 status text updated. authenticated={Bool(IsAuthenticated)}, region={_battleNetSettings.Sc2RegionId}, profileId={_battleNetSettings.Sc2ProfileId}");
        }

        #region WoW Region/Realm Loading

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
            if (string.IsNullOrEmpty(region)) return;

            var settings = _battleNetSettings;
            if (settings == null) return;

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
            if (_battleNetSettings == null) return;
            if (WowRealmCombo.SelectedItem is WowRealm realm)
            {
                _battleNetSettings.WowRealmSlug = realm.Slug;
                _logger.Info($"[BattleNet/Settings] WoW realm selected. name={realm.Name ?? "<unnamed>"}, slug={realm.Slug ?? "<none>"}");
            }
        }

        #endregion

        private void SetAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess())
            {
                AuthBusy = busy;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => AuthBusy = busy));
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
                "enabled={0}, userId={1}, battleTag={2}, sc2Region={3}, sc2Profile={4}, wowRegion={5}, wowRealmSlug={6}, wowCharacter={7}",
                Bool(settings.IsEnabled),
                MaskId(settings.BattleNetUserId),
                Presence(settings.BattleTag),
                settings.Sc2RegionId,
                settings.Sc2ProfileId > 0 ? MaskId(settings.Sc2ProfileId.ToString()) : "<none>",
                string.IsNullOrWhiteSpace(settings.WowRegion) ? "<none>" : settings.WowRegion,
                string.IsNullOrWhiteSpace(settings.WowRealmSlug) ? "<none>" : settings.WowRealmSlug,
                Presence(settings.WowCharacter));
        }
    }
}

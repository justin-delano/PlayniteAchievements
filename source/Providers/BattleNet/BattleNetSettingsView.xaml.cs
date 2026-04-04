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
            InitializeComponent();

            ConnectionLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderConnection"),
                ResourceProvider.GetString("LOCPlayAch_Provider_BattleNet"));
            AuthLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderAuth"),
                ResourceProvider.GetString("LOCPlayAch_Provider_BattleNet"));
        }

        public override void Initialize(IProviderSettings settings)
        {
            _battleNetSettings = settings as BattleNetSettings;
            base.Initialize(settings);
            LoadWowRegions();
            _ = RefreshAuthStatusAsync();
        }

        private void UpdateAuthStatus(AuthProbeResult result)
        {
            var isAuthenticated = result?.IsSuccess ?? false;
            IsAuthenticated = isAuthenticated;

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
            try
            {
                result = await _sessionManager.ProbeAuthStateAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Battle.net auth probe failed during settings refresh.");
                result = AuthProbeResult.ProbeFailed();
            }

            UpdateAuthStatus(result);
            UpdateSc2Status();
        }

        private async void LoginWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAuthBusy(true);
                var result = await _sessionManager.AuthenticateInteractiveAsync(forceInteractive: true, CancellationToken.None);
                if (result.IsSuccess)
                {
                    await RefreshAuthStatusAsync();
                    PlayniteAchievementsPlugin.NotifySettingsSaved();
                }
                else
                {
                    UpdateAuthStatus(result);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Battle.net web login failed");
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
                SetAuthBusy(true);
                _sessionManager.ClearSession();
                await RefreshAuthStatusAsync();
                PlayniteAchievementsPlugin.NotifySettingsSaved();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Battle.net logout failed");
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
        }

        #region WoW Region/Realm Loading

        private async void LoadWowRegions()
        {
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
        }

        private async void WowRegion_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var region = WowRegionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(region)) return;

            var settings = _battleNetSettings;
            if (settings == null) return;
            settings.WowRegion = region;

            try
            {
                var realms = await _apiClient.GetWowRealmsAsync(region, CancellationToken.None);
                WowRealmCombo.Items.Clear();
                foreach (var realm in realms)
                {
                    WowRealmCombo.Items.Add(realm);
                }

                if (!string.IsNullOrEmpty(settings.WowRealmSlug))
                {
                    WowRealmCombo.SelectedItem = realms.Find(r => r.Slug == settings.WowRealmSlug);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Failed to load realms for region {region}.");
            }
        }

        private void WowRealm_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_battleNetSettings == null) return;
            if (WowRealmCombo.SelectedItem is WowRealm realm)
            {
                _battleNetSettings.WowRealmSlug = realm.Slug;
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
    }
}

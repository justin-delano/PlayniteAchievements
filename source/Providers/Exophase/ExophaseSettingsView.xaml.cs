using System;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Providers.Exophase
{
    public partial class ExophaseSettingsView : ProviderSettingsViewBase, IAuthRefreshable
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(ExophaseSettingsView));
        private readonly ExophaseSessionManager _sessionManager;
        private ExophaseSettings _exophaseSettings;

        public static readonly DependencyProperty AuthBusyProperty =
            DependencyProperty.Register(nameof(AuthBusy), typeof(bool), typeof(ExophaseSettingsView), new PropertyMetadata(false));
        public bool AuthBusy { get => (bool)GetValue(AuthBusyProperty); set => SetValue(AuthBusyProperty, value); }

        public static readonly DependencyProperty IsAuthenticatedProperty =
            DependencyProperty.Register(nameof(IsAuthenticated), typeof(bool), typeof(ExophaseSettingsView), new PropertyMetadata(false));
        public bool IsAuthenticated { get => (bool)GetValue(IsAuthenticatedProperty); set => SetValue(IsAuthenticatedProperty, value); }

        public static readonly DependencyProperty AuthStatusProperty =
            DependencyProperty.Register(nameof(AuthStatus), typeof(string), typeof(ExophaseSettingsView), new PropertyMetadata(string.Empty));
        public string AuthStatus { get => (string)GetValue(AuthStatusProperty); set => SetValue(AuthStatusProperty, value); }

        public new ExophaseSettings Settings => _exophaseSettings;

        public ExophaseSettingsView(ExophaseSessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            InitializeComponent();
        }

        public override void Initialize(IProviderSettings settings)
        {
            _exophaseSettings = settings as ExophaseSettings;
            base.Initialize(settings);
            _ = RefreshAuthStatusAsync();
        }

        private void UpdateAuthStatus(AuthProbeResult result)
        {
            var isAuthenticated = result?.IsSuccess ?? false;
            IsAuthenticated = isAuthenticated;
            var providerName = ResourceProvider.GetString("LOCPlayAch_Provider_Exophase");

            if (isAuthenticated)
            {
                AuthStatus = string.Format(ResourceProvider.GetString("LOCPlayAch_Settings_Auth_AlreadyAuthenticated"), providerName);
                return;
            }

            var localized = !string.IsNullOrWhiteSpace(result?.MessageKey)
                ? ResourceProvider.GetString(result.MessageKey)
                : null;

            AuthStatus = string.IsNullOrWhiteSpace(localized)
                ? string.Format(ResourceProvider.GetString("LOCPlayAch_Settings_Auth_NotAuthenticated"), providerName)
                : localized;
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
                Logger.Debug(ex, "Exophase auth probe failed during settings refresh.");
                result = AuthProbeResult.ProbeFailed();
            }

            UpdateAuthStatus(result);
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
            catch (Exception ex) { Logger.Error(ex, "Exophase login failed"); }
            finally { SetAuthBusy(false); }
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
            catch (Exception ex) { Logger.Error(ex, "Exophase logout failed"); }
            finally { SetAuthBusy(false); }
        }

        private void SetAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess()) AuthBusy = busy;
            else Dispatcher.BeginInvoke(new Action(() => AuthBusy = busy));
        }

        private void ExophasePlatform_CheckboxLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkbox) || _exophaseSettings?.ManagedProviders == null)
            {
                return;
            }

            var token = checkbox.Tag as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            checkbox.IsChecked = _exophaseSettings.ManagedProviders.Contains(token);
        }

        private void ExophasePlatform_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkbox) || _exophaseSettings == null)
            {
                return;
            }

            var token = checkbox.Tag as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            if (_exophaseSettings.ManagedProviders == null)
            {
                _exophaseSettings.ManagedProviders = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (checkbox.IsChecked == true)
            {
                _exophaseSettings.ManagedProviders.Add(token);
            }
            else
            {
                _exophaseSettings.ManagedProviders.Remove(token);
            }
        }
    }
}

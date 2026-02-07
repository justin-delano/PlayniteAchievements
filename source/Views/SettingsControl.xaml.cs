// SettingsControl.xaml.cs
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using PlayniteAchievements.Services;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Common;
using PlayniteAchievements.Providers.Steam;
using Playnite.SDK;
using System.Diagnostics;
using System.Windows.Navigation;

namespace PlayniteAchievements.Views
{
    public partial class SettingsControl : UserControl, IDisposable
    {
        // -----------------------------
        // Option A (safe): UserControl DependencyProperties for auth UI
        // -----------------------------

        public static readonly DependencyProperty SteamAuthStatusProperty =
            DependencyProperty.Register(
                nameof(SteamAuthStatus),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(ResourceProvider.GetString("LOCPlayAch_Settings_Status_NotChecked")));

        public string SteamAuthStatus
        {
            get => (string)GetValue(SteamAuthStatusProperty);
            set => SetValue(SteamAuthStatusProperty, value);
        }

        public static readonly DependencyProperty SteamAuthBusyProperty =
            DependencyProperty.Register(
                nameof(SteamAuthBusy),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool SteamAuthBusy
        {
            get => (bool)GetValue(SteamAuthBusyProperty);
            set => SetValue(SteamAuthBusyProperty, value);
        }

        public static readonly DependencyProperty SteamAuthenticatedProperty =
            DependencyProperty.Register(
                nameof(SteamAuthenticated),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool SteamAuthenticated
        {
            get => (bool)GetValue(SteamAuthenticatedProperty);
            set => SetValue(SteamAuthenticatedProperty, value);
        }

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly SteamHTTPClient _steam;
        private readonly SteamSessionManager _sessionManager;
        private readonly ILogger _logger;

        public SettingsControl(PlayniteAchievementsSettingsViewModel settingsViewModel, ILogger logger, SteamHTTPClient steamClient, SteamSessionManager sessionManager, PlayniteAchievementsPlugin plugin)
        {
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _steam = steamClient;
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

            InitializeComponent();

            // Playnite does not reliably set DataContext for settings views.
            // Bind directly to the settings model so XAML uses {Binding SomeSetting}.
            DataContext = _settingsViewModel.Settings;

            // Debug logging to verify DataContext and Settings values
            _logger?.Info($"SettingsControl created. DataContext type: {DataContext?.GetType().Name}");
            _logger?.Info($"Settings.Persisted.UltraRareThreshold: {_settingsViewModel.Settings.Persisted.UltraRareThreshold}, EnablePeriodicUpdates: {_settingsViewModel.Settings.Persisted.EnablePeriodicUpdates}");

            Loaded += async (s, e) =>
            {
                // Ensure DataContext is still correct after Playnite initialization
                if (DataContext is not PlayniteAchievementsSettings)
                {
                    _logger?.Info($"DataContext was wrong type in Loaded event: {DataContext?.GetType().Name}, fixing...");
                    DataContext = _settingsViewModel.Settings;
                    _logger?.Info($"DataContext fixed to: {DataContext?.GetType().Name}");
                }
                else
                {
                    _logger?.Info($"DataContext verified correct in Loaded event: {DataContext?.GetType().Name}");
                }
                await CheckSteamAuthAsync().ConfigureAwait(false);
            };
        }

        // -----------------------------
        // Steam auth UI
        // -----------------------------

        private async void SteamAuth_Check_Click(object sender, RoutedEventArgs e)
        {
            await CheckSteamAuthAsync().ConfigureAwait(false);
        }

        private async void SteamAuth_Authenticate_Click(object sender, RoutedEventArgs e)
        {
            SetSteamAuthBusy(true);

            try
            {
                var (ok, msg) = await _sessionManager.AuthenticateInteractiveAsync(CancellationToken.None).ConfigureAwait(false);
                SetSteamAuthStatus(msg);
            }
            finally
            {
                SetSteamAuthBusy(false);
            }
        }

        private void SteamAuth_Clear_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager.ClearSession();
            SteamAuthenticated = false;
            SteamAuthStatus = ResourceProvider.GetString("LOCPlayAch_Settings_Status_CookiesCleared");
        }

        private async Task CheckSteamAuthAsync()
        {
            SetSteamAuthBusy(true);
            try
            {
                var (isLoggedIn, _) = await _sessionManager.ProbeLoggedInAsync(CancellationToken.None).ConfigureAwait(false);
                SteamAuthenticated = isLoggedIn;
                if (isLoggedIn)
                {
                    SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_SteamAuth_OK"));
                }
                else
                {
                    SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_AuthInvalid"));
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Steam auth check failed.");
                SteamAuthenticated = false;
                SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_AuthError"));
            }
            finally
            {
                SetSteamAuthBusy(false);
            }
        }

        private void SetSteamAuthStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                SteamAuthStatus = status;
            }
            else
            {
                // Use BeginInvoke for non-blocking marshal to UI thread
                Dispatcher.BeginInvoke(new Action(() => SteamAuthStatus = status));
            }
        }

        private void SetSteamAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess())
            {
                SteamAuthBusy = busy;
            }
            else
            {
                // Use BeginInvoke for non-blocking marshal to UI thread
                Dispatcher.BeginInvoke(new Action(() => SteamAuthBusy = busy));
            }
        }

        // -----------------------------
        // Cache actions
        // -----------------------------

        private void WipeCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plugin.AchievementService.Cache.ClearCache();
                var stillPresent = _plugin.AchievementService.Cache.CacheFileExists();
                
                var (msg, img) = !stillPresent
                    ? (ResourceProvider.GetString("LOCPlayAch_Settings_Cache_Wiped"), MessageBoxImage.Information)
                    : (ResourceProvider.GetString("LOCPlayAch_Settings_Cache_WipeFailed"), MessageBoxImage.Error);
                
                _plugin.PlayniteApi.Dialogs.ShowMessage(msg, ResourceProvider.GetString("LOCPlayAch_Title_PluginName"), MessageBoxButton.OK, img);
            }
            catch (Exception ex)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    "Failed to wipe cache: " + ex.Message,
                    "Friends Achievement Feed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearImageCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plugin.ImageService?.Clear();
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCPlayAch_Settings_ImageCache_Cleared"),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCPlayAch_Settings_ImageCache_ClearFailed") + ": " + ex.Message,
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ResetFirstTimeSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info($"Resetting FirstTimeSetupCompleted. Current value before: {_settingsViewModel.Settings.Persisted.FirstTimeSetupCompleted}");

                _settingsViewModel.Settings.Persisted.FirstTimeSetupCompleted = false;

                _logger.Info($"Value after setting to false: {_settingsViewModel.Settings.Persisted.FirstTimeSetupCompleted}");

                _plugin.SavePluginSettings(_settingsViewModel.Settings);

                _logger.Info("Settings saved. Verifying save...");

                // Verify the save worked by re-loading
                var reloaded = _plugin.LoadPluginSettings<PlayniteAchievementsSettings>();
                var reloadedValue = reloaded?.Persisted.FirstTimeSetupCompleted ?? true;
                _logger.Info($"Value after reload: {reloadedValue}");

                if (!reloadedValue)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        "First-time setup has been reset!\n\nClose and reopen the sidebar to see the landing page.",
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        "Failed to verify reset. The settings may not have been saved correctly.",
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reset first-time setup.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    "Failed to reset first-time setup: " + ex.Message,
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tc = sender as TabControl;
            if (tc == null) return;

            // Inspect the newly selected TabItem by name (uses x:Name from XAML)
            if (e.AddedItems == null || e.AddedItems.Count == 0) return;
            if (e.AddedItems[0] is not TabItem selected) return;

            var name = selected.Name ?? string.Empty;
            if (string.Equals(name, "SteamTab", StringComparison.OrdinalIgnoreCase))
            {
                await CheckSteamAuthAsync().ConfigureAwait(false);
                _logger?.Info("Checked Steam auth for Steam tab.");
            }
        }

        // -----------------------------
        // IDisposable implementation
        // -----------------------------

        public void Dispose()
        {
            try
            {
                _steam?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error disposing SteamClient in SettingsControl.");
            }
        }
        
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}

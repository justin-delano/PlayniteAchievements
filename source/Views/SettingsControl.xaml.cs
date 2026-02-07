// SettingsControl.xaml.cs
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
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

        public static readonly DependencyProperty SteamFullyConfiguredProperty =
            DependencyProperty.Register(
                nameof(SteamFullyConfigured),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool SteamFullyConfigured
        {
            get => (bool)GetValue(SteamFullyConfiguredProperty);
            set => SetValue(SteamFullyConfiguredProperty, value);
        }

        public static readonly DependencyProperty RaFullyConfiguredProperty =
            DependencyProperty.Register(
                nameof(RaFullyConfigured),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool RaFullyConfigured
        {
            get => (bool)GetValue(RaFullyConfiguredProperty);
            set => SetValue(RaFullyConfiguredProperty, value);
        }

        public static readonly DependencyProperty RaAuthStatusProperty =
            DependencyProperty.Register(
                nameof(RaAuthStatus),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(ResourceProvider.GetString("LOCPlayAch_Settings_Status_NotChecked")));

        public string RaAuthStatus
        {
            get => (string)GetValue(RaAuthStatusProperty);
            set => SetValue(RaAuthStatusProperty, value);
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
                UpdateRaAuthState();

                // Subscribe to RA credential changes
                if (_settingsViewModel.Settings.Persisted is PlayniteAchievementsSettingsPersisted persisted)
                {
                    PropertyChangedEventManager.AddHandler(persisted, OnRaCredentialChanged, nameof(RaUsername));
                    PropertyChangedEventManager.AddHandler(persisted, OnRaCredentialChanged, nameof(RaWebApiKey));
                }
            };
        }

        private void OnRaCredentialChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RaUsername) || e.PropertyName == nameof(RaWebApiKey))
            {
                UpdateRaAuthState();
            }
        }

        private void UpdateRaAuthState()
        {
            var settings = _settingsViewModel.Settings;
            var hasUsername = !string.IsNullOrEmpty(settings?.Persisted?.RaUsername);
            var hasApiKey = !string.IsNullOrEmpty(settings?.Persisted?.RaWebApiKey);
            var fullyConfigured = hasUsername && hasApiKey;

            SetRaFullyConfigured(fullyConfigured);

            // Set status message based on combined state
            if (fullyConfigured)
            {
                SetRaAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Ra_Configured"));
            }
            else if (!hasUsername && !hasApiKey)
            {
                SetRaAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Ra_BothNeeded"));
            }
            else if (!hasUsername)
            {
                SetRaAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Ra_UsernameNeeded"));
            }
            else
            {
                SetRaAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Ra_ApiKeyNeeded"));
            }
        }

        private void SetRaFullyConfigured(bool fullyConfigured)
        {
            if (Dispatcher.CheckAccess())
            {
                RaFullyConfigured = fullyConfigured;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => RaFullyConfigured = fullyConfigured));
            }
        }

        private void SetRaAuthStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                RaAuthStatus = status;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => RaAuthStatus = status));
            }
        }

        // -----------------------------
        // Credential text box handlers (triggers auth state update on Enter)
        // -----------------------------

        private void CredentialTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                // Determine which textbox and update the appropriate auth state
                if (sender is TextBox textBox)
                {
                    // Force binding update
                    var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                    bindingExpression?.UpdateSource();

                    // Check which property was bound and update auth state
                    if (textBox.GetBindingExpression(TextBox.TextProperty)?.Path.Path == "SteamApiKey")
                    {
                        // Update Steam auth state (API key changed, need to recheck web auth too)
                        _ = Task.Run(async () => await Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            await CheckSteamAuthAsync().ConfigureAwait(false);
                        })));
                    }
                    else if (textBox.GetBindingExpression(TextBox.TextProperty)?.Path.Path == "RaUsername" ||
                             textBox.GetBindingExpression(TextBox.TextProperty)?.Path.Path == "RaWebApiKey")
                    {
                        // Update RA auth state
                        UpdateRaAuthState();
                    }
                }

                // Move focus away from textbox to "commit" the entry
                var parent = (sender as FrameworkElement)?.Parent as FrameworkElement;
                parent?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
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

                // Update auth state after authentication attempt
                if (ok)
                {
                    SetSteamAuthenticated(true);
                }
                else
                {
                    // Verify actual login state even if auth reported failure
                    await CheckSteamAuthAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                SetSteamAuthBusy(false);
            }
        }

        private void SteamAuth_Clear_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager.ClearSession();
            SetSteamAuthenticated(false);
            UpdateCombinedAuthState(false);
            SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_CookiesCleared"));
        }

        private async Task CheckSteamAuthAsync()
        {
            SetSteamAuthBusy(true);
            try
            {
                var (isLoggedIn, _) = await _sessionManager.ProbeLoggedInAsync(CancellationToken.None).ConfigureAwait(false);
                SetSteamAuthenticated(isLoggedIn);

                // Update combined auth state
                UpdateCombinedAuthState(isLoggedIn);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Steam auth check failed.");
                SetSteamAuthenticated(false);
                SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_AuthError"));
                UpdateCombinedAuthState(false);
            }
            finally
            {
                SetSteamAuthBusy(false);
            }
        }

        private void UpdateCombinedAuthState(bool webAuthSuccessful)
        {
            var settings = _settingsViewModel.Settings;
            var hasApiKey = !string.IsNullOrEmpty(settings?.Persisted?.SteamApiKey);
            var fullyConfigured = hasApiKey && webAuthSuccessful;

            SetSteamFullyConfigured(fullyConfigured);

            // Set status message based on combined state
            if (fullyConfigured)
            {
                SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_SteamAuth_OK"));
            }
            else if (!hasApiKey && !webAuthSuccessful)
            {
                SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_BothNeeded"));
            }
            else if (!hasApiKey)
            {
                SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_ApiKeyNeeded"));
            }
            else
            {
                SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_WebAuthNeeded"));
            }
        }

        private void SetSteamAuthenticated(bool authenticated)
        {
            if (Dispatcher.CheckAccess())
            {
                SteamAuthenticated = authenticated;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => SteamAuthenticated = authenticated));
            }
        }

        private void SetSteamFullyConfigured(bool fullyConfigured)
        {
            if (Dispatcher.CheckAccess())
            {
                SteamFullyConfigured = fullyConfigured;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => SteamFullyConfigured = fullyConfigured));
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
                _plugin.ImageService?.ClearDiskCache();
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

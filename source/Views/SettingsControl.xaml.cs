// SettingsControl.xaml.cs
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using PlayniteAchievements.Services;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Common;
using PlayniteAchievements.Providers.Steam;
using PlayniteAchievements.Providers.GOG;
using PlayniteAchievements.Providers.Epic;
using PlayniteAchievements.Providers.PSN;
using PlayniteAchievements.Services.ThemeMigration;
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

        public static readonly DependencyProperty GogAuthStatusProperty =
            DependencyProperty.Register(
                nameof(GogAuthStatus),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(ResourceProvider.GetString("LOCPlayAch_Settings_GogAuth_NotChecked")));

        public string GogAuthStatus
        {
            get => (string)GetValue(GogAuthStatusProperty);
            set => SetValue(GogAuthStatusProperty, value);
        }

        public static readonly DependencyProperty GogAuthBusyProperty =
            DependencyProperty.Register(
                nameof(GogAuthBusy),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool GogAuthBusy
        {
            get => (bool)GetValue(GogAuthBusyProperty);
            set => SetValue(GogAuthBusyProperty, value);
        }

        public static readonly DependencyProperty GogAuthenticatedProperty =
            DependencyProperty.Register(
                nameof(GogAuthenticated),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool GogAuthenticated
        {
            get => (bool)GetValue(GogAuthenticatedProperty);
            set => SetValue(GogAuthenticatedProperty, value);
        }

        public static readonly DependencyProperty EpicAuthStatusProperty =
            DependencyProperty.Register(
                nameof(EpicAuthStatus),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(ResourceProvider.GetString("LOCPlayAch_Settings_EpicAuth_NotChecked")));

        public string EpicAuthStatus
        {
            get => (string)GetValue(EpicAuthStatusProperty);
            set => SetValue(EpicAuthStatusProperty, value);
        }

        public static readonly DependencyProperty EpicAuthBusyProperty =
            DependencyProperty.Register(
                nameof(EpicAuthBusy),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool EpicAuthBusy
        {
            get => (bool)GetValue(EpicAuthBusyProperty);
            set => SetValue(EpicAuthBusyProperty, value);
        }

        public static readonly DependencyProperty EpicAuthenticatedProperty =
            DependencyProperty.Register(
                nameof(EpicAuthenticated),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool EpicAuthenticated
        {
            get => (bool)GetValue(EpicAuthenticatedProperty);
            set => SetValue(EpicAuthenticatedProperty, value);
        }

        public static readonly DependencyProperty PsnAuthStatusProperty =
            DependencyProperty.Register(
                nameof(PsnAuthStatus),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(ResourceProvider.GetString("LOCPlayAch_Settings_PsnAuth_NotChecked")));

        public string PsnAuthStatus
        {
            get => (string)GetValue(PsnAuthStatusProperty);
            set => SetValue(PsnAuthStatusProperty, value);
        }

        public static readonly DependencyProperty PsnAuthBusyProperty =
            DependencyProperty.Register(
                nameof(PsnAuthBusy),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool PsnAuthBusy
        {
            get => (bool)GetValue(PsnAuthBusyProperty);
            set => SetValue(PsnAuthBusyProperty, value);
        }

        public static readonly DependencyProperty PsnAuthenticatedProperty =
            DependencyProperty.Register(
                nameof(PsnAuthenticated),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool PsnAuthenticated
        {
            get => (bool)GetValue(PsnAuthenticatedProperty);
            set => SetValue(PsnAuthenticatedProperty, value);
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

        // Theme migration UI state properties
        public static readonly DependencyProperty AvailableThemesProperty =
            DependencyProperty.Register(
                nameof(AvailableThemes),
                typeof(System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>),
                typeof(SettingsControl),
                new PropertyMetadata(null));

        public System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo> AvailableThemes
        {
            get => (System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>)GetValue(AvailableThemesProperty);
            set => SetValue(AvailableThemesProperty, value);
        }

        public static readonly DependencyProperty RevertableThemesProperty =
            DependencyProperty.Register(
                nameof(RevertableThemes),
                typeof(System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>),
                typeof(SettingsControl),
                new PropertyMetadata(null));

        public System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo> RevertableThemes
        {
            get => (System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>)GetValue(RevertableThemesProperty);
            set => SetValue(RevertableThemesProperty, value);
        }

        public static readonly DependencyProperty SelectedThemePathProperty =
            DependencyProperty.Register(
                nameof(SelectedThemePath),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string SelectedThemePath
        {
            get => (string)GetValue(SelectedThemePathProperty);
            set => SetValue(SelectedThemePathProperty, value);
        }

        public static readonly DependencyProperty SelectedRevertThemePathProperty =
            DependencyProperty.Register(
                nameof(SelectedRevertThemePath),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string SelectedRevertThemePath
        {
            get => (string)GetValue(SelectedRevertThemePathProperty);
            set => SetValue(SelectedRevertThemePathProperty, value);
        }

        public static readonly DependencyProperty HasThemesToMigrateProperty =
            DependencyProperty.Register(
                nameof(HasThemesToMigrate),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool HasThemesToMigrate
        {
            get => (bool)GetValue(HasThemesToMigrateProperty);
            set => SetValue(HasThemesToMigrateProperty, value);
        }

        public static readonly DependencyProperty HasRevertableThemesProperty =
            DependencyProperty.Register(
                nameof(HasRevertableThemes),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool HasRevertableThemes
        {
            get => (bool)GetValue(HasRevertableThemesProperty);
            set => SetValue(HasRevertableThemesProperty, value);
        }

        public static readonly DependencyProperty ShowNoThemesMessageProperty =
            DependencyProperty.Register(
                nameof(ShowNoThemesMessage),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(true));

        public bool ShowNoThemesMessage
        {
            get => (bool)GetValue(ShowNoThemesMessageProperty);
            set => SetValue(ShowNoThemesMessageProperty, value);
        }

        public static readonly DependencyProperty ShowNoRevertableThemesMessageProperty =
            DependencyProperty.Register(
                nameof(ShowNoRevertableThemesMessage),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(true));

        public bool ShowNoRevertableThemesMessage
        {
            get => (bool)GetValue(ShowNoRevertableThemesMessageProperty);
            set => SetValue(ShowNoRevertableThemesMessageProperty, value);
        }

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly ILogger _logger;
        private readonly ThemeDiscoveryService _themeDiscovery;
        private readonly ThemeMigrationService _themeMigration;
        private readonly SteamSessionManager _steamSessionManager;
        private readonly GogSessionManager _gogSessionManager;
        private readonly EpicSessionManager _epicSessionManager;
        private readonly PsnSessionManager _psnSessionManager;

        public SettingsControl(PlayniteAchievementsSettingsViewModel settingsViewModel, ILogger logger, PlayniteAchievementsPlugin plugin, SteamSessionManager steamSessionManager, GogSessionManager gogSessionManager, EpicSessionManager epicSessionManager, PsnSessionManager psnSessionManager)
        {
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _steamSessionManager = steamSessionManager ?? throw new ArgumentNullException(nameof(steamSessionManager));
            _gogSessionManager = gogSessionManager ?? throw new ArgumentNullException(nameof(gogSessionManager));
            _epicSessionManager = epicSessionManager ?? throw new ArgumentNullException(nameof(epicSessionManager));
            _psnSessionManager = psnSessionManager ?? throw new ArgumentNullException(nameof(psnSessionManager));

            _themeDiscovery = new ThemeDiscoveryService(_logger, plugin.PlayniteApi);
            _themeMigration = new ThemeMigrationService(_logger);

            InitializeComponent();

            // Playnite does not reliably set DataContext for settings views.
            // Bind directly to the settings model so XAML uses {Binding SomeSetting}.
            DataContext = _settingsViewModel.Settings;

            // Initialize theme collections
            AvailableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            RevertableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();

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
                await CheckGogAuthAsync().ConfigureAwait(false);
                await CheckEpicAuthAsync().ConfigureAwait(false);
                await CheckPsnAuthAsync().ConfigureAwait(false);
                UpdateRaAuthState();

                // Load themes on initial load
                LoadThemes();
            };
        }

        // -----------------------------
        // Theme migration
        // -----------------------------

        private void LoadThemes()
        {
            // Ensure we're on the UI thread before accessing DependencyProperties
            if (Dispatcher.CheckAccess())
            {
                LoadThemesInternal();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(LoadThemesInternal));
            }
        }

        private void LoadThemesInternal()
        {
            try
            {
                AvailableThemes.Clear();
                RevertableThemes.Clear();

                var themesPath = _themeDiscovery.GetDefaultThemesPath();
                if (string.IsNullOrEmpty(themesPath))
                {
                    _logger.Info("No themes path found for theme migration.");
                    UpdateThemeMigrationState();
                    return;
                }

                var themes = _themeDiscovery.DiscoverThemes(themesPath);

                // Themes that need migration (no backup, has SuccessStory)
                foreach (var theme in themes.Where(t => t.NeedsMigration))
                {
                    AvailableThemes.Add(theme);
                }

                // Themes that can be reverted (has backup)
                foreach (var theme in themes.Where(t => t.HasBackup))
                {
                    RevertableThemes.Add(theme);
                }

                UpdateThemeMigrationState();

                _logger.Info($"Loaded {AvailableThemes.Count} themes to migrate, {RevertableThemes.Count} themes to revert.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load themes for migration.");
            }
        }

        private void UpdateThemeMigrationState()
        {
            var hasThemes = AvailableThemes.Count > 0;
            var hasRevertable = RevertableThemes.Count > 0;

            HasThemesToMigrate = hasThemes;
            HasRevertableThemes = hasRevertable;
            ShowNoThemesMessage = !hasThemes;
            ShowNoRevertableThemesMessage = !hasRevertable;
        }

        private async void MigrateTheme_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedThemePath))
            {
                _logger.Warn("Migrate clicked but no theme selected.");
                return;
            }

            _logger.Info($"User requested theme migration for: {SelectedThemePath}");

            try
            {
                var result = await _themeMigration.MigrateThemeAsync(SelectedThemePath);

                if (result.Success)
                {
                    _logger.Info($"Theme migration successful: {SelectedThemePath}");

                    // Only show restart dialog if files were actually modified
                    if (result.FilesBackedUp > 0)
                    {
                        _plugin.PlayniteApi.Dialogs.ShowMessage(
                            $"{result.Message}{Environment.NewLine}{Environment.NewLine}{L("LOCPlayAch_ThemeMigration_RestartRequired", "Please restart Playnite to apply the theme changes.")}",
                            L("LOCPlayAch_ThemeMigration_Title", "Theme Migration"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        // No changes were made, just show info message
                        _plugin.PlayniteApi.Dialogs.ShowMessage(
                            result.Message,
                            L("LOCPlayAch_ThemeMigration_Title", "Theme Migration"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    // Reload themes to update the lists
                    LoadThemes();
                }
                else
                {
                    _logger.Warn($"Theme migration failed: {result.Message}");
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        result.Message,
                        L("LOCPlayAch_ThemeMigration_Title", "Theme Migration"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute theme migration.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_ThemeMigration_Failed", "Theme migration failed: {0}", ex.Message),
                    L("LOCPlayAch_ThemeMigration_Title", "Theme Migration"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void RevertTheme_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedRevertThemePath))
            {
                _logger.Warn("Revert clicked but no theme selected.");
                return;
            }

            _logger.Info($"User requested theme revert for: {SelectedRevertThemePath}");

            try
            {
                var result = await _themeMigration.RevertThemeAsync(SelectedRevertThemePath);

                if (result.Success)
                {
                    _logger.Info($"Theme revert successful: {SelectedRevertThemePath}");
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        result.Message,
                        L("LOCPlayAch_ThemeMigration_Revert", "Revert Theme"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Reload themes to update the lists
                    LoadThemes();
                }
                else
                {
                    _logger.Warn($"Theme revert failed: {result.Message}");
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        result.Message,
                        L("LOCPlayAch_ThemeMigration_Revert", "Revert Theme"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute theme revert.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_ThemeMigration_RevertFailed", "Revert failed: {0}", ex.Message),
                    L("LOCPlayAch_ThemeMigration_Revert", "Revert Theme"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
        // Credential text box handlers (Enter key commits entry)
        // -----------------------------

        private async void SteamApiKey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                await CheckSteamAuthAsync().ConfigureAwait(false);
                _ = Dispatcher.BeginInvoke(new Action(() => MoveFocusFrom((TextBox)sender)));
            }
        }

        private void RaUsername_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                UpdateRaAuthState();
                MoveFocusFrom((TextBox)sender);
            }
        }

        private void RaApiKey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                UpdateRaAuthState();
                MoveFocusFrom((TextBox)sender);
            }
        }

        private void MoveFocusFrom(TextBox textBox)
        {
            var parent = textBox?.Parent as FrameworkElement;
            parent?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
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
                var (ok, msg) = await _steamSessionManager.AuthenticateInteractiveAsync(CancellationToken.None).ConfigureAwait(false);
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
            _steamSessionManager.ClearSession();
            SetSteamAuthenticated(false);
            UpdateCombinedAuthState(false);
            SetSteamAuthStatus(ResourceProvider.GetString("LOCPlayAch_Settings_Status_CookiesCleared"));
        }

        private async Task CheckSteamAuthAsync()
        {
            SetSteamAuthBusy(true);
            try
            {
                var (isLoggedIn, _) = await _steamSessionManager.ProbeLoggedInAsync(CancellationToken.None).ConfigureAwait(false);
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
        // GOG auth UI
        // -----------------------------

        private async void GogAuth_Check_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[GogAuth] Settings: Check Auth clicked.");
            await CheckGogAuthAsync().ConfigureAwait(false);
        }

        private async void GogAuth_Login_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[GogAuth] Settings: Login clicked.");
            SetGogAuthBusy(true);
            SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_Checking");

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                {
                    var result = await _gogSessionManager.AuthenticateInteractiveAsync(
                        forceInteractive: true,
                        ct: cts.Token).ConfigureAwait(false);
                    ApplyGogAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetGogAuthenticated(false);
                SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Login failed.");
                SetGogAuthenticated(false);
                SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_Failed");
            }
            finally
            {
                SetGogAuthBusy(false);
            }
        }

        private void GogAuth_Clear_Click(object sender, RoutedEventArgs e)
        {
            _gogSessionManager.ClearSession();
            SetGogAuthenticated(false);
            SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_CookiesCleared");
        }

        private async Task CheckGogAuthAsync()
        {
            SetGogAuthBusy(true);
            SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_Checking");

            // Small delay to ensure user sees the checking feedback
            await Task.Delay(300).ConfigureAwait(false);

            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    var result = await _gogSessionManager.ProbeAuthenticationAsync(timeoutCts.Token).ConfigureAwait(false);
                    ApplyGogAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetGogAuthenticated(false);
                SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "GOG auth check failed.");
                SetGogAuthenticated(false);
                SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_ProbeFailed");
            }
            finally
            {
                SetGogAuthBusy(false);
            }
        }

        private void ApplyGogAuthResult(GogAuthResult result)
        {
            if (result == null)
            {
                _logger?.Debug("[GogAuth] ApplyGogAuthResult: result is null");
                SetGogAuthenticated(false);
                SetGogAuthStatusByKey("LOCPlayAch_Settings_GogAuth_Failed");
                return;
            }

            _logger?.Debug($"[GogAuth] ApplyGogAuthResult: Outcome={result.Outcome}, IsSuccess={result.IsSuccess}, UserId={result.UserId}");

            var authenticated =
                result.Outcome == GogAuthOutcome.Authenticated ||
                result.Outcome == GogAuthOutcome.AlreadyAuthenticated;

            _logger?.Debug($"[GogAuth] ApplyGogAuthResult: authenticated={authenticated}");

            // Set authenticated state
            SetGogAuthenticated(authenticated);

            var statusKey = string.IsNullOrWhiteSpace(result.MessageKey)
                ? GetDefaultMessageKeyForOutcome(result.Outcome)
                : result.MessageKey;

            if (!result.WindowOpened &&
                !result.IsSuccess &&
                (result.Outcome == GogAuthOutcome.Failed || result.Outcome == GogAuthOutcome.Cancelled))
            {
                statusKey = "LOCPlayAch_Settings_GogAuth_WindowNotOpened";
            }

            SetGogAuthStatusByKey(statusKey);
        }

        private static string GetDefaultMessageKeyForOutcome(GogAuthOutcome outcome)
        {
            switch (outcome)
            {
                case GogAuthOutcome.Authenticated:
                    return "LOCPlayAch_Settings_GogAuth_Verified";
                case GogAuthOutcome.AlreadyAuthenticated:
                    return "LOCPlayAch_Settings_GogAuth_AlreadyAuthenticated";
                case GogAuthOutcome.NotAuthenticated:
                    return "LOCPlayAch_Settings_GogAuth_NotAuthenticated";
                case GogAuthOutcome.Cancelled:
                    return "LOCPlayAch_Settings_GogAuth_Cancelled";
                case GogAuthOutcome.TimedOut:
                    return "LOCPlayAch_Settings_GogAuth_TimedOut";
                case GogAuthOutcome.ProbeFailed:
                    return "LOCPlayAch_Settings_GogAuth_ProbeFailed";
                case GogAuthOutcome.Failed:
                default:
                    return "LOCPlayAch_Settings_GogAuth_Failed";
            }
        }

        private static string GetGogProgressMessageKey(GogAuthProgressStep step)
        {
            switch (step)
            {
                case GogAuthProgressStep.CheckingExistingSession:
                    return "LOCPlayAch_Settings_GogAuth_CheckingExistingSession";
                case GogAuthProgressStep.OpeningLoginWindow:
                    return "LOCPlayAch_Settings_GogAuth_OpeningWindow";
                case GogAuthProgressStep.WaitingForUserLogin:
                    return "LOCPlayAch_Settings_GogAuth_WaitingForLogin";
                case GogAuthProgressStep.VerifyingSession:
                    return "LOCPlayAch_Settings_GogAuth_VerifyingSession";
                case GogAuthProgressStep.Completed:
                    return "LOCPlayAch_Settings_GogAuth_Completed";
                case GogAuthProgressStep.Failed:
                default:
                    return "LOCPlayAch_Settings_GogAuth_Failed";
            }
        }

        private void SetGogAuthStatusByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var value = ResourceProvider.GetString(key);
            SetGogAuthStatus(string.IsNullOrWhiteSpace(value) ? key : value);
        }

        private void SetGogAuthStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                GogAuthStatus = status;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => GogAuthStatus = status));
            }
        }

        private void SetGogAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess())
            {
                GogAuthBusy = busy;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => GogAuthBusy = busy));
            }
        }

        private void SetGogAuthenticated(bool authenticated)
        {
            _logger?.Debug($"[GogAuth] SetGogAuthenticated: value={authenticated}, CheckAccess={Dispatcher.CheckAccess()}");
            if (Dispatcher.CheckAccess())
            {
                GogAuthenticated = authenticated;
                _logger?.Debug($"[GogAuth] SetGogAuthenticated: set directly, GogAuthenticated={GogAuthenticated}");
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    GogAuthenticated = authenticated;
                    _logger?.Debug($"[GogAuth] SetGogAuthenticated: set via Dispatcher, GogAuthenticated={GogAuthenticated}");
                }));
            }
        }

        // -----------------------------
        // Epic auth UI
        // -----------------------------

        private async void EpicAuth_Check_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[EpicAuth] Settings: Check Auth clicked.");
            await CheckEpicAuthAsync().ConfigureAwait(false);
        }

        private async void EpicAuth_Login_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[EpicAuth] Settings: Login clicked.");
            SetEpicAuthBusy(true);
            SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_Checking");

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                {
                    var result = await _epicSessionManager.AuthenticateInteractiveAsync(
                        forceInteractive: true,
                        ct: cts.Token).ConfigureAwait(false);
                    ApplyEpicAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[EpicAuth] Login failed.");
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_Failed");
            }
            finally
            {
                SetEpicAuthBusy(false);
            }
        }

        private async void EpicAuth_LoginAlternative_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[EpicAuth] Settings: Alternative Login clicked.");
            SetEpicAuthBusy(true);
            SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_Checking");

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                {
                    var result = await _epicSessionManager.LoginAlternativeAsync(cts.Token).ConfigureAwait(false);
                    ApplyEpicAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[EpicAuth] Alternative login failed.");
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_Failed");
            }
            finally
            {
                SetEpicAuthBusy(false);
            }
        }

        private void EpicAuth_Clear_Click(object sender, RoutedEventArgs e)
        {
            _epicSessionManager.ClearSession();
            SetEpicAuthenticated(false);
            SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_CookiesCleared");
        }

        private async Task CheckEpicAuthAsync()
        {
            SetEpicAuthBusy(true);
            SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_Checking");

            // Small delay to ensure user sees the checking feedback
            await Task.Delay(300).ConfigureAwait(false);

            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    var result = await _epicSessionManager.ProbeAuthenticationAsync(timeoutCts.Token).ConfigureAwait(false);
                    ApplyEpicAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Epic auth check failed.");
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_ProbeFailed");
            }
            finally
            {
                SetEpicAuthBusy(false);
            }
        }

        private void ApplyEpicAuthResult(EpicAuthResult result)
        {
            if (result == null)
            {
                SetEpicAuthenticated(false);
                SetEpicAuthStatusByKey("LOCPlayAch_Settings_EpicAuth_Failed");
                return;
            }

            var authenticated =
                result.Outcome == EpicAuthOutcome.Authenticated ||
                result.Outcome == EpicAuthOutcome.AlreadyAuthenticated;

            SetEpicAuthenticated(authenticated);

            var statusKey = string.IsNullOrWhiteSpace(result.MessageKey)
                ? GetDefaultEpicMessageKeyForOutcome(result.Outcome)
                : result.MessageKey;

            if (!result.WindowOpened &&
                !result.IsSuccess &&
                (result.Outcome == EpicAuthOutcome.Failed || result.Outcome == EpicAuthOutcome.Cancelled))
            {
                statusKey = "LOCPlayAch_Settings_EpicAuth_WindowNotOpened";
            }

            SetEpicAuthStatusByKey(statusKey);
        }

        private static string GetDefaultEpicMessageKeyForOutcome(EpicAuthOutcome outcome)
        {
            switch (outcome)
            {
                case EpicAuthOutcome.Authenticated:
                    return "LOCPlayAch_Settings_EpicAuth_Verified";
                case EpicAuthOutcome.AlreadyAuthenticated:
                    return "LOCPlayAch_Settings_EpicAuth_AlreadyAuthenticated";
                case EpicAuthOutcome.NotAuthenticated:
                    return "LOCPlayAch_Settings_EpicAuth_NotAuthenticated";
                case EpicAuthOutcome.Cancelled:
                    return "LOCPlayAch_Settings_EpicAuth_Cancelled";
                case EpicAuthOutcome.TimedOut:
                    return "LOCPlayAch_Settings_EpicAuth_TimedOut";
                case EpicAuthOutcome.ProbeFailed:
                    return "LOCPlayAch_Settings_EpicAuth_ProbeFailed";
                case EpicAuthOutcome.Failed:
                default:
                    return "LOCPlayAch_Settings_EpicAuth_Failed";
            }
        }

        private static string GetEpicProgressMessageKey(EpicAuthProgressStep step)
        {
            switch (step)
            {
                case EpicAuthProgressStep.CheckingExistingSession:
                    return "LOCPlayAch_Settings_EpicAuth_CheckingExistingSession";
                case EpicAuthProgressStep.OpeningLoginWindow:
                    return "LOCPlayAch_Settings_EpicAuth_OpeningWindow";
                case EpicAuthProgressStep.WaitingForUserLogin:
                    return "LOCPlayAch_Settings_EpicAuth_WaitingForLogin";
                case EpicAuthProgressStep.VerifyingSession:
                    return "LOCPlayAch_Settings_EpicAuth_VerifyingSession";
                case EpicAuthProgressStep.Completed:
                    return "LOCPlayAch_Settings_EpicAuth_Completed";
                case EpicAuthProgressStep.Failed:
                default:
                    return "LOCPlayAch_Settings_EpicAuth_Failed";
            }
        }

        private void SetEpicAuthStatusByKey(string key, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var value = ResourceProvider.GetString(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                SetEpicAuthStatus(key);
            }
            else if (args != null && args.Length > 0)
            {
                try
                {
                    SetEpicAuthStatus(string.Format(value, args));
                }
                catch
                {
                    SetEpicAuthStatus(value);
                }
            }
            else
            {
                SetEpicAuthStatus(value);
            }
        }

        private void SetEpicAuthStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                EpicAuthStatus = status;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => EpicAuthStatus = status));
            }
        }

        private void SetEpicAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess())
            {
                EpicAuthBusy = busy;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => EpicAuthBusy = busy));
            }
        }

        private void SetEpicAuthenticated(bool authenticated)
        {
            if (Dispatcher.CheckAccess())
            {
                EpicAuthenticated = authenticated;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => EpicAuthenticated = authenticated));
            }
        }

        // -----------------------------
        // PSN auth UI
        // -----------------------------

        private async void PsnAuth_Check_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[PSNAch] Settings: Check Auth clicked.");
            await CheckPsnAuthAsync().ConfigureAwait(false);
        }

        private async void PsnAuth_Login_Click(object sender, RoutedEventArgs e)
        {
            _logger?.Info("[PSNAch] Settings: Login clicked.");
            SetPsnAuthBusy(true);
            SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_Checking");

            var progress = new Progress<PsnAuthProgressStep>(step =>
            {
                SetPsnAuthStatusByKey(GetPsnProgressMessageKey(step));
            });

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180)))
                {
                    var result = await _psnSessionManager
                        .AuthenticateInteractiveAsync(forceInteractive: true, ct: cts.Token, progress: progress)
                        .ConfigureAwait(false);
                    ApplyPsnAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetPsnAuthenticated(false);
                SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Login failed.");
                SetPsnAuthenticated(false);
                SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_Failed");
            }
            finally
            {
                SetPsnAuthBusy(false);
            }
        }

        private void PsnAuth_Clear_Click(object sender, RoutedEventArgs e)
        {
            _psnSessionManager.ClearSession();
            SetPsnAuthenticated(false);
            SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_CookiesCleared");
        }

        private async Task CheckPsnAuthAsync()
        {
            SetPsnAuthBusy(true);
            SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_Checking");

            await Task.Delay(300).ConfigureAwait(false);

            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    var result = await _psnSessionManager.ProbeAuthenticationAsync(timeoutCts.Token).ConfigureAwait(false);
                    ApplyPsnAuthResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                SetPsnAuthenticated(false);
                SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_TimedOut");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "PSN auth check failed.");
                SetPsnAuthenticated(false);
                SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_ProbeFailed");
            }
            finally
            {
                SetPsnAuthBusy(false);
            }
        }

        private void ApplyPsnAuthResult(PsnAuthResult result)
        {
            if (result == null)
            {
                SetPsnAuthenticated(false);
                SetPsnAuthStatusByKey("LOCPlayAch_Settings_PsnAuth_Failed");
                return;
            }

            var authenticated =
                result.Outcome == PsnAuthOutcome.Authenticated ||
                result.Outcome == PsnAuthOutcome.AlreadyAuthenticated;

            SetPsnAuthenticated(authenticated);

            var statusKey = string.IsNullOrWhiteSpace(result.MessageKey)
                ? GetDefaultPsnMessageKeyForOutcome(result.Outcome)
                : result.MessageKey;

            if (!result.WindowOpened &&
                !result.IsSuccess &&
                (result.Outcome == PsnAuthOutcome.Failed || result.Outcome == PsnAuthOutcome.Cancelled))
            {
                statusKey = "LOCPlayAch_Settings_PsnAuth_WindowNotOpened";
            }

            SetPsnAuthStatusByKey(statusKey);
        }

        private static string GetDefaultPsnMessageKeyForOutcome(PsnAuthOutcome outcome)
        {
            switch (outcome)
            {
                case PsnAuthOutcome.Authenticated:
                    return "LOCPlayAch_Settings_PsnAuth_Verified";
                case PsnAuthOutcome.AlreadyAuthenticated:
                    return "LOCPlayAch_Settings_PsnAuth_AlreadyAuthenticated";
                case PsnAuthOutcome.NotAuthenticated:
                    return "LOCPlayAch_Settings_PsnAuth_NotAuthenticated";
                case PsnAuthOutcome.Cancelled:
                    return "LOCPlayAch_Settings_PsnAuth_Cancelled";
                case PsnAuthOutcome.TimedOut:
                    return "LOCPlayAch_Settings_PsnAuth_TimedOut";
                case PsnAuthOutcome.ProbeFailed:
                    return "LOCPlayAch_Settings_PsnAuth_ProbeFailed";
                case PsnAuthOutcome.LibraryMissing:
                    return "LOCPlayAch_Settings_PsnAuth_LibraryMissing";
                case PsnAuthOutcome.Failed:
                default:
                    return "LOCPlayAch_Settings_PsnAuth_Failed";
            }
        }

        private static string GetPsnProgressMessageKey(PsnAuthProgressStep step)
        {
            switch (step)
            {
                case PsnAuthProgressStep.CheckingExistingSession:
                    return "LOCPlayAch_Settings_PsnAuth_CheckingExistingSession";
                case PsnAuthProgressStep.OpeningLoginWindow:
                    return "LOCPlayAch_Settings_PsnAuth_OpeningWindow";
                case PsnAuthProgressStep.WaitingForUserLogin:
                    return "LOCPlayAch_Settings_PsnAuth_WaitingForLogin";
                case PsnAuthProgressStep.Completed:
                    return "LOCPlayAch_Settings_PsnAuth_Completed";
                case PsnAuthProgressStep.Failed:
                default:
                    return "LOCPlayAch_Settings_PsnAuth_Failed";
            }
        }

        private void SetPsnAuthStatusByKey(string key, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var value = ResourceProvider.GetString(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                SetPsnAuthStatus(key);
            }
            else if (args != null && args.Length > 0)
            {
                try
                {
                    SetPsnAuthStatus(string.Format(value, args));
                }
                catch
                {
                    SetPsnAuthStatus(value);
                }
            }
            else
            {
                SetPsnAuthStatus(value);
            }
        }

        private void SetPsnAuthStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                PsnAuthStatus = status;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => PsnAuthStatus = status));
            }
        }

        private void SetPsnAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess())
            {
                PsnAuthBusy = busy;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => PsnAuthBusy = busy));
            }
        }

        private void SetPsnAuthenticated(bool authenticated)
        {
            if (Dispatcher.CheckAccess())
            {
                PsnAuthenticated = authenticated;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => PsnAuthenticated = authenticated));
            }
        }

        // -----------------------------
        // Cache actions
        // -----------------------------

        private void WipeCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plugin.AchievementManager.Cache.ClearCache();
                var stillPresent = _plugin.AchievementManager.Cache.CacheFileExists();
                
                var (msg, img) = !stillPresent
                    ? (ResourceProvider.GetString("LOCPlayAch_Settings_Cache_Wiped"), MessageBoxImage.Information)
                    : (ResourceProvider.GetString("LOCPlayAch_Settings_Cache_WipeFailed"), MessageBoxImage.Error);
                
                _plugin.PlayniteApi.Dialogs.ShowMessage(msg, ResourceProvider.GetString("LOCPlayAch_Title_PluginName"), MessageBoxButton.OK, img);
            }
            catch (Exception ex)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Settings_Cache_WipeFailedWithError", "Failed to wipe cache: {0}", ex.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
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
                        L("LOCPlayAch_Settings_ResetFirstTimeSetupDone", "First-time setup has been reset. Close and reopen the sidebar to see the landing page."),
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        L("LOCPlayAch_Settings_ResetFirstTimeSetupVerifyFailed", "Failed to verify reset. The settings may not have been saved correctly."),
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reset first-time setup.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Settings_ResetFirstTimeSetupFailed", "Failed to reset first-time setup: {0}", ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportDatabase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exportBaseDir = _plugin.GetPluginUserDataPath();
                var cache = _plugin.AchievementManager?.Cache;

                if (cache == null)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        L("LOCPlayAch_Settings_ExportDatabaseFailed", "Database not available."),
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var exportDir = cache.ExportDatabaseToCsv(exportBaseDir);

                _logger.Info($"Database exported to: {exportDir}");

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Settings_ExportDatabaseDone", "Database exported to:\n{0}", exportDir),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to export database.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Settings_ExportDatabaseFailed", "Failed to export database: {0}", ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ForceRebuildHashIndex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pluginUserDataPath = _plugin.GetPluginUserDataPath();
                var raCacheDir = System.IO.Path.Combine(pluginUserDataPath, "ra");

                if (!System.IO.Directory.Exists(raCacheDir))
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        L("LOCPlayAch_Settings_HashIndex_NoCacheDir", "No RetroAchievements cache directory found."),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Find and delete all hash index cache files
                var hashIndexFiles = System.IO.Directory.GetFiles(raCacheDir, "hashindex_*.json.gz");
                var deletedCount = 0;

                foreach (var file in hashIndexFiles)
                {
                    try
                    {
                        System.IO.File.Delete(file);
                        deletedCount++;
                        _logger.Info($"Deleted hash index cache: {file}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"Failed to delete hash index cache: {file}");
                    }
                }

                if (deletedCount > 0)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        LF("LOCPlayAch_Settings_HashIndex_DeletedCount", "Deleted {0} hash index cache file(s).{1}{1}The hash index will be rebuilt on the next scan.", deletedCount, Environment.NewLine),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        LF("LOCPlayAch_Settings_HashIndex_NoFiles", "No hash index cache files found to delete.{0}{0}The cache may have already been cleared, or no scans have been performed yet.", Environment.NewLine),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to force hash index rebuild.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Settings_HashIndex_ClearFailed", "Failed to clear hash index cache: {0}", ex.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
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
            else if (string.Equals(name, "GogTab", StringComparison.OrdinalIgnoreCase))
            {
                await CheckGogAuthAsync().ConfigureAwait(false);
                _logger?.Info("Checked GOG auth for GOG tab.");
            }
            else if (string.Equals(name, "EpicTab", StringComparison.OrdinalIgnoreCase))
            {
                await CheckEpicAuthAsync().ConfigureAwait(false);
                _logger?.Info("Checked Epic auth for Epic tab.");
            }
            else if (string.Equals(name, "PsnTab", StringComparison.OrdinalIgnoreCase))
            {
                await CheckPsnAuthAsync().ConfigureAwait(false);
                _logger?.Info("Checked PSN auth for PSN tab.");
            }
            else if (string.Equals(name, "ThemeMigrationTab", StringComparison.OrdinalIgnoreCase))
            {
                LoadThemes();
                _logger?.Info("Loaded themes for Theme Migration tab.");
            }
        }

        // -----------------------------
        // IDisposable implementation
        // -----------------------------

        public void Dispose()
        {
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string LF(string key, string fallbackFormat, params object[] args)
        {
            return string.Format(L(key, fallbackFormat), args);
        }
        
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}

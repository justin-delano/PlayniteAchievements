// SettingsControl.xaml.cs
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using System.ComponentModel;
using PlayniteAchievements.Services;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Services.Refresh;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Views.Settings.Display;
using PlayniteAchievements.Views.Settings.General;
using PlayniteAchievements.Views.Settings.Navigation;
using PlayniteAchievements.Common;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.ThemeMigration;
using PlayniteAchievements.Services.UI;
using Playnite.SDK;
using System.Diagnostics;
using System.Windows.Navigation;

namespace PlayniteAchievements.Views
{
    public partial class SettingsControl : UserControl, IDisposable
    {
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
                new PropertyMetadata(string.Empty, OnSelectedThemePathChanged));

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

        public ObservableCollection<ThemeMigrationElementOption> ThemeMigrationCustomOptions { get; } =
            new ObservableCollection<ThemeMigrationElementOption>();

        public static readonly DependencyProperty LegacyManualImportStatusProperty =
            DependencyProperty.Register(
                nameof(LegacyManualImportStatus),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(ResourceProvider.GetString("LOCPlayAch_CustomRefresh_ProviderStatus_Ready")));

        public string LegacyManualImportStatus
        {
            get => (string)GetValue(LegacyManualImportStatusProperty);
            set => SetValue(LegacyManualImportStatusProperty, value);
        }

        public static readonly DependencyProperty LegacyManualImportBusyProperty =
            DependencyProperty.Register(
                nameof(LegacyManualImportBusy),
                typeof(bool),
                typeof(SettingsControl),
                new PropertyMetadata(false));

        public bool LegacyManualImportBusy
        {
            get => (bool)GetValue(LegacyManualImportBusyProperty);
            set => SetValue(LegacyManualImportBusyProperty, value);
        }

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly ILogger _logger;
        private readonly ThemeDiscoveryService _themeDiscovery;
        private readonly ThemeMigrationService _themeMigration;
        private readonly ProviderRegistry _providerRegistry;
        private readonly Func<Window, string, string> _pickColor;
        private DisplaySettingsTab _displaySettingsTab;
        private GeneralSettingsTab _generalSettingsTab;
        private bool _providerNavigationBuilt;
        private bool _themeMigrationLoaded;
        private const string SuccessStoryExtensionId = "cebe6d32-8c46-4459-b993-5a5189d60788";
        private const string SuccessStoryFolderName = "SuccessStory";

        public static readonly DependencyProperty ProviderNavigationItemsProperty =
            DependencyProperty.Register(
                nameof(ProviderNavigationItems),
                typeof(ObservableCollection<ProviderNavigationItem>),
                typeof(SettingsControl),
                new PropertyMetadata(null));

        public ObservableCollection<ProviderNavigationItem> ProviderNavigationItems
        {
            get => (ObservableCollection<ProviderNavigationItem>)GetValue(ProviderNavigationItemsProperty);
            set => SetValue(ProviderNavigationItemsProperty, value);
        }

        public static readonly DependencyProperty SelectedProviderNavigationItemProperty =
            DependencyProperty.Register(
                nameof(SelectedProviderNavigationItem),
                typeof(ProviderNavigationItem),
                typeof(SettingsControl),
                new PropertyMetadata(null));

        public ProviderNavigationItem SelectedProviderNavigationItem
        {
            get => (ProviderNavigationItem)GetValue(SelectedProviderNavigationItemProperty);
            set => SetValue(SelectedProviderNavigationItemProperty, value);
        }

        /// <summary>
        /// Set before opening settings to navigate directly to a provider tab.
        /// Cleared after use.
        /// </summary>
        public static string PendingNavigationProviderKey { get; set; }

        public SettingsControl(
            PlayniteAchievementsSettingsViewModel settingsViewModel,
            ILogger logger,
            PlayniteAchievementsPlugin plugin,
            ProviderRegistry providerRegistry,
            Func<Window, string, string> pickColor)
        {
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
            _pickColor = pickColor ?? throw new ArgumentNullException(nameof(pickColor));

            _themeDiscovery = new ThemeDiscoveryService(_logger, plugin.PlayniteApi);
            _themeMigration = new ThemeMigrationService(
                _logger,
                _settingsViewModel.Settings,
                () => _plugin.SavePluginSettings(_settingsViewModel.Settings));

            InitializeComponent();

            // Initialize provider navigation overview
            ProviderNavigationItems = new ObservableCollection<ProviderNavigationItem>();

            // Playnite does not reliably set DataContext for settings views.
            // Bind directly to the settings model so XAML uses {Binding SomeSetting}.
            DataContext = _settingsViewModel.Settings;
            if (FriendsSettingsContent != null)
            {
                FriendsSettingsContent.Content = new FriendsSettingsTab(
                    _settingsViewModel.Settings,
                    _plugin,
                    _providerRegistry,
                    _logger);
            }

            if (DisplaySettingsContent != null)
            {
                _displaySettingsTab = new DisplaySettingsTab(
                    _settingsViewModel.Settings,
                    _plugin,
                    _logger,
                    _pickColor);
                DisplaySettingsContent.Content = _displaySettingsTab;
            }

            if (GeneralSettingsContent != null)
            {
                _generalSettingsTab = new GeneralSettingsTab(
                    _settingsViewModel.Settings,
                    _plugin,
                    _logger,
                    JumpToTab);
                GeneralSettingsContent.Content = _generalSettingsTab;
            }

            // Initialize theme collections
            AvailableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            RevertableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            InitializeThemeMigrationCustomOptions();

            // Debug logging to verify DataContext and Settings values
            _logger?.Info($"SettingsControl created. DataContext type: {DataContext?.GetType().Name}");
            _logger?.Info($"Settings.EnablePeriodicUpdates: {_settingsViewModel.Settings.Persisted.EnablePeriodicUpdates}");

            Loaded += (s, e) =>
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

                // Navigate to a specific provider item if requested (e.g., from auth notification click).
                if (!string.IsNullOrWhiteSpace(PendingNavigationProviderKey) && ProvidersTab != null)
                {
                    SettingsTabControl.SelectedItem = ProvidersTab;
                    NavigateToPendingProvider();
                }
            };
        }

        // -----------------------------
        // Theme migration
        // -----------------------------

        private void LoadThemes(bool force = false)
        {
            if (_themeMigrationLoaded && !force)
            {
                return;
            }

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
                    _themeMigrationLoaded = true;
                    UpdateThemeMigrationState();
                    return;
                }

                var cache = _settingsViewModel?.Settings?.Persisted?.ThemeMigrationVersionCache;
                var themes = _themeDiscovery.DiscoverThemes(themesPath, cache);

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
                _themeMigrationLoaded = true;

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
            UpdateThemeMigrationModeButtonState();
        }

        private async void MigrateThemeLimited_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteThemeMigrationAsync(MigrationMode.Limited);
        }

        private async void MigrateThemeFull_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteThemeMigrationAsync(MigrationMode.Full);
        }

        private async void MigrateThemeCustom_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteThemeMigrationAsync(MigrationMode.Custom, BuildCustomMigrationSelection());
        }

        private void ThemeMigrationCustomExpander_Expanded(object sender, RoutedEventArgs e)
        {
            UpdateThemeMigrationModeButtonState();
        }

        private void ThemeMigrationCustomExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            UpdateThemeMigrationModeButtonState();
        }

        private void ThemeMigrationSetAllLegacy_Click(object sender, RoutedEventArgs e)
        {
            SetAllThemeMigrationCustomOptions(false);
        }

        private void ThemeMigrationSetAllModern_Click(object sender, RoutedEventArgs e)
        {
            SetAllThemeMigrationCustomOptions(true);
        }

        private async Task ExecuteThemeMigrationAsync(MigrationMode mode, CustomMigrationSelection customSelection = null)
        {
            if (string.IsNullOrWhiteSpace(SelectedThemePath))
            {
                _logger.Warn("Migrate clicked but no theme selected.");
                return;
            }

            _logger.Info($"User requested {mode} theme migration for: {SelectedThemePath}");

            try
            {
                var result = await _themeMigration.MigrateThemeAsync(SelectedThemePath, mode, customSelection);

                if (result.Success)
                {
                    _logger.Info($"Theme migration ({mode}) successful: {SelectedThemePath}");

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
                    LoadThemes(force: true);
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
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
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
                    LoadThemes(force: true);
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
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    L("LOCPlayAch_ThemeMigration_Revert", "Revert Theme"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InitializeThemeMigrationCustomOptions()
        {
            ThemeMigrationCustomOptions.Clear();

            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginButton",
                "LOCPlayAch_Settings_ButtonPreview",
                "Button"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginChart",
                "LOCPlayAch_Settings_BarChartPreview",
                "Bar Chart"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginCompactList",
                "LOCPlayAch_Settings_CompactListPreview",
                "Compact List"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginCompactLocked",
                "LOCPlayAch_Settings_CompactLockedListPreview",
                "Compact Locked List"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginCompactUnlocked",
                "LOCPlayAch_Settings_CompactUnlockedListPreview",
                "Compact Unlocked List"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginList",
                "LOCPlayAch_Settings_AchievementDataGridPreview",
                "Achievement DataGrid"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginProgressBar",
                "LOCPlayAch_Settings_ProgressBarPreview",
                "Progress Bar"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginUserStats",
                "LOCPlayAch_Settings_StatsPreview",
                "Stats"));
            ThemeMigrationCustomOptions.Add(CreateThemeMigrationControlOption(
                "PluginViewItem",
                "LOCPlayAch_Settings_ViewItemPreview",
                "View Item"));
        }

        private ThemeMigrationElementOption CreateThemeMigrationControlOption(
            string key,
            string resourceKey,
            string fallback)
        {
            return new ThemeMigrationElementOption(
                key,
                L(resourceKey, fallback),
                isBindingOption: false,
                isModern: true);
        }

        private static void OnSelectedThemePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsControl control)
            {
                control.UpdateThemeMigrationModeButtonState();
            }
        }

        private void UpdateThemeMigrationModeButtonState()
        {
            var isCustomExpanded = ThemeMigrationCustomExpander?.IsExpanded == true;
            var isFullscreenTheme = ThemeMigrationService.IsFullscreenThemePath(SelectedThemePath);

            if (isFullscreenTheme && isCustomExpanded)
            {
                ThemeMigrationCustomExpander.IsExpanded = false;
                isCustomExpanded = false;
            }

            if (ThemeMigrationPresetButtons != null)
            {
                ThemeMigrationPresetButtons.IsEnabled = HasThemesToMigrate && !isCustomExpanded;
            }

            if (ThemeMigrationFullButton != null)
            {
                ThemeMigrationFullButton.IsEnabled = HasThemesToMigrate && !isCustomExpanded && !isFullscreenTheme;
            }

            if (ThemeMigrationCustomContainer != null)
            {
                ThemeMigrationCustomContainer.IsEnabled = HasThemesToMigrate && !isFullscreenTheme;
            }
        }

        private CustomMigrationSelection BuildCustomMigrationSelection()
        {
            var modernControlNames = ThemeMigrationCustomOptions
                .Where(option => option.IsModern)
                .Select(option => option.Key)
                .ToList();

            return new CustomMigrationSelection(modernControlNames, modernizeBindings: true);
        }

        private void ThemeMigrationRowSetLegacy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: ThemeMigrationElementOption option })
            {
                option.IsModern = false;
            }
        }

        private void ThemeMigrationRowSetModern_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: ThemeMigrationElementOption option })
            {
                option.IsModern = true;
            }
        }

        private void SetAllThemeMigrationCustomOptions(bool isModern)
        {
            foreach (var option in ThemeMigrationCustomOptions)
            {
                option.IsModern = isModern;
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
                        L("LOCPlayAch_Status_Succeeded", "Success!"),
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

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Status_Succeeded", "Success!"),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to force hash index rebuild.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // -----------------------------
        // Provider Navigation Overview
        // -----------------------------

        /// <summary>
        /// Builds provider navigation items for the overview from registered providers.
        /// Items are added in the natural discovery order.
        /// </summary>
        private void BuildProviderNavigationItems(bool selectDefault = true)
        {
            if (_providerNavigationBuilt)
            {
                return;
            }

            ProviderNavigationItems.Clear();

            foreach (var providerKey in _providerRegistry.GetSettingsViewProviderKeys())
            {
                var settings = _providerRegistry.GetSettingsForEdit(providerKey);
                if (settings == null) continue;

                var displayName = ProviderRegistry.GetLocalizedName(providerKey);
                var iconKey = $"ProviderIcon{providerKey}";
                var colorHex = "#FF4084D6";
                string redirectProviderKey = null;
                string subtitle = null;
                Func<ProviderSettingsViewBase> settingsViewFactory = null;

                if (_providerRegistry.TryGetProviderVisuals(providerKey, out var providerIconKey, out var providerColorHex))
                {
                    if (!string.IsNullOrWhiteSpace(providerIconKey))
                    {
                        iconKey = providerIconKey;
                    }

                    if (!string.IsNullOrWhiteSpace(providerColorHex))
                    {
                        colorHex = providerColorHex;
                    }
                }

                if (ProviderUiPolicies.TryGetSettingsRedirectProviderKey(providerKey, out redirectProviderKey))
                {
                    subtitle = string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Settings_ProviderServicedByFormat"),
                        ProviderRegistry.GetLocalizedName(redirectProviderKey));
                }
                else
                {
                    settingsViewFactory = () => _providerRegistry.CreateSettingsView(providerKey);
                }

                ProviderNavigationItems.Add(new ProviderNavigationItem(
                    providerKey,
                    displayName,
                    GetProviderSettingsGroupName(providerKey),
                    iconKey,
                    colorHex,
                    settings,
                    settingsViewFactory,
                    redirectProviderKey,
                    subtitle));
            }

            if (selectDefault && SelectedProviderNavigationItem == null && ProviderNavigationItems.Count > 0)
            {
                SelectedProviderNavigationItem = ProviderNavigationItems[0];
            }

            _providerNavigationBuilt = true;
            _logger?.Info($"Built {ProviderNavigationItems.Count} platform navigation items");
        }

        private static string GetProviderSettingsGroupName(string providerKey)
        {
            return ResourceProvider.GetString(ProviderUiPolicies.GetSettingsGroupResourceKey(providerKey));
        }

        private void NavigateToPendingProvider()
        {
            var targetKey = PendingNavigationProviderKey;
            if (string.IsNullOrWhiteSpace(targetKey))
                return;

            BuildProviderNavigationItems(selectDefault: false);

            PendingNavigationProviderKey = null;

            // Find and select the provider navigation item matching the target key
            var targetItem = ProviderNavigationItems?.FirstOrDefault(x =>
                string.Equals(x.ProviderKey, targetKey, StringComparison.OrdinalIgnoreCase));

            if (targetItem != null)
            {
                // Ensure Providers tab is visible
                if (ProvidersTab != null)
                {
                    SettingsTabControl.SelectedItem = ProvidersTab;
                }

                SelectedProviderNavigationItem = targetItem;
                _logger?.Info($"Navigated to pending provider: {targetKey}");
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is TabControl)) return;

            // Inspect the newly selected TabItem by name (uses x:Name from XAML)
            if (e.AddedItems == null || e.AddedItems.Count == 0) return;
            if (e.AddedItems[0] is not TabItem selected) return;

            var name = selected.Name ?? string.Empty;

            if (string.Equals(name, "ThemeMigrationTab", StringComparison.OrdinalIgnoreCase))
            {
                LoadThemes();
                _logger?.Info("Loaded themes for Theme Migration tab.");
            }
            else if (string.Equals(name, "ProvidersTab", StringComparison.OrdinalIgnoreCase))
            {
                BuildProviderNavigationItems(selectDefault: string.IsNullOrWhiteSpace(PendingNavigationProviderKey));
                NavigateToPendingProvider();
            }
        }

        // Quick navigation callback used by the General tab's quick-link buttons
        private void JumpToTab(string tabKey)
        {
            TabItem tab;
            switch (tabKey)
            {
                case "Display":
                    tab = DisplayTab;
                    break;
                case "Providers":
                    tab = ProvidersTab;
                    break;
                case "ThemeMigration":
                    tab = ThemeMigrationTab;
                    break;
                default:
                    return;
            }

            if (tab == null)
            {
                return;
            }

            SettingsTabControl.SelectedItem = tab;
            if (ReferenceEquals(tab, ProvidersTab))
            {
                BuildProviderNavigationItems();
            }

            tab.BringIntoView();
        }

        // -----------------------------
        // IDisposable implementation
        // -----------------------------

        public void Dispose()
        {
            _displaySettingsTab?.Dispose();
            _generalSettingsTab?.Dispose();
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

    public sealed class ThemeMigrationElementOption : INotifyPropertyChanged
    {
        private bool _isModern;

        public ThemeMigrationElementOption(string key, string displayName, bool isBindingOption, bool isModern)
        {
            Key = key;
            DisplayName = displayName;
            IsBindingOption = isBindingOption;
            _isModern = isModern;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public bool IsBindingOption { get; }

        public bool IsModern
        {
            get => _isModern;
            set
            {
                if (_isModern != value)
                {
                    _isModern = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModern)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}





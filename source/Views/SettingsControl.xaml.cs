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
        private PersistedSettings _subscribedPersistedSettings;


        // -----------------------------
        // Toast preview / mockup
        // -----------------------------

        /// <summary>
        /// Rebuilds the inline toast mockup from the current persisted settings so the preview
        /// reflects appearance toggles (glow, rarity color, shown fields, badge colors) live.
        /// </summary>
        private void UpdateToastMockup()
        {
            if (ToastMockupHost == null)
            {
                return;
            }

            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            ToastMockupHost.ContentTemplate = _toastTemplateResolver.ResolveTemplate();
            ToastMockupHost.Content = new AchievementToastViewModel(BuildToastPreviewArgs("mockup"), persisted);
        }

        private void ShowToastPreview(AchievementUnlockedEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            PlayniteAchievementsPlugin.NotifyAchievementUnlocked(args);
        }

        private AchievementUnlockedEventArgs BuildToastPreviewArgs(string kind)
        {
            var sampleGame = L("LOCPlayAch_Settings_ToastPreviewSampleGame", "Sample Game");
            var sampleCategory = L("LOCPlayAch_Settings_ToastPreviewSampleCategory", "Sample Category");
            var sampleTitle = L("LOCPlayAch_Settings_ToastPreviewSampleTitle", "Example Achievement");
            var sampleDescription = L("LOCPlayAch_Settings_ToastPreviewSampleDescription", "An example achievement description.");

            switch (kind)
            {
                case "common":
                    return SampleUnlock("Common", 61.4, false);
                case "uncommon":
                    return SampleUnlock("Uncommon", 28.7, false);
                case "rare":
                    return SampleUnlock("Rare", 9.3, false);
                case "ultrarare":
                    return SampleUnlock("UltraRare", 1.8, false);
                case "capstone":
                    var capstone = SampleUnlock("UltraRare", 1.2, true);
                    capstone.GameCompleted = true;
                    return capstone;
                case "friend":
                    var friend = SampleUnlock("Rare", 7.5, false);
                    friend.IsFriendUnlock = true;
                    friend.FriendDisplayName = L("LOCPlayAch_Settings_ToastPreviewSampleFriend", "Friend");
                    friend.FriendAvatarUrl =
                        "pack://application:,,,/PlayniteAchievements;component/Resources/UnlockedAchIcon.png";
                    return friend;
                case "mockup":
                default:
                    return SampleUnlock("Rare", 9.3, false);
            }

            AchievementUnlockedEventArgs SampleUnlock(string rarity, double percent, bool capstone)
            {
                return new AchievementUnlockedEventArgs
                {
                    IsPreview = true,
                    GameName = sampleGame,
                    Category = sampleCategory,
                    DisplayName = sampleTitle,
                    Description = sampleDescription,
                    RarityTier = rarity,
                    GlobalPercent = percent,
                    IsCapstone = capstone,
                    UnlockedCount = 27,
                    TotalCount = 40
                };
            }
        }

        private void ToastPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: string rarity })
            {
                ShowToastPreview(BuildToastPreviewArgs(rarity));
            }
        }

        private void ScreenshotDirectory_Browse_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsViewModel?.Settings?.Persisted;
            if (settings == null)
            {
                return;
            }

            var selected = _plugin?.PlayniteApi?.Dialogs?.SelectFolder();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                settings.UnlockScreenshotDirectory = selected;
            }
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

        public static readonly DependencyProperty ViewAchievementsHotkeyButtonTextProperty =
            DependencyProperty.Register(
                nameof(ViewAchievementsHotkeyButtonText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string ViewAchievementsHotkeyButtonText
        {
            get => (string)GetValue(ViewAchievementsHotkeyButtonTextProperty);
            set => SetValue(ViewAchievementsHotkeyButtonTextProperty, value);
        }

        public static readonly DependencyProperty ManageAchievementsHotkeyButtonTextProperty =
            DependencyProperty.Register(
                nameof(ManageAchievementsHotkeyButtonText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string ManageAchievementsHotkeyButtonText
        {
            get => (string)GetValue(ManageAchievementsHotkeyButtonTextProperty);
            set => SetValue(ManageAchievementsHotkeyButtonTextProperty, value);
        }

        public static readonly DependencyProperty OverviewHotkeyButtonTextProperty =
            DependencyProperty.Register(
                nameof(OverviewHotkeyButtonText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string OverviewHotkeyButtonText
        {
            get => (string)GetValue(OverviewHotkeyButtonTextProperty);
            set => SetValue(OverviewHotkeyButtonTextProperty, value);
        }

        public static readonly DependencyProperty HotkeyCaptureStatusTextProperty =
            DependencyProperty.Register(
                nameof(HotkeyCaptureStatusText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string HotkeyCaptureStatusText
        {
            get => (string)GetValue(HotkeyCaptureStatusTextProperty);
            set => SetValue(HotkeyCaptureStatusTextProperty, value);
        }

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly ILogger _logger;
        private readonly ThemeDiscoveryService _themeDiscovery;
        private readonly ThemeMigrationService _themeMigration;
        private readonly ProviderRegistry _providerRegistry;
        private readonly Func<Window, string, string> _pickColor;
        private readonly AchievementToastTemplateResolver _toastTemplateResolver;
        private DisplaySettingsTab _displaySettingsTab;
        private bool _providerNavigationBuilt;
        private bool _themeMigrationLoaded;
        private HotkeyCaptureTarget? _capturingHotkey;
        private const string SuccessStoryExtensionId = "cebe6d32-8c46-4459-b993-5a5189d60788";
        private const string SuccessStoryFolderName = "SuccessStory";

        private enum HotkeyCaptureTarget
        {
            ViewAchievements,
            ManageAchievements,
            Overview
        }

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
            _toastTemplateResolver = new AchievementToastTemplateResolver(plugin.PlayniteApi, logger);

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

            // Initialize theme collections
            AvailableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            RevertableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            InitializeThemeMigrationCustomOptions();

            // Subscribe to settings property changes to refresh the toast mockup
            _settingsViewModel.Settings.PropertyChanged += OnSettingsObjectPropertyChanged;
            SubscribeToPersistedSettings(_settingsViewModel.Settings.Persisted);
            UpdateHotkeyButtonTexts();

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

                UpdateToastMockup();
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

        // -----------------------------
        // Cache actions
        // -----------------------------

        private void WipeCache_Click(object sender, RoutedEventArgs e)
        {
            string message = null;
            var image = MessageBoxImage.Information;
            Exception operationError = null;
            var progressText = L(
                "LOCPlayAch_Settings_Cache_ProgressClearing",
                "Clearing cached achievement data...");

            RunMaintenanceProgress(
                progressText,
                isIndeterminate: true,
                operation: progress =>
                {
                    try
                    {
                        _plugin.RefreshRuntime.Cache.ClearCache();
                        message = L("LOCPlayAch_Status_Succeeded", "Success!");
                        image = MessageBoxImage.Information;
                    }
                    catch (Exception ex)
                    {
                        operationError = ex;
                    }
                });

            if (operationError != null)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", operationError.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _plugin.PlayniteApi.Dialogs.ShowMessage(
                message ?? L("LOCPlayAch_Status_Succeeded", "Success!"),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                image);
        }

        private void ClearUnownedFriendGameData_Click(object sender, RoutedEventArgs e)
        {
            var friendCache = _plugin?.RefreshRuntime?.Cache as IFriendCacheManager;
            if (friendCache == null)
            {
                return;
            }

            try
            {
                var stats = friendCache.GetUnownedFriendGameCacheStats() ?? new FriendUnownedCacheStats();
                if (stats.Games <= 0 &&
                    stats.DefinitionStates <= 0 &&
                    stats.OwnershipRows <= 0 &&
                    stats.ProgressRows <= 0 &&
                    stats.AchievementRows <= 0 &&
                    stats.Definitions <= 0)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        L("LOCPlayAch_FriendsOverview_ClearUnowned_None", "No unowned friend game data is cached."),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var message = LF(
                    "LOCPlayAch_FriendsOverview_ClearUnowned_Confirm",
                    "Clear unowned friend game data?\n\nThis will delete {0:N0} provider-only games, {1:N0} achievement definitions, {2:N0} friend ownership rows, {3:N0} friend progress rows, {4:N0} achievement rows, and {5:N0} definition-state rows. Owned/shared friend data and your current-user cache will be kept.",
                    stats.Games,
                    stats.Definitions,
                    stats.OwnershipRows,
                    stats.ProgressRows,
                    stats.AchievementRows,
                    stats.DefinitionStates);

                if (_plugin.PlayniteApi.Dialogs.ShowMessage(
                        message,
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = friendCache.ClearUnownedFriendGameData();
                if (result?.Success != true)
                {
                    _plugin.PlayniteApi.Dialogs.ShowMessage(
                        LF("LOCPlayAch_Status_Failed", "Error: {0}", result?.ErrorMessage ?? "unknown"),
                        L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Remove every cached unowned cover/icon file in one pass.
                _plugin.ImageService?.ClearGameCache(FriendImageCacheFolders.Games);

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF(
                        "LOCPlayAch_FriendsOverview_ClearUnowned_Done",
                        "Cleared {0:N0} provider-only games and {1:N0} friend progress rows.",
                        result.Games,
                        result.ProgressRows),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to clear unowned friend game data.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearAllIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.All);

        private void ClearCompressedIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.CompressedOnly);

        private void ClearFullResolutionIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.FullResolutionOnly);

        private void ClearLockedIconCache_Click(object sender, RoutedEventArgs e) =>
            ClearIconCache(IconCacheClearScope.LockedOnly);

        private void ClearIconCache(IconCacheClearScope scope)
        {
            var fileLabel = ResourceProvider.GetString(GetIconCacheFileLabelResourceKey(scope));
            var scanningText = LF(
                "LOCPlayAch_Settings_IconCache_ProgressScanning",
                "Scanning cached {0} files...",
                fileLabel);
            var deletingTextFormat = L(
                "LOCPlayAch_Settings_IconCache_ProgressDeletingCount",
                "Deleting cached {0} files... ({1}/{2})");
            var deletedCount = 0;
            Exception operationError = null;

            RunMaintenanceProgress(
                scanningText,
                isIndeterminate: false,
                operation: progress =>
                {
                    try
                    {
                        UpdateMaintenanceProgress(progress, current: 0, max: 1);

                        IEnumerable<string> additionalPaths = null;
                        if (scope == IconCacheClearScope.LockedOnly)
                        {
                            additionalPaths = GetExplicitLockedIconCachePaths(progress);
                        }

                        _plugin.ImageService?.Clear();
                        deletedCount = _plugin.ImageService?.ClearDiskCache(
                            scope,
                            additionalPaths,
                            (processed, total) =>
                            {
                                var safeTotal = Math.Max(1, total);
                                var safeProcessed = total <= 0
                                    ? 1
                                    : Math.Max(0, Math.Min(total, processed));

                                var progressText = total <= 0
                                    ? LF(
                                        "LOCPlayAch_Settings_IconCache_ProgressNoFiles",
                                        "No cached {0} files were found.",
                                        fileLabel)
                                    : string.Format(
                                        deletingTextFormat,
                                        fileLabel,
                                        safeProcessed,
                                        total);

                                UpdateMaintenanceProgress(
                                    progress,
                                    text: progressText,
                                    current: safeProcessed,
                                    max: safeTotal);
                            }) ?? 0;
                    }
                    catch (Exception ex)
                    {
                        operationError = ex;
                    }
                });

            if (operationError != null)
            {
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", operationError.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var message = L("LOCPlayAch_Status_Succeeded", "Success!");

            _plugin.PlayniteApi.Dialogs.ShowMessage(
                message,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private string GetIconCacheFileLabelResourceKey(IconCacheClearScope scope)
        {
            switch (scope)
            {
                case IconCacheClearScope.CompressedOnly:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_Compressed";
                case IconCacheClearScope.FullResolutionOnly:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_FullResolution";
                case IconCacheClearScope.LockedOnly:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_Locked";
                default:
                    return "LOCPlayAch_Settings_IconCache_FileLabel_All";
            }
        }

        private void RunMaintenanceProgress(
            string initialText,
            bool isIndeterminate,
            Action<GlobalProgressActionArgs> operation)
        {
            var progressOptions = new GlobalProgressOptions(initialText)
            {
                Cancelable = false,
                IsIndeterminate = isIndeterminate
            };

            _plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(async progress =>
            {
                UpdateMaintenanceProgress(progress, text: initialText, isIndeterminate: isIndeterminate);
                await Task.Run(() => operation?.Invoke(progress)).ConfigureAwait(false);
            }, progressOptions);
        }

        private void UpdateMaintenanceProgress(
            GlobalProgressActionArgs progress,
            string text = null,
            int? current = null,
            int? max = null,
            bool? isIndeterminate = null)
        {
            if (progress == null)
            {
                return;
            }

            Action update = () =>
            {
                if (max.HasValue)
                {
                    progress.ProgressMaxValue = max.Value;
                }

                if (current.HasValue)
                {
                    progress.CurrentProgressValue = current.Value;
                }

                if (isIndeterminate.HasValue)
                {
                    progress.IsIndeterminate = isIndeterminate.Value;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    progress.Text = text;
                }
            };

            if (progress.MainDispatcher != null)
            {
                progress.MainDispatcher.InvokeIfNeeded(update);
            }
            else
            {
                update();
            }
        }

        private IEnumerable<string> GetExplicitLockedIconCachePaths(GlobalProgressActionArgs progress = null)
        {
            var dataService = _plugin?.AchievementDataService;
            var cachedGameIds = dataService?.GetCachedGameIds();
            if (cachedGameIds == null || cachedGameIds.Count == 0)
            {
                if (progress != null)
                {
                    UpdateMaintenanceProgress(
                        progress,
                        text: L(
                            "LOCPlayAch_Settings_IconCache_ProgressNoLockedReferences",
                            "No cached locked icon references were found."),
                        current: 1,
                        max: 1);
                }

                return Array.Empty<string>();
            }

            var lockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (progress != null)
            {
                UpdateMaintenanceProgress(progress, current: 0, max: cachedGameIds.Count);
            }

            for (var i = 0; i < cachedGameIds.Count; i++)
            {
                var gameId = cachedGameIds[i];
                if (progress != null)
                {
                    UpdateMaintenanceProgress(
                        progress,
                        text: LF(
                            "LOCPlayAch_Settings_IconCache_ProgressScanningLockedReferences",
                            "Scanning cached locked icon references... ({0}/{1})",
                            i + 1,
                            cachedGameIds.Count),
                        current: i + 1,
                        max: cachedGameIds.Count);
                }

                var gameData = dataService?.GetRawGameAchievementData(gameId);
                var achievements = gameData?.Achievements;
                if (achievements == null)
                {
                    continue;
                }

                foreach (var achievement in achievements)
                {
                    var lockedPath = achievement?.LockedIconPath;
                    if (!DiskImageService.IsLocalIconPath(lockedPath))
                    {
                        continue;
                    }

                    var unlockedPath = achievement?.UnlockedIconPath;
                    if (!string.IsNullOrWhiteSpace(unlockedPath) &&
                        string.Equals(lockedPath.Trim(), unlockedPath.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    lockedPaths.Add(lockedPath);
                }
            }

            return lockedPaths;
        }

        private void ResetFirstTimeSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info($"Resetting FirstTimeSetupCompleted. Current value before: {_settingsViewModel.Settings.Persisted.FirstTimeSetupCompleted}");

                _settingsViewModel.Settings.Persisted.FirstTimeSetupCompleted = false;

                _logger.Info($"Value after setting to false: {_settingsViewModel.Settings.Persisted.FirstTimeSetupCompleted}");

                _plugin.SavePluginSettings(_settingsViewModel.Settings);

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Status_Succeeded", "Success!"),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reset first-time setup.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
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
                var exportDir = _plugin.RefreshRuntime.Cache.ExportDatabaseToCsv(exportBaseDir);

                _logger.Info($"Database exported to: {exportDir}");

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Status_Succeeded", "Success!") + "\n" + exportDir,
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to export database.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataPath = _plugin.GetPluginUserDataPath();

                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = dataPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to open extension data folder.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
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
        // Tagging Methods
        // -----------------------------

        /// <summary>
        /// Commits pending tag name changes from text boxes to the source.
        /// </summary>
        private void CommitTagNameBindings()
        {
            var textBoxes = new TextBox[]
            {
                HasAchievementsTagTextBox,
                InProgressTagTextBox,
                CompletedTagTextBox,
                NoAchievementsTagTextBox,
                CustomizedTagTextBox,
                NotCustomizedTagTextBox,
                ExcludedTagTextBox,
                ExcludedFromSummariesTagTextBox
            };

            foreach (var textBox in textBoxes)
            {
                var binding = textBox?.GetBindingExpression(TextBox.TextProperty);
                if (binding != null)
                {
                    binding.UpdateSource();
                }
            }
        }

        private void ApplyAndSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Commit any pending text box changes
                CommitTagNameBindings();

                var tagSyncService = _plugin.TagSyncService;
                if (tagSyncService == null)
                {
                    _logger?.Warn("TagSyncService not available");
                    return;
                }

                // Sync all tags (handles orphan cleanup and re-tagging with new names)
                tagSyncService.SyncAllTags();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply and sync tags.");
                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    LF("LOCPlayAch_Status_Failed", "Error: {0}", ex.Message),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemoveAllTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Tagging_RemoveAllConfirm", "Remove all Playnite Achievements tags from all games?"),
                    L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                var tagSyncService = _plugin.TagSyncService;
                if (tagSyncService == null)
                {
                    _logger?.Warn("TagSyncService not available");
                    return;
                }

                tagSyncService.RemoveAllTags();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to remove tags.");
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

        // Quick navigation button handler from General tab
        private void JumpToTab_Click(object sender, RoutedEventArgs e)
        {
            TabItem tab;
            switch (sender is Button button ? button.CommandParameter as string : null)
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

        private void HotkeyCapture_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is string targetName &&
                Enum.TryParse(targetName, out HotkeyCaptureTarget target))
            {
                StartHotkeyCapture(target, button);
            }
        }

        private void ResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            if (!(sender is Button { CommandParameter: string targetName }) ||
                !Enum.TryParse(targetName, out HotkeyCaptureTarget target))
            {
                return;
            }

            switch (target)
            {
                case HotkeyCaptureTarget.ViewAchievements:
                    persisted.ViewAchievementsHotkey = PersistedSettings.DefaultViewAchievementsHotkey;
                    break;
                case HotkeyCaptureTarget.ManageAchievements:
                    persisted.ManageAchievementsHotkey = PersistedSettings.DefaultManageAchievementsHotkey;
                    break;
                case HotkeyCaptureTarget.Overview:
                    persisted.OverviewHotkey = PersistedSettings.DefaultOverviewHotkey;
                    break;
                default:
                    return;
            }

            EndHotkeyCapture();
        }

        private void HotkeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingHotkey.HasValue)
            {
                return;
            }

            var target = _capturingHotkey.Value;
            if ((target == HotkeyCaptureTarget.ViewAchievements &&
                 !ReferenceEquals(sender, ViewAchievementsHotkeyCaptureButton)) ||
                (target == HotkeyCaptureTarget.ManageAchievements &&
                 !ReferenceEquals(sender, ManageAchievementsHotkeyCaptureButton)) ||
                (target == HotkeyCaptureTarget.Overview &&
                 !ReferenceEquals(sender, OverviewHotkeyCaptureButton)))
            {
                return;
            }

            e.Handled = true;
            var key = GetEffectiveHotkeyCaptureKey(e);
            if (key == Key.Escape)
            {
                EndHotkeyCapture();
                return;
            }

            if (key == Key.Back || key == Key.Delete)
            {
                SetCapturedHotkey(target, string.Empty);
                EndHotkeyCapture();
                return;
            }

            if (!AchievementHotkeyGesture.TryCreate(key, Keyboard.Modifiers, out var gesture))
            {
                HotkeyCaptureStatusText = L(
                    "LOCPlayAch_Hotkeys_InvalidShortcut",
                    "Unsupported shortcut. Press a letter, digit, function key, or a modified shortcut.");
                return;
            }

            if (IsDuplicateHotkey(target, gesture))
            {
                HotkeyCaptureStatusText = L(
                    "LOCPlayAch_Hotkeys_DuplicateShortcut",
                    "That shortcut is already assigned.");
                return;
            }

            SetCapturedHotkey(target, gesture.ToString());
            EndHotkeyCapture();
        }

        private void StartHotkeyCapture(HotkeyCaptureTarget target, Button button)
        {
            _capturingHotkey = target;
            HotkeyCaptureStatusText = L("LOCPlayAch_Hotkeys_CapturePrompt", "Press a shortcut...");

            if (target == HotkeyCaptureTarget.ViewAchievements)
            {
                ViewAchievementsHotkeyButtonText = L("LOCPlayAch_Hotkeys_CaptureButton", "Press keys...");
                ManageAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settingsViewModel?.Settings?.Persisted?.ManageAchievementsHotkey);
                OverviewHotkeyButtonText = FormatHotkeyButtonText(
                    _settingsViewModel?.Settings?.Persisted?.OverviewHotkey);
            }
            else if (target == HotkeyCaptureTarget.ManageAchievements)
            {
                ManageAchievementsHotkeyButtonText = L("LOCPlayAch_Hotkeys_CaptureButton", "Press keys...");
                ViewAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settingsViewModel?.Settings?.Persisted?.ViewAchievementsHotkey);
                OverviewHotkeyButtonText = FormatHotkeyButtonText(
                    _settingsViewModel?.Settings?.Persisted?.OverviewHotkey);
            }
            else
            {
                OverviewHotkeyButtonText = L("LOCPlayAch_Hotkeys_CaptureButton", "Press keys...");
                ViewAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settingsViewModel?.Settings?.Persisted?.ViewAchievementsHotkey);
                ManageAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settingsViewModel?.Settings?.Persisted?.ManageAchievementsHotkey);
            }

            button?.Focus();
            Keyboard.Focus(button);
        }

        private void EndHotkeyCapture()
        {
            _capturingHotkey = null;
            HotkeyCaptureStatusText = string.Empty;
            UpdateHotkeyButtonTexts();
        }

        private void SetCapturedHotkey(HotkeyCaptureTarget target, string hotkey)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            if (target == HotkeyCaptureTarget.ViewAchievements)
            {
                persisted.ViewAchievementsHotkey = hotkey;
            }
            else if (target == HotkeyCaptureTarget.ManageAchievements)
            {
                persisted.ManageAchievementsHotkey = hotkey;
            }
            else
            {
                persisted.OverviewHotkey = hotkey;
            }
        }

        private bool IsDuplicateHotkey(HotkeyCaptureTarget target, AchievementHotkeyGesture gesture)
        {
            if (gesture == null || gesture.IsEmpty)
            {
                return false;
            }

            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return false;
            }

            return IsMatchingHotkey(target, HotkeyCaptureTarget.ViewAchievements, persisted.ViewAchievementsHotkey, gesture) ||
                   IsMatchingHotkey(target, HotkeyCaptureTarget.ManageAchievements, persisted.ManageAchievementsHotkey, gesture) ||
                   IsMatchingHotkey(target, HotkeyCaptureTarget.Overview, persisted.OverviewHotkey, gesture);
        }

        private static bool IsMatchingHotkey(
            HotkeyCaptureTarget currentTarget,
            HotkeyCaptureTarget comparedTarget,
            string comparedText,
            AchievementHotkeyGesture gesture)
        {
            if (currentTarget == comparedTarget)
            {
                return false;
            }

            return AchievementHotkeyGesture.TryParse(comparedText, out var otherGesture) &&
                   otherGesture != null &&
                   !otherGesture.IsEmpty &&
                   gesture.Equals(otherGesture);
        }

        private void UpdateHotkeyButtonTexts()
        {
            if (_capturingHotkey.HasValue)
            {
                return;
            }

            var persisted = _settingsViewModel?.Settings?.Persisted;
            ViewAchievementsHotkeyButtonText = FormatHotkeyButtonText(persisted?.ViewAchievementsHotkey);
            ManageAchievementsHotkeyButtonText = FormatHotkeyButtonText(persisted?.ManageAchievementsHotkey);
            OverviewHotkeyButtonText = FormatHotkeyButtonText(persisted?.OverviewHotkey);
        }

        private string FormatHotkeyButtonText(string hotkey)
        {
            return string.IsNullOrWhiteSpace(hotkey)
                ? L("LOCPlayAch_Hotkeys_None", "None")
                : hotkey;
        }

        private static Key GetEffectiveHotkeyCaptureKey(KeyEventArgs e)
        {
            if (e == null)
            {
                return Key.None;
            }

            if (e.Key == Key.System)
            {
                return e.SystemKey;
            }

            if (e.Key == Key.ImeProcessed)
            {
                return e.ImeProcessedKey;
            }

            return e.Key;
        }

        // -----------------------------
        // Settings property change handling for toast mockup refresh
        // -----------------------------

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Refresh the toast mockup when toast-affecting settings change
            var refreshProperties = new[]
            {
                nameof(Models.Settings.PersistedSettings.ShowCompactListRarityBar),
                nameof(Models.Settings.PersistedSettings.ShowHiddenIcon),
                nameof(Models.Settings.PersistedSettings.ShowHiddenTitle),
                nameof(Models.Settings.PersistedSettings.ShowHiddenDescription),
                nameof(Models.Settings.PersistedSettings.ShowHiddenSuffix),
                nameof(Models.Settings.PersistedSettings.ShowLockedIcon),
                nameof(Models.Settings.PersistedSettings.UseSeparateLockedIconsWhenAvailable),
                nameof(Models.Settings.PersistedSettings.UseUniformRarityBadges),
                nameof(Models.Settings.PersistedSettings.RarityColors),
                nameof(Models.Settings.PersistedSettings.ToastShowHeader),
                nameof(Models.Settings.PersistedSettings.ToastShowName),
                nameof(Models.Settings.PersistedSettings.ToastShowRarityBadge),
                nameof(Models.Settings.PersistedSettings.ToastShowRarityGlow),
                nameof(Models.Settings.PersistedSettings.ToastRarityColoredName),
                nameof(Models.Settings.PersistedSettings.ToastShowRarityPercent),
                nameof(Models.Settings.PersistedSettings.ToastShowDescription),
                nameof(Models.Settings.PersistedSettings.ToastShowCategory),
                nameof(Models.Settings.PersistedSettings.ToastShowGameName)
            };

            if (e.PropertyName == nameof(Models.Settings.PersistedSettings.ViewAchievementsHotkey) ||
                e.PropertyName == nameof(Models.Settings.PersistedSettings.ManageAchievementsHotkey) ||
                e.PropertyName == nameof(Models.Settings.PersistedSettings.OverviewHotkey))
            {
                UpdateHotkeyButtonTexts();
            }

            if (refreshProperties.Contains(e.PropertyName))
            {
                UpdateToastMockup();
            }
        }

        private void OnSettingsObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(PlayniteAchievementsSettings.Persisted), StringComparison.Ordinal))
            {
                return;
            }

            SubscribeToPersistedSettings(_settingsViewModel?.Settings?.Persisted);
            UpdateHotkeyButtonTexts();
            UpdateToastMockup();
        }

        private void SubscribeToPersistedSettings(PersistedSettings persisted)
        {
            if (ReferenceEquals(_subscribedPersistedSettings, persisted))
            {
                return;
            }

            if (_subscribedPersistedSettings != null)
            {
                _subscribedPersistedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            }

            _subscribedPersistedSettings = persisted;

            if (_subscribedPersistedSettings != null)
            {
                _subscribedPersistedSettings.PropertyChanged += OnSettingsPropertyChanged;
            }
        }

        // -----------------------------
        // IDisposable implementation
        // -----------------------------

        public void Dispose()
        {
            if (_settingsViewModel?.Settings != null)
            {
                _settingsViewModel.Settings.PropertyChanged -= OnSettingsObjectPropertyChanged;
            }

            SubscribeToPersistedSettings(null);
            _displaySettingsTab?.Dispose();
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





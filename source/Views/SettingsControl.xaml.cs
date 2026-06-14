// SettingsControl.xaml.cs
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Threading.Tasks;
using System.ComponentModel;
using PlayniteAchievements.Services;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Common;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.ThemeMigration;
using Playnite.SDK;
using System.Diagnostics;
using System.Windows.Navigation;

namespace PlayniteAchievements.Views
{
    public partial class SettingsControl : UserControl, IDisposable
    {
        // -----------------------------
        // Mock data for settings preview
        // -----------------------------

        private System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> _mockCompactListItems;

        /// <summary>
        /// Gets mock achievement items for compact list preview in settings.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> MockCompactListItems
        {
            get
            {
                if (_mockCompactListItems == null)
                {
                    _mockCompactListItems = new System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem>(
                        MockDataHelper.CreateMockCompactListItems(
                            GetShowRarityBar(), GetShowRarityGlow(),
                            GetShowHiddenIcon(), GetShowHiddenTitle(),
                            GetShowHiddenDescription(), GetShowHiddenSuffix(), GetShowLockedIcon()));
                }
                return _mockCompactListItems;
            }
        }

        private System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> _mockCompactUnlockedListItems;

        /// <summary>
        /// Gets mock unlocked achievement items for unlocked list preview.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> MockCompactUnlockedListItems
        {
            get
            {
                if (_mockCompactUnlockedListItems == null)
                {
                    _mockCompactUnlockedListItems = new System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem>(
                        MockDataHelper.CreateMockUnlockedListItems(
                            GetShowRarityBar(), GetShowRarityGlow(), GetShowLockedIcon()));
                }
                return _mockCompactUnlockedListItems;
            }
        }

        private System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> _mockCompactLockedListItems;

        /// <summary>
        /// Gets mock locked achievement items for locked list preview.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> MockCompactLockedListItems
        {
            get
            {
                if (_mockCompactLockedListItems == null)
                {
                    _mockCompactLockedListItems = new System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem>(
                        MockDataHelper.CreateMockLockedListItems(
                            GetShowRarityBar(), GetShowRarityGlow(),
                            GetShowHiddenIcon(), GetShowHiddenTitle(),
                            GetShowHiddenDescription(), GetShowHiddenSuffix(), GetShowLockedIcon()));
                }
                return _mockCompactLockedListItems;
            }
        }

        private List<AchievementDisplayItem> _mockDataGridItems;

        /// <summary>
        /// Gets mock achievement items for datagrid preview in settings.
        /// </summary>
        public List<AchievementDisplayItem> MockDataGridItems
        {
            get
            {
                if (_mockDataGridItems == null)
                {
                    _mockDataGridItems = MockDataHelper.CreateMockDataGridItems(
                        GetShowRarityBar(), GetShowRarityGlow(),
                        GetShowHiddenIcon(), GetShowHiddenTitle(),
                        GetShowHiddenDescription(), GetShowHiddenSuffix(), GetShowLockedIcon());
                }
                return _mockDataGridItems;
            }
        }

        private ModernThemeBindings _previewThemeData;
        private ModernThemeBindings _unlockedPreviewThemeData;
        private ModernThemeBindings _hiddenPreviewThemeData;
        private ModernThemeBindings _lockedPreviewThemeData;

        /// <summary>
        /// Gets modern theme bindings populated with mock achievements for modern control previews.
        /// Used by modern controls via ThemeDataOverride binding.
        /// </summary>
        public ModernThemeBindings PreviewThemeData
        {
            get
            {
                if (_previewThemeData == null)
                {
                    _previewThemeData = MockDataHelper.GetPreviewThemeData();
                }
                return _previewThemeData;
            }
        }

        /// <summary>
        /// Gets modern theme bindings with a single unlocked achievement for visibility preview.
        /// </summary>
        public ModernThemeBindings UnlockedPreviewThemeData
        {
            get
            {
                if (_unlockedPreviewThemeData == null)
                {
                    _unlockedPreviewThemeData = MockDataHelper.GetUnlockedPreviewThemeData();
                }
                return _unlockedPreviewThemeData;
            }
        }

        /// <summary>
        /// Gets modern theme bindings with a single hidden achievement for visibility preview.
        /// </summary>
        public ModernThemeBindings HiddenPreviewThemeData
        {
            get
            {
                if (_hiddenPreviewThemeData == null)
                {
                    _hiddenPreviewThemeData = MockDataHelper.GetHiddenPreviewThemeData();
                }
                return _hiddenPreviewThemeData;
            }
        }

        /// <summary>
        /// Gets modern theme bindings with a single locked achievement for visibility preview.
        /// </summary>
        public ModernThemeBindings LockedPreviewThemeData
        {
            get
            {
                if (_lockedPreviewThemeData == null)
                {
                    _lockedPreviewThemeData = MockDataHelper.GetLockedPreviewThemeData();
                }
                return _lockedPreviewThemeData;
            }
        }

        // Helper methods to get settings values with defaults
        private bool GetShowRarityBar() => _settingsViewModel?.Settings?.Persisted?.ShowCompactListRarityBar ?? true;
        private bool GetShowRarityGlow() => _settingsViewModel?.Settings?.Persisted?.ShowRarityGlow ?? true;
        private bool GetShowHiddenIcon() => _settingsViewModel?.Settings?.Persisted?.ShowHiddenIcon ?? true;
        private bool GetShowHiddenTitle() => _settingsViewModel?.Settings?.Persisted?.ShowHiddenTitle ?? true;
        private bool GetShowHiddenDescription() => _settingsViewModel?.Settings?.Persisted?.ShowHiddenDescription ?? true;
        private bool GetShowHiddenSuffix() => _settingsViewModel?.Settings?.Persisted?.ShowHiddenSuffix ?? true;
        private bool GetShowLockedIcon() => _settingsViewModel?.Settings?.Persisted?.ShowLockedIcon ?? true;

        /// <summary>
        /// Refreshes mock preview items to reflect current settings.
        /// Repopulates collections with new items that have updated visibility settings.
        /// </summary>
        public void RefreshMockPreviews()
        {
            var settings = _settingsViewModel?.Settings?.Persisted;
            if (settings == null) return;

            // Repopulate compact list items
            if (_mockCompactListItems != null)
            {
                _mockCompactListItems.Clear();
                var newItems = MockDataHelper.CreateMockCompactListItems(
                    settings.ShowCompactListRarityBar, settings.ShowRarityGlow,
                    settings.ShowHiddenIcon, settings.ShowHiddenTitle,
                    settings.ShowHiddenDescription, settings.ShowHiddenSuffix, settings.ShowLockedIcon);
                foreach (var item in newItems)
                    _mockCompactListItems.Add(item);
            }

            // Repopulate unlocked list items
            if (_mockCompactUnlockedListItems != null)
            {
                _mockCompactUnlockedListItems.Clear();
                var newItems = MockDataHelper.CreateMockUnlockedListItems(
                    settings.ShowCompactListRarityBar, settings.ShowRarityGlow, settings.ShowLockedIcon);
                foreach (var item in newItems)
                    _mockCompactUnlockedListItems.Add(item);
            }

            // Repopulate locked list items
            if (_mockCompactLockedListItems != null)
            {
                _mockCompactLockedListItems.Clear();
                var newItems = MockDataHelper.CreateMockLockedListItems(
                    settings.ShowCompactListRarityBar, settings.ShowRarityGlow,
                    settings.ShowHiddenIcon, settings.ShowHiddenTitle,
                    settings.ShowHiddenDescription, settings.ShowHiddenSuffix, settings.ShowLockedIcon);
                foreach (var item in newItems)
                    _mockCompactLockedListItems.Add(item);
            }

            // Repopulate datagrid items
            if (_mockDataGridItems != null)
            {
                _mockDataGridItems = MockDataHelper.CreateMockDataGridItems(
                    settings.ShowCompactListRarityBar, settings.ShowRarityGlow,
                    settings.ShowHiddenIcon, settings.ShowHiddenTitle,
                    settings.ShowHiddenDescription, settings.ShowHiddenSuffix, settings.ShowLockedIcon);
                // For List<T>, need to raise property changed - but since binding uses ItemsSource,
                // we'll assign a new list which triggers refresh
            }

            // Refresh the preview modern theme bindings used by modern controls
            _previewThemeData?.RefreshDisplayItems(
                settings.ShowHiddenIcon, settings.ShowHiddenTitle, settings.ShowHiddenDescription,
                settings.ShowHiddenSuffix, settings.ShowLockedIcon, settings.UseSeparateLockedIconsWhenAvailable, settings.ShowRarityGlow, settings.ShowCompactListRarityBar);
            _unlockedPreviewThemeData?.RefreshDisplayItems(
                settings.ShowHiddenIcon, settings.ShowHiddenTitle, settings.ShowHiddenDescription,
                settings.ShowHiddenSuffix, settings.ShowLockedIcon, settings.UseSeparateLockedIconsWhenAvailable, settings.ShowRarityGlow, settings.ShowCompactListRarityBar);
            _hiddenPreviewThemeData?.RefreshDisplayItems(
                settings.ShowHiddenIcon, settings.ShowHiddenTitle, settings.ShowHiddenDescription,
                settings.ShowHiddenSuffix, settings.ShowLockedIcon, settings.UseSeparateLockedIconsWhenAvailable, settings.ShowRarityGlow, settings.ShowCompactListRarityBar);
            _lockedPreviewThemeData?.RefreshDisplayItems(
                settings.ShowHiddenIcon, settings.ShowHiddenTitle, settings.ShowHiddenDescription,
                settings.ShowHiddenSuffix, settings.ShowLockedIcon, settings.UseSeparateLockedIconsWhenAvailable, settings.ShowRarityGlow, settings.ShowCompactListRarityBar);
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

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly ILogger _logger;
        private readonly ThemeDiscoveryService _themeDiscovery;
        private readonly ThemeMigrationService _themeMigration;
        private readonly ProviderRegistry _providerRegistry;
        private ICollectionView _providerNavigationView;
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
                new PropertyMetadata(null, OnSelectedProviderNavigationItemChanged));

        public ProviderNavigationItem SelectedProviderNavigationItem
        {
            get => (ProviderNavigationItem)GetValue(SelectedProviderNavigationItemProperty);
            set => SetValue(SelectedProviderNavigationItemProperty, value);
        }

        public static readonly DependencyProperty ProviderSearchTextProperty =
            DependencyProperty.Register(
                nameof(ProviderSearchText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty, OnProviderSearchTextChanged));

        public string ProviderSearchText
        {
            get => (string)GetValue(ProviderSearchTextProperty);
            set => SetValue(ProviderSearchTextProperty, value);
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
            ProviderRegistry providerRegistry)
        {
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));

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

            // Initialize theme collections
            AvailableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            RevertableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            InitializeThemeMigrationCustomOptions();

            // Subscribe to settings property changes to refresh mock previews
            _settingsViewModel.Settings.Persisted.PropertyChanged += OnSettingsPropertyChanged;

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

        private void ResetDisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info("Resetting Display tab settings to defaults.");

                _settingsViewModel.Settings.Persisted.ResetDisplaySettingsToDefaults();
                PercentRarityHelper.ApplyBadgeApplicationResources(
                    _settingsViewModel.Settings.Persisted.UseUniformRarityBadges);
                RefreshMockPreviews();
                _plugin.SavePluginSettings(_settingsViewModel.Settings);

                _plugin.PlayniteApi.Dialogs.ShowMessage(
                    L("LOCPlayAch_Status_Succeeded", "Success!"),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reset Display tab settings.");
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
        // Compact list sort direction toggles
        // -----------------------------

        private void ToggleCompactListSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.CompactListSortDescending = !persisted.CompactListSortDescending;
            }
        }

        private void ToggleCompactUnlockedListSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.CompactUnlockedListSortDescending = !persisted.CompactUnlockedListSortDescending;
            }
        }

        private void ToggleCompactLockedListSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.CompactLockedListSortDescending = !persisted.CompactLockedListSortDescending;
            }
        }

        private void ToggleOverviewGameSummariesGridSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.OverviewGameSummariesGridSortDescending = !persisted.OverviewGameSummariesGridSortDescending;
            }
        }

        private void ToggleStartPageGameSummariesGridSortDescending(object sender, RoutedEventArgs e)
        {
            var settings = _settingsViewModel?.Settings?.Persisted?.StartPageGameSummariesGrid;
            if (settings != null)
            {
                settings.SortDescending = !settings.SortDescending;
            }
        }

        private void ToggleOverviewSelectedGameGridSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.OverviewSelectedGameGridSortDescending = !persisted.OverviewSelectedGameGridSortDescending;
            }
        }

        private void ToggleSingleGameGridSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.SingleGameGridSortDescending = !persisted.SingleGameGridSortDescending;
            }
        }

        private void ToggleStartPageRecentUnlocksGridSortDescending(object sender, RoutedEventArgs e)
        {
            var settings = _settingsViewModel?.Settings?.Persisted?.StartPageRecentUnlocksGrid;
            if (settings != null)
            {
                settings.SortDescending = !settings.SortDescending;
            }
        }

        private void ToggleAchievementDataGridSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted != null)
            {
                persisted.AchievementDataGridSortDescending = !persisted.AchievementDataGridSortDescending;
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

            ConfigureProviderNavigationView();

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

        private void ConfigureProviderNavigationView()
        {
            _providerNavigationView = CollectionViewSource.GetDefaultView(ProviderNavigationItems);
            if (_providerNavigationView == null)
            {
                return;
            }

            _providerNavigationView.Filter = FilterProviderNavigationItem;
            _providerNavigationView.GroupDescriptions.Clear();
            _providerNavigationView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(ProviderNavigationItem.GroupName)));
            _providerNavigationView.Refresh();
        }

        private bool FilterProviderNavigationItem(object item)
        {
            if (!(item is ProviderNavigationItem providerItem))
            {
                return false;
            }

            var searchText = ProviderSearchText;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            return ContainsSearchText(providerItem.DisplayName, searchText) ||
                ContainsSearchText(providerItem.ProviderKey, searchText) ||
                ContainsSearchText(providerItem.GroupName, searchText) ||
                ContainsSearchText(providerItem.Subtitle, searchText);
        }

        private static bool ContainsSearchText(string value, string searchText)
            => !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void OnProviderSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsControl control)
            {
                control._providerNavigationView?.Refresh();
                control.SelectFirstVisibleProviderIfNeeded();
            }
        }

        private void SelectFirstVisibleProviderIfNeeded()
        {
            if (_providerNavigationView == null)
            {
                return;
            }

            if (SelectedProviderNavigationItem != null &&
                _providerNavigationView.Contains(SelectedProviderNavigationItem))
            {
                return;
            }

            SelectedProviderNavigationItem = _providerNavigationView
                .Cast<ProviderNavigationItem>()
                .FirstOrDefault();
        }

        private static void OnSelectedProviderNavigationItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsControl control && e.NewValue is ProviderNavigationItem item)
            {
                control.OnSelectedProviderNavigationItemChangedInternal(item);
            }
        }

        private void OnSelectedProviderNavigationItemChangedInternal(ProviderNavigationItem item)
        {
            if (item == null) return;

            if (item.IsRedirect)
            {
                var redirectItem = ProviderNavigationItems?.FirstOrDefault(x =>
                    string.Equals(x.ProviderKey, item.RedirectProviderKey, StringComparison.OrdinalIgnoreCase));

                if (redirectItem != null && !ReferenceEquals(item, redirectItem))
                {
                    ProviderSearchText = string.Empty;
                    SelectedProviderNavigationItem = redirectItem;
                    _logger?.Info($"Redirected {item.ProviderKey} settings navigation to {redirectItem.ProviderKey}");
                }
                else
                {
                    _logger?.Warn($"Provider settings redirect target not found for {item.ProviderKey}: {item.RedirectProviderKey}");
                }

                return;
            }

            var settingsView = item.EnsureSettingsView();
            if (settingsView == null) return;
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

        // Quick navigation button handlers from General tab
        private void JumpToDisplay_Click(object sender, RoutedEventArgs e)
        {
            if (DisplayTab != null)
            {
                SettingsTabControl.SelectedItem = DisplayTab;
                DisplayTab.BringIntoView();
            }
        }

        private void JumpToProviders_Click(object sender, RoutedEventArgs e)
        {
            if (ProvidersTab != null)
            {
                SettingsTabControl.SelectedItem = ProvidersTab;
                BuildProviderNavigationItems();
                ProvidersTab.BringIntoView();
            }
        }

        private void JumpToThemeMigration_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeMigrationTab != null)
            {
                SettingsTabControl.SelectedItem = ThemeMigrationTab;
                ThemeMigrationTab.BringIntoView();
            }
        }

        // -----------------------------
        // Settings property change handling for mock preview refresh
        // -----------------------------

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Refresh mock previews when display-affecting settings change
            var refreshProperties = new[]
            {
                nameof(Models.Settings.PersistedSettings.ShowCompactListRarityBar),
                nameof(Models.Settings.PersistedSettings.ShowRarityGlow),
                nameof(Models.Settings.PersistedSettings.ShowHiddenIcon),
                nameof(Models.Settings.PersistedSettings.ShowHiddenTitle),
                nameof(Models.Settings.PersistedSettings.ShowHiddenDescription),
                nameof(Models.Settings.PersistedSettings.ShowHiddenSuffix),
                nameof(Models.Settings.PersistedSettings.ShowLockedIcon),
                nameof(Models.Settings.PersistedSettings.UseSeparateLockedIconsWhenAvailable),
                nameof(Models.Settings.PersistedSettings.UseUniformRarityBadges)
            };

            if (e.PropertyName == nameof(Models.Settings.PersistedSettings.UseUniformRarityBadges))
            {
                PercentRarityHelper.ApplyBadgeApplicationResources(
                    _settingsViewModel?.Settings?.Persisted?.UseUniformRarityBadges ?? false);
            }

            if (refreshProperties.Contains(e.PropertyName))
            {
                RefreshMockPreviews();
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

    /// <summary>
    /// Represents a provider item in the settings Providers overview navigation.
    /// </summary>
    public sealed class ProviderNavigationItem : PlayniteAchievements.Common.ObservableObject
    {
        private readonly IProviderSettings _settings;
        private readonly Func<ProviderSettingsViewBase> _settingsViewFactory;
        private ProviderSettingsViewBase _settingsView;

        public ProviderNavigationItem(
            string providerKey,
            string displayName,
            string groupName,
            string providerIconKey,
            string providerColorHex,
            IProviderSettings settings,
            Func<ProviderSettingsViewBase> settingsViewFactory,
            string redirectProviderKey = null,
            string subtitle = null)
        {
            ProviderKey = providerKey;
            DisplayName = displayName;
            GroupName = groupName;
            ProviderIconKey = providerIconKey;
            ProviderColorHex = providerColorHex;
            _settingsViewFactory = settingsViewFactory;
            RedirectProviderKey = redirectProviderKey;
            Subtitle = subtitle;
            _settings = settings;

            if (_settings != null)
            {
                _settings.PropertyChanged += Settings_PropertyChanged;
            }
        }

        public string ProviderKey { get; }
        public string DisplayName { get; }
        public string GroupName { get; }
        public string ProviderIconKey { get; }
        public string ProviderColorHex { get; }
        public string RedirectProviderKey { get; }
        public string Subtitle { get; }
        public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
        public bool IsRedirect => !string.IsNullOrWhiteSpace(RedirectProviderKey);
        public bool IsEnabled => _settings?.IsEnabled ?? true;
        public ProviderSettingsViewBase SettingsView => _settingsView;

        public ProviderSettingsViewBase EnsureSettingsView()
        {
            if (IsRedirect || _settingsView != null)
            {
                return _settingsView;
            }

            var view = _settingsViewFactory?.Invoke();
            if (view != null)
            {
                view.Initialize(_settings);
                _settingsView = view;
                OnPropertyChanged(nameof(SettingsView));
            }

            return _settingsView;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                string.Equals(e.PropertyName, nameof(IProviderSettings.IsEnabled), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(IsEnabled));
            }
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





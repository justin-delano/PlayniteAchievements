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
using PlayniteAchievements.Views.Helpers;
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
        // -----------------------------
        // Mock data for settings preview
        // -----------------------------

        private System.Collections.ObjectModel.ObservableCollection<AchievementDisplayItem> _mockCompactListItems;
        private ObservableCollection<ResourceAppearanceItem> _resourceAppearanceItems;
        private ObservableCollection<RarityAppearanceItem> _rarityAppearanceItems;
        private ObservableCollection<CompletedBadgeAppearanceItem> _completedBadgeAppearanceItems;
        private ObservableCollection<TrophyAppearanceItem> _trophyAppearanceItems;
        private ObservableCollection<RarityPalettePreset> _rarityPalettePresets;
        private PersistedSettings _subscribedPersistedSettings;

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
                            GetShowRarityBar(),
                            GetShowHiddenIcon(), GetShowHiddenTitle(),
                            GetShowHiddenDescription(), GetShowHiddenSuffix(), GetShowLockedIcon()));
                }
                return _mockCompactListItems;
            }
        }

        public ObservableCollection<ResourceAppearanceItem> ResourceAppearanceItems
        {
            get
            {
                if (_resourceAppearanceItems == null)
                {
                    _resourceAppearanceItems = new ObservableCollection<ResourceAppearanceItem>(
                        PlayAchResourceService.ResourceDescriptors.Select(descriptor =>
                            new ResourceAppearanceItem(
                                descriptor,
                                _settingsViewModel.Settings.Persisted,
                                ApplyResourceAppearanceOverrides)));
                }

                return _resourceAppearanceItems;
            }
        }

        public ObservableCollection<RarityAppearanceItem> RarityAppearanceItems
        {
            get
            {
                if (_rarityAppearanceItems == null)
                {
                    _rarityAppearanceItems = new ObservableCollection<RarityAppearanceItem>
                    {
                        new RarityAppearanceItem(RarityTier.Common, _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new RarityAppearanceItem(RarityTier.Uncommon, _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new RarityAppearanceItem(RarityTier.Rare, _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new RarityAppearanceItem(RarityTier.UltraRare, _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides)
                    };
                }

                return _rarityAppearanceItems;
            }
        }

        public ObservableCollection<CompletedBadgeAppearanceItem> CompletedBadgeAppearanceItems
        {
            get
            {
                if (_completedBadgeAppearanceItems == null)
                {
                    _completedBadgeAppearanceItems = new ObservableCollection<CompletedBadgeAppearanceItem>
                    {
                        new CompletedBadgeAppearanceItem("Gradient start", true, _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new CompletedBadgeAppearanceItem("Gradient end", false, _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides)
                    };
                }

                return _completedBadgeAppearanceItems;
            }
        }

        public ObservableCollection<TrophyAppearanceItem> TrophyAppearanceItems
        {
            get
            {
                if (_trophyAppearanceItems == null)
                {
                    _trophyAppearanceItems = new ObservableCollection<TrophyAppearanceItem>
                    {
                        new TrophyAppearanceItem("Bronze", "TrophyBronze", _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new TrophyAppearanceItem("Silver", "TrophySilver", _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new TrophyAppearanceItem("Gold", "TrophyGold", _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides),
                        new TrophyAppearanceItem("Platinum", "TrophyPlatinum", _settingsViewModel.Settings.Persisted, ApplyRarityAppearanceOverrides)
                    };
                }

                return _trophyAppearanceItems;
            }
        }

        public ObservableCollection<RarityPalettePreset> RarityPalettePresets
        {
            get
            {
                if (_rarityPalettePresets == null)
                {
                    _rarityPalettePresets = new ObservableCollection<RarityPalettePreset>(CreateRarityPalettePresets());
                }

                return _rarityPalettePresets;
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
                            GetShowRarityBar(), GetShowLockedIcon()));
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
                            GetShowRarityBar(),
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
                        GetShowRarityBar(),
                        GetShowHiddenIcon(), GetShowHiddenTitle(),
                        GetShowHiddenDescription(), GetShowHiddenSuffix(), GetShowLockedIcon());
                }
                return _mockDataGridItems;
            }
        }

        private ModernThemeBindings _previewThemeData;
        private ModernThemeBindings _achievementVisibilityPreviewThemeData;

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
        /// Gets modern theme bindings with locked and hidden achievements for visibility preview.
        /// </summary>
        public ModernThemeBindings AchievementVisibilityPreviewThemeData
        {
            get
            {
                if (_achievementVisibilityPreviewThemeData == null)
                {
                    _achievementVisibilityPreviewThemeData = MockDataHelper.GetAchievementVisibilityPreviewThemeData();
                }
                return _achievementVisibilityPreviewThemeData;
            }
        }

        // Helper methods to get settings values with defaults
        private bool GetShowRarityBar() => _settingsViewModel?.Settings?.Persisted?.ShowCompactListRarityBar ?? true;
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
                    settings.ShowCompactListRarityBar,
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
                    settings.ShowCompactListRarityBar, settings.ShowLockedIcon);
                foreach (var item in newItems)
                    _mockCompactUnlockedListItems.Add(item);
            }

            // Repopulate locked list items
            if (_mockCompactLockedListItems != null)
            {
                _mockCompactLockedListItems.Clear();
                var newItems = MockDataHelper.CreateMockLockedListItems(
                    settings.ShowCompactListRarityBar,
                    settings.ShowHiddenIcon, settings.ShowHiddenTitle,
                    settings.ShowHiddenDescription, settings.ShowHiddenSuffix, settings.ShowLockedIcon);
                foreach (var item in newItems)
                    _mockCompactLockedListItems.Add(item);
            }

            // Repopulate datagrid items
            if (_mockDataGridItems != null)
            {
                _mockDataGridItems = MockDataHelper.CreateMockDataGridItems(
                    settings.ShowCompactListRarityBar,
                    settings.ShowHiddenIcon, settings.ShowHiddenTitle,
                    settings.ShowHiddenDescription, settings.ShowHiddenSuffix, settings.ShowLockedIcon);
                // For List<T>, need to raise property changed - but since binding uses ItemsSource,
                // we'll assign a new list which triggers refresh
            }

            // Refresh the preview modern theme bindings used by modern controls
            _previewThemeData?.RefreshDisplayItems(
                settings.ShowHiddenIcon, settings.ShowHiddenTitle, settings.ShowHiddenDescription,
                settings.ShowHiddenSuffix, settings.ShowLockedIcon, settings.UseSeparateLockedIconsWhenAvailable, settings.ShowCompactListRarityBar);
            _achievementVisibilityPreviewThemeData?.RefreshDisplayItems(
                settings.ShowHiddenIcon, settings.ShowHiddenTitle, settings.ShowHiddenDescription,
                settings.ShowHiddenSuffix, settings.ShowLockedIcon, settings.UseSeparateLockedIconsWhenAvailable, settings.ShowCompactListRarityBar);

            UpdateToastMockup();
        }

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

        public static readonly DependencyProperty StartPageActivityScopeTextProperty =
            DependencyProperty.Register(
                nameof(StartPageActivityScopeText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string StartPageActivityScopeText
        {
            get => (string)GetValue(StartPageActivityScopeTextProperty);
            set => SetValue(StartPageActivityScopeTextProperty, value);
        }

        public static readonly DependencyProperty StartPageProgressScopeTextProperty =
            DependencyProperty.Register(
                nameof(StartPageProgressScopeText),
                typeof(string),
                typeof(SettingsControl),
                new PropertyMetadata(string.Empty));

        public string StartPageProgressScopeText
        {
            get => (string)GetValue(StartPageProgressScopeTextProperty);
            set => SetValue(StartPageProgressScopeTextProperty, value);
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
        private ICollectionView _providerNavigationView;
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

            // Initialize theme collections
            AvailableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            RevertableThemes = new System.Collections.ObjectModel.ObservableCollection<ThemeDiscoveryService.ThemeInfo>();
            InitializeThemeMigrationCustomOptions();

            // Subscribe to settings property changes to refresh mock previews
            _settingsViewModel.Settings.PropertyChanged += OnSettingsObjectPropertyChanged;
            SubscribeToPersistedSettings(_settingsViewModel.Settings.Persisted);
            UpdateStartPageScopeTexts();
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

        private void ResetDisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info("Resetting Display tab settings to defaults.");

                _settingsViewModel.Settings.Persisted.ResetDisplaySettingsToDefaults();
                RefreshAppearanceEditorFromPersisted();
                RefreshMockPreviews();

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

        private void PickResourceColor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ResourceAppearanceItem item ||
                !item.IsBrush)
            {
                return;
            }

            var color = _pickColor(Window.GetWindow(this), item.CustomValue);
            if (!string.IsNullOrEmpty(color))
            {
                item.Mode = ResourceOverrideMode.Custom;
                item.CustomValue = color;
            }
        }

        private void PickRarityColor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RarityAppearanceItem rarityItem)
            {
                PickPaletteColor(
                    rarityItem.BaseColor,
                    color =>
                    {
                        rarityItem.BaseColor = color;
                    });
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is CompletedBadgeAppearanceItem completedItem)
            {
                PickPaletteColor(
                    completedItem.BaseColor,
                    color =>
                    {
                        completedItem.BaseColor = color;
                    });
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is TrophyAppearanceItem trophyItem)
            {
                PickPaletteColor(
                    trophyItem.BaseColor,
                    color =>
                    {
                        trophyItem.BaseColor = color;
                    });
            }
        }

        private void ResetRarityColor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RarityAppearanceItem rarityItem)
            {
                rarityItem.Reset();
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is CompletedBadgeAppearanceItem completedItem)
            {
                completedItem.Reset();
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is TrophyAppearanceItem trophyItem)
            {
                trophyItem.Reset();
            }
        }

        private void ApplySelectedRarityPalettePreset_Click(object sender, RoutedEventArgs e)
        {
            if (RarityPalettePresetComboBox?.SelectedItem is RarityPalettePreset preset)
            {
                ApplyRarityPalette(preset);
            }
        }

        private void ResetAllRarityColors_Click(object sender, RoutedEventArgs e)
        {
            ApplyRarityPalette(new RarityPalettePreset("Default", RarityColorSettings.CreateDefault(), null));
        }

        private void ApplyRarityPalette(RarityPalettePreset preset)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null || preset?.Colors == null)
            {
                return;
            }

            persisted.RarityColors = preset.Colors.Clone();
            persisted.ResourceOverrides = preset.ResourceBrushes != null
                ? CreateResourceOverrideSettings(preset.ResourceBrushes)
                : PersistedSettings.CreateDefaultResourceOverrides();
            RefreshAppearanceEditorFromPersisted();
        }

        private static Dictionary<string, ResourceOverrideSetting> CreateResourceOverrideSettings(
            IReadOnlyDictionary<string, string> brushes)
        {
            var overrides = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase);
            if (brushes == null)
            {
                return overrides;
            }

            foreach (var pair in brushes)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                overrides[pair.Key] = new ResourceOverrideSetting
                {
                    Mode = ResourceOverrideMode.Custom,
                    CustomValue = pair.Value.Trim()
                };
            }

            return overrides;
        }

        private static IReadOnlyList<RarityPalettePreset> CreateRarityPalettePresets()
        {
            var presets = new[]
            {
                Preset("Default",
                    RarityColorSettings.DefaultCommon,
                    RarityColorSettings.DefaultUncommon,
                    RarityColorSettings.DefaultRare,
                    RarityColorSettings.DefaultUltraRare,
                    RarityColorSettings.DefaultCompletedStart,
                    RarityColorSettings.DefaultCompletedEnd),

                Preset("Emerald Forest",     "#53633A", "#43A047", "#00875A", "#5E2B97", "#A3E635", "#FDE68A"),
                Preset("Abyssal Ocean",      "#40545A", "#168AAD", "#1D4ED8", "#312E81", "#22D3EE", "#BAE6FD"),
                Preset("Desert Mirage",      "#B08D57", "#2A9D8F", "#E76F51", "#B5179E", "#F4D35E", "#FF9F1C"),
                Preset("Frozen Aurora",      "#9FB3C8", "#67E8F9", "#60A5FA", "#7C3AED", "#34D399", "#F0ABFC"),
                Preset("Volcano Core",       "#5C4033", "#D94A1E", "#F97316", "#7F1D1D", "#FACC15", "#EF4444"),

                Preset("Rose Quartz",        "#9A8C98", "#F4A7B9", "#E85D75", "#6D2E46", "#FFB4A2", "#FFF0E6"),
                Preset("Bone Crypt",         "#A8A29E", "#556B2F", "#A44A3F", "#3B0764", "#F2D492", "#FFF8DC"),
                Preset("Neon City",          "#263238", "#00E5FF", "#FFEA00", "#FF1744", "#AA00FF", "#FF6D00"),
                Preset("Cosmic Nebula",      "#111827", "#14B8A6", "#4F46E5", "#C026D3", "#FB7185", "#22D3EE"),
                Preset("Candy Shop",         "#A7C957", "#7BDFF2", "#FFCB77", "#FF5D8F", "#B388EB", "#FFD6A5"),

                Preset("Noir Spotlight",     "#2F3437", "#9CA3AF", "#F4D35E", "#E63946", "#FFF7C2", "#F72585"),
                Preset("Royal Masquerade",   "#53354A", "#2E8B57", "#1D4ED8", "#C1121F", "#F4D35E", "#FFF1A8"),
                Preset("Autumn Court",       "#5C4033", "#606C38", "#BC6C25", "#780000", "#DDA15E", "#FFD166"),
                Preset("Stormbreaker",       "#4B5563", "#94A3B8", "#FACC15", "#1D4ED8", "#F8FAFC", "#FDE047"),
                Preset("Tidal Gold",         "#B08968", "#84DCC6", "#05668D", "#7B2CBF", "#F4D35E", "#FFF3B0"),

                Preset("Industrial Rust",    "#59636B", "#A65E2E", "#C49A2C", "#1565C0", "#F97316", "#FFE082"),
                Preset("Radioactive Lab",    "#3D3D29", "#A3E635", "#D9F99D", "#7C3AED", "#CCFF00", "#F5FF00"),
                Preset("Sakura Night",       "#353535", "#FFB3C6", "#FF8C42", "#5A189A", "#FFC2D1", "#F8F7FF"),
                Preset("Paper Lantern",      "#8D6E63", "#7CB342", "#E53935", "#3949AB", "#FFD166", "#FFF3B0"),
                Preset("Prismatic Crystal",  "#607D8B", "#4DD0E1", "#7E57C2", "#EC407A", "#B2EBF2", "#FFFFFF")
            };

            for (int i = 0; i < presets.Length; i++)
            {
                presets[i].DisplayLabel = i == 0
                    ? ResourceProvider.GetString("LOCPlayAch_Common_Default")
                    : string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Settings_Appearance_PresetNumbered"),
                        i);
            }

            return presets;
        }

        private static RarityPalettePreset Preset(
            string name,
            string common,
            string uncommon,
            string rare,
            string ultraRare,
            string completedStart,
            string completedEnd)
        {
            return new RarityPalettePreset(
                name,
                new RarityColorSettings
                {
                    Common = common,
                    Uncommon = uncommon,
                    Rare = rare,
                    UltraRare = ultraRare,
                    CompletedStart = completedStart,
                    CompletedEnd = completedEnd,
                    TrophyBronze = common,
                    TrophySilver = uncommon,
                    TrophyGold = rare,
                    TrophyPlatinum = ultraRare
                },
                string.Equals(name, "Default", StringComparison.Ordinal)
                    ? null
                    : CreatePresetResourceBrushes(common, uncommon, rare, ultraRare, completedStart, completedEnd));
        }

        private static IReadOnlyDictionary<string, string> CreatePresetResourceBrushes(
            string common,
            string uncommon,
            string rare,
            string ultraRare,
            string completedStart,
            string completedEnd)
        {
            var baseSurface = "#FF0D1018";
            var basePanel = "#FF141925";
            var baseStrong = "#FF070912";

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PlayAch.Brush.Text"] = "#F5F7FB",
                ["PlayAch.Brush.Text.Secondary"] = "#C4CBD8",
                ["PlayAch.Brush.Text.Tertiary"] = "#8792A3",
                ["PlayAch.Brush.WindowSurface"] = BlendColorText(baseStrong, ultraRare, 0.06),
                ["PlayAch.Brush.Surface"] = BlendColorText(baseSurface, common, 0.10),
                ["PlayAch.Brush.GridSurface"] = BlendColorText(baseSurface, rare, 0.10),
                ["PlayAch.Brush.Panel"] = BlendColorText(basePanel, rare, 0.12),
                ["PlayAch.Brush.Border"] = WithAlpha(common, 0xCC),
                ["PlayAch.Brush.ControlBorder"] = WithAlpha(rare, 0xD8),
                ["PlayAch.Brush.Glyph"] = WithAlpha(uncommon, 0xF0),
                ["PlayAch.Brush.Accent"] = WithAlpha(ultraRare, 0xFF),
                ["PlayAch.Brush.Selection"] = WithAlpha(completedStart, 0xFF),
                ["PlayAch.Brush.ControlSurface"] = BlendColorText(basePanel, uncommon, 0.18),
                ["PlayAch.Brush.PopupSurface"] = BlendColorText(basePanel, ultraRare, 0.16),
                ["PlayAch.Brush.PopupBorder"] = WithAlpha(completedEnd, 0xD8)
            };
        }

        private static string BlendColorText(string from, string to, double amount)
        {
            if (!TryParseColor(from, out var fromColor) ||
                !TryParseColor(to, out var toColor))
            {
                return from;
            }

            amount = Math.Max(0, Math.Min(1, amount));
            return ColorToText(Color.FromArgb(
                0xFF,
                (byte)Math.Round(fromColor.R + ((toColor.R - fromColor.R) * amount)),
                (byte)Math.Round(fromColor.G + ((toColor.G - fromColor.G) * amount)),
                (byte)Math.Round(fromColor.B + ((toColor.B - fromColor.B) * amount))));
        }

        private static string WithAlpha(string value, byte alpha)
        {
            return TryParseColor(value, out var color)
                ? ColorToText(Color.FromArgb(alpha, color.R, color.G, color.B))
                : value;
        }

        private static string ColorToText(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private void PickPaletteColor(string currentValue, Action<string> applyColor)
        {
            var color = _pickColor(Window.GetWindow(this), currentValue);
            if (!string.IsNullOrEmpty(color))
            {
                applyColor?.Invoke(color);
            }
        }

        private void RebuildResourceAppearanceItems()
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            var items = ResourceAppearanceItems;
            items.Clear();
            foreach (var descriptor in PlayAchResourceService.ResourceDescriptors)
            {
                items.Add(new ResourceAppearanceItem(
                    descriptor,
                    persisted,
                    ApplyResourceAppearanceOverrides));
            }
        }

        private void RebuildRarityAppearanceItems()
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            var items = RarityAppearanceItems;
            items.Clear();
            items.Add(new RarityAppearanceItem(RarityTier.Common, persisted, ApplyRarityAppearanceOverrides));
            items.Add(new RarityAppearanceItem(RarityTier.Uncommon, persisted, ApplyRarityAppearanceOverrides));
            items.Add(new RarityAppearanceItem(RarityTier.Rare, persisted, ApplyRarityAppearanceOverrides));
            items.Add(new RarityAppearanceItem(RarityTier.UltraRare, persisted, ApplyRarityAppearanceOverrides));
        }

        private void RebuildCompletedBadgeAppearanceItems()
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            var items = CompletedBadgeAppearanceItems;
            items.Clear();
            items.Add(new CompletedBadgeAppearanceItem("Gradient start", true, persisted, ApplyRarityAppearanceOverrides));
            items.Add(new CompletedBadgeAppearanceItem("Gradient end", false, persisted, ApplyRarityAppearanceOverrides));
        }

        private void RebuildTrophyAppearanceItems()
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            var items = TrophyAppearanceItems;
            items.Clear();
            items.Add(new TrophyAppearanceItem("Bronze", "TrophyBronze", persisted, ApplyRarityAppearanceOverrides));
            items.Add(new TrophyAppearanceItem("Silver", "TrophySilver", persisted, ApplyRarityAppearanceOverrides));
            items.Add(new TrophyAppearanceItem("Gold", "TrophyGold", persisted, ApplyRarityAppearanceOverrides));
            items.Add(new TrophyAppearanceItem("Platinum", "TrophyPlatinum", persisted, ApplyRarityAppearanceOverrides));
        }

        private void RefreshAppearanceEditorFromPersisted()
        {
            RebuildResourceAppearanceItems();
            RebuildRarityAppearanceItems();
            RebuildCompletedBadgeAppearanceItems();
            RebuildTrophyAppearanceItems();
            ApplyResourceAppearanceOverrides();
            ApplyRarityAppearanceOverrides();
        }

        private void ApplyResourceAppearanceOverrides()
        {
            var resources = Application.Current?.Resources;
            if (resources != null)
            {
                PlayAchResourceService.Apply(
                    resources,
                    _settingsViewModel?.Settings?.Persisted?.ResourceOverrides);
            }
        }

        private void ApplyRarityAppearanceOverrides()
        {
            RarityAppearanceHelper.ApplyBadgeApplicationResources(
                _settingsViewModel?.Settings?.Persisted);
            RefreshRarityAppearanceItems();
        }

        private void RefreshRarityAppearanceItems()
        {
            if (_rarityAppearanceItems != null)
            {
                foreach (var item in _rarityAppearanceItems)
                {
                    item.Refresh();
                }
            }

            if (_completedBadgeAppearanceItems != null)
            {
                foreach (var item in _completedBadgeAppearanceItems)
                {
                    item.Refresh();
                }
            }

            if (_trophyAppearanceItems != null)
            {
                foreach (var item in _trophyAppearanceItems)
                {
                    item.Refresh();
                }
            }
        }

        private static bool TryParseColor(string value, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(value);
                return true;
            }
            catch
            {
                color = Colors.Transparent;
                return false;
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

        private void StartPageActivityScopeSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            OpenStartPageActivityScopeContextMenu(sender as Button);
        }

        private void StartPageProgressScopeSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            OpenStartPageProgressScopeContextMenu(sender as Button);
        }

        private void OpenStartPageActivityScopeContextMenu(Button button)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (button == null || persisted == null)
            {
                return;
            }

            var options = new[]
            {
                new { Scope = GameActivityScope.Played, Label = L("LOCPlayAch_Filter_Played", "Played") },
                new { Scope = GameActivityScope.Unplayed, Label = L("LOCPlayAch_Filter_Unplayed", "Unplayed") }
            };

            var menu = PrepareStartPageScopeContextMenu(button);
            if (menu == null)
            {
                return;
            }

            var current = persisted.StartPageActivityScope;
            foreach (var option in options)
            {
                var scope = option.Scope;
                var item = CreateStartPageScopeMenuItem(
                    button,
                    option.Label,
                    current.HasFlag(scope),
                    isChecked =>
                    {
                        var settings = _settingsViewModel?.Settings?.Persisted;
                        if (settings == null)
                        {
                            return;
                        }

                        settings.StartPageActivityScope = isChecked
                            ? settings.StartPageActivityScope | scope
                            : settings.StartPageActivityScope & ~scope;
                        UpdateStartPageScopeTexts();
                    });
                menu.Items.Add(item);
            }

            OpenSelectorContextMenu(button, menu);
        }

        private void OpenStartPageProgressScopeContextMenu(Button button)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (button == null || persisted == null)
            {
                return;
            }

            var options = new[]
            {
                new { Scope = GameProgressScope.Completed, Label = L("LOCPlayAch_Filter_Complete", "Complete") },
                new { Scope = GameProgressScope.InProgress, Label = L("LOCPlayAch_Filter_InProgress", "In Progress") },
                new { Scope = GameProgressScope.NoProgress, Label = L("LOCPlayAch_Filter_NoProgress", "No Progress") }
            };

            var menu = PrepareStartPageScopeContextMenu(button);
            if (menu == null)
            {
                return;
            }

            var current = persisted.StartPageProgressScope;
            foreach (var option in options)
            {
                var scope = option.Scope;
                var item = CreateStartPageScopeMenuItem(
                    button,
                    option.Label,
                    current.HasFlag(scope),
                    isChecked =>
                    {
                        var settings = _settingsViewModel?.Settings?.Persisted;
                        if (settings == null)
                        {
                            return;
                        }

                        settings.StartPageProgressScope = isChecked
                            ? settings.StartPageProgressScope | scope
                            : settings.StartPageProgressScope & ~scope;
                        UpdateStartPageScopeTexts();
                    });
                menu.Items.Add(item);
            }

            OpenSelectorContextMenu(button, menu);
        }

        private static ContextMenu PrepareStartPageScopeContextMenu(Button button)
        {
            var menu = button?.ContextMenu;
            if (menu == null)
            {
                return null;
            }

            menu.Items.Clear();
            return menu;
        }

        private static MenuItem CreateStartPageScopeMenuItem(
            Button button,
            string header,
            bool isChecked,
            Action<bool> setSelection)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                StaysOpenOnClick = true,
                IsChecked = isChecked
            };

            var itemStyle = button?.TryFindResource("AchievementMultiSelectMenuItemStyle") as Style;
            if (itemStyle != null)
            {
                item.Style = itemStyle;
            }

            item.Click += (_, __) => setSelection?.Invoke(item.IsChecked);
            return item;
        }

        private static void OpenSelectorContextMenu(Button button, ContextMenu menu)
        {
            if (button == null || menu == null || menu.Items.Count == 0)
            {
                return;
            }

            RoutedEventHandler onClosed = null;
            onClosed = (_, __) =>
            {
                menu.Closed -= onClosed;
                button.ReleaseMouseCapture();
            };

            menu.Closed += onClosed;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
        }

        private void UpdateStartPageScopeTexts()
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            var activityScope = persisted?.StartPageActivityScope ??
                PersistedSettings.DefaultStartPageActivityScope;
            var progressScope = persisted?.StartPageProgressScope ??
                PersistedSettings.DefaultStartPageProgressScope;

            StartPageActivityScopeText = GetActivityScopeText(activityScope);
            StartPageProgressScopeText = GetProgressScopeText(progressScope);
        }

        private static string GetActivityScopeText(GameActivityScope scope)
        {
            scope = PersistedSettings.NormalizeStartPageActivityScope(scope);
            if (scope == GameActivityScope.None)
            {
                return L("LOCPlayAch_Filter_ActivitySelectorPlaceholder", "Activity");
            }

            var labels = new List<string>();
            if (scope.HasFlag(GameActivityScope.Played))
            {
                labels.Add(L("LOCPlayAch_Filter_Played", "Played"));
            }

            if (scope.HasFlag(GameActivityScope.Unplayed))
            {
                labels.Add(L("LOCPlayAch_Filter_Unplayed", "Unplayed"));
            }

            return labels.Count > 0
                ? string.Join(", ", labels)
                : L("LOCPlayAch_Filter_ActivitySelectorPlaceholder", "Activity");
        }

        private static string GetProgressScopeText(GameProgressScope scope)
        {
            scope = PersistedSettings.NormalizeStartPageProgressScope(scope);
            if (scope == GameProgressScope.None)
            {
                return L("LOCPlayAch_Progress", "Progress");
            }

            var labels = new List<string>();
            if (scope.HasFlag(GameProgressScope.Completed))
            {
                labels.Add(L("LOCPlayAch_Filter_Complete", "Complete"));
            }

            if (scope.HasFlag(GameProgressScope.InProgress))
            {
                labels.Add(L("LOCPlayAch_Filter_InProgress", "In Progress"));
            }

            if (scope.HasFlag(GameProgressScope.NoProgress))
            {
                labels.Add(L("LOCPlayAch_Filter_NoProgress", "No Progress"));
            }

            return labels.Count > 0
                ? string.Join(", ", labels)
                : L("LOCPlayAch_Progress", "Progress");
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
        // Settings property change handling for mock preview refresh
        // -----------------------------

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Refresh mock previews when display-affecting settings change
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

            if (RarityAppearanceHelper.IsAppearanceSettingPropertyName(e.PropertyName))
            {
                ApplyRarityAppearanceOverrides();
            }

            if (e.PropertyName == nameof(Models.Settings.PersistedSettings.StartPageActivityScope) ||
                e.PropertyName == nameof(Models.Settings.PersistedSettings.StartPageProgressScope))
            {
                UpdateStartPageScopeTexts();
            }

            if (e.PropertyName == nameof(Models.Settings.PersistedSettings.ViewAchievementsHotkey) ||
                e.PropertyName == nameof(Models.Settings.PersistedSettings.ManageAchievementsHotkey) ||
                e.PropertyName == nameof(Models.Settings.PersistedSettings.OverviewHotkey))
            {
                UpdateHotkeyButtonTexts();
            }

            if (refreshProperties.Contains(e.PropertyName))
            {
                RefreshMockPreviews();
            }
        }

        private void OnSettingsObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(PlayniteAchievementsSettings.Persisted), StringComparison.Ordinal))
            {
                return;
            }

            SubscribeToPersistedSettings(_settingsViewModel?.Settings?.Persisted);
            RefreshAppearanceEditorFromPersisted();
            RefreshMockPreviews();
            UpdateStartPageScopeTexts();
            UpdateHotkeyButtonTexts();
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

    public sealed class ResourceAppearanceItem : PlayniteAchievements.Common.ObservableObject
    {
        private readonly PersistedSettings _settings;
        private readonly Action _applyResources;
        private ResourceOverrideMode _mode;
        private string _customValue;

        public ResourceAppearanceItem(
            ResourceOverrideDescriptor descriptor,
            PersistedSettings settings,
            Action applyResources)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _applyResources = applyResources;

            if (_settings.ResourceOverrides != null &&
                _settings.ResourceOverrides.TryGetValue(descriptor.ResourceKey, out var persisted) &&
                persisted != null)
            {
                _mode = persisted.Mode;
                _customValue = persisted.CustomValue;
            }
            else
            {
                _mode = ResourceOverrideMode.FollowPlaynite;
                _customValue = GetCurrentPlayniteValueText(descriptor);
            }
        }

        public ResourceOverrideDescriptor Descriptor { get; }
        public string DisplayName => ResourceProvider.GetString(Descriptor.DisplayName);
        public string ResourceKey => Descriptor.ResourceKey;
        public string PlayniteResourceKey => Descriptor.PlayniteResourceKey;
        public bool IsBrush => Descriptor.ValueKind == ResourceOverrideValueKind.Brush;
        public bool IsFontSize => Descriptor.ValueKind == ResourceOverrideValueKind.FontSize;
        public bool IsFontFamily => Descriptor.ValueKind == ResourceOverrideValueKind.FontFamily;

        public ResourceOverrideMode Mode
        {
            get => _mode;
            set
            {
                if (SetValueAndReturn(ref _mode, value))
                {
                    if (_mode == ResourceOverrideMode.Custom && string.IsNullOrWhiteSpace(_customValue))
                    {
                        _customValue = GetCurrentPlayniteValueText(Descriptor);
                        OnPropertyChanged(nameof(CustomValue));
                    }

                    Persist();
                    OnPropertyChanged(nameof(IsCustom));
                    OnPropertyChanged(nameof(IsTransparent));
                    OnPropertyChanged(nameof(DisplayValueText));
                    OnPropertyChanged(nameof(PreviewBrush));
                }
            }
        }

        public bool IsCustom => Mode == ResourceOverrideMode.Custom;

        public string CustomValue
        {
            get => _customValue;
            set
            {
                if (SetValueAndReturn(ref _customValue, value))
                {
                    Persist();
                    OnPropertyChanged(nameof(DisplayValueText));
                    OnPropertyChanged(nameof(PreviewBrush));
                }
            }
        }

        public bool IsTransparent => Mode == ResourceOverrideMode.Transparent;

        public string DisplayValueText
        {
            get => IsTransparent
                ? PlayAchResourceService.TransparentValue
                : IsCustom ? CustomValue : GetCurrentPlayniteValueText(Descriptor);
            set
            {
                if (!IsCustom)
                {
                    return;
                }

                CustomValue = value;
            }
        }

        public Brush PreviewBrush
        {
            get
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(DisplayValueText));
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }

        private void Persist()
        {
            if (_settings.ResourceOverrides == null)
            {
                _settings.ResourceOverrides = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase);
            }

            if (Mode == ResourceOverrideMode.FollowPlaynite)
            {
                _settings.ResourceOverrides.Remove(ResourceKey);
                _settings.OnPropertyChanged(nameof(PersistedSettings.ResourceOverrides));
                _applyResources?.Invoke();
                return;
            }

            _settings.ResourceOverrides[ResourceKey] = new ResourceOverrideSetting
            {
                Mode = Mode,
                CustomValue = Mode == ResourceOverrideMode.Transparent
                    ? PlayAchResourceService.TransparentValue
                    : CustomValue
            };

            _settings.OnPropertyChanged(nameof(PersistedSettings.ResourceOverrides));
            _applyResources?.Invoke();
        }

        private static string GetCurrentPlayniteValueText(ResourceOverrideDescriptor descriptor)
        {
            var value = FindPlayniteResourceValue(descriptor);
            switch (descriptor.ValueKind)
            {
                case ResourceOverrideValueKind.Brush:
                    return BrushToText(value as Brush);

                case ResourceOverrideValueKind.FontSize:
                    return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

                case ResourceOverrideValueKind.FontFamily:
                    return value?.ToString() ?? string.Empty;

                default:
                    return string.Empty;
            }
        }

        private static object FindPlayniteResourceValue(ResourceOverrideDescriptor descriptor)
        {
            var value = Application.Current?.TryFindResource(descriptor.PlayniteResourceKey);
            if (value != null)
            {
                return value;
            }

            foreach (var fallbackKey in descriptor.FallbackPlayniteResourceKeys)
            {
                value = Application.Current?.TryFindResource(fallbackKey);
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static string BrushToText(Brush brush)
        {
            if (brush is SolidColorBrush solid)
            {
                var color = solid.Color;
                return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            }

            return string.Empty;
        }
    }

    public sealed class RarityAppearanceItem : PlayniteAchievements.Common.ObservableObject
    {
        private readonly PersistedSettings _settings;
        private readonly Action _applyResources;

        public RarityAppearanceItem(
            RarityTier tier,
            PersistedSettings settings,
            Action applyResources)
        {
            Tier = tier;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _applyResources = applyResources;

            if (_settings.RarityColors == null)
            {
                _settings.RarityColors = RarityColorSettings.CreateDefault();
            }
        }

        public RarityTier Tier { get; }

        public string DisplayName => Tier.ToDisplayText();

        public string BaseColor
        {
            get => GetColor();
            set
            {
                SetColor(value);
                _settings.OnPropertyChanged(nameof(PersistedSettings.RarityColors));
                _applyResources?.Invoke();
                Refresh();
            }
        }

        public Brush PreviewBrush
        {
            get
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BaseColor));
                    if (brush.CanFreeze)
                    {
                        brush.Freeze();
                    }

                    return brush;
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }

        public ImageSource PreviewBadge =>
            RarityAppearanceHelper.CreateBadgePreview(Tier, _settings);

        public void Refresh()
        {
            OnPropertyChanged(nameof(BaseColor));
            OnPropertyChanged(nameof(PreviewBrush));
            OnPropertyChanged(nameof(PreviewBadge));
        }

        public void Reset()
        {
            SetColor(GetDefaultColor());
            _settings.OnPropertyChanged(nameof(PersistedSettings.RarityColors));
            _applyResources?.Invoke();
            Refresh();
        }

        private string GetColor()
        {
            var colors = _settings.RarityColors ?? RarityColorSettings.CreateDefault();
            switch (Tier)
            {
                case RarityTier.UltraRare:
                    return colors.UltraRare;
                case RarityTier.Rare:
                    return colors.Rare;
                case RarityTier.Uncommon:
                    return colors.Uncommon;
                default:
                    return colors.Common;
            }
        }

        private string GetDefaultColor()
        {
            switch (Tier)
            {
                case RarityTier.UltraRare:
                    return RarityColorSettings.DefaultUltraRare;
                case RarityTier.Rare:
                    return RarityColorSettings.DefaultRare;
                case RarityTier.Uncommon:
                    return RarityColorSettings.DefaultUncommon;
                default:
                    return RarityColorSettings.DefaultCommon;
            }
        }

        private void SetColor(string value)
        {
            if (_settings.RarityColors == null)
            {
                _settings.RarityColors = RarityColorSettings.CreateDefault();
            }

            switch (Tier)
            {
                case RarityTier.UltraRare:
                    _settings.RarityColors.UltraRare = value;
                    break;
                case RarityTier.Rare:
                    _settings.RarityColors.Rare = value;
                    break;
                case RarityTier.Uncommon:
                    _settings.RarityColors.Uncommon = value;
                    break;
                default:
                    _settings.RarityColors.Common = value;
                    break;
            }
        }
    }

    public sealed class RarityPalettePreset
    {
        public RarityPalettePreset(
            string name,
            RarityColorSettings colors,
            IReadOnlyDictionary<string, string> resourceBrushes)
        {
            Name = name;
            Colors = colors ?? RarityColorSettings.CreateDefault();
            ResourceBrushes = resourceBrushes;
        }

        public string Name { get; }

        public RarityColorSettings Colors { get; }

        public IReadOnlyDictionary<string, string> ResourceBrushes { get; }

        public string DisplayLabel { get; set; }

        public Brush CommonBrush => CreateSwatchBrush(Colors.Common);

        public Brush UncommonBrush => CreateSwatchBrush(Colors.Uncommon);

        public Brush RareBrush => CreateSwatchBrush(Colors.Rare);

        public Brush UltraRareBrush => CreateSwatchBrush(Colors.UltraRare);

        private static Brush CreateSwatchBrush(string colorHex)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                if (brush.CanFreeze)
                {
                    brush.Freeze();
                }

                return brush;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }

    public sealed class CompletedBadgeAppearanceItem : PlayniteAchievements.Common.ObservableObject
    {
        private readonly bool _isStartColor;
        private readonly PersistedSettings _settings;
        private readonly Action _applyResources;

        public CompletedBadgeAppearanceItem(
            string displayName,
            bool isStartColor,
            PersistedSettings settings,
            Action applyResources)
        {
            DisplayName = displayName;
            _isStartColor = isStartColor;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _applyResources = applyResources;

            if (_settings.RarityColors == null)
            {
                _settings.RarityColors = RarityColorSettings.CreateDefault();
            }
        }

        public string DisplayName { get; }

        public string BaseColor
        {
            get => GetColor();
            set
            {
                SetColor(value);
                _settings.OnPropertyChanged(nameof(PersistedSettings.RarityColors));
                _applyResources?.Invoke();
                Refresh();
            }
        }

        public Brush PreviewBrush
        {
            get
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BaseColor));
                    if (brush.CanFreeze)
                    {
                        brush.Freeze();
                    }

                    return brush;
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }

        public ImageSource PreviewBadge =>
            RarityAppearanceHelper.CreateCompletedBadgePreview(_settings);

        public void Refresh()
        {
            OnPropertyChanged(nameof(BaseColor));
            OnPropertyChanged(nameof(PreviewBrush));
            OnPropertyChanged(nameof(PreviewBadge));
        }

        public void Reset()
        {
            SetColor(_isStartColor
                ? RarityColorSettings.DefaultCompletedStart
                : RarityColorSettings.DefaultCompletedEnd);
            _settings.OnPropertyChanged(nameof(PersistedSettings.RarityColors));
            _applyResources?.Invoke();
            Refresh();
        }

        private string GetColor()
        {
            var colors = _settings.RarityColors ?? RarityColorSettings.CreateDefault();
            return _isStartColor ? colors.CompletedStart : colors.CompletedEnd;
        }

        private void SetColor(string value)
        {
            if (_settings.RarityColors == null)
            {
                _settings.RarityColors = RarityColorSettings.CreateDefault();
            }

            if (_isStartColor)
            {
                _settings.RarityColors.CompletedStart = value;
            }
            else
            {
                _settings.RarityColors.CompletedEnd = value;
            }
        }
    }

    public sealed class TrophyAppearanceItem : PlayniteAchievements.Common.ObservableObject
    {
        private readonly string _trophyKey;
        private readonly PersistedSettings _settings;
        private readonly Action _applyResources;

        public TrophyAppearanceItem(
            string displayName,
            string trophyKey,
            PersistedSettings settings,
            Action applyResources)
        {
            DisplayName = displayName;
            _trophyKey = trophyKey;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _applyResources = applyResources;

            if (_settings.RarityColors == null)
            {
                _settings.RarityColors = RarityColorSettings.CreateDefault();
            }
        }

        public string DisplayName { get; }

        public string BaseColor
        {
            get => GetColor();
            set
            {
                SetColor(value);
                _settings.OnPropertyChanged(nameof(PersistedSettings.RarityColors));
                _applyResources?.Invoke();
                Refresh();
            }
        }

        public Brush PreviewBrush
        {
            get
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BaseColor));
                    if (brush.CanFreeze)
                    {
                        brush.Freeze();
                    }

                    return brush;
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }

        public ImageSource PreviewBadge =>
            RarityAppearanceHelper.CreateTrophyPreview(_trophyKey, _settings);

        public void Refresh()
        {
            OnPropertyChanged(nameof(BaseColor));
            OnPropertyChanged(nameof(PreviewBrush));
            OnPropertyChanged(nameof(PreviewBadge));
        }

        public void Reset()
        {
            SetColor(GetDefaultColor());
            _settings.OnPropertyChanged(nameof(PersistedSettings.RarityColors));
            _applyResources?.Invoke();
            Refresh();
        }

        private string GetColor()
        {
            var colors = _settings.RarityColors ?? RarityColorSettings.CreateDefault();
            switch (_trophyKey)
            {
                case "TrophyPlatinum":
                    return colors.TrophyPlatinum;
                case "TrophyGold":
                    return colors.TrophyGold;
                case "TrophySilver":
                    return colors.TrophySilver;
                default:
                    return colors.TrophyBronze;
            }
        }

        private string GetDefaultColor()
        {
            switch (_trophyKey)
            {
                case "TrophyPlatinum":
                    return RarityColorSettings.DefaultTrophyPlatinum;
                case "TrophyGold":
                    return RarityColorSettings.DefaultTrophyGold;
                case "TrophySilver":
                    return RarityColorSettings.DefaultTrophySilver;
                default:
                    return RarityColorSettings.DefaultTrophyBronze;
            }
        }

        private void SetColor(string value)
        {
            if (_settings.RarityColors == null)
            {
                _settings.RarityColors = RarityColorSettings.CreateDefault();
            }

            switch (_trophyKey)
            {
                case "TrophyPlatinum":
                    _settings.RarityColors.TrophyPlatinum = value;
                    break;
                case "TrophyGold":
                    _settings.RarityColors.TrophyGold = value;
                    break;
                case "TrophySilver":
                    _settings.RarityColors.TrophySilver = value;
                    break;
                default:
                    _settings.RarityColors.TrophyBronze = value;
                    break;
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





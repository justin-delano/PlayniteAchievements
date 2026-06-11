using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Tagging;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Persisted user settings for PlayniteAchievements plugin.
    /// These settings are serialized to the plugin settings JSON file.
    /// </summary>
    public class PersistedSettings : ObservableObject
    {
        public const double DefaultAchievementDataGridMaxHeight = 600d;
        public const double MinimumGridRowHeight = 32d;
        public const int DefaultStartPageGridMaxRows = 25;
        public const int MinimumGridMaxRows = 1;
        public const double DefaultSidebarOverviewLeftColumnRatio = 0.5d;
        public const double MinSidebarOverviewLeftColumnRatio = 0.01d;
        public const double MaxSidebarOverviewLeftColumnRatio = 0.99d;

        public PersistedSettings()
        {
            AttachStartPageSettingsHandlers();
        }

        #region Backing Fields

        private string _globalLanguage = "english";
        private bool _enablePeriodicUpdates = true;
        private bool _includeHiddenGamesInBulkScans = true;
        private int _periodicUpdateHours = 6;
        private bool _enableNotifications = true;
        private bool _notifyPeriodicUpdates = true;
        private bool _notifyOnRebuild = true;
        private int _recentRefreshGamesCount = 10;
        private bool _showHiddenIcon = false;
        private bool _showHiddenTitle = false;
        private bool _showHiddenDescription = false;
        private bool _showHiddenSuffix = true;
        private bool _showLockedIcon = true;
        private bool _preserveAchievementIconResolution = false;
        private bool _useSeparateLockedIconsWhenAvailable = false;
        private HashSet<Guid> _separateLockedIconEnabledGameIds = new HashSet<Guid>();
        private bool _showRarityGlow = true;
        private bool _useUniformRarityBadges = false;
        private bool _useCoverImages = true;
        private bool _includeUnplayedGames = true;
        private bool _showSidebarCollectionScoreCard = true;
        private bool _showSidebarPrestigeScoreCard = true;
        private bool _showSidebarPieCharts = true;
        private bool _showSidebarGamesPieChart = true;
        private bool _showSidebarProviderPieChart = true;
        private bool _showSidebarRarityPieChart = true;
        private bool _showSidebarTrophyPieChart = true;
        private bool _showSidebarPiePercentages = true;
        private SidebarPieSmallSliceMode _sidebarPieSmallSliceMode = SidebarPieSmallSliceMode.Round;
        private bool _sidebarPieChartVisibilityInitializedFromIndividualSettings;
        private bool _showSidebarBarCharts = true;
        private bool _showSidebarGameMetadata = true;
        private bool _showTopMenuBarButton = true;
        private bool _showCompactListRarityBar = true;
        private bool _showCompletionBorder = true;
        private bool _showOverviewGridColumnHeaders = true;
        private bool _showAchievementGridColumnHeaders = true;
        private bool _showDesktopThemeAchievementGridColumnHeaders = true;
        private GridAlignment _gridColumnHeaderAlignment = GridAlignment.Center;
        private GridAlignment _gridCellAlignment = GridAlignment.Left;
        private GridVerticalAlignment _gridCellVerticalAlignment = GridVerticalAlignment.Center;
        private bool _enableAchievementCompactListControl = true;
        private bool _enableAchievementDataGridControl = true;
        private bool _enableAchievementCompactUnlockedListControl = true;
        private bool _enableAchievementCompactLockedListControl = true;
        private bool _enableAchievementProgressBarControl = true;
        private bool _enableAchievementStatsControl = true;
        private bool _enableAchievementButtonControl = true;
        private bool _enableAchievementViewItemControl = true;
        private bool _enableAchievementPieChartControl = true;
        private bool _enableAchievementBarChartControl = true;
        private double? _achievementDataGridMaxHeight = DefaultAchievementDataGridMaxHeight;
        private double? _singleGameGridRowHeight;
        private double? _sidebarOverviewGridRowHeight;
        private double? _sidebarRecentAchievementsGridRowHeight;
        private double? _sidebarSelectedGameGridRowHeight;
        private double? _desktopThemeAchievementGridRowHeight;
        private int? _singleGameGridMaxRows;
        private int? _sidebarOverviewGridMaxRows;
        private int? _sidebarRecentAchievementsGridMaxRows;
        private int? _sidebarSelectedGameGridMaxRows;
        private int? _desktopThemeAchievementGridMaxRows;
        private StartPageGamesOverviewGridSettings _startPageGamesOverviewGrid =
            new StartPageGamesOverviewGridSettings();
        private StartPageRecentUnlocksGridSettings _startPageRecentUnlocksGrid =
            new StartPageRecentUnlocksGridSettings();
        private StartPagePieWidgetSettings _startPagePieCharts =
            new StartPagePieWidgetSettings();
        private bool _enableParallelProviderRefresh = true;
        private int _scanDelayMs = 200;
        private int _maxRetryAttempts = 3;
        private List<CustomRefreshPreset> _customRefreshPresets = new List<CustomRefreshPreset>();
        private Dictionary<string, bool> _dataGridColumnVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _dataGridColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _dataGridColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _sidebarAchievementColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _sidebarAchievementColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _sidebarAchievementColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _sidebarGameColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _sidebarGameColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _sidebarGameColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _singleGameColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _singleGameColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _singleGameColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _desktopThemeColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _desktopThemeColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _desktopThemeColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> _gamesOverviewColumnVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _gamesOverviewColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _gamesOverviewColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _gamesOverviewColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> _startPageAchievementColumnVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _startPageAchievementColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _startPageAchievementColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _startPageAchievementColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> _startPageGamesOverviewColumnVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _startPageGamesOverviewColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _startPageGamesOverviewColumnOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GridAlignment> _startPageGamesOverviewColumnAlignments = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
        private double _sidebarOverviewLeftColumnRatio = DefaultSidebarOverviewLeftColumnRatio;
        private Dictionary<string, WindowPlacementState> _windowPlacements =
            new Dictionary<string, WindowPlacementState>(StringComparer.OrdinalIgnoreCase);
        private TimelineRange _sidebarTimelineRange = TimelineRange.OneYear;
        private bool _firstTimeSetupCompleted = false;
        private bool _seenThemeMigration = false;
        private HashSet<Guid> _excludedGameIds = new HashSet<Guid>();
        private HashSet<Guid> _excludedFromSummariesGameIds = new HashSet<Guid>();
        private Dictionary<Guid, string> _manualCapstones = new Dictionary<Guid, string>();
        private Dictionary<Guid, List<string>> _achievementOrderOverrides = new Dictionary<Guid, List<string>>();
        private Dictionary<Guid, Dictionary<string, string>> _achievementCategoryOverrides =
            new Dictionary<Guid, Dictionary<string, string>>();
        private Dictionary<Guid, Dictionary<string, string>> _achievementCategoryTypeOverrides =
            new Dictionary<Guid, Dictionary<string, string>>();
        private Dictionary<string, ThemeMigrationCacheEntry> _themeMigrationVersionCache =
            new Dictionary<string, ThemeMigrationCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private CompactListSortMode _compactListSortMode = CompactListSortMode.None;
        private bool _compactListSortDescending = false;
        private CompactListSortMode _compactUnlockedListSortMode = CompactListSortMode.None;
        private bool _compactUnlockedListSortDescending = false;
        private CompactListSortMode _compactLockedListSortMode = CompactListSortMode.None;
        private bool _compactLockedListSortDescending = false;
        private GamesOverviewSortMode _gamesOverviewGridSortMode = GamesOverviewSortMode.RecentUnlock;
        private bool _gamesOverviewGridSortDescending = true;
        private CompactListSortMode _sidebarSelectedGameGridSortMode = CompactListSortMode.UnlockTime;
        private bool _sidebarSelectedGameGridSortDescending = true;
        private CompactListSortMode _singleGameGridSortMode = CompactListSortMode.UnlockTime;
        private bool _singleGameGridSortDescending = true;
        private CompactListSortMode _achievementDataGridSortMode = CompactListSortMode.UnlockTime;
        private bool _achievementDataGridSortDescending = true;
        private TaggingSettings _taggingSettings;
        private Dictionary<string, JObject> _providerSettings = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Provider Settings Dictionary

        /// <summary>
        /// Dictionary of provider settings as JSON objects.
        /// Key is the provider key (e.g., "Steam", "Epic"), value is the settings as a JObject.
        /// </summary>
        public Dictionary<string, JObject> ProviderSettings
        {
            get => _providerSettings;
            set => SetValue(ref _providerSettings, value ?? new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase));
        }

        #endregion

        #region Global Settings

        /// <summary>
        /// Global language for achievement text, used by all providers that support localization.
        /// </summary>
        public string GlobalLanguage
        {
            get => _globalLanguage;
            set => SetValue(ref _globalLanguage, value);
        }

        #endregion

        #region Update and Refresh Settings

        /// <summary>
        /// Enable the background periodic updates.
        /// </summary>
        public bool EnablePeriodicUpdates
        {
            get => _enablePeriodicUpdates;
            set => SetValue(ref _enablePeriodicUpdates, value);
        }

        /// <summary>
        /// When true, bulk refreshes include games marked hidden in Playnite.
        /// Explicit user-targeted refreshes ignore this setting.
        /// </summary>
        public bool IncludeHiddenGamesInBulkScans
        {
            get => _includeHiddenGamesInBulkScans;
            set => SetValue(ref _includeHiddenGamesInBulkScans, value);
        }

        /// <summary>
        /// Hours between periodic background updates.
        /// </summary>
        public int PeriodicUpdateHours
        {
            get => _periodicUpdateHours;
            set => SetValue(ref _periodicUpdateHours, Math.Max(1, value));
        }

        /// <summary>
        /// Maximum recent games to refresh when using Recent Refresh.
        /// </summary>
        public int RecentRefreshGamesCount
        {
            get => _recentRefreshGamesCount;
            set => SetValue(ref _recentRefreshGamesCount, Math.Max(1, value));
        }

        /// <summary>
        /// Saved presets for Custom Refresh dialog.
        /// </summary>
        public List<CustomRefreshPreset> CustomRefreshPresets
        {
            get => _customRefreshPresets;
            set
            {
                var normalized = new List<CustomRefreshPreset>(
                    CustomRefreshPreset.NormalizePresets(value, CustomRefreshPreset.MaxPresetCount));
                SetValue(ref _customRefreshPresets, normalized);
            }
        }

        #endregion

        #region Notification Settings

        /// <summary>
        /// Enable non-modal notifications (toasts) from the plugin.
        /// </summary>
        public bool EnableNotifications
        {
            get => _enableNotifications;
            set => SetValue(ref _enableNotifications, value);
        }

        /// <summary>
        /// Show lightweight toast when periodic background updates complete.
        /// </summary>
        public bool NotifyPeriodicUpdates
        {
            get => _notifyPeriodicUpdates;
            set => SetValue(ref _notifyPeriodicUpdates, value);
        }

        /// <summary>
        /// Show a toast when a manual or managed rebuild completes or fails.
        /// </summary>
        public bool NotifyOnRebuild
        {
            get => _notifyOnRebuild;
            set => SetValue(ref _notifyOnRebuild, value);
        }

        #endregion

        #region Display Preferences

        /// <summary>
        /// When true, hidden achievement icons are shown before reveal.
        /// </summary>
        public bool ShowHiddenIcon
        {
            get => _showHiddenIcon;
            set => SetValue(ref _showHiddenIcon, value);
        }

        /// <summary>
        /// When true, hidden achievement titles are shown before reveal.
        /// </summary>
        public bool ShowHiddenTitle
        {
            get => _showHiddenTitle;
            set => SetValue(ref _showHiddenTitle, value);
        }

        /// <summary>
        /// When true, hidden achievement descriptions are shown before reveal.
        /// </summary>
        public bool ShowHiddenDescription
        {
            get => _showHiddenDescription;
            set => SetValue(ref _showHiddenDescription, value);
        }

        /// <summary>
        /// When true, hidden achievements show "(Hidden Achievement)" suffix after their title.
        /// </summary>
        public bool ShowHiddenSuffix
        {
            get => _showHiddenSuffix;
            set => SetValue(ref _showHiddenSuffix, value);
        }

        /// <summary>
        /// When true, locked achievement icons are shown.
        /// This uses a provider-supplied locked icon when available, otherwise a grayscaled unlocked fallback.
        /// When false, locked achievement icons are hidden with a placeholder until revealed.
        /// </summary>
        public bool ShowLockedIcon
        {
            get => _showLockedIcon;
            set => SetValue(ref _showLockedIcon, value);
        }

        /// <summary>
        /// When true, achievement icons are cached at their original decoded size instead of the optimized 128px cache mode.
        /// Changes apply on the next refresh.
        /// </summary>
        public bool PreserveAchievementIconResolution
        {
            get => _preserveAchievementIconResolution;
            set => SetValue(ref _preserveAchievementIconResolution, value);
        }

        /// <summary>
        /// When true, providers with distinct locked icons will cache and use them instead of grayscaling the unlocked icon.
        /// Changes apply on the next refresh for newly cached icons.
        /// </summary>
        public bool UseSeparateLockedIconsWhenAvailable
        {
            get => _useSeparateLockedIconsWhenAvailable;
            set => SetValue(ref _useSeparateLockedIconsWhenAvailable, value);
        }

        /// <summary>
        /// Game IDs that always use separate locked icons when available, regardless of the global default.
        /// When absent, the game falls back to the global UseSeparateLockedIconsWhenAvailable setting.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public HashSet<Guid> SeparateLockedIconEnabledGameIds
        {
            get => _separateLockedIconEnabledGameIds;
            set => SetValue(ref _separateLockedIconEnabledGameIds, value ?? new HashSet<Guid>());
        }

        /// <summary>
        /// Resolves whether a game should use separate locked icons after applying the per-game override.
        /// </summary>
        public bool ShouldUseSeparateLockedIcons(Guid? playniteGameId)
        {
            if (UseSeparateLockedIconsWhenAvailable)
            {
                return true;
            }

            return playniteGameId.HasValue &&
                   playniteGameId.Value != Guid.Empty &&
                   SeparateLockedIconEnabledGameIds?.Contains(playniteGameId.Value) == true;
        }

        /// <summary>
        /// When true, unlocked achievement icons display rarity-based glow effects.
        /// </summary>
        public bool ShowRarityGlow
        {
            get => _showRarityGlow;
            set => SetValue(ref _showRarityGlow, value);
        }

        /// <summary>
        /// When true, all rarity badges use the hexagon shape while keeping rarity colors.
        /// </summary>
        public bool UseUniformRarityBadges
        {
            get => _useUniformRarityBadges;
            set => SetValue(ref _useUniformRarityBadges, value);
        }

        /// <summary>
        /// When true, use Playnite cover images instead of icons/logos in the games list.
        /// </summary>
        public bool UseCoverImages
        {
            get => _useCoverImages;
            set => SetValue(ref _useCoverImages, value);
        }

        public bool IncludeUnplayedGames
        {
            get => _includeUnplayedGames;
            set => SetValue(ref _includeUnplayedGames, value);
        }

        /// <summary>
        /// When true, shows the collection score card in the sidebar header.
        /// </summary>
        public bool ShowSidebarCollectionScoreCard
        {
            get => _showSidebarCollectionScoreCard;
            set => SetValue(ref _showSidebarCollectionScoreCard, value);
        }

        /// <summary>
        /// When true, shows the prestige score card in the sidebar header.
        /// </summary>
        public bool ShowSidebarPrestigeScoreCard
        {
            get => _showSidebarPrestigeScoreCard;
            set => SetValue(ref _showSidebarPrestigeScoreCard, value);
        }

        /// <summary>
        /// Legacy aggregate toggle for sidebar pie charts.
        /// New builds use per-chart visibility settings, but this is preserved for migration.
        /// </summary>
        public bool ShowSidebarPieCharts
        {
            get => _showSidebarPieCharts;
            set
            {
                if (_showSidebarPieCharts == value)
                {
                    return;
                }

                SetValue(ref _showSidebarPieCharts, value);
                if (_sidebarPieChartVisibilityInitializedFromIndividualSettings)
                {
                    return;
                }

                ShowSidebarGamesPieChart = value;
                ShowSidebarProviderPieChart = value;
                ShowSidebarRarityPieChart = value;
                ShowSidebarTrophyPieChart = value;
            }
        }

        /// <summary>
        /// When true, shows the completed-games pie chart in the sidebar.
        /// </summary>
        public bool ShowSidebarGamesPieChart
        {
            get => _showSidebarGamesPieChart;
            set
            {
                _sidebarPieChartVisibilityInitializedFromIndividualSettings = true;
                SetValue(ref _showSidebarGamesPieChart, value);
            }
        }

        /// <summary>
        /// When true, shows the platform/provider pie chart in the sidebar.
        /// </summary>
        public bool ShowSidebarProviderPieChart
        {
            get => _showSidebarProviderPieChart;
            set
            {
                _sidebarPieChartVisibilityInitializedFromIndividualSettings = true;
                SetValue(ref _showSidebarProviderPieChart, value);
            }
        }

        /// <summary>
        /// When true, shows the rarity pie chart in the sidebar.
        /// </summary>
        public bool ShowSidebarRarityPieChart
        {
            get => _showSidebarRarityPieChart;
            set
            {
                _sidebarPieChartVisibilityInitializedFromIndividualSettings = true;
                SetValue(ref _showSidebarRarityPieChart, value);
            }
        }

        /// <summary>
        /// When true, shows the trophy pie chart in the sidebar.
        /// </summary>
        public bool ShowSidebarTrophyPieChart
        {
            get => _showSidebarTrophyPieChart;
            set
            {
                _sidebarPieChartVisibilityInitializedFromIndividualSettings = true;
                SetValue(ref _showSidebarTrophyPieChart, value);
            }
        }

        /// <summary>
        /// When true, shows the center percentage text on sidebar pie charts.
        /// </summary>
        public bool ShowSidebarPiePercentages
        {
            get => _showSidebarPiePercentages;
            set => SetValue(ref _showSidebarPiePercentages, value);
        }

        /// <summary>
        /// Determines how sidebar pie charts handle slices below five percent.
        /// </summary>
        public SidebarPieSmallSliceMode SidebarPieSmallSliceMode
        {
            get => _sidebarPieSmallSliceMode;
            set => SetValue(ref _sidebarPieSmallSliceMode, value);
        }

        /// <summary>
        /// When true, shows the timeline bar chart at the bottom of the right sidebar.
        /// When false, the achievements list takes the full space.
        /// </summary>
        public bool ShowSidebarBarCharts
        {
            get => _showSidebarBarCharts;
            set => SetValue(ref _showSidebarBarCharts, value);
        }

        /// <summary>
        /// When true, shows platform/playtime/region metadata under game names in the sidebar games overview.
        /// </summary>
        public bool ShowSidebarGameMetadata
        {
            get => _showSidebarGameMetadata;
            set => SetValue(ref _showSidebarGameMetadata, value);
        }

        /// <summary>
        /// When true, shows the top menu bar button for opening the achievements window.
        /// </summary>
        public bool ShowTopMenuBarButton
        {
            get => _showTopMenuBarButton;
            set => SetValue(ref _showTopMenuBarButton, value);
        }

        /// <summary>
        /// When true, shows the rarity bar at the bottom of compact list achievement items.
        /// </summary>
        public bool ShowCompactListRarityBar
        {
            get => _showCompactListRarityBar;
            set => SetValue(ref _showCompactListRarityBar, value);
        }

        /// <summary>
        /// When true, completed games display a blue border in the games overview.
        /// </summary>
        public bool ShowCompletionBorder
        {
            get => _showCompletionBorder;
            set => SetValue(ref _showCompletionBorder, value);
        }

        /// <summary>
        /// When true, shows column headers in games overview grids.
        /// </summary>
        public bool ShowOverviewGridColumnHeaders
        {
            get => _showOverviewGridColumnHeaders;
            set => SetValue(ref _showOverviewGridColumnHeaders, value);
        }

        /// <summary>
        /// When true, shows column headers in sidebar achievement grids.
        /// </summary>
        public bool ShowAchievementGridColumnHeaders
        {
            get => _showAchievementGridColumnHeaders;
            set => SetValue(ref _showAchievementGridColumnHeaders, value);
        }

        /// <summary>
        /// When true, shows column headers in desktop theme achievement grids.
        /// </summary>
        public bool ShowDesktopThemeAchievementGridColumnHeaders
        {
            get => _showDesktopThemeAchievementGridColumnHeaders;
            set => SetValue(ref _showDesktopThemeAchievementGridColumnHeaders, value);
        }

        /// <summary>
        /// Horizontal alignment for text shown in DataGrid column headers.
        /// </summary>
        public GridAlignment GridColumnHeaderAlignment
        {
            get => _gridColumnHeaderAlignment;
            set => SetValue(ref _gridColumnHeaderAlignment, value);
        }

        /// <summary>
        /// Horizontal alignment for textual DataGrid cell content.
        /// </summary>
        public GridAlignment GridCellAlignment
        {
            get => _gridCellAlignment;
            set => SetValue(ref _gridCellAlignment, value);
        }

        /// <summary>
        /// Vertical alignment for DataGrid cell content.
        /// </summary>
        public GridVerticalAlignment GridCellVerticalAlignment
        {
            get => _gridCellVerticalAlignment;
            set => SetValue(ref _gridCellVerticalAlignment, value);
        }

        /// <summary>
        /// When true, enables the modern compact list control.
        /// </summary>
        public bool EnableAchievementCompactListControl
        {
            get => _enableAchievementCompactListControl;
            set => SetValue(ref _enableAchievementCompactListControl, value);
        }

        /// <summary>
        /// When true, enables the modern achievement datagrid control.
        /// </summary>
        public bool EnableAchievementDataGridControl
        {
            get => _enableAchievementDataGridControl;
            set => SetValue(ref _enableAchievementDataGridControl, value);
        }

        /// <summary>
        /// When true, enables the modern compact unlocked list control.
        /// </summary>
        public bool EnableAchievementCompactUnlockedListControl
        {
            get => _enableAchievementCompactUnlockedListControl;
            set => SetValue(ref _enableAchievementCompactUnlockedListControl, value);
        }

        /// <summary>
        /// When true, enables the modern compact locked list control.
        /// </summary>
        public bool EnableAchievementCompactLockedListControl
        {
            get => _enableAchievementCompactLockedListControl;
            set => SetValue(ref _enableAchievementCompactLockedListControl, value);
        }

        /// <summary>
        /// When true, enables the modern progress bar control.
        /// </summary>
        public bool EnableAchievementProgressBarControl
        {
            get => _enableAchievementProgressBarControl;
            set => SetValue(ref _enableAchievementProgressBarControl, value);
        }

        /// <summary>
        /// When true, enables the modern stats control.
        /// </summary>
        public bool EnableAchievementStatsControl
        {
            get => _enableAchievementStatsControl;
            set => SetValue(ref _enableAchievementStatsControl, value);
        }

        /// <summary>
        /// When true, enables the modern button control.
        /// </summary>
        public bool EnableAchievementButtonControl
        {
            get => _enableAchievementButtonControl;
            set => SetValue(ref _enableAchievementButtonControl, value);
        }

        /// <summary>
        /// When true, enables the modern view item control.
        /// </summary>
        public bool EnableAchievementViewItemControl
        {
            get => _enableAchievementViewItemControl;
            set => SetValue(ref _enableAchievementViewItemControl, value);
        }

        /// <summary>
        /// When true, enables the modern pie chart control.
        /// </summary>
        public bool EnableAchievementPieChartControl
        {
            get => _enableAchievementPieChartControl;
            set => SetValue(ref _enableAchievementPieChartControl, value);
        }

        /// <summary>
        /// When true, enables the modern bar chart control.
        /// </summary>
        public bool EnableAchievementBarChartControl
        {
            get => _enableAchievementBarChartControl;
            set => SetValue(ref _enableAchievementBarChartControl, value);
        }

        /// <summary>
        /// Sort mode for the compact list (all achievements) control.
        /// None preserves provider order.
        /// </summary>
        public CompactListSortMode CompactListSortMode
        {
            get => _compactListSortMode;
            set => SetValue(ref _compactListSortMode, value);
        }

        /// <summary>
        /// When true, reverses the sort direction for the compact list (all achievements) control.
        /// </summary>
        public bool CompactListSortDescending
        {
            get => _compactListSortDescending;
            set => SetValue(ref _compactListSortDescending, value);
        }

        /// <summary>
        /// Sort mode for the compact unlocked list control.
        /// None preserves newest-first ordering.
        /// </summary>
        public CompactListSortMode CompactUnlockedListSortMode
        {
            get => _compactUnlockedListSortMode;
            set => SetValue(ref _compactUnlockedListSortMode, value);
        }

        /// <summary>
        /// When true, reverses the sort direction for the compact unlocked list control.
        /// </summary>
        public bool CompactUnlockedListSortDescending
        {
            get => _compactUnlockedListSortDescending;
            set => SetValue(ref _compactUnlockedListSortDescending, value);
        }

        /// <summary>
        /// Sort mode for the compact locked list control.
        /// None preserves provider order.
        /// </summary>
        public CompactListSortMode CompactLockedListSortMode
        {
            get => _compactLockedListSortMode;
            set => SetValue(ref _compactLockedListSortMode, value);
        }

        /// <summary>
        /// When true, reverses the sort direction for the compact locked list control.
        /// </summary>
        public bool CompactLockedListSortDescending
        {
            get => _compactLockedListSortDescending;
            set => SetValue(ref _compactLockedListSortDescending, value);
        }

        /// <summary>
        /// Sort mode for the sidebar games overview grid.
        /// </summary>
        public GamesOverviewSortMode GamesOverviewGridSortMode
        {
            get => _gamesOverviewGridSortMode;
            set => SetValue(ref _gamesOverviewGridSortMode, value);
        }

        /// <summary>
        /// When true, reverses the configured sort direction for the sidebar games overview grid.
        /// </summary>
        public bool GamesOverviewGridSortDescending
        {
            get => _gamesOverviewGridSortDescending;
            set => SetValue(ref _gamesOverviewGridSortDescending, value);
        }

        /// <summary>
        /// Sort mode for the sidebar selected-game grid.
        /// None preserves custom order when configured, otherwise provider order.
        /// </summary>
        public CompactListSortMode SidebarSelectedGameGridSortMode
        {
            get => _sidebarSelectedGameGridSortMode;
            set => SetValue(ref _sidebarSelectedGameGridSortMode, value);
        }

        /// <summary>
        /// When true, reverses the configured sort direction for the sidebar selected-game grid.
        /// Ignored when SidebarSelectedGameGridSortMode is None.
        /// </summary>
        public bool SidebarSelectedGameGridSortDescending
        {
            get => _sidebarSelectedGameGridSortDescending;
            set => SetValue(ref _sidebarSelectedGameGridSortDescending, value);
        }

        /// <summary>
        /// Sort mode for the single-game achievement grid.
        /// None preserves custom order when configured, otherwise provider order.
        /// </summary>
        public CompactListSortMode SingleGameGridSortMode
        {
            get => _singleGameGridSortMode;
            set => SetValue(ref _singleGameGridSortMode, value);
        }

        /// <summary>
        /// When true, reverses the configured sort direction for the single-game grid.
        /// Ignored when SingleGameGridSortMode is None.
        /// </summary>
        public bool SingleGameGridSortDescending
        {
            get => _singleGameGridSortDescending;
            set => SetValue(ref _singleGameGridSortDescending, value);
        }

        /// <summary>
        /// Sort mode for the shared modern AchievementDataGrid control.
        /// None preserves custom order when configured, otherwise provider order.
        /// </summary>
        public CompactListSortMode AchievementDataGridSortMode
        {
            get => _achievementDataGridSortMode;
            set => SetValue(ref _achievementDataGridSortMode, value);
        }

        /// <summary>
        /// When true, reverses the configured sort direction for the shared modern AchievementDataGrid control.
        /// Ignored when AchievementDataGridSortMode is None.
        /// </summary>
        public bool AchievementDataGridSortDescending
        {
            get => _achievementDataGridSortDescending;
            set => SetValue(ref _achievementDataGridSortDescending, value);
        }

        /// <summary>
        /// Maximum height for AchievementDataGrid controls (null = unlimited).
        /// Defaults to 600 so theme-hosted grids do not expand indefinitely.
        /// </summary>
        public double? AchievementDataGridMaxHeight
        {
            get => _achievementDataGridMaxHeight;
            set => SetValue(ref _achievementDataGridMaxHeight, value);
        }

        /// <summary>
        /// Fixed row height for the single-game achievement grid (null = automatic).
        /// </summary>
        public double? SingleGameGridRowHeight
        {
            get => _singleGameGridRowHeight;
            set => SetValue(ref _singleGameGridRowHeight, NormalizeGridRowHeight(value));
        }

        /// <summary>
        /// Fixed row height for the sidebar games overview grid (null = automatic).
        /// </summary>
        public double? SidebarOverviewGridRowHeight
        {
            get => _sidebarOverviewGridRowHeight;
            set => SetValue(ref _sidebarOverviewGridRowHeight, NormalizeGridRowHeight(value));
        }

        /// <summary>
        /// Fixed row height for the sidebar recent achievements grid (null = automatic).
        /// </summary>
        public double? SidebarRecentAchievementsGridRowHeight
        {
            get => _sidebarRecentAchievementsGridRowHeight;
            set => SetValue(ref _sidebarRecentAchievementsGridRowHeight, NormalizeGridRowHeight(value));
        }

        /// <summary>
        /// Fixed row height for the sidebar selected-game achievement grid (null = automatic).
        /// </summary>
        public double? SidebarSelectedGameGridRowHeight
        {
            get => _sidebarSelectedGameGridRowHeight;
            set => SetValue(ref _sidebarSelectedGameGridRowHeight, NormalizeGridRowHeight(value));
        }

        /// <summary>
        /// Fixed row height for the Start Page games overview grid (null = automatic).
        /// </summary>
        public double? StartPageGamesOverviewGridRowHeight
        {
            get => StartPageGamesOverviewGrid.RowHeight;
            set => StartPageGamesOverviewGrid.RowHeight = value;
        }

        /// <summary>
        /// Fixed row height for the Start Page recent unlocks grid (null = automatic).
        /// </summary>
        public double? StartPageRecentAchievementsGridRowHeight
        {
            get => StartPageRecentUnlocksGrid.RowHeight;
            set => StartPageRecentUnlocksGrid.RowHeight = value;
        }

        /// <summary>
        /// Fixed row height for the desktop theme achievement grid (null = automatic).
        /// </summary>
        public double? DesktopThemeAchievementGridRowHeight
        {
            get => _desktopThemeAchievementGridRowHeight;
            set => SetValue(ref _desktopThemeAchievementGridRowHeight, NormalizeGridRowHeight(value));
        }

        /// <summary>
        /// Maximum rendered rows for the single-game achievement grid (null = unlimited).
        /// </summary>
        public int? SingleGameGridMaxRows
        {
            get => _singleGameGridMaxRows;
            set => SetValue(ref _singleGameGridMaxRows, NormalizeGridMaxRows(value));
        }

        /// <summary>
        /// Maximum rendered rows for the sidebar games overview grid (null = unlimited).
        /// </summary>
        public int? SidebarOverviewGridMaxRows
        {
            get => _sidebarOverviewGridMaxRows;
            set => SetValue(ref _sidebarOverviewGridMaxRows, NormalizeGridMaxRows(value));
        }

        /// <summary>
        /// Maximum rendered rows for the sidebar recent achievements grid (null = unlimited).
        /// </summary>
        public int? SidebarRecentAchievementsGridMaxRows
        {
            get => _sidebarRecentAchievementsGridMaxRows;
            set => SetValue(ref _sidebarRecentAchievementsGridMaxRows, NormalizeGridMaxRows(value));
        }

        /// <summary>
        /// Maximum rendered rows for the sidebar selected-game achievement grid (null = unlimited).
        /// </summary>
        public int? SidebarSelectedGameGridMaxRows
        {
            get => _sidebarSelectedGameGridMaxRows;
            set => SetValue(ref _sidebarSelectedGameGridMaxRows, NormalizeGridMaxRows(value));
        }

        /// <summary>
        /// Maximum rendered rows for the Start Page games overview grid (null = unlimited).
        /// Defaults to 25 to preserve the previous Start Page behavior.
        /// </summary>
        public int? StartPageGamesOverviewGridMaxRows
        {
            get => StartPageGamesOverviewGrid.MaxRows;
            set => StartPageGamesOverviewGrid.MaxRows = value;
        }

        /// <summary>
        /// Maximum rendered rows for the Start Page recent unlocks grid (null = unlimited).
        /// Defaults to 25 to preserve the previous Start Page behavior.
        /// </summary>
        public int? StartPageRecentAchievementsGridMaxRows
        {
            get => StartPageRecentUnlocksGrid.MaxRows;
            set => StartPageRecentUnlocksGrid.MaxRows = value;
        }

        /// <summary>
        /// Maximum rendered rows for the desktop theme achievement grid (null = unlimited).
        /// </summary>
        public int? DesktopThemeAchievementGridMaxRows
        {
            get => _desktopThemeAchievementGridMaxRows;
            set => SetValue(ref _desktopThemeAchievementGridMaxRows, NormalizeGridMaxRows(value));
        }

        public StartPageGamesOverviewGridSettings StartPageGamesOverviewGrid
        {
            get => _startPageGamesOverviewGrid ?? (_startPageGamesOverviewGrid = AttachStartPageSettings(
                new StartPageGamesOverviewGridSettings()));
            set
            {
                var normalized = value ?? new StartPageGamesOverviewGridSettings();
                if (ReferenceEquals(_startPageGamesOverviewGrid, normalized))
                {
                    return;
                }

                DetachStartPageSettings(_startPageGamesOverviewGrid);
                _startPageGamesOverviewGrid = AttachStartPageSettings(normalized);
                OnPropertyChanged();
            }
        }

        public StartPageRecentUnlocksGridSettings StartPageRecentUnlocksGrid
        {
            get => _startPageRecentUnlocksGrid ?? (_startPageRecentUnlocksGrid = AttachStartPageSettings(
                new StartPageRecentUnlocksGridSettings()));
            set
            {
                var normalized = value ?? new StartPageRecentUnlocksGridSettings();
                if (ReferenceEquals(_startPageRecentUnlocksGrid, normalized))
                {
                    return;
                }

                DetachStartPageSettings(_startPageRecentUnlocksGrid);
                _startPageRecentUnlocksGrid = AttachStartPageSettings(normalized);
                OnPropertyChanged();
            }
        }

        public StartPagePieWidgetSettings StartPagePieCharts
        {
            get => _startPagePieCharts ?? (_startPagePieCharts = AttachStartPageSettings(
                new StartPagePieWidgetSettings()));
            set => SetStartPagePieSettings(ref _startPagePieCharts, value, nameof(StartPagePieCharts));
        }

        /// <summary>
        /// When true, providers execute concurrently during refresh runs.
        /// Disable to force deterministic sequential provider execution.
        /// </summary>
        public bool EnableParallelProviderRefresh
        {
            get => _enableParallelProviderRefresh;
            set => SetValue(ref _enableParallelProviderRefresh, value);
        }

        /// <summary>
        /// Base delay in milliseconds for retry/backoff after transient errors.
        /// Default is 200ms. Higher values are safer for strict APIs but slower after failures.
        /// Set to 0 for fastest retry behavior.
        /// </summary>
        public int ScanDelayMs
        {
            get => _scanDelayMs;
            set => SetValue(ref _scanDelayMs, Math.Max(0, value));
        }

        /// <summary>
        /// Maximum retry attempts when encountering rate limit or transient errors.
        /// Default is 3. Each retry uses exponential backoff with jitter.
        /// </summary>
        public int MaxRetryAttempts
        {
            get => _maxRetryAttempts;
            set => SetValue(ref _maxRetryAttempts, Math.Max(0, Math.Min(value, 10)));
        }

        #endregion

        #region Theme Integration Settings

        #endregion

        #region UI Column Settings

        /// <summary>
        /// Persisted visibility state for shared achievement DataGrid columns.
        /// Key is a stable column identifier, value indicates whether the column is visible.
        /// </summary>
        public Dictionary<string, bool> DataGridColumnVisibility
        {
            get => _dataGridColumnVisibility;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _dataGridColumnVisibility, normalized);
            }
        }

        /// <summary>
        /// Legacy shared width storage for achievement DataGrid columns.
        /// New installs use scope-specific maps; this remains for backward compatibility fallback.
        /// </summary>
        public Dictionary<string, double> DataGridColumnWidths
        {
            get => _dataGridColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _dataGridColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Legacy shared order storage for achievement DataGrid columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> DataGridColumnOrder
        {
            get => _dataGridColumnOrder;
            set => SetValue(ref _dataGridColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted widths for sidebar achievement columns (recent + selected game panels).
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> SidebarAchievementColumnWidths
        {
            get => _sidebarAchievementColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _sidebarAchievementColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for sidebar recent achievement columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> SidebarAchievementColumnOrder
        {
            get => _sidebarAchievementColumnOrder;
            set => SetValue(ref _sidebarAchievementColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for sidebar recent achievement columns.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> SidebarAchievementColumnAlignments
        {
            get => _sidebarAchievementColumnAlignments;
            set => SetValue(ref _sidebarAchievementColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted widths for the sidebar selected game achievement columns.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> SidebarGameColumnWidths
        {
            get => _sidebarGameColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _sidebarGameColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for sidebar selected game achievement columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> SidebarGameColumnOrder
        {
            get => _sidebarGameColumnOrder;
            set => SetValue(ref _sidebarGameColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for sidebar selected-game achievement columns.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> SidebarGameColumnAlignments
        {
            get => _sidebarGameColumnAlignments;
            set => SetValue(ref _sidebarGameColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted widths for the single-game achievement columns.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> SingleGameColumnWidths
        {
            get => _singleGameColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _singleGameColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for the single-game achievement columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> SingleGameColumnOrder
        {
            get => _singleGameColumnOrder;
            set => SetValue(ref _singleGameColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for single-game achievement columns.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> SingleGameColumnAlignments
        {
            get => _singleGameColumnAlignments;
            set => SetValue(ref _singleGameColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted widths for desktop theme integration achievement columns.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> DesktopThemeColumnWidths
        {
            get => _desktopThemeColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _desktopThemeColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for desktop theme integration achievement columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> DesktopThemeColumnOrder
        {
            get => _desktopThemeColumnOrder;
            set => SetValue(ref _desktopThemeColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for desktop theme integration achievement columns.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> DesktopThemeColumnAlignments
        {
            get => _desktopThemeColumnAlignments;
            set => SetValue(ref _desktopThemeColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted visibility state for games overview columns in the sidebar.
        /// Key is a stable column identifier, value indicates whether the column is visible.
        /// </summary>
        public Dictionary<string, bool> GamesOverviewColumnVisibility
        {
            get => _gamesOverviewColumnVisibility;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _gamesOverviewColumnVisibility, normalized);
            }
        }

        /// <summary>
        /// Persisted widths for games overview columns in the sidebar.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> GamesOverviewColumnWidths
        {
            get => _gamesOverviewColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _gamesOverviewColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for games overview columns in the sidebar.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> GamesOverviewColumnOrder
        {
            get => _gamesOverviewColumnOrder;
            set => SetValue(ref _gamesOverviewColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for games overview columns in the sidebar.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> GamesOverviewColumnAlignments
        {
            get => _gamesOverviewColumnAlignments;
            set => SetValue(ref _gamesOverviewColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted visibility state for StartPage achievement grid columns.
        /// Key is a stable column identifier, value indicates whether the column is visible.
        /// </summary>
        public Dictionary<string, bool> StartPageAchievementColumnVisibility
        {
            get => _startPageAchievementColumnVisibility;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _startPageAchievementColumnVisibility, normalized);
            }
        }

        /// <summary>
        /// Persisted widths for StartPage achievement grid columns.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> StartPageAchievementColumnWidths
        {
            get => _startPageAchievementColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _startPageAchievementColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for StartPage achievement grid columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> StartPageAchievementColumnOrder
        {
            get => _startPageAchievementColumnOrder;
            set => SetValue(ref _startPageAchievementColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for StartPage achievement grid columns.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> StartPageAchievementColumnAlignments
        {
            get => _startPageAchievementColumnAlignments;
            set => SetValue(ref _startPageAchievementColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted visibility state for StartPage games overview grid columns.
        /// Key is a stable column identifier, value indicates whether the column is visible.
        /// </summary>
        public Dictionary<string, bool> StartPageGamesOverviewColumnVisibility
        {
            get => _startPageGamesOverviewColumnVisibility;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _startPageGamesOverviewColumnVisibility, normalized);
            }
        }

        /// <summary>
        /// Persisted widths for StartPage games overview grid columns.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> StartPageGamesOverviewColumnWidths
        {
            get => _startPageGamesOverviewColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _startPageGamesOverviewColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted order for StartPage games overview grid columns.
        /// Key is a stable column identifier, value is DisplayIndex.
        /// </summary>
        public Dictionary<string, int> StartPageGamesOverviewColumnOrder
        {
            get => _startPageGamesOverviewColumnOrder;
            set => SetValue(ref _startPageGamesOverviewColumnOrder, NormalizeColumnOrder(value));
        }

        /// <summary>
        /// Persisted cell text alignment overrides for StartPage games overview grid columns.
        /// Missing keys inherit the global GridCellAlignment setting.
        /// </summary>
        public Dictionary<string, GridAlignment> StartPageGamesOverviewColumnAlignments
        {
            get => _startPageGamesOverviewColumnAlignments;
            set => SetValue(ref _startPageGamesOverviewColumnAlignments, NormalizeColumnAlignments(value));
        }

        /// <summary>
        /// Persisted sidebar overview splitter position. Represents left column width
        /// as a ratio of the combined left and right overview columns.
        /// </summary>
        public double SidebarOverviewLeftColumnRatio
        {
            get => _sidebarOverviewLeftColumnRatio;
            set
            {
                var normalized = double.IsNaN(value) || double.IsInfinity(value)
                    ? DefaultSidebarOverviewLeftColumnRatio
                    : Math.Max(MinSidebarOverviewLeftColumnRatio, Math.Min(MaxSidebarOverviewLeftColumnRatio, value));
                SetValue(ref _sidebarOverviewLeftColumnRatio, normalized);
            }
        }

        /// <summary>
        /// Saved bounds for plugin-owned windows keyed by stable window name.
        /// </summary>
        public Dictionary<string, WindowPlacementState> WindowPlacements
        {
            get => _windowPlacements;
            set
            {
                var normalized = new Dictionary<string, WindowPlacementState>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        var key = (pair.Key ?? string.Empty).Trim();
                        var placement = pair.Value;
                        if (!string.IsNullOrWhiteSpace(key) && placement?.IsValid() == true)
                        {
                            normalized[key] = placement.Clone();
                        }
                    }
                }

                SetValue(ref _windowPlacements, normalized);
            }
        }

        /// <summary>
        /// Last selected range for the sidebar achievements-over-time chart.
        /// </summary>
        public TimelineRange SidebarTimelineRange
        {
            get => _sidebarTimelineRange;
            set => SetValue(ref _sidebarTimelineRange, value);
        }

        #endregion

        #region General Settings

        /// <summary>
        /// Indicates whether the user has completed the first-time setup flow.
        /// When false, the sidebar shows a landing page guiding users through initial configuration.
        /// </summary>
        public bool FirstTimeSetupCompleted
        {
            get => _firstTimeSetupCompleted;
            set => SetValue(ref _firstTimeSetupCompleted, value);
        }

        /// <summary>
        /// Indicates whether the user has seen the theme migration landing page.
        /// When false, the sidebar always shows the landing page to promote theme migration.
        /// </summary>
        public bool SeenThemeMigration
        {
            get => _seenThemeMigration;
            set => SetValue(ref _seenThemeMigration, value);
        }

        /// <summary>
        /// Cache mapping ThemePath -> last migrated theme.yaml Version.
        /// Used to detect themes that have been upgraded since migration and may need re-migration.
        /// </summary>
        public Dictionary<string, ThemeMigrationCacheEntry> ThemeMigrationVersionCache
        {
            get => _themeMigrationVersionCache;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, ThemeMigrationCacheEntry>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, ThemeMigrationCacheEntry>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _themeMigrationVersionCache, normalized);
            }
        }

        #endregion

        #region User Preferences (Survive Cache Clear)

        /// <summary>
        /// Game IDs that the user has explicitly excluded from achievement tracking.
        /// These exclusions persist across cache clears.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public HashSet<Guid> ExcludedGameIds
        {
            get => _excludedGameIds;
            set => SetValue(ref _excludedGameIds, value ?? new HashSet<Guid>());
        }

        /// <summary>
        /// Game IDs that are excluded from all summary surfaces.
        /// These exclusions persist across cache clears.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public HashSet<Guid> ExcludedFromSummariesGameIds
        {
            get => _excludedFromSummariesGameIds;
            set => SetValue(ref _excludedFromSummariesGameIds, value ?? new HashSet<Guid>());
        }

        /// <summary>
        /// Manual capstone selections. Key = Playnite Game ID, Value = Achievement ApiName.
        /// These selections persist across cache clears.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public Dictionary<Guid, string> ManualCapstones
        {
            get => _manualCapstones;
            set => SetValue(ref _manualCapstones, value ?? new Dictionary<Guid, string>());
        }

        /// <summary>
        /// Manual achievement order per game.
        /// Key = Playnite Game ID, Value = full ordered list of achievement ApiName values.
        /// These overrides persist across cache clears.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public Dictionary<Guid, List<string>> AchievementOrderOverrides
        {
            get => _achievementOrderOverrides;
            set => SetValue(ref _achievementOrderOverrides, NormalizeAchievementOrderOverrides(value));
        }

        /// <summary>
        /// Manual achievement category overrides per game.
        /// Key = Playnite Game ID, Value = map of Achievement ApiName -> Category.
        /// These overrides persist across cache clears.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public Dictionary<Guid, Dictionary<string, string>> AchievementCategoryOverrides
        {
            get => _achievementCategoryOverrides;
            set => SetValue(ref _achievementCategoryOverrides, NormalizeAchievementCategoryOverrides(value));
        }

        /// <summary>
        /// Manual achievement category type overrides per game.
        /// Key = Playnite Game ID, Value = map of Achievement ApiName -> CategoryType.
        /// Allowed values: Base, DLC, Singleplayer, Multiplayer.
        /// These overrides persist across cache clears.
        /// </summary>
        [JsonIgnore]
        [DontSerialize]
        public Dictionary<Guid, Dictionary<string, string>> AchievementCategoryTypeOverrides
        {
            get => _achievementCategoryTypeOverrides;
            set => SetValue(ref _achievementCategoryTypeOverrides, NormalizeAchievementCategoryTypeOverrides(value));
        }

        #endregion

        #region Tagging Settings

        /// <summary>
        /// Settings for Playnite tag integration, allowing games to be tagged
        /// based on their achievement status for filtering and organization.
        /// </summary>
        public TaggingSettings TaggingSettings
        {
            get => _taggingSettings;
            set => SetValue(ref _taggingSettings, value ?? new TaggingSettings());
        }

        #endregion

        #region StartPage Settings Helpers

        private void SetStartPagePieSettings(
            ref StartPagePieWidgetSettings field,
            StartPagePieWidgetSettings value,
            string propertyName)
        {
            var normalized = value ?? new StartPagePieWidgetSettings();
            if (ReferenceEquals(field, normalized))
            {
                return;
            }

            DetachStartPageSettings(field);
            field = AttachStartPageSettings(normalized);
            OnPropertyChanged(propertyName);
        }

        private void AttachStartPageSettingsHandlers()
        {
            _startPageGamesOverviewGrid = AttachStartPageSettings(
                _startPageGamesOverviewGrid ?? new StartPageGamesOverviewGridSettings());
            _startPageRecentUnlocksGrid = AttachStartPageSettings(
                _startPageRecentUnlocksGrid ?? new StartPageRecentUnlocksGridSettings());
            _startPagePieCharts = AttachStartPageSettings(
                _startPagePieCharts ?? new StartPagePieWidgetSettings());
        }

        private T AttachStartPageSettings<T>(T settings)
            where T : ObservableObject
        {
            if (settings != null)
            {
                settings.PropertyChanged -= StartPageSettings_PropertyChanged;
                settings.PropertyChanged += StartPageSettings_PropertyChanged;
            }

            return settings;
        }

        private void DetachStartPageSettings(ObservableObject settings)
        {
            if (settings != null)
            {
                settings.PropertyChanged -= StartPageSettings_PropertyChanged;
            }
        }

        private void StartPageSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var childPropertyName = e?.PropertyName;
            if (ReferenceEquals(sender, _startPageGamesOverviewGrid))
            {
                RaiseStartPageSettingsChanged(nameof(StartPageGamesOverviewGrid), childPropertyName);
                RaiseLegacyStartPageGridPropertyChanged(
                    childPropertyName,
                    nameof(StartPageGamesOverviewGridRowHeight),
                    nameof(StartPageGamesOverviewGridMaxRows));
                return;
            }

            if (ReferenceEquals(sender, _startPageRecentUnlocksGrid))
            {
                RaiseStartPageSettingsChanged(nameof(StartPageRecentUnlocksGrid), childPropertyName);
                RaiseLegacyStartPageGridPropertyChanged(
                    childPropertyName,
                    nameof(StartPageRecentAchievementsGridRowHeight),
                    nameof(StartPageRecentAchievementsGridMaxRows));
                return;
            }

            if (ReferenceEquals(sender, _startPagePieCharts))
            {
                RaiseStartPageSettingsChanged(nameof(StartPagePieCharts), childPropertyName);
            }
        }

        private void RaiseStartPageSettingsChanged(string parentPropertyName, string childPropertyName)
        {
            if (string.IsNullOrWhiteSpace(parentPropertyName))
            {
                return;
            }

            OnPropertyChanged(parentPropertyName);
            if (!string.IsNullOrWhiteSpace(childPropertyName))
            {
                OnPropertyChanged($"{parentPropertyName}.{childPropertyName}");
            }
        }

        private void RaiseLegacyStartPageGridPropertyChanged(
            string childPropertyName,
            string rowHeightPropertyName,
            string maxRowsPropertyName)
        {
            if (string.Equals(childPropertyName, nameof(StartPageGamesOverviewGridSettings.RowHeight), StringComparison.Ordinal))
            {
                OnPropertyChanged(rowHeightPropertyName);
            }
            else if (string.Equals(childPropertyName, nameof(StartPageGamesOverviewGridSettings.MaxRows), StringComparison.Ordinal))
            {
                OnPropertyChanged(maxRowsPropertyName);
            }
        }

        #endregion

        #region Clone Method

        /// <summary>
        /// Creates a deep copy of this PersistedSettings instance.
        /// Provider-specific settings are cloned via the ProviderSettings dictionary.
        /// </summary>
        public PersistedSettings Clone()
        {
            var clonedProviderSettings = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            if (this.ProviderSettings != null)
            {
                foreach (var kvp in this.ProviderSettings)
                {
                    clonedProviderSettings[kvp.Key] = kvp.Value?.DeepClone() as JObject;
                }
            }

            return new PersistedSettings
            {
                // Provider Settings Dictionary (contains all provider-specific settings)
                ProviderSettings = clonedProviderSettings,

                // Global Settings
                GlobalLanguage = this.GlobalLanguage,

                // Update and Refresh Settings
                EnablePeriodicUpdates = this.EnablePeriodicUpdates,
                IncludeHiddenGamesInBulkScans = this.IncludeHiddenGamesInBulkScans,
                PeriodicUpdateHours = this.PeriodicUpdateHours,
                RecentRefreshGamesCount = this.RecentRefreshGamesCount,
                CustomRefreshPresets = this.CustomRefreshPresets != null
                    ? new List<CustomRefreshPreset>(CustomRefreshPreset.NormalizePresets(this.CustomRefreshPresets, CustomRefreshPreset.MaxPresetCount))
                    : new List<CustomRefreshPreset>(),

                // Notification Settings
                EnableNotifications = this.EnableNotifications,
                NotifyPeriodicUpdates = this.NotifyPeriodicUpdates,
                NotifyOnRebuild = this.NotifyOnRebuild,

                // Display Preferences
                ShowHiddenIcon = this.ShowHiddenIcon,
                ShowHiddenTitle = this.ShowHiddenTitle,
                ShowHiddenDescription = this.ShowHiddenDescription,
                ShowHiddenSuffix = this.ShowHiddenSuffix,
                ShowLockedIcon = this.ShowLockedIcon,
                PreserveAchievementIconResolution = this.PreserveAchievementIconResolution,
                UseSeparateLockedIconsWhenAvailable = this.UseSeparateLockedIconsWhenAvailable,
                ShowRarityGlow = this.ShowRarityGlow,
                UseUniformRarityBadges = this.UseUniformRarityBadges,
                UseCoverImages = this.UseCoverImages,
                IncludeUnplayedGames = this.IncludeUnplayedGames,
                ShowSidebarCollectionScoreCard = this.ShowSidebarCollectionScoreCard,
                ShowSidebarPrestigeScoreCard = this.ShowSidebarPrestigeScoreCard,
                ShowSidebarPieCharts = this.ShowSidebarPieCharts,
                ShowSidebarGamesPieChart = this.ShowSidebarGamesPieChart,
                ShowSidebarProviderPieChart = this.ShowSidebarProviderPieChart,
                ShowSidebarRarityPieChart = this.ShowSidebarRarityPieChart,
                ShowSidebarTrophyPieChart = this.ShowSidebarTrophyPieChart,
                ShowSidebarPiePercentages = this.ShowSidebarPiePercentages,
                SidebarPieSmallSliceMode = this.SidebarPieSmallSliceMode,
                ShowSidebarBarCharts = this.ShowSidebarBarCharts,
                ShowSidebarGameMetadata = this.ShowSidebarGameMetadata,
                ShowTopMenuBarButton = this.ShowTopMenuBarButton,
                ShowCompactListRarityBar = this.ShowCompactListRarityBar,
                ShowCompletionBorder = this.ShowCompletionBorder,
                ShowOverviewGridColumnHeaders = this.ShowOverviewGridColumnHeaders,
                ShowAchievementGridColumnHeaders = this.ShowAchievementGridColumnHeaders,
                ShowDesktopThemeAchievementGridColumnHeaders = this.ShowDesktopThemeAchievementGridColumnHeaders,
                GridColumnHeaderAlignment = this.GridColumnHeaderAlignment,
                GridCellAlignment = this.GridCellAlignment,
                GridCellVerticalAlignment = this.GridCellVerticalAlignment,
                EnableAchievementCompactListControl = this.EnableAchievementCompactListControl,
                EnableAchievementDataGridControl = this.EnableAchievementDataGridControl,
                EnableAchievementCompactUnlockedListControl = this.EnableAchievementCompactUnlockedListControl,
                EnableAchievementCompactLockedListControl = this.EnableAchievementCompactLockedListControl,
                EnableAchievementProgressBarControl = this.EnableAchievementProgressBarControl,
                EnableAchievementStatsControl = this.EnableAchievementStatsControl,
                EnableAchievementButtonControl = this.EnableAchievementButtonControl,
                EnableAchievementViewItemControl = this.EnableAchievementViewItemControl,
                EnableAchievementPieChartControl = this.EnableAchievementPieChartControl,
                EnableAchievementBarChartControl = this.EnableAchievementBarChartControl,
                CompactListSortMode = this.CompactListSortMode,
                CompactListSortDescending = this.CompactListSortDescending,
                CompactUnlockedListSortMode = this.CompactUnlockedListSortMode,
                CompactUnlockedListSortDescending = this.CompactUnlockedListSortDescending,
                CompactLockedListSortMode = this.CompactLockedListSortMode,
                CompactLockedListSortDescending = this.CompactLockedListSortDescending,
                GamesOverviewGridSortMode = this.GamesOverviewGridSortMode,
                GamesOverviewGridSortDescending = this.GamesOverviewGridSortDescending,
                SidebarSelectedGameGridSortMode = this.SidebarSelectedGameGridSortMode,
                SidebarSelectedGameGridSortDescending = this.SidebarSelectedGameGridSortDescending,
                SingleGameGridSortMode = this.SingleGameGridSortMode,
                SingleGameGridSortDescending = this.SingleGameGridSortDescending,
                AchievementDataGridSortMode = this.AchievementDataGridSortMode,
                AchievementDataGridSortDescending = this.AchievementDataGridSortDescending,
                AchievementDataGridMaxHeight = this.AchievementDataGridMaxHeight,
                SingleGameGridRowHeight = this.SingleGameGridRowHeight,
                SidebarOverviewGridRowHeight = this.SidebarOverviewGridRowHeight,
                SidebarRecentAchievementsGridRowHeight = this.SidebarRecentAchievementsGridRowHeight,
                SidebarSelectedGameGridRowHeight = this.SidebarSelectedGameGridRowHeight,
                StartPageGamesOverviewGridRowHeight = this.StartPageGamesOverviewGridRowHeight,
                StartPageRecentAchievementsGridRowHeight = this.StartPageRecentAchievementsGridRowHeight,
                DesktopThemeAchievementGridRowHeight = this.DesktopThemeAchievementGridRowHeight,
                SingleGameGridMaxRows = this.SingleGameGridMaxRows,
                SidebarOverviewGridMaxRows = this.SidebarOverviewGridMaxRows,
                SidebarRecentAchievementsGridMaxRows = this.SidebarRecentAchievementsGridMaxRows,
                SidebarSelectedGameGridMaxRows = this.SidebarSelectedGameGridMaxRows,
                StartPageGamesOverviewGridMaxRows = this.StartPageGamesOverviewGridMaxRows,
                StartPageRecentAchievementsGridMaxRows = this.StartPageRecentAchievementsGridMaxRows,
                DesktopThemeAchievementGridMaxRows = this.DesktopThemeAchievementGridMaxRows,
                StartPageGamesOverviewGrid = this.StartPageGamesOverviewGrid?.Clone() ??
                    new StartPageGamesOverviewGridSettings(),
                StartPageRecentUnlocksGrid = this.StartPageRecentUnlocksGrid?.Clone() ??
                    new StartPageRecentUnlocksGridSettings(),
                StartPagePieCharts = this.StartPagePieCharts?.Clone() ??
                    new StartPagePieWidgetSettings(),
                EnableParallelProviderRefresh = this.EnableParallelProviderRefresh,
                ScanDelayMs = this.ScanDelayMs,
                MaxRetryAttempts = this.MaxRetryAttempts,

                // UI Column Settings
                DataGridColumnVisibility = this.DataGridColumnVisibility != null
                    ? new Dictionary<string, bool>(this.DataGridColumnVisibility, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                DataGridColumnWidths = this.DataGridColumnWidths != null
                    ? new Dictionary<string, double>(this.DataGridColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                DataGridColumnOrder = this.DataGridColumnOrder != null
                    ? new Dictionary<string, int>(this.DataGridColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                SidebarAchievementColumnWidths = this.SidebarAchievementColumnWidths != null
                    ? new Dictionary<string, double>(this.SidebarAchievementColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                SidebarAchievementColumnOrder = this.SidebarAchievementColumnOrder != null
                    ? new Dictionary<string, int>(this.SidebarAchievementColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                SidebarAchievementColumnAlignments = this.SidebarAchievementColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.SidebarAchievementColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                SidebarGameColumnWidths = this.SidebarGameColumnWidths != null
                    ? new Dictionary<string, double>(this.SidebarGameColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                SidebarGameColumnOrder = this.SidebarGameColumnOrder != null
                    ? new Dictionary<string, int>(this.SidebarGameColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                SidebarGameColumnAlignments = this.SidebarGameColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.SidebarGameColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                SingleGameColumnWidths = this.SingleGameColumnWidths != null
                    ? new Dictionary<string, double>(this.SingleGameColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                SingleGameColumnOrder = this.SingleGameColumnOrder != null
                    ? new Dictionary<string, int>(this.SingleGameColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                SingleGameColumnAlignments = this.SingleGameColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.SingleGameColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                DesktopThemeColumnWidths = this.DesktopThemeColumnWidths != null
                    ? new Dictionary<string, double>(this.DesktopThemeColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                DesktopThemeColumnOrder = this.DesktopThemeColumnOrder != null
                    ? new Dictionary<string, int>(this.DesktopThemeColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                DesktopThemeColumnAlignments = this.DesktopThemeColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.DesktopThemeColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                GamesOverviewColumnVisibility = this.GamesOverviewColumnVisibility != null
                    ? new Dictionary<string, bool>(this.GamesOverviewColumnVisibility, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                GamesOverviewColumnWidths = this.GamesOverviewColumnWidths != null
                    ? new Dictionary<string, double>(this.GamesOverviewColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                GamesOverviewColumnOrder = this.GamesOverviewColumnOrder != null
                    ? new Dictionary<string, int>(this.GamesOverviewColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                GamesOverviewColumnAlignments = this.GamesOverviewColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.GamesOverviewColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                StartPageAchievementColumnVisibility = this.StartPageAchievementColumnVisibility != null
                    ? new Dictionary<string, bool>(this.StartPageAchievementColumnVisibility, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                StartPageAchievementColumnWidths = this.StartPageAchievementColumnWidths != null
                    ? new Dictionary<string, double>(this.StartPageAchievementColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                StartPageAchievementColumnOrder = this.StartPageAchievementColumnOrder != null
                    ? new Dictionary<string, int>(this.StartPageAchievementColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                StartPageAchievementColumnAlignments = this.StartPageAchievementColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.StartPageAchievementColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                StartPageGamesOverviewColumnVisibility = this.StartPageGamesOverviewColumnVisibility != null
                    ? new Dictionary<string, bool>(this.StartPageGamesOverviewColumnVisibility, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                StartPageGamesOverviewColumnWidths = this.StartPageGamesOverviewColumnWidths != null
                    ? new Dictionary<string, double>(this.StartPageGamesOverviewColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                StartPageGamesOverviewColumnOrder = this.StartPageGamesOverviewColumnOrder != null
                    ? new Dictionary<string, int>(this.StartPageGamesOverviewColumnOrder, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                StartPageGamesOverviewColumnAlignments = this.StartPageGamesOverviewColumnAlignments != null
                    ? new Dictionary<string, GridAlignment>(this.StartPageGamesOverviewColumnAlignments, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase),
                SidebarOverviewLeftColumnRatio = this.SidebarOverviewLeftColumnRatio,
                WindowPlacements = this.WindowPlacements != null
                    ? this.WindowPlacements.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.Clone(),
                        StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, WindowPlacementState>(StringComparer.OrdinalIgnoreCase),
                SidebarTimelineRange = this.SidebarTimelineRange,

                // General Settings
                FirstTimeSetupCompleted = this.FirstTimeSetupCompleted,
                SeenThemeMigration = this.SeenThemeMigration,
                ThemeMigrationVersionCache = this.ThemeMigrationVersionCache != null
                    ? this.ThemeMigrationVersionCache.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value == null
                            ? null
                            : new ThemeMigrationCacheEntry
                            {
                                ThemeName = kvp.Value.ThemeName,
                                ThemePath = kvp.Value.ThemePath,
                                MigratedThemeVersion = kvp.Value.MigratedThemeVersion,
                                MigratedAtUtc = kvp.Value.MigratedAtUtc
                            },
                        StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, ThemeMigrationCacheEntry>(StringComparer.OrdinalIgnoreCase),

                // User Preferences (Survive Cache Clear)
                ExcludedGameIds = this.ExcludedGameIds != null
                    ? new HashSet<Guid>(this.ExcludedGameIds)
                    : new HashSet<Guid>(),
                ExcludedFromSummariesGameIds = this.ExcludedFromSummariesGameIds != null
                    ? new HashSet<Guid>(this.ExcludedFromSummariesGameIds)
                    : new HashSet<Guid>(),
                SeparateLockedIconEnabledGameIds = this.SeparateLockedIconEnabledGameIds != null
                    ? new HashSet<Guid>(this.SeparateLockedIconEnabledGameIds)
                    : new HashSet<Guid>(),
                ManualCapstones = this.ManualCapstones != null
                    ? new Dictionary<Guid, string>(this.ManualCapstones)
                    : new Dictionary<Guid, string>(),
                AchievementOrderOverrides = this.AchievementOrderOverrides != null
                    ? this.AchievementOrderOverrides.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value != null
                            ? new List<string>(kvp.Value)
                            : new List<string>())
                    : new Dictionary<Guid, List<string>>(),
                AchievementCategoryOverrides = this.AchievementCategoryOverrides != null
                    ? this.AchievementCategoryOverrides.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value != null
                            ? new Dictionary<string, string>(kvp.Value, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                    : new Dictionary<Guid, Dictionary<string, string>>(),
                AchievementCategoryTypeOverrides = this.AchievementCategoryTypeOverrides != null
                    ? this.AchievementCategoryTypeOverrides.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value != null
                            ? new Dictionary<string, string>(kvp.Value, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                    : new Dictionary<Guid, Dictionary<string, string>>(),

                // Tagging Settings
                TaggingSettings = this.TaggingSettings?.Clone() ?? new TaggingSettings()
            };
        }

        public static double? NormalizeGridRowHeight(double? value)
        {
            if (!value.HasValue ||
                double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) ||
                value.Value <= 0)
            {
                return null;
            }

            return Math.Max(MinimumGridRowHeight, value.Value);
        }

        public static int? NormalizeGridMaxRows(int? value)
        {
            if (!value.HasValue || value.Value <= 0)
            {
                return null;
            }

            return Math.Max(MinimumGridMaxRows, value.Value);
        }

        private static string NormalizePath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static Dictionary<string, int> NormalizeColumnOrder(Dictionary<string, int> value)
        {
            var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (value == null)
            {
                return normalized;
            }

            foreach (var pair in value)
            {
                var key = (pair.Key ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(key) && pair.Value >= 0)
                {
                    normalized[key] = pair.Value;
                }
            }

            return normalized;
        }

        private static Dictionary<string, GridAlignment> NormalizeColumnAlignments(Dictionary<string, GridAlignment> value)
        {
            var normalized = new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
            if (value == null)
            {
                return normalized;
            }

            foreach (var pair in value)
            {
                var key = (pair.Key ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(key) &&
                    Enum.IsDefined(typeof(GridAlignment), pair.Value))
                {
                    normalized[key] = pair.Value;
                }
            }

            return normalized;
        }

        private static Dictionary<Guid, List<string>> NormalizeAchievementOrderOverrides(
            Dictionary<Guid, List<string>> value)
        {
            var normalized = new Dictionary<Guid, List<string>>();
            if (value == null)
            {
                return normalized;
            }

            foreach (var pair in value)
            {
                var order = Services.AchievementOrderHelper.NormalizeApiNames(pair.Value);
                if (order.Count > 0)
                {
                    normalized[pair.Key] = order;
                }
            }

            return normalized;
        }

        private static Dictionary<Guid, Dictionary<string, string>> NormalizeAchievementCategoryOverrides(
            Dictionary<Guid, Dictionary<string, string>> value)
        {
            var normalized = new Dictionary<Guid, Dictionary<string, string>>();
            if (value == null)
            {
                return normalized;
            }

            foreach (var gamePair in value)
            {
                var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (gamePair.Value != null)
                {
                    foreach (var categoryPair in gamePair.Value)
                    {
                        var key = (categoryPair.Key ?? string.Empty).Trim();
                        var category = (categoryPair.Value ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(category))
                        {
                            continue;
                        }

                        categories[key] = category;
                    }
                }

                if (categories.Count > 0)
                {
                    normalized[gamePair.Key] = categories;
                }
            }

            return normalized;
        }

        private static Dictionary<Guid, Dictionary<string, string>> NormalizeAchievementCategoryTypeOverrides(
            Dictionary<Guid, Dictionary<string, string>> value)
        {
            var normalized = new Dictionary<Guid, Dictionary<string, string>>();
            if (value == null)
            {
                return normalized;
            }

            foreach (var gamePair in value)
            {
                var categoryTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (gamePair.Value != null)
                {
                    foreach (var categoryTypePair in gamePair.Value)
                    {
                        var key = (categoryTypePair.Key ?? string.Empty).Trim();
                        var categoryType = Services.AchievementCategoryTypeHelper.Normalize(categoryTypePair.Value);
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(categoryType))
                        {
                            continue;
                        }

                        categoryTypes[key] = categoryType;
                    }
                }

                if (categoryTypes.Count > 0)
                {
                    normalized[gamePair.Key] = categoryTypes;
                }
            }

            return normalized;
        }

        #endregion
    }
}



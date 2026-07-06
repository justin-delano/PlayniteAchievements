using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Controls
{
    public partial class GameSummariesGridControl : UserControl, IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private DataGridColumnLayoutService _columnPersistence;
        private bool _isAttached;
        private PersistedSettings _subscribedPersisted;
        private const double DefaultCoverColumnWidth = 96;
        private const double DefaultPlatformColumnWidth = 44;

        private static readonly IReadOnlyDictionary<string, double> DefaultImageColumnWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cover"] = DefaultCoverColumnWidth,
                ["GameSummaryPlatform"] = DefaultPlatformColumnWidth
            };

        private static readonly IReadOnlyDictionary<string, double> LegacyImageColumnRuntimeDefaults =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cover"] = 64,
                ["GameSummaryPlatform"] = 36
            };

        // Bind to FriendGameSummaryItem-only properties; valid only on the Friends Overview surface.
        private static readonly string[] FriendOnlyColumnKeys =
        {
            "FriendGameFriendsWithUnlocks",
            "FriendGameLastUnlock"
        };

        private static readonly string[] SelectedFriendOnlyColumnKeys =
        {
            "GameSummaryLastUnlock"
        };

        // Columns with no per-category meaning; dropped entirely from category-summaries grids.
        private static readonly string[] CategoryExcludedColumnKeys =
        {
            "GameSummaryPlatform",
            "GameSummaryPlaytime",
            "GameSummaryLastPlayed"
        };

        private static readonly string[] MirroredBadgeResourceKeys =
        {
            "PlayAch.Brush.CompletedGame",
            "BadgeCompletedGame",
            "BadgeRarityUltraRare",
            "BadgeRarityRare",
            "BadgeRarityUncommon",
            "BadgeRarityCommon",
            "TrophyPlatinum",
            "TrophyGold",
            "TrophySilver",
            "TrophyBronze"
        };

        // Defaults are applied only when a saved layout is missing a key.
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> DefaultVisibilityByColumnSettingsKey =
            new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.OrdinalIgnoreCase)
            {
                ["OverviewGameSummaries"] = CreateGameSummaryVisibility(),
                ["StartPageGameSummaries"] = CreateGameSummaryVisibility(
                    platform: false,
                    lastPlayed: false,
                    playtime: false,
                    total: false,
                    collectionScore: false,
                    prestigeScore: false),
                ["StartPageOverview"] = CreateGameSummaryVisibility(
                    platform: false,
                    lastPlayed: false,
                    playtime: false,
                    total: false,
                    collectionScore: false,
                    prestigeScore: false),
                // Single-game summary: Cover, Game, Progress, Total visible; the rest hidden.
                ["ViewAchievementsGameSummaries"] = CreateGameSummaryVisibility(),
                ["FriendsOverviewGameSummaries"] = CreateGameSummaryVisibility(
                    platform: true,
                    lastPlayed: false,
                    playtime: false,
                    progress: false,
                    total: false,
                    collectionScore: false,
                    prestigeScore: false,
                    friendsWithUnlocks: true,
                    lastFriendUnlock: true),
                ["FriendsOverviewSelectedFriendGameSummaries"] = CreateGameSummaryVisibility(
                    platform: true,
                    lastPlayed: true,
                    lastUnlock: true,
                    playtime: true,
                    progress: true,
                    total: true,
                    collectionScore: false,
                    prestigeScore: false),
                // Category-summaries surfaces: full game-summary set, Cover kept, platform/playtime/
                // last-played dropped (no per-category meaning). Friend-only columns are excluded at
                // attach time since category rows are plain GameSummaryItem.
                ["ViewAchievementsCategorySummaries"] = CreateCategorySummaryVisibility(),
                ["OverviewSelectedGameCategorySummaries"] = CreateCategorySummaryVisibility(),
                ["FriendsOverviewCategorySummaries"] = CreateCategorySummaryVisibility(),
                ["DesktopThemeCategorySummaries"] = CreateCategorySummaryVisibility()
            };

        private static IReadOnlyDictionary<string, bool> CreateCategorySummaryVisibility()
        {
            return CreateGameSummaryVisibility(
                cover: true,
                game: true,
                platform: false,
                lastPlayed: false,
                lastUnlock: true,
                playtime: false,
                progress: true,
                total: true,
                collectionScore: true,
                prestigeScore: true,
                points: true);
        }

        private static IReadOnlyDictionary<string, bool> CreateGameSummaryVisibility(
            bool cover = true,
            bool game = true,
            bool platform = false,
            bool lastPlayed = false,
            bool lastUnlock = false,
            bool playtime = false,
            bool progress = true,
            bool total = true,
            bool collectionScore = false,
            bool prestigeScore = false,
            bool friendsWithUnlocks = false,
            bool lastFriendUnlock = false,
            bool points = false)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cover"] = cover,
                ["GameSummaryName"] = game,
                ["GameSummaryPlatform"] = platform,
                ["GameSummaryLastPlayed"] = lastPlayed,
                ["GameSummaryLastUnlock"] = lastUnlock,
                ["GameSummaryPlaytime"] = playtime,
                ["GameSummaryProgression"] = progress,
                ["TotalAchievements"] = total,
                ["GameSummaryCollectionScore"] = collectionScore,
                ["GameSummaryPrestigeScore"] = prestigeScore,
                ["FriendGameFriendsWithUnlocks"] = friendsWithUnlocks,
                ["FriendGameLastUnlock"] = lastFriendUnlock,
                ["GameSummaryPoints"] = points
            };
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable<GameSummaryItem>),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(null));

        public IEnumerable<GameSummaryItem> ItemsSource
        {
            get => (IEnumerable<GameSummaryItem>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(GameSummaryItem),
                typeof(GameSummariesGridControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public GameSummaryItem SelectedItem
        {
            get => (GameSummaryItem)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty UseCoverImagesProperty =
            DependencyProperty.Register(
                nameof(UseCoverImages),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(false));

        public bool UseCoverImages
        {
            get => (bool)GetValue(UseCoverImagesProperty);
            set => SetValue(UseCoverImagesProperty, value);
        }

        public static readonly DependencyProperty FixedRowHeightProperty =
            DependencyProperty.Register(
                nameof(FixedRowHeight),
                typeof(double?),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(null, OnRowSizingChanged));

        public double? FixedRowHeight
        {
            get => (double?)GetValue(FixedRowHeightProperty);
            set => SetValue(FixedRowHeightProperty, value);
        }

        public static readonly DependencyProperty ShowMetadataPlatformProperty =
            DependencyProperty.Register(
                nameof(ShowMetadataPlatform),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(true));

        public bool ShowMetadataPlatform
        {
            get => (bool)GetValue(ShowMetadataPlatformProperty);
            set => SetValue(ShowMetadataPlatformProperty, value);
        }

        public static readonly DependencyProperty ShowMetadataPlaytimeProperty =
            DependencyProperty.Register(
                nameof(ShowMetadataPlaytime),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(true));

        public bool ShowMetadataPlaytime
        {
            get => (bool)GetValue(ShowMetadataPlaytimeProperty);
            set => SetValue(ShowMetadataPlaytimeProperty, value);
        }

        public static readonly DependencyProperty ShowMetadataRegionProperty =
            DependencyProperty.Register(
                nameof(ShowMetadataRegion),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(true));

        public bool ShowMetadataRegion
        {
            get => (bool)GetValue(ShowMetadataRegionProperty);
            set => SetValue(ShowMetadataRegionProperty, value);
        }

        public static readonly DependencyProperty ShowCompletionBorderProperty =
            DependencyProperty.Register(
                nameof(ShowCompletionBorder),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(true));

        public bool ShowCompletionBorder
        {
            get => (bool)GetValue(ShowCompletionBorderProperty);
            set => SetValue(ShowCompletionBorderProperty, value);
        }

        public static readonly DependencyProperty ColumnSettingsKeyProperty =
            DependencyProperty.Register(
                nameof(ColumnSettingsKey),
                typeof(string),
                typeof(GameSummariesGridControl),
                new PropertyMetadata("OverviewGameSummaries"));

        public string ColumnSettingsKey
        {
            get => (string)GetValue(ColumnSettingsKeyProperty);
            set => SetValue(ColumnSettingsKeyProperty, value);
        }

        public static readonly DependencyProperty LastPlayedDateModeProperty =
            DependencyProperty.Register(
                nameof(LastPlayedDateMode),
                typeof(DateDisplayMode),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(DateDisplayMode.DateAndTime));

        // Resolved per-surface display mode for the "Last Played" column; bound by the cell template.
        public DateDisplayMode LastPlayedDateMode
        {
            get => (DateDisplayMode)GetValue(LastPlayedDateModeProperty);
            private set => SetValue(LastPlayedDateModeProperty, value);
        }

        public static readonly DependencyProperty ShowColumnHeadersProperty =
            DependencyProperty.Register(
                nameof(ShowColumnHeaders),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(true, OnShowColumnHeadersChanged));

        public static readonly DependencyProperty DisableRowSelectionProperty =
            DependencyProperty.Register(
                nameof(DisableRowSelection),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(false));

        // When true, rows cannot stay selected/highlighted (used by informational single-row surfaces).
        public bool DisableRowSelection
        {
            get => (bool)GetValue(DisableRowSelectionProperty);
            set => SetValue(DisableRowSelectionProperty, value);
        }

        public bool ShowColumnHeaders
        {
            get => (bool)GetValue(ShowColumnHeadersProperty);
            set => SetValue(ShowColumnHeadersProperty, value);
        }

        public static readonly DependencyProperty DelayInitialRenderUntilNormalizedProperty =
            DependencyProperty.Register(
                nameof(DelayInitialRenderUntilNormalized),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(false, OnDelayInitialRenderUntilNormalizedChanged));

        public bool DelayInitialRenderUntilNormalized
        {
            get => (bool)GetValue(DelayInitialRenderUntilNormalizedProperty);
            set => SetValue(DelayInitialRenderUntilNormalizedProperty, value);
        }

        public static readonly DependencyProperty ControlBarProperty =
            DependencyProperty.Register(
                nameof(ControlBar),
                typeof(GridControlBarViewModel),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(null));

        public GridControlBarViewModel ControlBar
        {
            get => (GridControlBarViewModel)GetValue(ControlBarProperty);
            set => SetValue(ControlBarProperty, value);
        }

        public static readonly DependencyProperty ShowControlBarProperty =
            DependencyProperty.Register(
                nameof(ShowControlBar),
                typeof(bool),
                typeof(GameSummariesGridControl),
                new PropertyMetadata(true));

        public bool ShowControlBar
        {
            get => (bool)GetValue(ShowControlBarProperty);
            set => SetValue(ShowControlBarProperty, value);
        }

        public event SelectionChangedEventHandler SelectionChanged;

        public event EventHandler<DataGridSortingEventArgs> Sorting;

        public static readonly RoutedEvent RowPreviewMouseLeftButtonDownEvent =
            EventManager.RegisterRoutedEvent(
                nameof(RowPreviewMouseLeftButtonDown),
                RoutingStrategy.Bubble,
                typeof(MouseButtonEventHandler),
                typeof(GameSummariesGridControl));

        public event MouseButtonEventHandler RowPreviewMouseLeftButtonDown
        {
            add => AddHandler(RowPreviewMouseLeftButtonDownEvent, value);
            remove => RemoveHandler(RowPreviewMouseLeftButtonDownEvent, value);
        }

        public static readonly RoutedEvent RowPreviewMouseRightButtonDownEvent =
            EventManager.RegisterRoutedEvent(
                nameof(RowPreviewMouseRightButtonDown),
                RoutingStrategy.Bubble,
                typeof(MouseButtonEventHandler),
                typeof(GameSummariesGridControl));

        public event MouseButtonEventHandler RowPreviewMouseRightButtonDown
        {
            add => AddHandler(RowPreviewMouseRightButtonDownEvent, value);
            remove => RemoveHandler(RowPreviewMouseRightButtonDownEvent, value);
        }

        public static readonly RoutedEvent RowPreviewMouseRightButtonUpEvent =
            EventManager.RegisterRoutedEvent(
                nameof(RowPreviewMouseRightButtonUp),
                RoutingStrategy.Bubble,
                typeof(MouseButtonEventHandler),
                typeof(GameSummariesGridControl));

        public event MouseButtonEventHandler RowPreviewMouseRightButtonUp
        {
            add => AddHandler(RowPreviewMouseRightButtonUpEvent, value);
            remove => RemoveHandler(RowPreviewMouseRightButtonUpEvent, value);
        }

        public GameSummariesGridControl()
        {
            InitializeComponent();
            UpdateColumnHeadersVisibility();
        }

        public DataGrid InternalDataGrid => GameSummariesGrid;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isAttached)
            {
                return;
            }

            var settings = PlayniteAchievementsPlugin.Instance?.Settings;
            if (settings?.Persisted == null || GameSummariesGrid == null)
            {
                return;
            }

            UpdateColumnHeadersVisibility();
            UpdateRealizedRowHeights();
            MirrorBadgeResources();

            UpdateLastPlayedDateMode(settings);
            if (_subscribedPersisted == null)
            {
                _subscribedPersisted = settings.Persisted;
                _subscribedPersisted.PropertyChanged += OnPersistedSettingsChanged;
            }
            RarityAppearanceHelper.AppearanceChanged -= RarityAppearanceHelper_AppearanceChanged;
            RarityAppearanceHelper.AppearanceChanged += RarityAppearanceHelper_AppearanceChanged;

            DataGridAlignmentBehavior.SetColumnCellAlignmentOverridesProvider(
                GameSummariesGrid,
                () => GetAlignmentsByKey(settings));
            DataGridAlignmentBehavior.SetColumnCellVerticalAlignmentOverridesProvider(
                GameSummariesGrid,
                () => GetCellVerticalAlignmentsByKey(settings));
            DataGridAlignmentBehavior.SetColumnHeaderHorizontalAlignmentOverridesProvider(
                GameSummariesGrid,
                () => GetHeaderAlignmentsByKey(settings));

            _columnPersistence = new DataGridColumnLayoutService(
                GameSummariesGrid,
                Logger,
                () => GetWidthsByKey(settings),
                map => SetWidthsByKey(settings, map),
                () => GetVisibilityByKey(settings),
                map => SetVisibilityByKey(settings, map),
                () => SavePluginSettings(settings),
                defaultWidthSeeds: DefaultImageColumnWidthSeeds,
                getOrder: () => GetOrderByKey(settings),
                setOrder: map => SetOrderByKey(settings, map),
                getCellAlignments: () => GetAlignmentsByKey(settings),
                setCellAlignments: map => SetAlignmentsByKey(settings, map),
                getDefaultCellAlignment: () => settings.Persisted?.GridCellAlignment ?? GridAlignment.Left,
                getCellVerticalAlignments: () => GetCellVerticalAlignmentsByKey(settings),
                setCellVerticalAlignments: map => SetCellVerticalAlignmentsByKey(settings, map),
                getDefaultCellVerticalAlignment: () => settings.Persisted?.GridCellVerticalAlignment ?? GridVerticalAlignment.Center,
                getHeaderHorizontalAlignments: () => GetHeaderAlignmentsByKey(settings),
                setHeaderHorizontalAlignments: map => SetHeaderAlignmentsByKey(settings, map),
                getDefaultHeaderHorizontalAlignment: () => settings.Persisted?.GridColumnHeaderAlignment ?? GridAlignment.Center,
                applyCellAlignments: () => DataGridAlignmentBehavior.Refresh(GameSummariesGrid),
                isRuntimeDefaultWidth: IsLegacyImageColumnRuntimeDefaultWidth);
            _columnPersistence.DelayInitialRenderUntilNormalized = DelayInitialRenderUntilNormalized;
            ApplyFriendColumnRestrictions();
            _columnPersistence.Attach();
            _isAttached = true;
        }

        private static void OnShowColumnHeadersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GameSummariesGridControl control)
            {
                control.UpdateColumnHeadersVisibility();
            }
        }

        private static void OnDelayInitialRenderUntilNormalizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GameSummariesGridControl control && control._columnPersistence != null)
            {
                control._columnPersistence.DelayInitialRenderUntilNormalized = e.NewValue is bool value && value;
            }
        }

        private void UpdateColumnHeadersVisibility()
        {
            if (GameSummariesGrid != null)
            {
                GameSummariesGrid.HeadersVisibility = ShowColumnHeaders
                    ? DataGridHeadersVisibility.Column
                    : DataGridHeadersVisibility.None;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private void RarityAppearanceHelper_AppearanceChanged(object sender, EventArgs e)
        {
            Dispatcher?.BeginInvoke(new Action(MirrorBadgeResources));
        }

        private void MirrorBadgeResources()
        {
            foreach (var key in MirroredBadgeResourceKeys)
            {
                try
                {
                    var resource = Application.Current?.TryFindResource(key);
                    if (resource != null)
                    {
                        Resources[key] = resource;
                    }
                }
                catch
                {
                    // Keep local fallback resources if application resources are unavailable.
                }
            }
        }

        private static void OnRowSizingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GameSummariesGridControl control)
            {
                control.UpdateRealizedRowHeights();
            }
        }

        private void GameSummariesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            ApplyFixedRowHeight(e.Row);
        }

        private void UpdateRealizedRowHeights()
        {
            if (GameSummariesGrid == null)
            {
                return;
            }

            foreach (var item in GameSummariesGrid.Items)
            {
                if (GameSummariesGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                {
                    ApplyFixedRowHeight(row);
                }
            }
        }

        private void ApplyFixedRowHeight(DataGridRow row)
        {
            if (row == null)
            {
                return;
            }

            var fixedHeight = ResolveFixedRowHeight();
            if (fixedHeight.HasValue)
            {
                row.Height = fixedHeight.Value;
                row.MinHeight = fixedHeight.Value;
                return;
            }

            row.ClearValue(FrameworkElement.HeightProperty);
            row.ClearValue(FrameworkElement.MinHeightProperty);
        }

        private double? ResolveFixedRowHeight()
        {
            var height = FixedRowHeight;
            if (!height.HasValue ||
                double.IsNaN(height.Value) ||
                double.IsInfinity(height.Value) ||
                height.Value <= 0)
            {
                return null;
            }

            return Math.Max(PersistedSettings.MinimumGridRowHeight, height.Value);
        }

        private Dictionary<string, double> GetWidthsByKey(PlayniteAchievementsSettings settings)
        {
            return GetSurfaceSettings(settings)?.GetWidths();
        }

        private void SetWidthsByKey(PlayniteAchievementsSettings settings, Dictionary<string, double> map)
        {
            GetSurfaceSettings(settings)?.SetWidths(map);
        }

        private Dictionary<string, int> GetOrderByKey(PlayniteAchievementsSettings settings)
        {
            return GetSurfaceSettings(settings)?.GetOrder();
        }

        private void SetOrderByKey(PlayniteAchievementsSettings settings, Dictionary<string, int> map)
        {
            GetSurfaceSettings(settings)?.SetOrder(map);
        }

        private Dictionary<string, bool> GetVisibilityByKey(PlayniteAchievementsSettings settings)
        {
            var surfaceSettings = GetSurfaceSettings(settings);
            return surfaceSettings == null
                ? null
                : ApplyDefaultVisibility(surfaceSettings, surfaceSettings.GetVisibility());
        }

        private Dictionary<string, bool> ApplyDefaultVisibility(
            GameSummarySurfaceSettings surfaceSettings,
            Dictionary<string, bool> map)
        {
            if (surfaceSettings == null)
            {
                return map;
            }

            var defaults = GetDefaultVisibility(ColumnSettingsKey);
            if (defaults == null || defaults.Count == 0)
            {
                return map;
            }

            if (map == null)
            {
                map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                surfaceSettings.SetVisibility(map);
            }

            foreach (var pair in defaults)
            {
                if (!map.ContainsKey(pair.Key))
                {
                    map[pair.Key] = pair.Value;
                }
            }

            return map;
        }

        private static IReadOnlyDictionary<string, bool> GetDefaultVisibility(string columnSettingsKey)
        {
            if (!string.IsNullOrWhiteSpace(columnSettingsKey) &&
                DefaultVisibilityByColumnSettingsKey.TryGetValue(columnSettingsKey, out var defaults))
            {
                return defaults;
            }

            return DefaultVisibilityByColumnSettingsKey.TryGetValue("OverviewGameSummaries", out var fallback)
                ? fallback
                : null;
        }

        private void SetVisibilityByKey(PlayniteAchievementsSettings settings, Dictionary<string, bool> map)
        {
            GetSurfaceSettings(settings)?.SetVisibility(map);
        }

        private Dictionary<string, GridAlignment> GetAlignmentsByKey(PlayniteAchievementsSettings settings)
        {
            return GetSurfaceSettings(settings)?.GetAlignments();
        }

        private void SetAlignmentsByKey(PlayniteAchievementsSettings settings, Dictionary<string, GridAlignment> map)
        {
            GetSurfaceSettings(settings)?.SetAlignments(map);
        }

        private Dictionary<string, GridVerticalAlignment> GetCellVerticalAlignmentsByKey(PlayniteAchievementsSettings settings)
        {
            return GetSurfaceSettings(settings)?.GetVerticalAlignments();
        }

        private void SetCellVerticalAlignmentsByKey(
            PlayniteAchievementsSettings settings,
            Dictionary<string, GridVerticalAlignment> map)
        {
            GetSurfaceSettings(settings)?.SetVerticalAlignments(map);
        }

        private Dictionary<string, GridAlignment> GetHeaderAlignmentsByKey(PlayniteAchievementsSettings settings)
        {
            return GetSurfaceSettings(settings)?.GetHeaderAlignments();
        }

        private void SetHeaderAlignmentsByKey(PlayniteAchievementsSettings settings, Dictionary<string, GridAlignment> map)
        {
            GetSurfaceSettings(settings)?.SetHeaderAlignments(map);
        }

        private GameSummarySurfaceSettings GetSurfaceSettings(PlayniteAchievementsSettings settings)
        {
            var persisted = settings?.Persisted;
            return persisted == null
                ? null
                : CreateSurfaceSettings(persisted, ResolveSurface());
        }

        private static GameSummarySurfaceSettings CreateSurfaceSettings(
            PersistedSettings persisted,
            GridSurface surface)
        {
            switch (surface)
            {
                case GridSurface.StartPage:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.StartPageGameSummariesColumnWidths,
                        SetWidths = map => persisted.StartPageGameSummariesColumnWidths = map,
                        GetOrder = () => persisted.StartPageGameSummariesColumnOrder,
                        SetOrder = map => persisted.StartPageGameSummariesColumnOrder = map,
                        GetVisibility = () => persisted.StartPageGameSummariesColumnVisibility,
                        SetVisibility = map => persisted.StartPageGameSummariesColumnVisibility = map,
                        GetAlignments = () => persisted.StartPageGameSummariesColumnAlignments,
                        SetAlignments = map => persisted.StartPageGameSummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.StartPageGameSummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.StartPageGameSummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.StartPageGameSummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.StartPageGameSummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.StartPageGameSummariesLastPlayedDateMode
                    };
                case GridSurface.ViewAchievements:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.ViewAchievementsGameSummariesColumnWidths,
                        SetWidths = map => persisted.ViewAchievementsGameSummariesColumnWidths = map,
                        GetOrder = () => persisted.ViewAchievementsGameSummariesColumnOrder,
                        SetOrder = map => persisted.ViewAchievementsGameSummariesColumnOrder = map,
                        GetVisibility = () => persisted.ViewAchievementsGameSummariesColumnVisibility,
                        SetVisibility = map => persisted.ViewAchievementsGameSummariesColumnVisibility = map,
                        GetAlignments = () => persisted.ViewAchievementsGameSummariesColumnAlignments,
                        SetAlignments = map => persisted.ViewAchievementsGameSummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.ViewAchievementsGameSummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.ViewAchievementsGameSummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.ViewAchievementsGameSummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.ViewAchievementsGameSummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.ViewAchievementsGameSummariesLastPlayedDateMode
                    };
                case GridSurface.FriendsOverview:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.FriendsOverviewGameSummariesColumnWidths,
                        SetWidths = map => persisted.FriendsOverviewGameSummariesColumnWidths = map,
                        GetOrder = () => persisted.FriendsOverviewGameSummariesColumnOrder,
                        SetOrder = map => persisted.FriendsOverviewGameSummariesColumnOrder = map,
                        GetVisibility = () => persisted.FriendsOverviewGameSummariesColumnVisibility,
                        SetVisibility = map => persisted.FriendsOverviewGameSummariesColumnVisibility = map,
                        GetAlignments = () => persisted.FriendsOverviewGameSummariesColumnAlignments,
                        SetAlignments = map => persisted.FriendsOverviewGameSummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.FriendsOverviewGameSummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.FriendsOverviewGameSummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.FriendsOverviewGameSummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.FriendsOverviewGameSummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.FriendsOverviewGameSummariesLastPlayedDateMode
                    };
                case GridSurface.FriendsOverviewSelectedFriend:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.FriendsOverviewSelectedFriendGameSummariesColumnWidths,
                        SetWidths = map => persisted.FriendsOverviewSelectedFriendGameSummariesColumnWidths = map,
                        GetOrder = () => persisted.FriendsOverviewSelectedFriendGameSummariesColumnOrder,
                        SetOrder = map => persisted.FriendsOverviewSelectedFriendGameSummariesColumnOrder = map,
                        GetVisibility = () => persisted.FriendsOverviewSelectedFriendGameSummariesColumnVisibility,
                        SetVisibility = map => persisted.FriendsOverviewSelectedFriendGameSummariesColumnVisibility = map,
                        GetAlignments = () => persisted.FriendsOverviewSelectedFriendGameSummariesColumnAlignments,
                        SetAlignments = map => persisted.FriendsOverviewSelectedFriendGameSummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.FriendsOverviewSelectedFriendGameSummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.FriendsOverviewSelectedFriendGameSummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.FriendsOverviewSelectedFriendGameSummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.FriendsOverviewSelectedFriendGameSummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.FriendsOverviewGameSummariesLastPlayedDateMode
                    };
                case GridSurface.ViewAchievementsCategory:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.ViewAchievementsCategorySummariesColumnWidths,
                        SetWidths = map => persisted.ViewAchievementsCategorySummariesColumnWidths = map,
                        GetOrder = () => persisted.ViewAchievementsCategorySummariesColumnOrder,
                        SetOrder = map => persisted.ViewAchievementsCategorySummariesColumnOrder = map,
                        GetVisibility = () => persisted.ViewAchievementsCategorySummariesColumnVisibility,
                        SetVisibility = map => persisted.ViewAchievementsCategorySummariesColumnVisibility = map,
                        GetAlignments = () => persisted.ViewAchievementsCategorySummariesColumnAlignments,
                        SetAlignments = map => persisted.ViewAchievementsCategorySummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.ViewAchievementsCategorySummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.ViewAchievementsCategorySummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.ViewAchievementsCategorySummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.ViewAchievementsCategorySummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.ViewAchievementsGameSummariesLastPlayedDateMode
                    };
                case GridSurface.OverviewSelectedGameCategory:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.OverviewSelectedGameCategorySummariesColumnWidths,
                        SetWidths = map => persisted.OverviewSelectedGameCategorySummariesColumnWidths = map,
                        GetOrder = () => persisted.OverviewSelectedGameCategorySummariesColumnOrder,
                        SetOrder = map => persisted.OverviewSelectedGameCategorySummariesColumnOrder = map,
                        GetVisibility = () => persisted.OverviewSelectedGameCategorySummariesColumnVisibility,
                        SetVisibility = map => persisted.OverviewSelectedGameCategorySummariesColumnVisibility = map,
                        GetAlignments = () => persisted.OverviewSelectedGameCategorySummariesColumnAlignments,
                        SetAlignments = map => persisted.OverviewSelectedGameCategorySummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.OverviewSelectedGameCategorySummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.OverviewSelectedGameCategorySummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.OverviewSelectedGameCategorySummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.OverviewSelectedGameCategorySummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.OverviewGameSummariesLastPlayedDateMode
                    };
                case GridSurface.FriendsOverviewCategory:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.FriendsOverviewCategorySummariesColumnWidths,
                        SetWidths = map => persisted.FriendsOverviewCategorySummariesColumnWidths = map,
                        GetOrder = () => persisted.FriendsOverviewCategorySummariesColumnOrder,
                        SetOrder = map => persisted.FriendsOverviewCategorySummariesColumnOrder = map,
                        GetVisibility = () => persisted.FriendsOverviewCategorySummariesColumnVisibility,
                        SetVisibility = map => persisted.FriendsOverviewCategorySummariesColumnVisibility = map,
                        GetAlignments = () => persisted.FriendsOverviewCategorySummariesColumnAlignments,
                        SetAlignments = map => persisted.FriendsOverviewCategorySummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.FriendsOverviewCategorySummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.FriendsOverviewCategorySummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.FriendsOverviewCategorySummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.FriendsOverviewCategorySummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.FriendsOverviewGameSummariesLastPlayedDateMode
                    };
                case GridSurface.DesktopThemeCategory:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.DesktopThemeCategorySummariesColumnWidths,
                        SetWidths = map => persisted.DesktopThemeCategorySummariesColumnWidths = map,
                        GetOrder = () => persisted.DesktopThemeCategorySummariesColumnOrder,
                        SetOrder = map => persisted.DesktopThemeCategorySummariesColumnOrder = map,
                        GetVisibility = () => persisted.DesktopThemeCategorySummariesColumnVisibility,
                        SetVisibility = map => persisted.DesktopThemeCategorySummariesColumnVisibility = map,
                        GetAlignments = () => persisted.DesktopThemeCategorySummariesColumnAlignments,
                        SetAlignments = map => persisted.DesktopThemeCategorySummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.DesktopThemeCategorySummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.DesktopThemeCategorySummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.DesktopThemeCategorySummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.DesktopThemeCategorySummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.OverviewGameSummariesLastPlayedDateMode
                    };
                default:
                    return new GameSummarySurfaceSettings
                    {
                        GetWidths = () => persisted.OverviewGameSummariesColumnWidths,
                        SetWidths = map => persisted.OverviewGameSummariesColumnWidths = map,
                        GetOrder = () => persisted.OverviewGameSummariesColumnOrder,
                        SetOrder = map => persisted.OverviewGameSummariesColumnOrder = map,
                        GetVisibility = () => persisted.OverviewGameSummariesColumnVisibility,
                        SetVisibility = map => persisted.OverviewGameSummariesColumnVisibility = map,
                        GetAlignments = () => persisted.OverviewGameSummariesColumnAlignments,
                        SetAlignments = map => persisted.OverviewGameSummariesColumnAlignments = map,
                        GetVerticalAlignments = () => persisted.OverviewGameSummariesColumnVerticalAlignments,
                        SetVerticalAlignments = map => persisted.OverviewGameSummariesColumnVerticalAlignments = map,
                        GetHeaderAlignments = () => persisted.OverviewGameSummariesColumnHeaderAlignments,
                        SetHeaderAlignments = map => persisted.OverviewGameSummariesColumnHeaderAlignments = map,
                        GetLastPlayedDateMode = () => persisted.OverviewGameSummariesLastPlayedDateMode
                    };
            }
        }

        private sealed class GameSummarySurfaceSettings
        {
            public Func<Dictionary<string, double>> GetWidths { get; set; }
            public Action<Dictionary<string, double>> SetWidths { get; set; }
            public Func<Dictionary<string, int>> GetOrder { get; set; }
            public Action<Dictionary<string, int>> SetOrder { get; set; }
            public Func<Dictionary<string, bool>> GetVisibility { get; set; }
            public Action<Dictionary<string, bool>> SetVisibility { get; set; }
            public Func<Dictionary<string, GridAlignment>> GetAlignments { get; set; }
            public Action<Dictionary<string, GridAlignment>> SetAlignments { get; set; }
            public Func<Dictionary<string, GridVerticalAlignment>> GetVerticalAlignments { get; set; }
            public Action<Dictionary<string, GridVerticalAlignment>> SetVerticalAlignments { get; set; }
            public Func<Dictionary<string, GridAlignment>> GetHeaderAlignments { get; set; }
            public Action<Dictionary<string, GridAlignment>> SetHeaderAlignments { get; set; }
            public Func<DateDisplayMode> GetLastPlayedDateMode { get; set; }
        }

        private enum GridSurface
        {
            Overview,
            StartPage,
            ViewAchievements,
            FriendsOverview,
            FriendsOverviewSelectedFriend,
            ViewAchievementsCategory,
            OverviewSelectedGameCategory,
            FriendsOverviewCategory,
            DesktopThemeCategory
        }

        private GridSurface ResolveSurface()
        {
            if (string.Equals(ColumnSettingsKey, "StartPageGameSummaries", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ColumnSettingsKey, "StartPageOverview", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.StartPage;
            }

            if (string.Equals(ColumnSettingsKey, "ViewAchievementsGameSummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.ViewAchievements;
            }

            if (string.Equals(ColumnSettingsKey, "FriendsOverviewGameSummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.FriendsOverview;
            }

            if (string.Equals(ColumnSettingsKey, "FriendsOverviewSelectedFriendGameSummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.FriendsOverviewSelectedFriend;
            }

            if (string.Equals(ColumnSettingsKey, "ViewAchievementsCategorySummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.ViewAchievementsCategory;
            }

            if (string.Equals(ColumnSettingsKey, "OverviewSelectedGameCategorySummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.OverviewSelectedGameCategory;
            }

            if (string.Equals(ColumnSettingsKey, "FriendsOverviewCategorySummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.FriendsOverviewCategory;
            }

            if (string.Equals(ColumnSettingsKey, "DesktopThemeCategorySummaries", StringComparison.OrdinalIgnoreCase))
            {
                return GridSurface.DesktopThemeCategory;
            }

            return GridSurface.Overview;
        }

        private static bool IsCategorySurface(GridSurface surface)
        {
            return surface == GridSurface.ViewAchievementsCategory ||
                   surface == GridSurface.OverviewSelectedGameCategory ||
                   surface == GridSurface.FriendsOverviewCategory ||
                   surface == GridSurface.DesktopThemeCategory;
        }

        // Keep the friend columns out of every grid except Friends Overview: collapse them so
        // they never render and exclude them from the column visibility menu so they cannot be toggled on.
        private void ApplyFriendColumnRestrictions()
        {
            if (_columnPersistence == null)
            {
                return;
            }

            var surface = ResolveSurface();
            if (surface != GridSurface.FriendsOverview)
            {
                foreach (var key in FriendOnlyColumnKeys)
                {
                    _columnPersistence.ForcedCollapsedKeys.Add(key);
                    _columnPersistence.ExcludedVisibilityKeys.Add(key);
                }
            }

            if (surface != GridSurface.FriendsOverviewSelectedFriend)
            {
                foreach (var key in SelectedFriendOnlyColumnKeys)
                {
                    _columnPersistence.ForcedCollapsedKeys.Add(key);
                    _columnPersistence.ExcludedVisibilityKeys.Add(key);
                }
            }

            if (IsCategorySurface(surface))
            {
                foreach (var key in CategoryExcludedColumnKeys)
                {
                    _columnPersistence.ForcedCollapsedKeys.Add(key);
                    _columnPersistence.ExcludedVisibilityKeys.Add(key);
                }
            }
        }

        private void OnPersistedSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(PersistedSettings.OverviewGameSummariesLastPlayedDateMode) ||
                e.PropertyName == nameof(PersistedSettings.StartPageGameSummariesLastPlayedDateMode) ||
                e.PropertyName == nameof(PersistedSettings.ViewAchievementsGameSummariesLastPlayedDateMode) ||
                e.PropertyName == nameof(PersistedSettings.FriendsOverviewGameSummariesLastPlayedDateMode))
            {
                UpdateLastPlayedDateMode(PlayniteAchievementsPlugin.Instance?.Settings);
            }
        }

        private void UpdateLastPlayedDateMode(PlayniteAchievementsSettings settings)
        {
            var surfaceSettings = GetSurfaceSettings(settings);
            if (surfaceSettings != null)
            {
                LastPlayedDateMode = surfaceSettings.GetLastPlayedDateMode();
            }
        }

        private static bool IsLegacyImageColumnRuntimeDefaultWidth(string key, double width)
        {
            return !string.IsNullOrWhiteSpace(key) &&
                   LegacyImageColumnRuntimeDefaults.TryGetValue(key, out var legacyWidth) &&
                   Math.Abs(ColumnWidthNormalization.RoundPixelWidth(width) -
                            ColumnWidthNormalization.RoundPixelWidth(legacyWidth)) <= 0.2;
        }

        private static void SavePluginSettings(PlayniteAchievementsSettings settings)
        {
            var plugin = PlayniteAchievementsPlugin.Instance;
            if (plugin == null || settings == null)
            {
                return;
            }

            try
            {
                plugin.SavePluginSettings(settings);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to persist game summaries column settings.");
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DisableRowSelection && GameSummariesGrid?.SelectedItem != null)
            {
                // Defer to avoid re-entrant selection changes; keeps informational rows unselected.
                Dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (GameSummariesGrid != null)
                    {
                        GameSummariesGrid.SelectedIndex = -1;
                    }
                }));
                return;
            }

            SelectionChanged?.Invoke(sender, e);
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            Sorting?.Invoke(sender, e);
            if (e.Handled)
            {
                return;
            }

            DataGridSortingHelper.ApplyCollectionViewSorting(sender, e, GameSummariesGrid);
        }

        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ForwardRowMouseEvent(e, RowPreviewMouseLeftButtonDownEvent, sender);
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ForwardRowMouseEvent(e, RowPreviewMouseRightButtonDownEvent, sender);
        }

        private void DataGridRow_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ForwardRowMouseEvent(e, RowPreviewMouseRightButtonUpEvent, sender);
        }

        private void ForwardRowMouseEvent(MouseButtonEventArgs sourceEvent, RoutedEvent routedEvent, object source)
        {
            var forwardedEvent = new MouseButtonEventArgs(
                sourceEvent.MouseDevice,
                sourceEvent.Timestamp,
                sourceEvent.ChangedButton)
            {
                RoutedEvent = routedEvent,
                Source = source
            };
            RaiseEvent(forwardedEvent);
            if (forwardedEvent.Handled)
            {
                sourceEvent.Handled = true;
            }
        }

        private void DataGridColumnMenu_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            var header = VisualTreeHelpers.FindVisualParent<DataGridColumnHeader>(
                e.OriginalSource as DependencyObject);
            if (header?.Column == null)
            {
                return;
            }

            e.Handled = true;
            OpenColumnVisibilityMenu(grid, header, useControllerPlacement: false);
        }

        public bool OpenColumnVisibilityMenuForController()
        {
            var header = FullscreenControllerNavigationService.GetFocusedDataGridColumnHeader(GameSummariesGrid);
            if (header == null)
            {
                return false;
            }

            return OpenColumnVisibilityMenu(
                GameSummariesGrid,
                header,
                useControllerPlacement: true);
        }

        public bool IsColumnHeaderFocusedForController()
        {
            return FullscreenControllerNavigationService.IsFocusWithinDataGridColumnHeader(GameSummariesGrid);
        }

        public bool ActivateFocusedColumnHeaderForController()
        {
            return FullscreenControllerNavigationService.ActivateFocusedDataGridColumnHeader(GameSummariesGrid);
        }

        public bool OpenFocusedControlBarMenuForController()
        {
            return ControlBarHost?.OpenFocusedSelectorForController() == true;
        }

        public bool IsControlBarFocusedForController()
        {
            return ControlBarHost?.IsKeyboardFocusWithin == true;
        }

        public IList<UIElement> GetControlBarControllerElements()
        {
            return ControlBarHost?.GetControllerElements() ?? new List<UIElement>();
        }

        private bool OpenColumnVisibilityMenu(DataGrid grid, FrameworkElement owner, bool useControllerPlacement)
        {
            if (grid == null || owner == null)
            {
                return false;
            }

            var menu = _columnPersistence?.BuildColumnVisibilityMenu((owner as DataGridColumnHeader)?.Column);
            if (menu == null || menu.Items.Count == 0)
            {
                return false;
            }

            ContextMenuStyleHelper.ApplyAchievementContextMenuStyle(owner, menu);
            if (useControllerPlacement)
            {
                return FullscreenControllerNavigationService.OpenContextMenu(owner, menu);
            }

            menu.Placement = PlacementMode.Bottom;
            menu.PlacementTarget = owner;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
            return true;
        }

        public void SetSortIndicator(string sortMemberPath, ListSortDirection? direction)
        {
            DataGridSortingHelper.SetSortIndicator(GameSummariesGrid, sortMemberPath, direction);
        }

        public void Refresh()
        {
            _columnPersistence?.Refresh();
        }

        public void Dispose()
        {
            if (!_isAttached)
            {
                return;
            }

            _columnPersistence?.Dispose();
            _columnPersistence = null;
            if (_subscribedPersisted != null)
            {
                _subscribedPersisted.PropertyChanged -= OnPersistedSettingsChanged;
                _subscribedPersisted = null;
            }
            RarityAppearanceHelper.AppearanceChanged -= RarityAppearanceHelper_AppearanceChanged;
            DataGridAlignmentBehavior.SetColumnCellAlignmentOverridesProvider(GameSummariesGrid, null);
            DataGridAlignmentBehavior.SetColumnCellVerticalAlignmentOverridesProvider(GameSummariesGrid, null);
            DataGridAlignmentBehavior.SetColumnHeaderHorizontalAlignmentOverridesProvider(GameSummariesGrid, null);
            _isAttached = false;
        }
    }
}

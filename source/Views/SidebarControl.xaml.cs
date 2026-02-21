using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views
{
    public partial class SidebarControl : UserControl, IDisposable
    {
        private readonly SidebarViewModel _viewModel;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly AchievementManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private bool _isActive;
        private Guid? _lastSelectedOverviewGameId;
        private DataGridRow _pendingRightClickRow;
        private readonly Dictionary<DataGridColumn, EventHandler> _columnWidthChangedHandlers = new Dictionary<DataGridColumn, EventHandler>();
        private readonly Dictionary<string, double> _pendingSidebarAchievementWidthUpdates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _pendingGamesOverviewWidthUpdates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer _columnWidthSaveTimer;
        private bool _isApplyingPersistedColumnWidths;
        private bool _isColumnResizeInProgress;
        private string _lastSidebarAchievementResizedColumnKey;
        private string _lastGamesOverviewResizedColumnKey;
        private Dictionary<string, double> _pendingRecentAchievementToggleWidths;
        private Dictionary<string, double> _pendingGameAchievementToggleWidths;
        private const double MinimumColumnWidthRatio = 0.1;
        private const double WidthNormalizationSafetyPadding = 1.0;
        private static readonly IReadOnlyDictionary<string, double> DefaultSidebarAchievementColumnWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Achievement"] = 520,
                ["UnlockDate"] = 230,
                ["Rarity"] = 170,
                ["Points"] = 120
            };
        private static readonly IReadOnlyDictionary<string, double> DefaultGamesOverviewColumnWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["OverviewGameName"] = 500,
                ["OverviewLastPlayed"] = 240,
                ["OverviewProgression"] = 360,
                ["TotalAchievements"] = 180
            };

        public SidebarControl()
        {
            InitializeComponent();
            InitializeColumnWidthPersistence();
        }

        public SidebarControl(IPlayniteAPI api, ILogger logger, AchievementManager achievementManager, PlayniteAchievementsSettings settings)
        {
            InitializeComponent();
            InitializeColumnWidthPersistence();

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings;
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _playniteApi = api ?? throw new ArgumentNullException(nameof(api));

            _viewModel = new SidebarViewModel(achievementManager, api, logger, settings);
            DataContext = _viewModel;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            _viewModel.SetActive(false);
            ApplyPersistedColumnVisibilityToSidebarGrids();
            AttachColumnWidthChangeHandlersToSidebarGrids();
            ApplyPersistedColumnWidthsToSidebarGrids();
        }

        public void Activate()
        {
            if (_isActive)
            {
                return;
            }

            _isActive = true;
            _viewModel?.SetActive(true);
            ApplyPersistedColumnVisibilityToSidebarGrids();
            ApplyPersistedColumnWidthsToSidebarGrids();
        }

        public void Deactivate()
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            _viewModel?.SetActive(false);
        }

        /// <summary>
        /// Refreshes the view data. Called when settings are saved or when manual refresh is needed.
        /// </summary>
        public void RefreshView()
        {
            ApplyPersistedColumnVisibilityToSidebarGrids();
            ApplyPersistedColumnWidthsToSidebarGrids();
            _ = _viewModel?.RefreshViewAsync();
        }

        public void Dispose()
        {
            try
            {
                Deactivate();
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                }
                TearDownColumnWidthPersistence();
                _viewModel?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "SidebarControl dispose failed.");
            }
        }

        private void InitializeColumnWidthPersistence()
        {
            _columnWidthSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _columnWidthSaveTimer.Tick += ColumnWidthSaveTimer_Tick;
        }

        private void TearDownColumnWidthPersistence()
        {
            FlushQueuedColumnWidthUpdates();

            foreach (var pair in _columnWidthChangedHandlers.ToList())
            {
                TryDetachColumnWidthChangedHandler(pair.Key, pair.Value);
            }

            _columnWidthChangedHandlers.Clear();
            _pendingSidebarAchievementWidthUpdates.Clear();
            _pendingGamesOverviewWidthUpdates.Clear();
            DetachGridWidthNormalizationHandlers(GamesOverviewDataGrid);
            DetachGridWidthNormalizationHandlers(RecentAchievementsDataGrid);
            DetachGridWidthNormalizationHandlers(GameAchievementsDataGrid);

            if (_columnWidthSaveTimer != null)
            {
                _columnWidthSaveTimer.Stop();
                _columnWidthSaveTimer.Tick -= ColumnWidthSaveTimer_Tick;
                _columnWidthSaveTimer = null;
            }
        }

        private void AttachColumnWidthChangeHandlersToSidebarGrids()
        {
            AttachColumnWidthChangeHandlers(GamesOverviewDataGrid);
            AttachColumnWidthChangeHandlers(RecentAchievementsDataGrid);
            AttachColumnWidthChangeHandlers(GameAchievementsDataGrid);
            AttachGridWidthNormalizationHandlers(GamesOverviewDataGrid);
            AttachGridWidthNormalizationHandlers(RecentAchievementsDataGrid);
            AttachGridWidthNormalizationHandlers(GameAchievementsDataGrid);
        }

        private void AttachGridWidthNormalizationHandlers(DataGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            grid.Loaded -= GridWidthNormalization_Loaded;
            grid.Loaded += GridWidthNormalization_Loaded;
            grid.SizeChanged -= GridWidthNormalization_SizeChanged;
            grid.SizeChanged += GridWidthNormalization_SizeChanged;
            grid.PreviewMouseLeftButtonDown -= GridColumnResize_PreviewMouseLeftButtonDown;
            grid.PreviewMouseLeftButtonDown += GridColumnResize_PreviewMouseLeftButtonDown;
            grid.PreviewMouseLeftButtonUp -= GridColumnResize_PreviewMouseLeftButtonUp;
            grid.PreviewMouseLeftButtonUp += GridColumnResize_PreviewMouseLeftButtonUp;
            grid.LostMouseCapture -= GridColumnResize_LostMouseCapture;
            grid.LostMouseCapture += GridColumnResize_LostMouseCapture;
        }

        private void DetachGridWidthNormalizationHandlers(DataGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            grid.Loaded -= GridWidthNormalization_Loaded;
            grid.SizeChanged -= GridWidthNormalization_SizeChanged;
            grid.PreviewMouseLeftButtonDown -= GridColumnResize_PreviewMouseLeftButtonDown;
            grid.PreviewMouseLeftButtonUp -= GridColumnResize_PreviewMouseLeftButtonUp;
            grid.LostMouseCapture -= GridColumnResize_LostMouseCapture;
        }

        private void GridWidthNormalization_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                NormalizeGridColumnsToContainer(grid);
            }
        }

        private void GridWidthNormalization_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged)
            {
                return;
            }

            if (sender is DataGrid grid)
            {
                if (!grid.IsVisible || grid.ActualWidth <= 1)
                {
                    return;
                }

                var isSharedAchievementGrid = grid == RecentAchievementsDataGrid || grid == GameAchievementsDataGrid;
                var isVisibilityActivation = e.PreviousSize.Width <= 1;
                if (isSharedAchievementGrid && isVisibilityActivation && HasPendingSharedAchievementToggleWidths())
                {
                    return;
                }

                var shouldRescaleAll = !isSharedAchievementGrid || !isVisibilityActivation;

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (grid.IsLoaded && !_isColumnResizeInProgress)
                        {
                            NormalizeGridColumnsToContainer(grid, rescaleAll: shouldRescaleAll);
                        }
                    }),
                    DispatcherPriority.Render);
            }
        }

        private void GridColumnResize_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsColumnResizeThumbHit(e.OriginalSource as DependencyObject))
            {
                _isColumnResizeInProgress = true;
            }
        }

        private void GridColumnResize_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CompleteColumnResizeNormalization(sender as DataGrid);
        }

        private void GridColumnResize_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CompleteColumnResizeNormalization(sender as DataGrid);
        }

        private void CompleteColumnResizeNormalization(DataGrid grid)
        {
            if (!_isColumnResizeInProgress)
            {
                return;
            }

            _isColumnResizeInProgress = false;
            if (grid == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => NormalizeGridColumnsToContainer(grid)), DispatcherPriority.Background);
        }

        private void AttachColumnWidthChangeHandlers(DataGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            foreach (var column in grid.Columns)
            {
                AttachColumnWidthChangedHandler(grid, column);
            }
        }

        private void AttachColumnWidthChangedHandler(DataGrid ownerGrid, DataGridColumn column)
        {
            if (ownerGrid == null || column == null || _columnWidthChangedHandlers.ContainsKey(column))
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            if (descriptor == null)
            {
                return;
            }

            EventHandler handler = (_, __) => OnColumnWidthChanged(ownerGrid, column);
            descriptor.AddValueChanged(column, handler);
            _columnWidthChangedHandlers[column] = handler;
        }

        private static void TryDetachColumnWidthChangedHandler(DataGridColumn column, EventHandler handler)
        {
            if (column == null || handler == null)
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            descriptor?.RemoveValueChanged(column, handler);
        }

        private void OnColumnWidthChanged(DataGrid sourceGrid, DataGridColumn sourceColumn)
        {
            if (_isApplyingPersistedColumnWidths || !_isColumnResizeInProgress)
            {
                return;
            }

            if (sourceColumn == null || !sourceColumn.CanUserResize)
            {
                return;
            }

            var columnKey = GetPersistedColumnKey(sourceColumn);
            if (string.IsNullOrWhiteSpace(columnKey))
            {
                return;
            }

            var width = sourceColumn?.ActualWidth ?? 0;
            if (!IsValidPersistedColumnWidth(width))
            {
                return;
            }

            if (sourceGrid == RecentAchievementsDataGrid || sourceGrid == GameAchievementsDataGrid)
            {
                _lastSidebarAchievementResizedColumnKey = columnKey;
                QueueColumnWidthPersistence(_pendingSidebarAchievementWidthUpdates, columnKey, width);
                ApplyColumnWidthToGridByKey(RecentAchievementsDataGrid, columnKey, width);
                ApplyColumnWidthToGridByKey(GameAchievementsDataGrid, columnKey, width);
                return;
            }

            if (sourceGrid == GamesOverviewDataGrid)
            {
                _lastGamesOverviewResizedColumnKey = columnKey;
                QueueColumnWidthPersistence(_pendingGamesOverviewWidthUpdates, columnKey, width);
            }
        }

        private void ColumnWidthSaveTimer_Tick(object sender, EventArgs e)
        {
            _columnWidthSaveTimer?.Stop();
            var shouldNormalizeAchievements = _pendingSidebarAchievementWidthUpdates.Count > 0;
            var shouldNormalizeOverview = _pendingGamesOverviewWidthUpdates.Count > 0;
            FlushQueuedColumnWidthUpdates();

            if (_isColumnResizeInProgress)
            {
                return;
            }

            if (shouldNormalizeAchievements)
            {
                NormalizeSharedAchievementColumnsToContainer(RecentAchievementsDataGrid);
            }

            if (shouldNormalizeOverview)
            {
                NormalizeGridColumnsToContainer(GamesOverviewDataGrid);
            }
        }

        private void QueueColumnWidthPersistence(Dictionary<string, double> pendingMap, string columnKey, double width)
        {
            if (pendingMap == null || string.IsNullOrWhiteSpace(columnKey) || !IsValidPersistedColumnWidth(width))
            {
                return;
            }

            pendingMap[columnKey] = Math.Round(width, 2);
            _columnWidthSaveTimer?.Stop();
            _columnWidthSaveTimer?.Start();
        }

        private void FlushQueuedColumnWidthUpdates()
        {
            if (_settings?.Persisted == null)
            {
                return;
            }

            var changed = false;
            if (_pendingSidebarAchievementWidthUpdates.Count > 0)
            {
                changed |= FlushColumnWidthMap(_settings.Persisted.SidebarAchievementColumnWidths, _pendingSidebarAchievementWidthUpdates);
            }

            if (_pendingGamesOverviewWidthUpdates.Count > 0)
            {
                changed |= FlushColumnWidthMap(_settings.Persisted.GamesOverviewColumnWidths, _pendingGamesOverviewWidthUpdates);
            }

            if (changed)
            {
                SavePluginSettings();
            }
        }

        private static bool FlushColumnWidthMap(Dictionary<string, double> targetMap, Dictionary<string, double> pendingMap)
        {
            if (targetMap == null || pendingMap == null || pendingMap.Count == 0)
            {
                return false;
            }

            var changed = false;
            foreach (var update in pendingMap)
            {
                if (!IsValidPersistedColumnWidth(update.Value))
                {
                    continue;
                }

                if (!targetMap.TryGetValue(update.Key, out var existing) || Math.Abs(existing - update.Value) > 0.1)
                {
                    targetMap[update.Key] = update.Value;
                    changed = true;
                }
            }

            pendingMap.Clear();
            return changed;
        }

        private static bool IsValidPersistedColumnWidth(double width)
        {
            return !double.IsNaN(width) && !double.IsInfinity(width) && width > 0;
        }

        private void ClearLeftSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearLeftSearch();
        }

        private void ClearRightSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearRightSearch();
        }

        private void ClearGameSelection_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.IsGameSelected == true)
            {
                PrecomputeSharedAchievementColumnsForToggle(toGameSelected: false);
            }

            _viewModel?.ClearGameSelection();
            _lastSelectedOverviewGameId = null;
        }

        private void GamesOverview_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null || !(sender is DataGrid grid)) return;

            // Get the row that was clicked
            var hitTestResult = VisualTreeHelper.HitTest(grid, e.GetPosition(grid));
            if (hitTestResult == null) return;

            // Walk up the visual tree to find the DataGridRow
            DependencyObject current = hitTestResult.VisualHit;
            while (current != null && !(current is DataGridRow))
            {
                current = VisualTreeHelper.GetParent(current);
            }

            // If we found a row and it's already selected, deselect it
            if (current is DataGridRow row && row.IsSelected)
            {
                grid.SelectedItem = null;
                if (_viewModel.IsGameSelected)
                {
                    PrecomputeSharedAchievementColumnsForToggle(toGameSelected: false);
                }

                _viewModel.ClearGameSelection();
                _lastSelectedOverviewGameId = null;
                e.Handled = true;
            }
        }

        private void GamesOverview_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null) return;

            if (!(sender is DataGrid grid)) return;

            if (grid.SelectedItem is GameOverviewItem item)
            {
                var currentGameId = item.PlayniteGameId;
                var gameChanged = !_lastSelectedOverviewGameId.HasValue ||
                                  currentGameId != _lastSelectedOverviewGameId.Value;
                var wasGameSelected = _viewModel.IsGameSelected;

                if (!wasGameSelected)
                {
                    PrecomputeSharedAchievementColumnsForToggle(toGameSelected: true);
                }

                _viewModel.SelectedGame = item;
                _lastSelectedOverviewGameId = currentGameId;

                if (gameChanged)
                {
                    ResetAchievementsSortDirection();
                    ResetAchievementsScrollPosition();
                }
            }
            else
            {
                _lastSelectedOverviewGameId = null;
            }
        }

        private void PrecomputeSharedAchievementColumnsForToggle(bool toGameSelected)
        {
            _pendingRecentAchievementToggleWidths = null;
            _pendingGameAchievementToggleWidths = null;

            var sourceGrid = toGameSelected ? RecentAchievementsDataGrid : GameAchievementsDataGrid;
            if (sourceGrid == null || !sourceGrid.IsLoaded)
            {
                sourceGrid = toGameSelected ? GameAchievementsDataGrid : RecentAchievementsDataGrid;
            }

            if (sourceGrid == null || !sourceGrid.IsLoaded)
            {
                return;
            }

            if (TryBuildSharedAchievementWidthPlans(sourceGrid, rescaleAll: true, out var recentPlan, out var gamePlan))
            {
                _pendingRecentAchievementToggleWidths = recentPlan;
                _pendingGameAchievementToggleWidths = gamePlan;
                ApplySharedAchievementWidthPlans(recentPlan, gamePlan);
            }
        }

        private void ResetAchievementsScrollPosition()
        {
            if (GameAchievementsDataGrid == null) return;

            // Use Dispatcher to wait for collection to update and layout to render
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (GameAchievementsDataGrid == null) return;

                // Clear any selection
                GameAchievementsDataGrid.SelectedIndex = -1;

                if (GameAchievementsDataGrid.Items.Count > 0)
                {
                    GameAchievementsDataGrid.ScrollIntoView(GameAchievementsDataGrid.Items[0]);
                }

                // Also try to reset via ScrollViewer for more reliable scroll reset
                if (FindVisualChild<ScrollViewer>(GameAchievementsDataGrid) is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToTop();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ResetAchievementsSortDirection()
        {
            if (GameAchievementsDataGrid == null) return;

            // Clear all sort directions first
            foreach (var column in GameAchievementsDataGrid.Columns)
            {
                column.SortDirection = null;
            }

            // Set default sort on UnlockTime column to match data order (descending)
            var unlockTimeColumn = GameAchievementsDataGrid.Columns
                .FirstOrDefault(c => c.SortMemberPath == "UnlockTime");
            if (unlockTimeColumn != null)
            {
                unlockTimeColumn.SortDirection = ListSortDirection.Descending;
            }
        }

        private void ResetRecentAchievementsSortDirection()
        {
            if (RecentAchievementsDataGrid == null) return;

            // Clear all sort directions first
            foreach (var column in RecentAchievementsDataGrid.Columns)
            {
                column.SortDirection = null;
            }

            // Set default sort on UnlockTime column to match data order (descending)
            var unlockTimeColumn = RecentAchievementsDataGrid.Columns
                .FirstOrDefault(c => c.SortMemberPath == "UnlockTime");
            if (unlockTimeColumn != null)
            {
                unlockTimeColumn.SortDirection = ListSortDirection.Descending;
            }
        }

        private void ResetRecentAchievementsToDefaultSort()
        {
            if (_viewModel == null || RecentAchievementsDataGrid == null)
            {
                return;
            }

            if (IsRecentAchievementsDefaultSortApplied())
            {
                return;
            }

            _viewModel.SortDataGrid(RecentAchievementsDataGrid, "UnlockTime", ListSortDirection.Descending);
            ResetRecentAchievementsSortDirection();
        }

        private bool IsRecentAchievementsDefaultSortApplied()
        {
            if (RecentAchievementsDataGrid == null || RecentAchievementsDataGrid.Columns == null)
            {
                return false;
            }

            var unlockTimeColumn = RecentAchievementsDataGrid.Columns
                .FirstOrDefault(c => c?.SortMemberPath == "UnlockTime");
            if (unlockTimeColumn?.SortDirection != ListSortDirection.Descending)
            {
                return false;
            }

            foreach (var column in RecentAchievementsDataGrid.Columns)
            {
                if (column == null || column == unlockTimeColumn)
                {
                    continue;
                }

                if (column.SortDirection != null)
                {
                    return false;
                }
            }

            return true;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (e.PropertyName != nameof(SidebarViewModel.IsGameSelected))
            {
                return;
            }

            if (!_viewModel.IsGameSelected)
            {
                ResetRecentAchievementsToDefaultSort();
            }

            if (TryApplyPendingSharedAchievementToggleWidths())
            {
                return;
            }

            QueueActiveAchievementGridNormalization(rescaleAll: false);
        }

        private void QueueActiveAchievementGridNormalization(bool rescaleAll)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_viewModel == null)
                    {
                        return;
                    }

                    var activeGrid = _viewModel.IsGameSelected ? GameAchievementsDataGrid : RecentAchievementsDataGrid;
                    TryNormalizeActiveAchievementGrid(activeGrid, rescaleAll);
                }),
                DispatcherPriority.Loaded);
        }

        private bool TryNormalizeActiveAchievementGrid(DataGrid activeGrid, bool rescaleAll)
        {
            if (activeGrid == null ||
                !activeGrid.IsLoaded ||
                !activeGrid.IsVisible ||
                activeGrid.ActualWidth <= 1)
            {
                return false;
            }

            NormalizeSharedAchievementColumnsToContainer(activeGrid, rescaleAll);
            return true;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        private void AchievementRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is AchievementDisplayItem item)
            {
                _viewModel?.RevealAchievementCommand?.Execute(item);
            }
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                // Prevent right-click from changing row selection and causing refresh jitter.
                e.Handled = true;
                _pendingRightClickRow = row;
            }
        }

        private void DataGridRow_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                e.Handled = true;
                var targetRow = _pendingRightClickRow ?? row;
                _pendingRightClickRow = null;
                OpenContextMenuForRow(targetRow);
            }
        }

        private void OpenContextMenuForRow(DataGridRow row)
        {
            if (row == null || !row.IsLoaded || row.DataContext == null)
            {
                return;
            }

            // Build a fresh menu per-open to avoid stale item/context coupling.
            var menu = BuildContextMenu(row.DataContext);
            if (menu == null || menu.Items.Count == 0)
            {
                return;
            }

            row.ContextMenu = menu;
            menu.PlacementTarget = row;
            menu.IsOpen = true;
        }

        private ContextMenu BuildContextMenu(object rowData)
        {
            if (rowData is GameOverviewItem)
            {
                return CreateGameRowContextMenu(rowData);
            }

            if (rowData is AchievementDisplayItem || rowData is RecentAchievementItem)
            {
                return CreateAchievementRowContextMenu(rowData);
            }

            return null;
        }

        private ContextMenu CreateGameRowContextMenu(object rowData)
        {
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_RefreshGame",
                () => ExecuteViewModelCommand(_viewModel?.RefreshSingleGameCommand, rowData)));
            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_SetCapstone",
                () => OpenCapstoneForRow(rowData)));
            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_OpenGameInLibrary",
                () => ExecuteViewModelCommand(_viewModel?.OpenGameInLibraryCommand, rowData)));
            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_ClearData",
                () => ClearDataForRow(rowData)));

            // Add exclude/include toggle
            if (TryGetPlayniteGameId(rowData, out var gameId))
            {
                var plugin = PlayniteAchievementsPlugin.Instance;
                var isExcluded = plugin?.IsGameExcluded(gameId) ?? false;
                var excludeLabel = isExcluded
                    ? ResourceProvider.GetString("LOCPlayAch_Menu_IncludeGame")
                    : ResourceProvider.GetString("LOCPlayAch_Menu_ExcludeGame");
                menu.Items.Add(CreateMenuItem(excludeLabel,
                    () => plugin?.ToggleGameExclusion(gameId)));

                // Add RA game ID override options (only for RA-capable games)
                if (plugin?.IsRaCapable(gameId) == true)
                {
                    var hasOverride = plugin.HasRaGameIdOverride(gameId);
                    var raLabel = hasOverride
                        ? ResourceProvider.GetString("LOCPlayAch_Menu_ChangeRaGameId")
                        : ResourceProvider.GetString("LOCPlayAch_Menu_SetRaGameId");
                    menu.Items.Add(CreateMenuItem(raLabel,
                        () => plugin.ShowRaGameIdDialogForGame(gameId)));

                    if (hasOverride)
                    {
                        menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_ClearRaGameId",
                            () => plugin.ClearRaGameIdOverrideForGame(gameId)));
                    }
                }
            }

            return menu;
        }

        private ContextMenu CreateAchievementRowContextMenu(object rowData)
        {
            var menu = new ContextMenu();

            if (!IsCurrentSidebarGame(rowData))
            {
                menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_OpenGameInSidebar",
                    () => ExecuteViewModelCommand(_viewModel?.OpenGameInSidebarCommand, rowData)));
            }

            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_OpenGameInLibrary",
                () => ExecuteViewModelCommand(_viewModel?.OpenGameInLibraryCommand, rowData)));
            return menu;
        }

        private bool IsCurrentSidebarGame(object rowData)
        {
            if (_viewModel?.SelectedGame?.PlayniteGameId.HasValue != true)
            {
                return false;
            }

            if (!TryGetPlayniteGameId(rowData, out var rowGameId))
            {
                return false;
            }

            return rowGameId == _viewModel.SelectedGame.PlayniteGameId.Value;
        }

        private static bool TryGetPlayniteGameId(object parameter, out Guid gameId)
        {
            switch (parameter)
            {
                case GameOverviewItem game when game.PlayniteGameId.HasValue:
                    gameId = game.PlayniteGameId.Value;
                    return true;
                case AchievementDisplayItem achievement when achievement.PlayniteGameId.HasValue:
                    gameId = achievement.PlayniteGameId.Value;
                    return true;
                case RecentAchievementItem recent when recent.PlayniteGameId.HasValue:
                    gameId = recent.PlayniteGameId.Value;
                    return true;
                case Guid id when id != Guid.Empty:
                    gameId = id;
                    return true;
                default:
                    gameId = Guid.Empty;
                    return false;
            }
        }

        private MenuItem CreateMenuItem(string resourceKey, Action onClick)
        {
            var item = new MenuItem { Header = ResolveMenuText(resourceKey) };
            item.Click += (_, __) => onClick?.Invoke();
            return item;
        }

        private string ResolveMenuText(string resourceKey)
        {
            if (TryFindResource(resourceKey) is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return resourceKey;
        }

        private static void ExecuteViewModelCommand(ICommand command, object parameter)
        {
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }

        private void OpenCapstoneForRow(object rowData)
        {
            if (!TryGetPlayniteGameId(rowData, out var gameId))
            {
                return;
            }

            PlayniteAchievementsPlugin.Instance?.OpenCapstoneView(gameId);
        }

        private void ClearDataForRow(object rowData)
        {
            if (!TryGetPlayniteGameId(rowData, out var gameId))
            {
                return;
            }

            var game = _playniteApi?.Database?.Games?.Get(gameId);
            if (game == null)
            {
                return;
            }

            var result = _playniteApi?.Dialogs?.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_ConfirmSingle"), game.Name),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) ?? System.Windows.MessageBoxResult.None;

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _achievementManager.RemoveGameCache(game.Id);
                _playniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_SuccessSingle"), game.Name),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to clear cached data for game '{game.Name}' ({game.Id}).");
                _playniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_Failed"), ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void DataGridColumnMenu_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;
            if (FindVisualParent<DataGridRow>(source) != null)
            {
                return;
            }

            e.Handled = true;
            OpenColumnVisibilityMenu(grid, e.GetPosition(grid));
        }

        private void OpenColumnVisibilityMenu(DataGrid grid, Point position)
        {
            if (grid == null)
            {
                return;
            }

            var menu = BuildColumnVisibilityMenu(grid);
            if (menu == null || menu.Items.Count == 0)
            {
                return;
            }

            menu.Placement = PlacementMode.RelativePoint;
            menu.PlacementTarget = grid;
            menu.HorizontalOffset = position.X;
            menu.VerticalOffset = position.Y;
            menu.IsOpen = true;
        }

        private ContextMenu BuildColumnVisibilityMenu(DataGrid grid)
        {
            var menu = new ContextMenu();

            foreach (var item in BuildColumnVisibilityMenuItems(grid))
            {
                menu.Items.Add(item);
            }

            return menu;
        }

        private IEnumerable<MenuItem> BuildColumnVisibilityMenuItems(DataGrid grid)
        {
            if (grid == null)
            {
                yield break;
            }

            foreach (var column in grid.Columns.Where(ShouldIncludeColumnInVisibilityMenu))
            {
                var headerText = ResolveColumnHeaderText(column.Header);
                if (string.IsNullOrWhiteSpace(headerText))
                {
                    continue;
                }

                var targetColumn = column;
                var item = new MenuItem
                {
                    Header = headerText,
                    IsCheckable = true,
                    IsChecked = targetColumn.Visibility == Visibility.Visible,
                    StaysOpenOnClick = true
                };

                item.Click += (_, __) =>
                {
                    var isVisible = item.IsChecked;
                    targetColumn.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                    OnColumnVisibilityChanged(grid, targetColumn, isVisible);
                };

                yield return item;
            }
        }

        private static bool ShouldIncludeColumnInVisibilityMenu(DataGridColumn column)
        {
            if (column == null)
            {
                return false;
            }

            var header = ResolveColumnHeaderText(column.Header);
            return !string.IsNullOrWhiteSpace(header);
        }

        private static string ResolveColumnHeaderText(object header)
        {
            switch (header)
            {
                case string text:
                    return text;
                case TextBlock textBlock:
                    return textBlock.Text;
                default:
                    return header?.ToString() ?? string.Empty;
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T typed)
                {
                    return typed;
                }

                // VisualTreeHelper only works with Visual or Visual3D elements
                // For non-visual elements like Run, use logical tree or content parent
                if (child is Visual || child is Visual3D)
                {
                    child = VisualTreeHelper.GetParent(child);
                }
                else if (child is FrameworkContentElement frameworkContentElement)
                {
                    child = frameworkContentElement.Parent;
                }
                else
                {
                    // Fallback: try logical parent for FrameworkElement
                    child = (child as FrameworkElement)?.Parent;
                }
            }

            return null;
        }

        private void OnColumnVisibilityChanged(DataGrid sourceGrid, DataGridColumn sourceColumn, bool isVisible)
        {
            var columnKey = GetPersistedColumnKey(sourceColumn);
            if (string.IsNullOrWhiteSpace(columnKey))
            {
                return;
            }

            if (sourceGrid == GamesOverviewDataGrid)
            {
                PersistColumnVisibility(_settings?.Persisted?.GamesOverviewColumnVisibility, columnKey, isVisible);
                NormalizeGridColumnsToContainer(GamesOverviewDataGrid);
                return;
            }

            if (sourceGrid == RecentAchievementsDataGrid || sourceGrid == GameAchievementsDataGrid)
            {
                PersistColumnVisibility(_settings?.Persisted?.DataGridColumnVisibility, columnKey, isVisible);
                ApplyColumnVisibilityToGridByKey(RecentAchievementsDataGrid, columnKey, isVisible);
                ApplyColumnVisibilityToGridByKey(GameAchievementsDataGrid, columnKey, isVisible);
                NormalizeSharedAchievementColumnsToContainer(sourceGrid);
            }
        }

        private void ApplyPersistedColumnVisibilityToSidebarGrids()
        {
            ApplyPersistedColumnVisibility(RecentAchievementsDataGrid, _settings?.Persisted?.DataGridColumnVisibility);
            ApplyPersistedColumnVisibility(GameAchievementsDataGrid, _settings?.Persisted?.DataGridColumnVisibility);
            ApplyPersistedColumnVisibility(GamesOverviewDataGrid, _settings?.Persisted?.GamesOverviewColumnVisibility);
            NormalizeSharedAchievementColumnsToContainer(RecentAchievementsDataGrid);
            NormalizeGridColumnsToContainer(GamesOverviewDataGrid);
        }

        private void ApplyPersistedColumnWidthsToSidebarGrids()
        {
            EnsureDefaultColumnWidthSeeds();

            var sidebarMap = GetSidebarAchievementColumnWidthsForRead();
            ApplyPersistedColumnWidths(RecentAchievementsDataGrid, sidebarMap);
            ApplyPersistedColumnWidths(GameAchievementsDataGrid, sidebarMap);
            ApplyPersistedColumnWidths(GamesOverviewDataGrid, _settings?.Persisted?.GamesOverviewColumnWidths);
            NormalizeSharedAchievementColumnsToContainer(RecentAchievementsDataGrid);
            NormalizeGridColumnsToContainer(GamesOverviewDataGrid);
        }

        private void EnsureDefaultColumnWidthSeeds()
        {
            if (_settings?.Persisted == null)
            {
                return;
            }

            var changed = false;

            var sidebarMap = _settings.Persisted.SidebarAchievementColumnWidths;
            if (sidebarMap == null)
            {
                sidebarMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _settings.Persisted.SidebarAchievementColumnWidths = sidebarMap;
                changed = true;
            }

            changed |= EnsureDefaultColumnWidthSeeds(sidebarMap, DefaultSidebarAchievementColumnWidthSeeds);

            var overviewMap = _settings.Persisted.GamesOverviewColumnWidths;
            if (overviewMap == null)
            {
                overviewMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _settings.Persisted.GamesOverviewColumnWidths = overviewMap;
                changed = true;
            }

            changed |= EnsureDefaultColumnWidthSeeds(overviewMap, DefaultGamesOverviewColumnWidthSeeds);

            if (changed)
            {
                SavePluginSettings();
            }
        }

        private static bool EnsureDefaultColumnWidthSeeds(
            Dictionary<string, double> targetMap,
            IReadOnlyDictionary<string, double> defaultWidths)
        {
            if (targetMap == null || defaultWidths == null || defaultWidths.Count == 0)
            {
                return false;
            }

            var changed = false;
            foreach (var pair in defaultWidths)
            {
                if (!targetMap.TryGetValue(pair.Key, out var width) || !IsValidPersistedColumnWidth(width))
                {
                    targetMap[pair.Key] = pair.Value;
                    changed = true;
                }
            }

            return changed;
        }

        private void ApplyPersistedColumnVisibility(DataGrid grid, Dictionary<string, bool> map)
        {
            if (grid == null || map == null || map.Count == 0)
            {
                return;
            }

            foreach (var column in grid.Columns)
            {
                var key = GetPersistedColumnKey(column);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (map.TryGetValue(key, out var isVisible))
                {
                    column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private static void ApplyColumnVisibilityToGridByKey(DataGrid grid, string columnKey, bool isVisible)
        {
            if (grid == null || string.IsNullOrWhiteSpace(columnKey))
            {
                return;
            }

            foreach (var column in grid.Columns)
            {
                var key = GetPersistedColumnKey(column);
                if (string.Equals(key, columnKey, StringComparison.OrdinalIgnoreCase))
                {
                    column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void ApplyPersistedColumnWidths(DataGrid grid, Dictionary<string, double> map)
        {
            if (grid == null || map == null || map.Count == 0)
            {
                return;
            }

            _isApplyingPersistedColumnWidths = true;
            try
            {
                foreach (var column in grid.Columns)
                {
                    if (column == null || !column.CanUserResize)
                    {
                        continue;
                    }

                    var key = GetPersistedColumnKey(column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (map.TryGetValue(key, out var width) && IsValidPersistedColumnWidth(width))
                    {
                        column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
                    }
                }
            }
            finally
            {
                _isApplyingPersistedColumnWidths = false;
            }
        }

        private Dictionary<string, double> GetSidebarAchievementColumnWidthsForRead()
        {
            var merged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            var legacyMap = _settings?.Persisted?.DataGridColumnWidths;
            if (legacyMap != null)
            {
                foreach (var pair in legacyMap)
                {
                    if (IsValidPersistedColumnWidth(pair.Value))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            var sidebarMap = _settings?.Persisted?.SidebarAchievementColumnWidths;
            if (sidebarMap != null)
            {
                foreach (var pair in sidebarMap)
                {
                    if (IsValidPersistedColumnWidth(pair.Value))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            return merged;
        }

        private void ApplyColumnWidthToGridByKey(DataGrid grid, string columnKey, double width)
        {
            if (grid == null || string.IsNullOrWhiteSpace(columnKey) || !IsValidPersistedColumnWidth(width))
            {
                return;
            }

            _isApplyingPersistedColumnWidths = true;
            try
            {
                foreach (var column in grid.Columns)
                {
                    if (column == null || !column.CanUserResize)
                    {
                        continue;
                    }

                    var key = GetPersistedColumnKey(column);
                    if (string.Equals(key, columnKey, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Math.Abs(column.ActualWidth - width) > 0.1)
                        {
                            column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
                        }
                    }
                }
            }
            finally
            {
                _isApplyingPersistedColumnWidths = false;
            }
        }

        private void NormalizeGridColumnsToContainer(DataGrid grid, bool rescaleAll = false)
        {
            if (grid == null || !grid.IsLoaded)
            {
                return;
            }

            if (grid == RecentAchievementsDataGrid || grid == GameAchievementsDataGrid)
            {
                NormalizeSharedAchievementColumnsToContainer(grid, rescaleAll);
                return;
            }

            if (TryBuildNormalizedPersistedWidths(grid, _lastGamesOverviewResizedColumnKey, rescaleAll, preferredWidthsByKey: null, fallbackAvailableWidth: 0, out var normalized))
            {
                ApplyColumnWidthsByKey(grid, normalized);
            }
        }

        private void NormalizeSharedAchievementColumnsToContainer(DataGrid referenceGrid, bool rescaleAll = false)
        {
            if (!TryBuildSharedAchievementWidthPlans(referenceGrid, rescaleAll, out var recentNormalized, out var gameNormalized))
            {
                return;
            }

            ApplySharedAchievementWidthPlans(recentNormalized, gameNormalized);
        }

        private bool TryBuildSharedAchievementWidthPlans(
            DataGrid referenceGrid,
            bool rescaleAll,
            out Dictionary<string, double> recentNormalized,
            out Dictionary<string, double> gameNormalized)
        {
            recentNormalized = null;
            gameNormalized = null;
            if (referenceGrid == null || !referenceGrid.IsLoaded)
            {
                return false;
            }

            var fallbackAvailableWidth = GetPreferredSharedAchievementAvailableWidth(referenceGrid);
            var preferredWidthsByKey = GetPreferredSharedAchievementWidths(referenceGrid, fallbackAvailableWidth);
            var protectedColumnKey = _lastSidebarAchievementResizedColumnKey;

            var hasRecentPlan = TryBuildNormalizedPersistedWidths(
                RecentAchievementsDataGrid,
                protectedColumnKey,
                rescaleAll,
                preferredWidthsByKey,
                fallbackAvailableWidth,
                out recentNormalized);
            var hasGamePlan = TryBuildNormalizedPersistedWidths(
                GameAchievementsDataGrid,
                protectedColumnKey,
                rescaleAll,
                preferredWidthsByKey,
                fallbackAvailableWidth,
                out gameNormalized);

            if (!hasRecentPlan && !hasGamePlan)
            {
                if (TryBuildNormalizedPersistedWidths(referenceGrid, _lastSidebarAchievementResizedColumnKey, rescaleAll, preferredWidthsByKey, fallbackAvailableWidth, out var fallbackNormalized))
                {
                    if (referenceGrid == RecentAchievementsDataGrid)
                    {
                        recentNormalized = fallbackNormalized;
                        hasRecentPlan = true;
                    }
                    else if (referenceGrid == GameAchievementsDataGrid)
                    {
                        gameNormalized = fallbackNormalized;
                        hasGamePlan = true;
                    }
                }
            }

            return hasRecentPlan || hasGamePlan;
        }

        private void ApplySharedAchievementWidthPlans(
            Dictionary<string, double> recentNormalized,
            Dictionary<string, double> gameNormalized)
        {
            if (recentNormalized != null &&
                recentNormalized.Count > 0 &&
                RecentAchievementsDataGrid != null &&
                RecentAchievementsDataGrid.IsLoaded)
            {
                ApplyColumnWidthsByKey(RecentAchievementsDataGrid, recentNormalized);
            }

            if (gameNormalized != null &&
                gameNormalized.Count > 0 &&
                GameAchievementsDataGrid != null &&
                GameAchievementsDataGrid.IsLoaded)
            {
                ApplyColumnWidthsByKey(GameAchievementsDataGrid, gameNormalized);
            }
        }

        private bool HasPendingSharedAchievementToggleWidths()
        {
            return (_pendingRecentAchievementToggleWidths != null && _pendingRecentAchievementToggleWidths.Count > 0) ||
                   (_pendingGameAchievementToggleWidths != null && _pendingGameAchievementToggleWidths.Count > 0);
        }

        private bool TryApplyPendingSharedAchievementToggleWidths()
        {
            if (!HasPendingSharedAchievementToggleWidths())
            {
                return false;
            }

            var hasActivePlan = _viewModel?.IsGameSelected == true
                ? _pendingGameAchievementToggleWidths != null && _pendingGameAchievementToggleWidths.Count > 0
                : _pendingRecentAchievementToggleWidths != null && _pendingRecentAchievementToggleWidths.Count > 0;

            ApplySharedAchievementWidthPlans(_pendingRecentAchievementToggleWidths, _pendingGameAchievementToggleWidths);
            _pendingRecentAchievementToggleWidths = null;
            _pendingGameAchievementToggleWidths = null;
            return hasActivePlan;
        }

        private double GetPreferredSharedAchievementAvailableWidth(DataGrid referenceGrid)
        {
            var availableWidth = GetGridAvailableWidth(referenceGrid);
            if (IsValidPersistedColumnWidth(availableWidth))
            {
                return availableWidth;
            }

            var alternateGrid = referenceGrid == RecentAchievementsDataGrid ? GameAchievementsDataGrid : RecentAchievementsDataGrid;
            var alternateWidth = GetGridAvailableWidth(alternateGrid);
            if (IsValidPersistedColumnWidth(alternateWidth))
            {
                return alternateWidth;
            }

            return 0;
        }

        private Dictionary<string, double> GetPreferredSharedAchievementWidths(DataGrid referenceGrid, double fallbackAvailableWidth)
        {
            var preferred = CaptureResizableColumnWidths(referenceGrid, fallbackAvailableWidth);
            if (preferred.Count > 0)
            {
                return preferred;
            }

            var alternateGrid = referenceGrid == RecentAchievementsDataGrid ? GameAchievementsDataGrid : RecentAchievementsDataGrid;
            preferred = CaptureResizableColumnWidths(alternateGrid, fallbackAvailableWidth);
            if (preferred.Count > 0)
            {
                return preferred;
            }

            var persisted = GetSidebarAchievementColumnWidthsForRead();
            return persisted ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, double> CaptureResizableColumnWidths(DataGrid grid, double fallbackAvailableWidth)
        {
            var captured = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (grid == null || !grid.IsLoaded || grid.Columns == null || grid.Columns.Count == 0)
            {
                return captured;
            }

            var visibleColumns = grid.Columns
                .Where(column => column != null && column.Visibility == Visibility.Visible)
                .ToList();
            if (visibleColumns.Count == 0)
            {
                return captured;
            }

            var availableWidth = GetGridAvailableWidth(grid, fallbackAvailableWidth);
            if (!IsValidPersistedColumnWidth(availableWidth))
            {
                return captured;
            }

            var minimumColumnWidth = ResolveResizableMinimumColumnWidth(
                visibleColumns,
                GetContainerRelativeMinimumColumnWidth(availableWidth),
                availableWidth);
            var minimumColumnWidths = ApplyMinimumColumnWidth(visibleColumns, minimumColumnWidth);
            foreach (var column in visibleColumns)
            {
                if (column == null || !column.CanUserResize)
                {
                    continue;
                }

                var key = GetPersistedColumnKey(column);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var minWidth = GetColumnMinimumWidth(minimumColumnWidths, column, minimumColumnWidth);
                captured[key] = Math.Max(minWidth, GetCurrentColumnWidth(column));
            }

            return captured;
        }

        private bool TryBuildNormalizedPersistedWidths(
            DataGrid grid,
            string protectedColumnKey,
            bool rescaleAll,
            IReadOnlyDictionary<string, double> preferredWidthsByKey,
            double fallbackAvailableWidth,
            out Dictionary<string, double> normalized)
        {
            normalized = null;
            if (grid == null || grid.Columns == null || grid.Columns.Count == 0)
            {
                return false;
            }

            var visibleColumns = grid.Columns
                .Where(column => column != null && column.Visibility == Visibility.Visible)
                .ToList();
            if (visibleColumns.Count == 0)
            {
                return false;
            }

            var availableWidth = GetGridAvailableWidth(grid, fallbackAvailableWidth);
            if (!IsValidPersistedColumnWidth(availableWidth))
            {
                return false;
            }

            var minimumColumnWidth = ResolveResizableMinimumColumnWidth(
                visibleColumns,
                GetContainerRelativeMinimumColumnWidth(availableWidth),
                availableWidth);
            var minimumColumnWidths = ApplyMinimumColumnWidth(visibleColumns, minimumColumnWidth);

            var keyColumns = visibleColumns
                .Select(column => new
                {
                    Column = column,
                    Key = GetPersistedColumnKey(column),
                    MinimumWidth = GetColumnMinimumWidth(minimumColumnWidths, column, minimumColumnWidth),
                    IsResizable = column.CanUserResize,
                    SeedWidth = ResolveSeedWidth(
                        GetPersistedColumnKey(column),
                        column,
                        preferredWidthsByKey,
                        GetColumnMinimumWidth(minimumColumnWidths, column, minimumColumnWidth))
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.IsResizable)
                .ToList();
            if (keyColumns.Count == 0)
            {
                return false;
            }

            var fixedWidth = visibleColumns
                .Where(column => string.IsNullOrWhiteSpace(GetPersistedColumnKey(column)) || !column.CanUserResize)
                .Sum(column => Math.Max(GetColumnMinimumWidth(minimumColumnWidths, column, minimumColumnWidth), GetCurrentColumnWidth(column)));

            var targetWidth = Math.Max(0, availableWidth - fixedWidth - WidthNormalizationSafetyPadding);
            if (targetWidth <= 0)
            {
                return false;
            }

            var keys = new List<string>(keyColumns.Count);
            var floorWidths = new List<double>(keyColumns.Count);
            var widths = new List<double>(keyColumns.Count);
            for (var i = 0; i < keyColumns.Count; i++)
            {
                var entry = keyColumns[i];
                keys.Add(entry.Key);
                floorWidths.Add(entry.MinimumWidth);
                widths.Add(Math.Max(entry.MinimumWidth, entry.SeedWidth));
            }

            var totalWidth = widths.Sum();
            var delta = targetWidth - totalWidth;
            if (Math.Abs(delta) > 0.2)
            {
                if (rescaleAll)
                {
                    RescaleWidthsProportionally(widths, floorWidths, targetWidth);
                }
                else
                {
                    var absorberOrder = BuildAbsorberOrder(keys, protectedColumnKey);
                    if (absorberOrder.Count == 0)
                    {
                        absorberOrder.Add(keys.Count - 1);
                    }

                    if (delta > 0)
                    {
                        widths[absorberOrder[0]] += delta;
                    }
                    else
                    {
                        foreach (var index in absorberOrder)
                        {
                            var capacity = widths[index] - floorWidths[index];
                            if (capacity <= 0)
                            {
                                continue;
                            }

                            var take = Math.Min(capacity, -delta);
                            widths[index] -= take;
                            delta += take;
                            if (delta >= -0.2)
                            {
                                break;
                            }
                        }

                        if (delta < -0.2)
                        {
                            var fallback = absorberOrder[0];
                            var fallbackBefore = widths[fallback];
                            widths[fallback] = Math.Max(floorWidths[fallback], widths[fallback] + delta);
                            delta += widths[fallback] - fallbackBefore;
                        }

                        if (delta < -0.2)
                        {
                            var protectedIndex = -1;
                            for (var i = 0; i < keys.Count; i++)
                            {
                                if (KeysEqual(keys[i], protectedColumnKey))
                                {
                                    protectedIndex = i;
                                    break;
                                }
                            }

                            if (protectedIndex >= 0)
                            {
                                var protectedCapacity = widths[protectedIndex] - floorWidths[protectedIndex];
                                if (protectedCapacity > 0)
                                {
                                    var take = Math.Min(protectedCapacity, -delta);
                                    widths[protectedIndex] -= take;
                                    delta += take;
                                }
                            }
                        }

                        if (delta < -0.2)
                        {
                            RescaleWidthsProportionally(widths, floorWidths, targetWidth);
                        }
                    }
                }
            }

            normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < keys.Count; i++)
            {
                normalized[keys[i]] = Math.Max(floorWidths[i], widths[i]);
            }

            return true;
        }

        private static double ResolveSeedWidth(
            string key,
            DataGridColumn column,
            IReadOnlyDictionary<string, double> preferredWidthsByKey,
            double fallbackMinimumWidth)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                preferredWidthsByKey != null &&
                preferredWidthsByKey.TryGetValue(key, out var preferredWidth) &&
                IsValidPersistedColumnWidth(preferredWidth))
            {
                return preferredWidth;
            }

            var currentWidth = GetCurrentColumnWidth(column);
            if (IsValidPersistedColumnWidth(currentWidth))
            {
                return currentWidth;
            }

            return fallbackMinimumWidth;
        }

        private static double GetContainerRelativeMinimumColumnWidth(double availableWidth)
        {
            if (!IsValidPersistedColumnWidth(availableWidth))
            {
                return 1;
            }

            return Math.Max(1, Math.Round(availableWidth * MinimumColumnWidthRatio, 2));
        }

        private static double ResolveResizableMinimumColumnWidth(
            IReadOnlyList<DataGridColumn> visibleColumns,
            double preferredMinimumWidth,
            double availableWidth)
        {
            if (!IsValidPersistedColumnWidth(preferredMinimumWidth) ||
                !IsValidPersistedColumnWidth(availableWidth) ||
                visibleColumns == null ||
                visibleColumns.Count == 0)
            {
                return Math.Max(1, preferredMinimumWidth);
            }

            var resizableColumns = visibleColumns
                .Where(column =>
                    column != null &&
                    column.CanUserResize &&
                    !string.IsNullOrWhiteSpace(GetPersistedColumnKey(column)))
                .ToList();
            if (resizableColumns.Count == 0)
            {
                return Math.Max(1, preferredMinimumWidth);
            }

            var fixedWidth = visibleColumns
                .Where(column => column == null || !column.CanUserResize || string.IsNullOrWhiteSpace(GetPersistedColumnKey(column)))
                .Sum(GetCurrentColumnWidth);
            var availableForResizable = Math.Max(1, availableWidth - fixedWidth - WidthNormalizationSafetyPadding);
            var maxFittableMinimum = Math.Max(1, availableForResizable / resizableColumns.Count);
            return Math.Max(1, Math.Min(preferredMinimumWidth, maxFittableMinimum));
        }

        private static Dictionary<DataGridColumn, double> ApplyMinimumColumnWidth(IReadOnlyList<DataGridColumn> columns, double minimumColumnWidth)
        {
            var result = new Dictionary<DataGridColumn, double>();
            if (columns == null || !IsValidPersistedColumnWidth(minimumColumnWidth))
            {
                return result;
            }

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (column == null)
                {
                    continue;
                }

                var resolvedMinWidth = ResolveColumnMinimumWidth(column, minimumColumnWidth);
                result[column] = resolvedMinWidth;

                if (Math.Abs(column.MinWidth - resolvedMinWidth) > 0.2)
                {
                    column.MinWidth = resolvedMinWidth;
                }

                if (!column.CanUserResize)
                {
                    var resolvedMaxWidth = ResolveFixedColumnMaximumWidth(column, resolvedMinWidth);
                    if (Math.Abs(column.MaxWidth - resolvedMaxWidth) > 0.2)
                    {
                        column.MaxWidth = resolvedMaxWidth;
                    }
                }
            }

            return result;
        }

        private static double ResolveColumnMinimumWidth(DataGridColumn column, double fallbackMinWidth)
        {
            if (column != null && !column.CanUserResize)
            {
                if (IsValidPersistedColumnWidth(column.MinWidth))
                {
                    return column.MinWidth;
                }

                var currentWidth = GetCurrentColumnWidth(column);
                if (IsValidPersistedColumnWidth(currentWidth))
                {
                    return currentWidth;
                }
            }

            return fallbackMinWidth;
        }

        private static double ResolveFixedColumnMaximumWidth(DataGridColumn column, double fallbackWidth)
        {
            if (column != null && IsValidPersistedColumnWidth(column.MaxWidth))
            {
                return column.MaxWidth;
            }

            var currentWidth = GetCurrentColumnWidth(column);
            if (IsValidPersistedColumnWidth(currentWidth))
            {
                return currentWidth;
            }

            return fallbackWidth;
        }

        private static double GetColumnMinimumWidth(Dictionary<DataGridColumn, double> minimumColumnWidths, DataGridColumn column, double fallbackMinWidth)
        {
            if (minimumColumnWidths != null &&
                column != null &&
                minimumColumnWidths.TryGetValue(column, out var resolvedWidth) &&
                IsValidPersistedColumnWidth(resolvedWidth))
            {
                return resolvedWidth;
            }

            return fallbackMinWidth;
        }

        private static List<int> BuildAbsorberOrder(IReadOnlyList<string> keys, string protectedColumnKey)
        {
            var order = new List<int>();
            if (keys == null || keys.Count == 0)
            {
                return order;
            }

            var preferredIndex = -1;
            for (var i = keys.Count - 1; i >= 0; i--)
            {
                if (KeysEqual(keys[i], protectedColumnKey))
                {
                    continue;
                }

                preferredIndex = i;
                break;
            }

            if (preferredIndex >= 0)
            {
                order.Add(preferredIndex);
            }

            for (var i = keys.Count - 1; i >= 0; i--)
            {
                if (i == preferredIndex || KeysEqual(keys[i], protectedColumnKey))
                {
                    continue;
                }

                order.Add(i);
            }

            if (order.Count == 0)
            {
                order.Add(keys.Count - 1);
            }

            return order;
        }

        private static void RescaleWidthsProportionally(IList<double> widths, IReadOnlyList<double> floorWidths, double targetWidth)
        {
            if (widths == null ||
                floorWidths == null ||
                widths.Count == 0 ||
                widths.Count != floorWidths.Count ||
                !IsValidPersistedColumnWidth(targetWidth))
            {
                return;
            }

            var weights = widths.Select(w => Math.Max(1, w)).ToList();
            var remainingTarget = targetWidth;
            var remainingWeight = weights.Sum();
            var remainingMinimum = floorWidths.Sum();
            for (var i = 0; i < widths.Count; i++)
            {
                var floorWidth = floorWidths[i];
                remainingMinimum -= floorWidth;
                var next = i == widths.Count - 1
                    ? remainingTarget
                    : remainingTarget * (weights[i] / remainingWeight);

                next = Math.Max(floorWidth, next);
                var maxForCurrent = remainingTarget - remainingMinimum;
                if (next > maxForCurrent)
                {
                    next = maxForCurrent;
                }

                widths[i] = next;
                remainingTarget -= next;
                remainingWeight -= weights[i];
            }
        }

        private static bool KeysEqual(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyColumnWidthsByKey(DataGrid grid, Dictionary<string, double> widthsByKey)
        {
            if (grid == null || widthsByKey == null || widthsByKey.Count == 0)
            {
                return;
            }

            _isApplyingPersistedColumnWidths = true;
            try
            {
                foreach (var column in grid.Columns)
                {
                    if (column == null || !column.CanUserResize)
                    {
                        continue;
                    }

                    var key = GetPersistedColumnKey(column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (!widthsByKey.TryGetValue(key, out var width) || !IsValidPersistedColumnWidth(width))
                    {
                        continue;
                    }

                    if (Math.Abs(GetCurrentColumnWidth(column) - width) <= 0.2)
                    {
                        continue;
                    }

                    column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
                }
            }
            finally
            {
                _isApplyingPersistedColumnWidths = false;
            }
        }

        private double GetGridAvailableWidth(DataGrid grid, double fallbackWidth = 0)
        {
            var width = grid?.ActualWidth ?? 0;
            if (!IsValidPersistedColumnWidth(width))
            {
                return IsValidPersistedColumnWidth(fallbackWidth) ? fallbackWidth : 0;
            }

            var scrollViewer = FindVisualChild<ScrollViewer>(grid);
            var viewportWidth = scrollViewer?.ViewportWidth ?? 0;
            if (IsValidPersistedColumnWidth(viewportWidth))
            {
                return Math.Max(0, viewportWidth);
            }

            var chrome = grid.BorderThickness.Left + grid.BorderThickness.Right + grid.Padding.Left + grid.Padding.Right + 2;
            width -= chrome;

            if (scrollViewer != null)
            {
                var computedScrollBarWidth = scrollViewer.ActualWidth - scrollViewer.ViewportWidth;
                if (IsValidPersistedColumnWidth(computedScrollBarWidth))
                {
                    width -= computedScrollBarWidth;
                }
                else if (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible ||
                         scrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Visible)
                {
                    width -= SystemParameters.VerticalScrollBarWidth;
                }
            }

            var resolved = Math.Max(0, width);
            if (!IsValidPersistedColumnWidth(resolved) && IsValidPersistedColumnWidth(fallbackWidth))
            {
                return fallbackWidth;
            }

            return resolved;
        }

        private static double GetCurrentColumnWidth(DataGridColumn column)
        {
            if (column == null)
            {
                return 0;
            }

            if (IsValidPersistedColumnWidth(column.ActualWidth))
            {
                return column.ActualWidth;
            }

            var display = column.Width.DisplayValue;
            return IsValidPersistedColumnWidth(display) ? display : 0;
        }

        private static bool IsColumnResizeThumbHit(DependencyObject source)
        {
            while (source != null)
            {
                if (source is Thumb thumb &&
                    (string.Equals(thumb.Name, "PART_LeftHeaderGripper", StringComparison.Ordinal) ||
                     string.Equals(thumb.Name, "PART_RightHeaderGripper", StringComparison.Ordinal)))
                {
                    return true;
                }

                source = GetParentForHitTesting(source);
            }

            return false;
        }

        private static DependencyObject GetParentForHitTesting(DependencyObject source)
        {
            if (source == null)
            {
                return null;
            }

            if (source is Visual || source is Visual3D)
            {
                return VisualTreeHelper.GetParent(source);
            }

            if (source is FrameworkContentElement frameworkContentElement)
            {
                return frameworkContentElement.Parent;
            }

            if (source is ContentElement contentElement)
            {
                return ContentOperations.GetParent(contentElement);
            }

            return null;
        }

        private void PersistColumnVisibility(Dictionary<string, bool> map, string columnKey, bool isVisible)
        {
            if (map == null || string.IsNullOrWhiteSpace(columnKey) || _settings?.Persisted == null)
            {
                return;
            }

            if (map.TryGetValue(columnKey, out var existing) && existing == isVisible)
            {
                return;
            }

            map[columnKey] = isVisible;
            SavePluginSettings();
        }

        private void SavePluginSettings()
        {
            var plugin = PlayniteAchievementsPlugin.Instance;
            if (plugin == null || _settings == null)
            {
                return;
            }

            try
            {
                plugin.SavePluginSettings(_settings);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to persist sidebar column layout settings.");
            }
        }

        private static string GetPersistedColumnKey(DataGridColumn column)
        {
            if (column == null)
            {
                return null;
            }

            var key = ColumnVisibilityHelper.GetColumnKey(column);
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key;
            }

            if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
            {
                return column.SortMemberPath;
            }

            return null;
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (_viewModel == null) return;

            e.Handled = true;

            var column = e.Column;
            if (column == null || string.IsNullOrEmpty(column.SortMemberPath)) return;

            var sortDirection = ListSortDirection.Ascending;
            if (column.SortDirection != null && column.SortDirection == ListSortDirection.Ascending)
            {
                sortDirection = ListSortDirection.Descending;
            }

            _viewModel.SortDataGrid((sender as DataGrid), column.SortMemberPath, sortDirection);

            foreach (var c in (sender as DataGrid).Columns)
            {
                if (c != column)
                {
                    c.SortDirection = null;
                }
            }
            column.SortDirection = sortDirection;
        }
    }
}

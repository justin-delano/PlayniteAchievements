using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        private readonly AchievementManager _achievementManager;
        private readonly PlayniteAchievementsSettings _settings;
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
        private const double MinimumNormalizedColumnWidth = 40;

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
            _achievementManager = achievementManager;
            _settings = settings;

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
                NormalizeGridColumnsToContainer(grid);
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
            if (_isApplyingPersistedColumnWidths)
            {
                return;
            }

            var columnKey = GetPersistedColumnKey(sourceColumn);
            if (string.IsNullOrWhiteSpace(columnKey))
            {
                if (!_isColumnResizeInProgress)
                {
                    NormalizeGridColumnsToContainer(sourceGrid);
                }
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
                if (!_isColumnResizeInProgress)
                {
                    NormalizeSharedAchievementColumnsToContainer(sourceGrid);
                }
                return;
            }

            if (sourceGrid == GamesOverviewDataGrid)
            {
                _lastGamesOverviewResizedColumnKey = columnKey;
                QueueColumnWidthPersistence(_pendingGamesOverviewWidthUpdates, columnKey, width);
                if (!_isColumnResizeInProgress)
                {
                    NormalizeGridColumnsToContainer(sourceGrid);
                }
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

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearSearch();
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

            _viewModel.SortDataGrid(RecentAchievementsDataGrid, "UnlockTime", ListSortDirection.Descending);
            ResetRecentAchievementsSortDirection();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (e.PropertyName == nameof(SidebarViewModel.IsGameSelected) && !_viewModel.IsGameSelected)
            {
                ResetRecentAchievementsToDefaultSort();
            }
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
            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_ScanGame",
                () => ExecuteViewModelCommand(_viewModel?.ScanSingleGameCommand, rowData)));
            menu.Items.Add(CreateMenuItem("LOCPlayAch_Menu_OpenGameInLibrary",
                () => ExecuteViewModelCommand(_viewModel?.OpenGameInLibraryCommand, rowData)));
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

                child = VisualTreeHelper.GetParent(child);
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
            var sidebarMap = GetSidebarAchievementColumnWidthsForRead();
            ApplyPersistedColumnWidths(RecentAchievementsDataGrid, sidebarMap);
            ApplyPersistedColumnWidths(GameAchievementsDataGrid, sidebarMap);
            ApplyPersistedColumnWidths(GamesOverviewDataGrid, _settings?.Persisted?.GamesOverviewColumnWidths);
            NormalizeSharedAchievementColumnsToContainer(RecentAchievementsDataGrid);
            NormalizeGridColumnsToContainer(GamesOverviewDataGrid);
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

        private void NormalizeGridColumnsToContainer(DataGrid grid)
        {
            if (grid == null || !grid.IsLoaded)
            {
                return;
            }

            if (grid == RecentAchievementsDataGrid || grid == GameAchievementsDataGrid)
            {
                NormalizeSharedAchievementColumnsToContainer(grid);
                return;
            }

            if (TryBuildNormalizedPersistedWidths(grid, _lastGamesOverviewResizedColumnKey, out var normalized))
            {
                ApplyColumnWidthsByKey(grid, normalized);
            }
        }

        private void NormalizeSharedAchievementColumnsToContainer(DataGrid referenceGrid)
        {
            if (referenceGrid == null || !referenceGrid.IsLoaded)
            {
                return;
            }

            if (!TryBuildNormalizedPersistedWidths(referenceGrid, _lastSidebarAchievementResizedColumnKey, out var normalized))
            {
                return;
            }

            ApplyColumnWidthsByKey(RecentAchievementsDataGrid, normalized);
            ApplyColumnWidthsByKey(GameAchievementsDataGrid, normalized);
        }

        private bool TryBuildNormalizedPersistedWidths(DataGrid grid, string protectedColumnKey, out Dictionary<string, double> normalized)
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

            var keyColumns = visibleColumns
                .Select(column => new { Column = column, Key = GetPersistedColumnKey(column) })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .ToList();
            if (keyColumns.Count == 0)
            {
                return false;
            }

            var availableWidth = GetGridAvailableWidth(grid);
            if (!IsValidPersistedColumnWidth(availableWidth))
            {
                return false;
            }

            var fixedWidth = visibleColumns
                .Where(column => string.IsNullOrWhiteSpace(GetPersistedColumnKey(column)))
                .Sum(GetCurrentColumnWidth);

            var targetWidth = availableWidth - fixedWidth;
            if (targetWidth <= 0)
            {
                return false;
            }

            var floorWidth = Math.Max(1, Math.Min(MinimumNormalizedColumnWidth, targetWidth / keyColumns.Count));
            var keys = new List<string>(keyColumns.Count);
            var widths = new List<double>(keyColumns.Count);
            for (var i = 0; i < keyColumns.Count; i++)
            {
                var entry = keyColumns[i];
                if (entry.Column.MinWidth > floorWidth)
                {
                    entry.Column.MinWidth = floorWidth;
                }

                keys.Add(entry.Key);
                widths.Add(Math.Max(floorWidth, GetCurrentColumnWidth(entry.Column)));
            }

            var totalWidth = widths.Sum();
            var delta = targetWidth - totalWidth;
            if (Math.Abs(delta) > 0.2)
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
                        var capacity = widths[index] - floorWidth;
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
                        widths[fallback] = Math.Max(1, widths[fallback] + delta);
                    }
                }
            }

            normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < keys.Count; i++)
            {
                normalized[keys[i]] = Math.Max(1, widths[i]);
            }

            return true;
        }

        private static List<int> BuildAbsorberOrder(IReadOnlyList<string> keys, string protectedColumnKey)
        {
            var order = new List<int>();
            if (keys == null || keys.Count == 0)
            {
                return order;
            }

            var preferredIndex = FindPreferredAbsorberIndex(keys, protectedColumnKey);
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

            for (var i = keys.Count - 1; i >= 0; i--)
            {
                if (i == preferredIndex || !KeysEqual(keys[i], protectedColumnKey))
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

        private static int FindPreferredAbsorberIndex(IReadOnlyList<string> keys, string protectedColumnKey)
        {
            if (keys == null || keys.Count == 0)
            {
                return -1;
            }

            var preferredKeys = new[]
            {
                "Achievement",
                "OverviewGameName",
                "Game",
                "OverviewProgression"
            };

            for (var p = 0; p < preferredKeys.Length; p++)
            {
                for (var i = 0; i < keys.Count; i++)
                {
                    if (!KeysEqual(keys[i], preferredKeys[p]) || KeysEqual(keys[i], protectedColumnKey))
                    {
                        continue;
                    }

                    return i;
                }
            }

            for (var i = keys.Count - 1; i >= 0; i--)
            {
                if (!KeysEqual(keys[i], protectedColumnKey))
                {
                    return i;
                }
            }

            return keys.Count - 1;
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

        private double GetGridAvailableWidth(DataGrid grid)
        {
            var width = grid?.ActualWidth ?? 0;
            if (!IsValidPersistedColumnWidth(width))
            {
                return 0;
            }

            var chrome = grid.BorderThickness.Left + grid.BorderThickness.Right + grid.Padding.Left + grid.Padding.Right + 2;
            width -= chrome;

            var scrollViewer = FindVisualChild<ScrollViewer>(grid);
            if (scrollViewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                width -= SystemParameters.VerticalScrollBarWidth;
            }

            return Math.Max(0, width);
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

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
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

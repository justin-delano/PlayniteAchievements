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
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    public partial class SingleGameControl : UserControl
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly Dictionary<DataGridColumn, EventHandler> _columnWidthChangedHandlers = new Dictionary<DataGridColumn, EventHandler>();
        private readonly Dictionary<string, double> _pendingColumnWidthUpdates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer _columnWidthSaveTimer;
        private bool _isApplyingPersistedColumnWidths;
        private bool _isColumnResizeInProgress;
        private string _lastResizedColumnKey;
        private const double MinimumNormalizedColumnWidth = 40;
        private static readonly IReadOnlyDictionary<string, double> DefaultSingleGameColumnWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Achievement"] = 520,
                ["UnlockDate"] = 230,
                ["Rarity"] = 170,
                ["Points"] = 120
            };

        public SingleGameControl()
        {
            InitializeComponent();
            InitializeColumnWidthPersistence();
        }

        public SingleGameControl(
            Guid gameId,
            AchievementManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            InitializeComponent();
            InitializeColumnWidthPersistence();

            _settings = settings;
            _logger = logger;
            DataContext = new SingleGameControlModel(gameId, achievementManager, playniteApi, logger, settings);
            ApplyPersistedColumnVisibility(AchievementsDataGrid);
            AttachColumnWidthChangeHandlers(AchievementsDataGrid);
            AttachGridWidthNormalizationHandlers(AchievementsDataGrid);
            ApplyPersistedColumnWidths(AchievementsDataGrid);

            // Subscribe to settings saved event to refresh when credentials change
            PlayniteAchievementsPlugin.SettingsSaved += Plugin_SettingsSaved;
        }

        private void Plugin_SettingsSaved(object sender, EventArgs e)
        {
            RefreshView();
            ApplyPersistedColumnVisibility(AchievementsDataGrid);
            ApplyPersistedColumnWidths(AchievementsDataGrid);
        }

        private SingleGameControlModel ViewModel => DataContext as SingleGameControlModel;

        public string WindowTitle => ViewModel?.GameName != null
            ? $"{ViewModel.GameName} - Achievements"
            : "Achievements";

        /// <summary>
        /// Refreshes the game data display. Called when settings are saved.
        /// </summary>
        public void RefreshView()
        {
            ViewModel?.RefreshView();
        }

        public void Cleanup()
        {
            PlayniteAchievementsPlugin.SettingsSaved -= Plugin_SettingsSaved;
            TearDownColumnWidthPersistence();
            ViewModel?.Dispose();
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
            _pendingColumnWidthUpdates.Clear();
            DetachGridWidthNormalizationHandlers(AchievementsDataGrid);

            if (_columnWidthSaveTimer != null)
            {
                _columnWidthSaveTimer.Stop();
                _columnWidthSaveTimer.Tick -= ColumnWidthSaveTimer_Tick;
                _columnWidthSaveTimer = null;
            }
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
                NormalizeColumnsToContainer(grid);
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
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (grid.IsLoaded && !_isColumnResizeInProgress)
                        {
                            NormalizeColumnsToContainer(grid, rescaleAll: true);
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

            Dispatcher.BeginInvoke(new Action(() => NormalizeColumnsToContainer(grid)), DispatcherPriority.Background);
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

        private void OnColumnWidthChanged(DataGrid sourceGrid, DataGridColumn column)
        {
            if (_isApplyingPersistedColumnWidths || !_isColumnResizeInProgress || sourceGrid == null || column == null)
            {
                return;
            }

            var key = GetPersistedColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var width = column.ActualWidth;
            if (!IsValidPersistedColumnWidth(width))
            {
                return;
            }

            _lastResizedColumnKey = key;
            QueueColumnWidthPersistence(key, width);
        }

        private void ColumnWidthSaveTimer_Tick(object sender, EventArgs e)
        {
            _columnWidthSaveTimer?.Stop();
            var shouldNormalize = _pendingColumnWidthUpdates.Count > 0;
            FlushQueuedColumnWidthUpdates();

            if (shouldNormalize && !_isColumnResizeInProgress)
            {
                NormalizeColumnsToContainer(AchievementsDataGrid);
            }
        }

        private void QueueColumnWidthPersistence(string columnKey, double width)
        {
            if (string.IsNullOrWhiteSpace(columnKey) || !IsValidPersistedColumnWidth(width))
            {
                return;
            }

            _pendingColumnWidthUpdates[columnKey] = Math.Round(width, 2);
            _columnWidthSaveTimer?.Stop();
            _columnWidthSaveTimer?.Start();
        }

        private void FlushQueuedColumnWidthUpdates()
        {
            if (_pendingColumnWidthUpdates.Count == 0 || _settings?.Persisted == null)
            {
                return;
            }

            var map = _settings.Persisted.SingleGameColumnWidths;
            var changed = false;
            foreach (var update in _pendingColumnWidthUpdates)
            {
                if (!IsValidPersistedColumnWidth(update.Value))
                {
                    continue;
                }

                if (!map.TryGetValue(update.Key, out var existing) || Math.Abs(existing - update.Value) > 0.1)
                {
                    map[update.Key] = update.Value;
                    changed = true;
                }
            }

            _pendingColumnWidthUpdates.Clear();

            if (changed)
            {
                SavePluginSettings();
            }
        }

        private void AchievementRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is AchievementDisplayItem item)
            {
                if (item.CanReveal)
                {
                    ViewModel?.RevealAchievementCommand.Execute(item);
                    e.Handled = true;
                }
            }
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (ViewModel == null) return;

            e.Handled = true;

            var column = e.Column;
            if (column == null || string.IsNullOrEmpty(column.SortMemberPath)) return;

            var sortDirection = ListSortDirection.Ascending;
            if (column.SortDirection != null && column.SortDirection == ListSortDirection.Ascending)
            {
                sortDirection = ListSortDirection.Descending;
            }

            ViewModel.SortDataGrid(column.SortMemberPath, sortDirection);

            foreach (var c in (sender as DataGrid).Columns)
            {
                if (c != column)
                {
                    c.SortDirection = null;
                }
            }
            column.SortDirection = sortDirection;
        }

        private void DataGridColumnMenu_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row != null)
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

            foreach (var column in grid.Columns)
            {
                var headerText = ResolveColumnHeaderText(column?.Header);
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
                    OnColumnVisibilityChanged(targetColumn, isVisible);
                };

                menu.Items.Add(item);
            }

            return menu;
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

        private void OnColumnVisibilityChanged(DataGridColumn column, bool isVisible)
        {
            var key = GetPersistedColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            PersistColumnVisibility(key, isVisible);
            ApplyPersistedColumnVisibility(AchievementsDataGrid);
            NormalizeColumnsToContainer(AchievementsDataGrid);
        }

        private void ApplyPersistedColumnVisibility(DataGrid grid)
        {
            var map = _settings?.Persisted?.DataGridColumnVisibility;
            if (grid == null)
            {
                return;
            }

            if (map == null || map.Count == 0)
            {
                NormalizeColumnsToContainer(grid);
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

            NormalizeColumnsToContainer(grid);
        }

        private void ApplyPersistedColumnWidths(DataGrid grid)
        {
            EnsureDefaultSingleGameColumnWidthSeeds();

            var map = GetSingleGameColumnWidthsForRead();
            if (grid == null)
            {
                return;
            }

            if (map == null || map.Count == 0)
            {
                NormalizeColumnsToContainer(grid);
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

            NormalizeColumnsToContainer(grid);
        }

        private void EnsureDefaultSingleGameColumnWidthSeeds()
        {
            if (_settings?.Persisted == null)
            {
                return;
            }

            var map = _settings.Persisted.SingleGameColumnWidths;
            if (map == null)
            {
                map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _settings.Persisted.SingleGameColumnWidths = map;
            }

            var changed = false;
            foreach (var pair in DefaultSingleGameColumnWidthSeeds)
            {
                if (!map.TryGetValue(pair.Key, out var width) || !IsValidPersistedColumnWidth(width))
                {
                    map[pair.Key] = pair.Value;
                    changed = true;
                }
            }

            if (changed)
            {
                SavePluginSettings();
            }
        }

        private Dictionary<string, double> GetSingleGameColumnWidthsForRead()
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

            var map = _settings?.Persisted?.SingleGameColumnWidths;
            if (map != null)
            {
                foreach (var pair in map)
                {
                    if (IsValidPersistedColumnWidth(pair.Value))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            return merged;
        }

        private void NormalizeColumnsToContainer(DataGrid grid, bool rescaleAll = false)
        {
            if (grid == null || !grid.IsLoaded)
            {
                return;
            }

            if (TryBuildNormalizedPersistedWidths(grid, _lastResizedColumnKey, rescaleAll, out var normalized))
            {
                ApplyColumnWidthsByKey(grid, normalized);
            }
        }

        private bool TryBuildNormalizedPersistedWidths(DataGrid grid, string protectedColumnKey, bool rescaleAll, out Dictionary<string, double> normalized)
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
                if (rescaleAll)
                {
                    var weights = widths.Select(w => Math.Max(1, w)).ToList();
                    var remainingTarget = targetWidth;
                    var remainingWeight = weights.Sum();
                    for (var i = 0; i < widths.Count; i++)
                    {
                        var remainingColumns = widths.Count - i;
                        var minForOthers = floorWidth * Math.Max(0, remainingColumns - 1);
                        var next = i == widths.Count - 1
                            ? remainingTarget
                            : remainingTarget * (weights[i] / remainingWeight);

                        next = Math.Max(floorWidth, next);
                        var maxForCurrent = remainingTarget - minForOthers;
                        if (next > maxForCurrent)
                        {
                            next = maxForCurrent;
                        }

                        widths[i] = next;
                        remainingTarget -= next;
                        remainingWeight -= weights[i];
                    }
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

            var scrollViewer = FindVisualChild<ScrollViewer>(grid);
            var chrome = grid.BorderThickness.Left + grid.BorderThickness.Right + grid.Padding.Left + grid.Padding.Right + 2;
            width -= chrome;

            if (scrollViewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible ||
                scrollViewer?.VerticalScrollBarVisibility == ScrollBarVisibility.Visible)
            {
                width -= SystemParameters.VerticalScrollBarWidth;
            }

            var viewportWidth = scrollViewer?.ViewportWidth ?? 0;
            if (IsValidPersistedColumnWidth(viewportWidth))
            {
                var tolerance = SystemParameters.VerticalScrollBarWidth + 4;
                if (Math.Abs(viewportWidth - width) <= tolerance)
                {
                    width = viewportWidth;
                }
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

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
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

        private void PersistColumnVisibility(string columnKey, bool isVisible)
        {
            if (string.IsNullOrWhiteSpace(columnKey) || _settings?.Persisted == null)
            {
                return;
            }

            var map = _settings.Persisted.DataGridColumnVisibility;
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
                _logger?.Warn(ex, "Failed to persist single-game column layout settings.");
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

        private static bool IsValidPersistedColumnWidth(double width)
        {
            return !double.IsNaN(width) && !double.IsInfinity(width) && width > 0;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ClearSearch();
        }
    }
}

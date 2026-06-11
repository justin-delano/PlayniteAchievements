using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Provides reusable column width and visibility persistence for DataGrid controls.
    /// Encapsulates debouncing, normalization, and synchronization logic.
    /// </summary>
    public class ColumnWidthPersistenceService : IDisposable
    {
        private readonly DataGrid _grid;
        private readonly ILogger _logger;
        private readonly Func<Dictionary<string, double>> _getWidths;
        private readonly Action<Dictionary<string, double>> _setWidths;
        private readonly Func<Dictionary<string, bool>> _getVisibility;
        private readonly Action<Dictionary<string, bool>> _setVisibility;
        private readonly Action _saveSettings;
        private readonly IReadOnlyDictionary<string, double> _defaultWidthSeeds;
        private readonly Dictionary<DataGridColumn, EventHandler> _columnWidthChangedHandlers = new Dictionary<DataGridColumn, EventHandler>();
        private readonly Dictionary<DataGridColumn, EventHandler> _columnDisplayIndexChangedHandlers = new Dictionary<DataGridColumn, EventHandler>();
        private readonly Dictionary<string, double> _pendingWidthUpdates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<Dictionary<string, int>> _getOrder;
        private readonly Action<Dictionary<string, int>> _setOrder;
        private readonly Func<Dictionary<string, GridAlignment>> _getCellAlignments;
        private readonly Action<Dictionary<string, GridAlignment>> _setCellAlignments;
        private readonly Func<GridAlignment> _getDefaultCellAlignment;
        private readonly Func<Dictionary<string, GridVerticalAlignment>> _getCellVerticalAlignments;
        private readonly Action<Dictionary<string, GridVerticalAlignment>> _setCellVerticalAlignments;
        private readonly Func<GridVerticalAlignment> _getDefaultCellVerticalAlignment;
        private readonly Func<Dictionary<string, GridAlignment>> _getHeaderHorizontalAlignments;
        private readonly Action<Dictionary<string, GridAlignment>> _setHeaderHorizontalAlignments;
        private readonly Func<GridAlignment> _getDefaultHeaderHorizontalAlignment;
        private readonly Action _applyCellAlignments;
        private DispatcherTimer _saveTimer;
        private bool _isApplyingWidths;
        private bool _isApplyingOrder;
        private bool _isResizeInProgress;
        private bool _isColumnOrderSaveQueued;
        private string _lastResizedColumnKey;
        private bool _shouldRescaleAllOnInitialLoad;
        private bool _isAttached;
        private bool _persistedWidthsApplied;

        private const double MinimumColumnWidthRatio = 0.1;
        private const double WidthNormalizationSafetyPadding = 1.0;

        /// <summary>
        /// Column keys that should be excluded from the visibility toggle menu.
        /// Set before calling Attach() to exclude specific columns from user toggle.
        /// </summary>
        public ISet<string> ExcludedVisibilityKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Column keys that should not expose per-column cell alignment controls.
        /// </summary>
        public ISet<string> ExcludedCellAlignmentKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Column keys that should not expose per-column header alignment controls.
        /// </summary>
        public ISet<string> ExcludedHeaderAlignmentKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Column keys that should be forced to collapsed visibility.
        /// These columns will be collapsed during ApplyPersistedVisibility to prevent flicker.
        /// </summary>
        public ISet<string> ForcedCollapsedKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Creates a new ColumnWidthPersistenceService.
        /// </summary>
        /// <param name="grid">The DataGrid to manage.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="getWidths">Function to get the persisted width dictionary.</param>
        /// <param name="setWidths">Function to set the persisted width dictionary.</param>
        /// <param name="getVisibility">Function to get the persisted visibility dictionary.</param>
        /// <param name="setVisibility">Function to set the persisted visibility dictionary.</param>
        /// <param name="saveSettings">Action to save settings to disk.</param>
        /// <param name="defaultWidthSeeds">Default column widths for new installations.</param>
        public ColumnWidthPersistenceService(
            DataGrid grid,
            ILogger logger,
            Func<Dictionary<string, double>> getWidths,
            Action<Dictionary<string, double>> setWidths,
            Func<Dictionary<string, bool>> getVisibility,
            Action<Dictionary<string, bool>> setVisibility,
            Action saveSettings,
            IReadOnlyDictionary<string, double> defaultWidthSeeds = null,
            Func<Dictionary<string, int>> getOrder = null,
            Action<Dictionary<string, int>> setOrder = null,
            Func<Dictionary<string, GridAlignment>> getCellAlignments = null,
            Action<Dictionary<string, GridAlignment>> setCellAlignments = null,
            Func<GridAlignment> getDefaultCellAlignment = null,
            Func<Dictionary<string, GridVerticalAlignment>> getCellVerticalAlignments = null,
            Action<Dictionary<string, GridVerticalAlignment>> setCellVerticalAlignments = null,
            Func<GridVerticalAlignment> getDefaultCellVerticalAlignment = null,
            Func<Dictionary<string, GridAlignment>> getHeaderHorizontalAlignments = null,
            Action<Dictionary<string, GridAlignment>> setHeaderHorizontalAlignments = null,
            Func<GridAlignment> getDefaultHeaderHorizontalAlignment = null,
            Action applyCellAlignments = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _logger = logger;
            _getWidths = getWidths;
            _setWidths = setWidths;
            _getVisibility = getVisibility;
            _setVisibility = setVisibility;
            _saveSettings = saveSettings;
            _defaultWidthSeeds = defaultWidthSeeds ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _getOrder = getOrder;
            _setOrder = setOrder;
            _getCellAlignments = getCellAlignments;
            _setCellAlignments = setCellAlignments;
            _getDefaultCellAlignment = getDefaultCellAlignment;
            _getCellVerticalAlignments = getCellVerticalAlignments;
            _setCellVerticalAlignments = setCellVerticalAlignments;
            _getDefaultCellVerticalAlignment = getDefaultCellVerticalAlignment;
            _getHeaderHorizontalAlignments = getHeaderHorizontalAlignments;
            _setHeaderHorizontalAlignments = setHeaderHorizontalAlignments;
            _getDefaultHeaderHorizontalAlignment = getDefaultHeaderHorizontalAlignment;
            _applyCellAlignments = applyCellAlignments;
        }

        /// <summary>
        /// Attaches handlers and applies persisted settings.
        /// </summary>
        public void Attach()
        {
            if (_isAttached || _grid == null)
            {
                return;
            }

            _isAttached = true;
            InitializeTimer();
            EnsureDefaultSeeds();
            AttachWidthChangeHandlers();
            AttachDisplayIndexChangeHandlers();
            AttachNormalizationHandlers();
            ApplyPersistedOrder();
            ApplyPersistedVisibility();
            ApplyPersistedWidths();
        }

        /// <summary>
        /// Detaches handlers and flushes pending updates.
        /// </summary>
        public void Detach()
        {
            if (!_isAttached)
            {
                return;
            }

            FlushPendingUpdates();

            foreach (var pair in _columnWidthChangedHandlers.ToList())
            {
                TryDetachWidthChangedHandler(pair.Key, pair.Value);
            }

            foreach (var pair in _columnDisplayIndexChangedHandlers.ToList())
            {
                TryDetachDisplayIndexChangedHandler(pair.Key, pair.Value);
            }

            _columnWidthChangedHandlers.Clear();
            _columnDisplayIndexChangedHandlers.Clear();
            _pendingWidthUpdates.Clear();
            DetachNormalizationHandlers();

            if (_saveTimer != null)
            {
                _saveTimer.Stop();
                _saveTimer.Tick -= SaveTimer_Tick;
                _saveTimer = null;
            }

            _isAttached = false;
        }

        /// <summary>
        /// Refreshes persisted settings (call when settings change externally).
        /// </summary>
        public void Refresh()
        {
            if (!_isAttached)
            {
                return;
            }

            ApplyPersistedVisibility();
            ApplyPersistedOrder();
            ApplyPersistedWidths();
        }

        /// <summary>
        /// Normalizes columns to fill the container width.
        /// </summary>
        public void NormalizeToContainer(bool rescaleAll = false)
        {
            NormalizeColumnsToContainer(rescaleAll);
        }

        private void InitializeTimer()
        {
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _saveTimer.Tick += SaveTimer_Tick;
        }

        private void AttachWidthChangeHandlers()
        {
            if (_grid == null)
            {
                return;
            }

            foreach (var column in _grid.Columns)
            {
                AttachWidthChangedHandler(column);
            }
        }

        private void AttachDisplayIndexChangeHandlers()
        {
            if (_grid == null)
            {
                return;
            }

            foreach (var column in _grid.Columns)
            {
                AttachDisplayIndexChangedHandler(column);
            }
        }

        private void AttachDisplayIndexChangedHandler(DataGridColumn column)
        {
            if (column == null || _columnDisplayIndexChangedHandlers.ContainsKey(column))
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.DisplayIndexProperty, typeof(DataGridColumn));
            if (descriptor == null)
            {
                return;
            }

            EventHandler handler = (_, __) => OnColumnDisplayIndexChanged();
            descriptor.AddValueChanged(column, handler);
            _columnDisplayIndexChangedHandlers[column] = handler;
        }

        private void TryDetachDisplayIndexChangedHandler(DataGridColumn column, EventHandler handler)
        {
            if (column == null || handler == null)
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.DisplayIndexProperty, typeof(DataGridColumn));
            descriptor?.RemoveValueChanged(column, handler);
        }

        private void AttachWidthChangedHandler(DataGridColumn column)
        {
            if (column == null || _columnWidthChangedHandlers.ContainsKey(column))
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            if (descriptor == null)
            {
                return;
            }

            EventHandler handler = (_, __) => OnColumnWidthChanged(column);
            descriptor.AddValueChanged(column, handler);
            _columnWidthChangedHandlers[column] = handler;
        }

        private void TryDetachWidthChangedHandler(DataGridColumn column, EventHandler handler)
        {
            if (column == null || handler == null)
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            descriptor?.RemoveValueChanged(column, handler);
        }

        private void AttachNormalizationHandlers()
        {
            if (_grid == null)
            {
                return;
            }

            _grid.Loaded -= Grid_Loaded;
            _grid.Loaded += Grid_Loaded;
            _grid.SizeChanged -= Grid_SizeChanged;
            _grid.SizeChanged += Grid_SizeChanged;
            _grid.PreviewMouseLeftButtonDown -= Grid_PreviewMouseLeftButtonDown;
            _grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
            _grid.PreviewMouseLeftButtonUp -= Grid_PreviewMouseLeftButtonUp;
            _grid.PreviewMouseLeftButtonUp += Grid_PreviewMouseLeftButtonUp;
            _grid.LostMouseCapture -= Grid_LostMouseCapture;
            _grid.LostMouseCapture += Grid_LostMouseCapture;
        }

        private void DetachNormalizationHandlers()
        {
            if (_grid == null)
            {
                return;
            }

            _grid.Loaded -= Grid_Loaded;
            _grid.SizeChanged -= Grid_SizeChanged;
            _grid.PreviewMouseLeftButtonDown -= Grid_PreviewMouseLeftButtonDown;
            _grid.PreviewMouseLeftButtonUp -= Grid_PreviewMouseLeftButtonUp;
            _grid.LostMouseCapture -= Grid_LostMouseCapture;
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            var shouldRescaleAll = _shouldRescaleAllOnInitialLoad;
            NormalizeColumnsToContainer(shouldRescaleAll);
            if (shouldRescaleAll && IsValidWidth(GetGridAvailableWidth()))
            {
                _shouldRescaleAllOnInitialLoad = false;
            }
        }

        private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || _grid == null || !_grid.IsVisible || _grid.ActualWidth <= 1)
            {
                return;
            }

            var isVisibilityActivation = e.PreviousSize.Width <= 1;

            // Skip normalization on visibility activation if persisted widths were already applied
            // to avoid flicker when switching between visible grids
            if (isVisibilityActivation && _persistedWidthsApplied && !_shouldRescaleAllOnInitialLoad)
            {
                return;
            }

            var shouldRescaleAll = _shouldRescaleAllOnInitialLoad || !isVisibilityActivation;

            _grid.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_grid.IsLoaded && !_isResizeInProgress)
                    {
                        NormalizeColumnsToContainer(shouldRescaleAll);
                        if (shouldRescaleAll && _shouldRescaleAllOnInitialLoad && IsValidWidth(GetGridAvailableWidth()))
                        {
                            _shouldRescaleAllOnInitialLoad = false;
                        }
                    }
                }),
                DispatcherPriority.Render);
        }

        private void Grid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (VisualTreeHelpers.IsColumnResizeThumbHit(e.OriginalSource as DependencyObject))
            {
                _isResizeInProgress = true;
            }
        }

        private void Grid_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CompleteResizeNormalization();
        }

        private void Grid_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            CompleteResizeNormalization();
        }

        private void CompleteResizeNormalization()
        {
            if (!_isResizeInProgress)
            {
                return;
            }

            _isResizeInProgress = false;
            _grid?.Dispatcher.BeginInvoke(new Action(() => NormalizeColumnsToContainer()), DispatcherPriority.Background);
        }

        private void OnColumnWidthChanged(DataGridColumn column)
        {
            if (_isApplyingWidths || !_isResizeInProgress || column == null || !column.CanUserResize)
            {
                return;
            }

            var key = GetColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var width = column.ActualWidth;
            if (!IsValidWidth(width))
            {
                return;
            }

            _lastResizedColumnKey = key;
            QueueWidthUpdate(key, width);
        }

        private void OnColumnDisplayIndexChanged()
        {
            if (_isApplyingOrder || _grid == null || _getOrder == null || _setOrder == null)
            {
                return;
            }

            QueueColumnOrderSave();
        }

        private void QueueColumnOrderSave()
        {
            if (_isColumnOrderSaveQueued || _grid == null)
            {
                return;
            }

            _isColumnOrderSaveQueued = true;
            _grid.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _isColumnOrderSaveQueued = false;
                    PersistCurrentColumnOrder();
                }),
                DispatcherPriority.Background);
        }

        private void PersistCurrentColumnOrder()
        {
            if (_grid == null || _getOrder == null || _setOrder == null || _isApplyingOrder)
            {
                return;
            }

            var map = _getOrder.Invoke();
            if (map == null)
            {
                map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            var changed = false;
            foreach (var column in _grid.Columns)
            {
                var key = GetColumnKey(column);
                if (string.IsNullOrWhiteSpace(key) || column.DisplayIndex < 0)
                {
                    continue;
                }

                if (!map.TryGetValue(key, out var existing) || existing != column.DisplayIndex)
                {
                    map[key] = column.DisplayIndex;
                    changed = true;
                }
            }

            if (changed)
            {
                _setOrder.Invoke(map);
                _saveSettings?.Invoke();
            }
        }

        private void QueueWidthUpdate(string key, double width)
        {
            if (string.IsNullOrWhiteSpace(key) || !IsValidWidth(width))
            {
                return;
            }

            _pendingWidthUpdates[key] = ColumnWidthNormalization.RoundPixelWidth(width);
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }

        private void SaveTimer_Tick(object sender, EventArgs e)
        {
            _saveTimer?.Stop();
            var shouldNormalize = _pendingWidthUpdates.Count > 0;
            FlushPendingUpdates();

            if (shouldNormalize && !_isResizeInProgress)
            {
                NormalizeColumnsToContainer();
            }
        }

        private void FlushPendingUpdates()
        {
            var map = _getWidths?.Invoke();
            if (map == null || _pendingWidthUpdates.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var update in _pendingWidthUpdates)
            {
                if (!IsValidWidth(update.Value))
                {
                    continue;
                }

                if (!map.TryGetValue(update.Key, out var existing) || Math.Abs(existing - update.Value) > 0.1)
                {
                    map[update.Key] = update.Value;
                    changed = true;
                }
            }

            _pendingWidthUpdates.Clear();

            if (changed)
            {
                _setWidths?.Invoke(map);
                _saveSettings?.Invoke();
            }
        }

        private void EnsureDefaultSeeds()
        {
            var map = _getWidths?.Invoke();
            if (map == null)
            {
                map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _setWidths?.Invoke(map);
            }

            var changed = false;
            foreach (var pair in _defaultWidthSeeds)
            {
                if (!map.TryGetValue(pair.Key, out var width) || !IsValidWidth(width))
                {
                    map[pair.Key] = pair.Value;
                    changed = true;
                }
            }

            if (changed)
            {
                _setWidths?.Invoke(map);
                _shouldRescaleAllOnInitialLoad = true;
                _saveSettings?.Invoke();
            }
        }

        private void ApplyPersistedVisibility()
        {
            var map = _getVisibility?.Invoke();
            if (_grid == null)
            {
                return;
            }

            foreach (var column in _grid.Columns)
            {
                var key = GetColumnKey(column);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                // Force collapse columns that should never be visible in this context
                if (ForcedCollapsedKeys.Contains(key))
                {
                    column.Visibility = Visibility.Collapsed;
                    column.MinWidth = 0;
                    column.MaxWidth = 0;
                    column.Width = new DataGridLength(0, DataGridLengthUnitType.Pixel);
                    continue;
                }

                // Skip columns that are excluded from visibility persistence
                if (ExcludedVisibilityKeys.Contains(key))
                {
                    continue;
                }

                if (map != null && map.TryGetValue(key, out var isVisible))
                {
                    column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            NormalizeColumnsToContainer();
        }

        private void ApplyPersistedOrder()
        {
            var map = _getOrder?.Invoke();
            if (_grid == null || map == null || map.Count == 0)
            {
                return;
            }

            var orderedColumns = _grid.Columns
                .Where(column => column != null)
                .Select(column => new
                {
                    Column = column,
                    Key = GetColumnKey(column),
                    OriginalDisplayIndex = column.DisplayIndex
                })
                .Select(entry => new
                {
                    entry.Column,
                    entry.OriginalDisplayIndex,
                    SavedDisplayIndex = !string.IsNullOrWhiteSpace(entry.Key) && map.TryGetValue(entry.Key, out var displayIndex)
                        ? Math.Max(0, displayIndex)
                        : int.MaxValue
                })
                .OrderBy(entry => entry.SavedDisplayIndex)
                .ThenBy(entry => entry.OriginalDisplayIndex)
                .ToList();

            if (orderedColumns.Count == 0)
            {
                return;
            }

            _isApplyingOrder = true;
            try
            {
                for (var index = 0; index < orderedColumns.Count; index++)
                {
                    var column = orderedColumns[index].Column;
                    if (column.DisplayIndex != index)
                    {
                        column.DisplayIndex = index;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to apply persisted column order.");
            }
            finally
            {
                _isApplyingOrder = false;
            }
        }

        private void ApplyPersistedWidths()
        {
            EnsureDefaultSeeds();

            var map = _getWidths?.Invoke();
            if (_grid == null)
            {
                return;
            }

            if (map == null || map.Count == 0)
            {
                NormalizeColumnsToContainer();
                _persistedWidthsApplied = true;
                return;
            }

            _isApplyingWidths = true;
            try
            {
                foreach (var column in _grid.Columns)
                {
                    if (column == null || !column.CanUserResize)
                    {
                        continue;
                    }

                    var key = GetColumnKey(column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (map.TryGetValue(key, out var width) && IsValidWidth(width))
                    {
                        column.Width = new DataGridLength(ColumnWidthNormalization.RoundPixelWidth(width), DataGridLengthUnitType.Pixel);
                    }
                }
            }
            finally
            {
                _isApplyingWidths = false;
            }

            _persistedWidthsApplied = true;
            NormalizeColumnsToContainer();
        }

        private void NormalizeColumnsToContainer(bool rescaleAll = false)
        {
            if (_grid == null || !_grid.IsLoaded)
            {
                return;
            }

            if (TryBuildNormalizedWidths(_lastResizedColumnKey, rescaleAll, out var normalized))
            {
                ApplyWidthsByKey(normalized);
            }
        }

        private bool TryBuildNormalizedWidths(string protectedKey, bool rescaleAll, out Dictionary<string, double> normalized)
        {
            normalized = null;
            if (_grid == null || _grid.Columns == null || _grid.Columns.Count == 0)
            {
                return false;
            }

            var visibleColumns = _grid.Columns
                .Where(c => c != null && c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
            if (visibleColumns.Count == 0)
            {
                return false;
            }

            var availableWidth = GetGridAvailableWidth();
            if (!IsValidWidth(availableWidth))
            {
                return false;
            }

            var minimumWidth = ResolveMinimumColumnWidth(visibleColumns, availableWidth);
            var minimumWidths = ApplyColumnMinimumWidths(visibleColumns, minimumWidth);

            var keyColumns = visibleColumns
                .Select(c => new
                {
                    Column = c,
                    Key = GetColumnKey(c),
                    MinWidth = GetColumnMinWidth(minimumWidths, c, minimumWidth),
                    IsResizable = c.CanUserResize
                })
                .Where(e => !string.IsNullOrWhiteSpace(e.Key) && e.IsResizable)
                .Select(e => new
                {
                    e.Column,
                    e.Key,
                    e.MinWidth,
                    SeedWidth = ResolveSeedWidth(e.Key, e.Column, e.MinWidth)
                })
                .ToList();

            if (keyColumns.Count == 0)
            {
                return false;
            }

            var fixedWidth = visibleColumns
                .Where(c => string.IsNullOrWhiteSpace(GetColumnKey(c)) || !c.CanUserResize)
                .Sum(c => Math.Max(GetColumnMinWidth(minimumWidths, c, minimumWidth), GetCurrentWidth(c)));

            var targetWidth = Math.Max(0, availableWidth - fixedWidth - WidthNormalizationSafetyPadding);
            if (targetWidth <= 0)
            {
                return false;
            }

            var keys = keyColumns.Select(e => e.Key).ToList();
            var floorWidths = keyColumns.Select(e => e.MinWidth).ToList();
            var widths = keyColumns.Select(e => Math.Max(e.MinWidth, e.SeedWidth)).ToList();

            var totalWidth = widths.Sum();
            var delta = targetWidth - totalWidth;

            if (Math.Abs(delta) > 0.2)
            {
                if (rescaleAll)
                {
                    RescaleProportionally(widths, floorWidths, targetWidth);
                }
                else
                {
                    DistributeDelta(widths, floorWidths, keys, protectedKey, delta, targetWidth);
                }
            }

            normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < keys.Count; i++)
            {
                normalized[keys[i]] = Math.Max(
                    ColumnWidthNormalization.RoundPixelWidth(floorWidths[i]),
                    ColumnWidthNormalization.RoundPixelWidth(widths[i]));
            }

            return true;
        }

        private void ApplyWidthsByKey(Dictionary<string, double> widthsByKey)
        {
            if (_grid == null || widthsByKey == null || widthsByKey.Count == 0)
            {
                return;
            }

            _isApplyingWidths = true;
            try
            {
                foreach (var column in _grid.Columns)
                {
                    if (column == null || !column.CanUserResize)
                    {
                        continue;
                    }

                    var key = GetColumnKey(column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (!widthsByKey.TryGetValue(key, out var width) || !IsValidWidth(width))
                    {
                        continue;
                    }

                    if (Math.Abs(GetCurrentWidth(column) - width) <= 0.2)
                    {
                        continue;
                    }

                    column.Width = new DataGridLength(ColumnWidthNormalization.RoundPixelWidth(width), DataGridLengthUnitType.Pixel);
                }
            }
            finally
            {
                _isApplyingWidths = false;
            }
        }

        private double GetGridAvailableWidth()
        {
            var width = _grid?.ActualWidth ?? 0;
            if (!IsValidWidth(width))
            {
                return 0;
            }

            var scrollViewer = VisualTreeHelpers.FindVisualChild<ScrollViewer>(_grid);
            var viewportWidth = scrollViewer?.ViewportWidth ?? 0;
            if (IsValidWidth(viewportWidth))
            {
                return Math.Max(0, viewportWidth);
            }

            var chrome = _grid.BorderThickness.Left + _grid.BorderThickness.Right + _grid.Padding.Left + _grid.Padding.Right + 2;
            width -= chrome;

            if (scrollViewer != null)
            {
                var scrollBarWidth = scrollViewer.ActualWidth - scrollViewer.ViewportWidth;
                if (IsValidWidth(scrollBarWidth))
                {
                    width -= scrollBarWidth;
                }
                else if (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible ||
                         scrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Visible)
                {
                    width -= SystemParameters.VerticalScrollBarWidth;
                }
            }

            return Math.Max(0, width);
        }

        private double ResolveSeedWidth(string key, DataGridColumn column, double fallbackMin)
        {
            // Check pending updates first (from user resize) to avoid flicker
            if (!string.IsNullOrWhiteSpace(key) && _pendingWidthUpdates.TryGetValue(key, out var pending) && IsValidWidth(pending))
            {
                return pending;
            }

            // Then check persisted settings
            var map = _getWidths?.Invoke();
            if (!string.IsNullOrWhiteSpace(key) && map != null && map.TryGetValue(key, out var preferred) && IsValidWidth(preferred))
            {
                return preferred;
            }

            // Finally use current column width
            var current = GetCurrentWidth(column);
            return IsValidWidth(current) ? current : fallbackMin;
        }

        private double ResolveMinimumColumnWidth(IReadOnlyList<DataGridColumn> columns, double availableWidth)
        {
            var preferred = ColumnWidthNormalization.RoundPixelWidth(availableWidth * MinimumColumnWidthRatio);

            if (!IsValidWidth(availableWidth) || columns == null || columns.Count == 0)
            {
                return Math.Max(1, preferred);
            }

            var resizable = columns.Where(c => c != null && c.CanUserResize && !string.IsNullOrWhiteSpace(GetColumnKey(c))).ToList();
            if (resizable.Count == 0)
            {
                return Math.Max(1, preferred);
            }

            var fixedWidth = columns.Where(c => c == null || !c.CanUserResize || string.IsNullOrWhiteSpace(GetColumnKey(c))).Sum(GetCurrentWidth);
            var availableForResizable = Math.Max(1, availableWidth - fixedWidth - WidthNormalizationSafetyPadding);
            var maxFittable = Math.Max(1, availableForResizable / resizable.Count);
            return Math.Max(1, Math.Min(preferred, maxFittable));
        }

        private Dictionary<DataGridColumn, double> ApplyColumnMinimumWidths(IReadOnlyList<DataGridColumn> columns, double minimumWidth)
        {
            var result = new Dictionary<DataGridColumn, double>();
            if (columns == null || !IsValidWidth(minimumWidth))
            {
                return result;
            }

            foreach (var column in columns)
            {
                if (column == null)
                {
                    continue;
                }

                var resolved = ColumnWidthNormalization.ResolveColumnMinimumWidth(column, minimumWidth);
                result[column] = resolved;

                if (Math.Abs(column.MinWidth - resolved) > 0.2)
                {
                    column.MinWidth = resolved;
                }

                if (!column.CanUserResize)
                {
                    var maxResolved = ResolveColumnMaxWidth(column, resolved);
                    if (Math.Abs(column.MaxWidth - maxResolved) > 0.2)
                    {
                        column.MaxWidth = maxResolved;
                    }
                }
            }

            return result;
        }

        private double ResolveColumnMaxWidth(DataGridColumn column, double fallback)
        {
            if (column != null && IsValidWidth(column.MaxWidth))
            {
                return column.MaxWidth;
            }

            var current = GetCurrentWidth(column);
            return IsValidWidth(current) ? current : fallback;
        }

        private static double GetColumnMinWidth(Dictionary<DataGridColumn, double> map, DataGridColumn column, double fallback)
        {
            if (map != null && column != null && map.TryGetValue(column, out var resolved) && IsValidWidth(resolved))
            {
                return resolved;
            }

            return fallback;
        }

        private static double GetCurrentWidth(DataGridColumn column)
        {
            if (column == null)
            {
                return 0;
            }

            if (IsValidWidth(column.ActualWidth))
            {
                return column.ActualWidth;
            }

            var display = column.Width.DisplayValue;
            return IsValidWidth(display) ? display : 0;
        }

        private static void RescaleProportionally(IList<double> widths, IReadOnlyList<double> floors, double target)
        {
            if (widths == null || floors == null || widths.Count == 0 || widths.Count != floors.Count || !IsValidWidth(target))
            {
                return;
            }

            var weights = widths.Select(w => Math.Max(1, w)).ToList();
            var remainingTarget = target;
            var remainingWeight = weights.Sum();
            var remainingMin = floors.Sum();

            for (var i = 0; i < widths.Count; i++)
            {
                var floor = floors[i];
                remainingMin -= floor;
                var next = i == widths.Count - 1 ? remainingTarget : remainingTarget * (weights[i] / remainingWeight);
                next = Math.Max(floor, next);
                var maxForCurrent = remainingTarget - remainingMin;
                if (next > maxForCurrent)
                {
                    next = maxForCurrent;
                }

                widths[i] = next;
                remainingTarget -= next;
                remainingWeight -= weights[i];
            }
        }

        private static void DistributeDelta(IList<double> widths, IReadOnlyList<double> floors, IReadOnlyList<string> keys, string protectedKey, double delta, double target)
        {
            var absorberOrder = BuildAbsorberOrder(keys, protectedKey);
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
                    var capacity = widths[index] - floors[index];
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
                    var before = widths[fallback];
                    widths[fallback] = Math.Max(floors[fallback], widths[fallback] + delta);
                    delta += widths[fallback] - before;
                }

                if (delta < -0.2)
                {
                    var protectedIndex = -1;
                    for (var i = 0; i < keys.Count; i++)
                    {
                        if (string.Equals(keys[i], protectedKey, StringComparison.OrdinalIgnoreCase))
                        {
                            protectedIndex = i;
                            break;
                        }
                    }

                    if (protectedIndex >= 0)
                    {
                        var capacity = widths[protectedIndex] - floors[protectedIndex];
                        if (capacity > 0)
                        {
                            var take = Math.Min(capacity, -delta);
                            widths[protectedIndex] -= take;
                            delta += take;
                        }
                    }
                }

                if (delta < -0.2)
                {
                    RescaleProportionally(widths, floors, target);
                }
            }
        }

        private static List<int> BuildAbsorberOrder(IReadOnlyList<string> keys, string protectedKey)
        {
            var order = new List<int>();
            if (keys == null || keys.Count == 0)
            {
                return order;
            }

            var preferredIndex = -1;
            for (var i = keys.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(keys[i], protectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    preferredIndex = i;
                    break;
                }
            }

            if (preferredIndex >= 0)
            {
                order.Add(preferredIndex);
            }

            for (var i = keys.Count - 1; i >= 0; i--)
            {
                if (i == preferredIndex || string.Equals(keys[i], protectedKey, StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Handles column visibility menu interactions.
        /// </summary>
        public void OnColumnVisibilityChanged(DataGridColumn column, bool isVisible)
        {
            var key = GetColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var map = _getVisibility?.Invoke();
            if (map != null)
            {
                if (map.TryGetValue(key, out var existing) && existing == isVisible)
                {
                    return;
                }

                map[key] = isVisible;
                _saveSettings?.Invoke();
            }

            ApplyPersistedVisibility();
        }

        /// <summary>
        /// Builds a column menu for the grid.
        /// Columns in ExcludedVisibilityKeys are not included in the visibility section.
        /// </summary>
        public ContextMenu BuildColumnVisibilityMenu(DataGridColumn contextColumn = null)
        {
            if (_grid == null)
            {
                return null;
            }

            var menu = new ContextMenu();
            var hasAlignmentSection = AddAlignmentSection(menu, contextColumn);
            var separatorIndex = menu.Items.Count;
            var hasVisibilitySection = AddVisibilitySection(menu);
            if (hasAlignmentSection && hasVisibilitySection)
            {
                menu.Items.Insert(separatorIndex, new Separator { Margin = new Thickness(8, 8, 8, 0) });
            }

            return hasAlignmentSection || hasVisibilitySection ? menu : null;
        }

        private bool AddAlignmentSection(ContextMenu menu, DataGridColumn contextColumn)
        {
            if (menu == null || !CanShowAlignmentSection(contextColumn))
            {
                return false;
            }

            var headerText = ResolveColumnDisplayName(contextColumn);
            if (string.IsNullOrWhiteSpace(headerText))
            {
                return false;
            }

            var rowItem = CreateAlignmentButtonRowItem(contextColumn);
            if (rowItem == null)
            {
                return false;
            }

            menu.Items.Add(CreateSectionHeader(headerText));
            menu.Items.Add(rowItem);
            return true;
        }

        private MenuItem CreateAlignmentButtonRowItem(DataGridColumn contextColumn)
        {
            MenuItem item = null;
            Action refreshRow = null;

            refreshRow = () =>
            {
                if (item != null)
                {
                    item.Header = CreateAlignmentButtonRow(contextColumn, refreshRow);
                }
            };

            item = new MenuItem
            {
                StaysOpenOnClick = true,
                Focusable = false,
                Cursor = Cursors.Arrow,
                Style = CreateCenteredCompactMenuItemStyle(new Thickness(8, 0, 8, 0))
            };

            item.Header = CreateAlignmentButtonRow(contextColumn, refreshRow);
            return item.Header == null ? null : item;
        }

        private FrameworkElement CreateAlignmentButtonRow(DataGridColumn contextColumn, Action refreshRow)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };

            var key = GetColumnKey(contextColumn);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (CanShowHeaderHorizontalAlignment(contextColumn))
            {
                var hasOverride = TryGetColumnHeaderHorizontalAlignmentOverride(key, out var overrideAlignment);
                var effectiveAlignment = hasOverride ? overrideAlignment : GetDefaultHeaderHorizontalAlignment();
                var description = CreateHorizontalAlignmentDescription(
                    "Header horizontal alignment",
                    effectiveAlignment,
                    hasOverride);

                row.Children.Add(CreateAlignmentButton(
                    CreateHeaderAlignmentIcon(effectiveAlignment, !hasOverride),
                    description,
                    !hasOverride,
                    () =>
                    {
                        CycleColumnHeaderHorizontalAlignment(contextColumn);
                        refreshRow?.Invoke();
                    }));
            }

            if (CanShowCellHorizontalAlignment(contextColumn))
            {
                var hasOverride = TryGetColumnCellAlignmentOverride(key, out var overrideAlignment);
                var effectiveAlignment = hasOverride ? overrideAlignment : GetDefaultCellAlignment();
                var description = CreateHorizontalAlignmentDescription(
                    "Cell horizontal alignment",
                    effectiveAlignment,
                    hasOverride);

                row.Children.Add(CreateAlignmentButton(
                    CreateAlignmentIcon(effectiveAlignment, !hasOverride),
                    description,
                    !hasOverride,
                    () =>
                    {
                        CycleColumnCellAlignment(contextColumn);
                        refreshRow?.Invoke();
                    }));
            }

            if (CanShowCellVerticalAlignment(contextColumn))
            {
                var hasOverride = TryGetColumnCellVerticalAlignmentOverride(key, out var overrideAlignment);
                var effectiveAlignment = hasOverride ? overrideAlignment : GetDefaultCellVerticalAlignment();
                var description = CreateVerticalAlignmentDescription(
                    "Cell vertical alignment",
                    effectiveAlignment,
                    hasOverride);

                row.Children.Add(CreateAlignmentButton(
                    CreateVerticalAlignmentIcon(effectiveAlignment, !hasOverride),
                    description,
                    !hasOverride,
                    () =>
                    {
                        CycleColumnCellVerticalAlignment(contextColumn);
                        refreshRow?.Invoke();
                    }));
            }

            if (row.Children.Count == 0)
            {
                return null;
            }

            if (row.Children[row.Children.Count - 1] is FrameworkElement lastButton)
            {
                lastButton.Margin = new Thickness(0);
            }

            return row;
        }

        private static Button CreateAlignmentButton(
            FrameworkElement icon,
            string description,
            bool isDefault,
            Action onClick)
        {
            if (icon != null)
            {
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
            }

            var button = new Button
            {
                Width = 34,
                Height = 28,
                MinWidth = 34,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(4),
                BorderThickness = isDefault ? new Thickness(1) : new Thickness(1.6),
                Cursor = Cursors.Hand,
                ToolTip = description,
                Content = icon,
                Opacity = 1,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = CreateCompactAlignmentButtonStyle()
            };

            SetTextBrushOpacity(button, Control.BorderBrushProperty, isDefault ? 0.42 : 0.92);
            SetTextBrushOpacity(button, Control.BackgroundProperty, isDefault ? 0.05 : 0.10);
            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            AutomationProperties.SetName(button, description);

            button.Click += (_, e) =>
            {
                e.Handled = true;
                onClick?.Invoke();
            };

            return button;
        }

        private bool AddVisibilitySection(ContextMenu menu)
        {
            if (menu == null)
            {
                return false;
            }

            var visibilityItems = new List<MenuItem>();
            foreach (var column in _grid.Columns
                         .Where(c => c != null)
                         .OrderBy(c => c.DisplayIndex))
            {
                var key = GetColumnKey(column);
                if (!string.IsNullOrWhiteSpace(key) && ExcludedVisibilityKeys.Contains(key))
                {
                    continue;
                }

                var headerText = ResolveColumnDisplayName(column);
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

                visibilityItems.Add(item);
            }

            if (visibilityItems.Count == 0)
            {
                return false;
            }

            menu.Items.Add(CreateSectionHeader("Columns"));
            foreach (var item in visibilityItems)
            {
                menu.Items.Add(item);
            }

            return true;
        }

        private bool CanShowAlignmentSection(DataGridColumn column)
        {
            return CanShowCellHorizontalAlignment(column) ||
                   CanShowCellVerticalAlignment(column) ||
                   CanShowHeaderHorizontalAlignment(column);
        }

        private bool CanShowCellHorizontalAlignment(DataGridColumn column)
        {
            return CanShowCellAlignmentControl(column) &&
                   _getCellAlignments != null &&
                   _setCellAlignments != null;
        }

        private bool CanShowCellVerticalAlignment(DataGridColumn column)
        {
            return CanShowCellAlignmentControl(column) &&
                   _getCellVerticalAlignments != null &&
                   _setCellVerticalAlignments != null;
        }

        private bool CanShowHeaderHorizontalAlignment(DataGridColumn column)
        {
            if (column == null || _getHeaderHorizontalAlignments == null || _setHeaderHorizontalAlignments == null)
            {
                return false;
            }

            var key = GetColumnKey(column);
            return !string.IsNullOrWhiteSpace(key) &&
                   !ExcludedCellAlignmentKeys.Contains(key) &&
                   !ExcludedHeaderAlignmentKeys.Contains(key);
        }

        private bool CanShowCellAlignmentControl(DataGridColumn column)
        {
            if (column == null)
            {
                return false;
            }

            var key = GetColumnKey(column);
            return !string.IsNullOrWhiteSpace(key) && !ExcludedCellAlignmentKeys.Contains(key);
        }

        private void CycleColumnCellAlignment(DataGridColumn column)
        {
            var key = GetColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var map = _getCellAlignments?.Invoke() ??
                      new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
            var hasOverride = map.TryGetValue(key, out var current);
            var next = GetNextHorizontalAlignmentOverride(hasOverride, current);

            if (next.HasValue)
            {
                map[key] = next.Value;
            }
            else
            {
                map.Remove(key);
            }

            _setCellAlignments?.Invoke(map);
            _saveSettings?.Invoke();
            _applyCellAlignments?.Invoke();
        }

        private void CycleColumnCellVerticalAlignment(DataGridColumn column)
        {
            var key = GetColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var map = _getCellVerticalAlignments?.Invoke() ??
                      new Dictionary<string, GridVerticalAlignment>(StringComparer.OrdinalIgnoreCase);
            var hasOverride = map.TryGetValue(key, out var current);
            var next = GetNextVerticalAlignmentOverride(hasOverride, current);

            if (next.HasValue)
            {
                map[key] = next.Value;
            }
            else
            {
                map.Remove(key);
            }

            _setCellVerticalAlignments?.Invoke(map);
            _saveSettings?.Invoke();
            _applyCellAlignments?.Invoke();
        }

        private void CycleColumnHeaderHorizontalAlignment(DataGridColumn column)
        {
            var key = GetColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var map = _getHeaderHorizontalAlignments?.Invoke() ??
                      new Dictionary<string, GridAlignment>(StringComparer.OrdinalIgnoreCase);
            var hasOverride = map.TryGetValue(key, out var current);
            var next = GetNextHorizontalAlignmentOverride(hasOverride, current);

            if (next.HasValue)
            {
                map[key] = next.Value;
            }
            else
            {
                map.Remove(key);
            }

            _setHeaderHorizontalAlignments?.Invoke(map);
            _saveSettings?.Invoke();
            _applyCellAlignments?.Invoke();
        }

        private bool TryGetColumnCellAlignmentOverride(string key, out GridAlignment alignment)
        {
            alignment = GridAlignment.Left;
            var map = _getCellAlignments?.Invoke();
            return !string.IsNullOrWhiteSpace(key) &&
                   map != null &&
                   map.TryGetValue(key, out alignment);
        }

        private bool TryGetColumnCellVerticalAlignmentOverride(string key, out GridVerticalAlignment alignment)
        {
            alignment = GridVerticalAlignment.Center;
            var map = _getCellVerticalAlignments?.Invoke();
            return !string.IsNullOrWhiteSpace(key) &&
                   map != null &&
                   map.TryGetValue(key, out alignment);
        }

        private bool TryGetColumnHeaderHorizontalAlignmentOverride(string key, out GridAlignment alignment)
        {
            alignment = GridAlignment.Center;
            var map = _getHeaderHorizontalAlignments?.Invoke();
            return !string.IsNullOrWhiteSpace(key) &&
                   map != null &&
                   map.TryGetValue(key, out alignment);
        }

        private GridAlignment GetDefaultCellAlignment()
        {
            return _getDefaultCellAlignment?.Invoke() ?? GridAlignment.Left;
        }

        private GridVerticalAlignment GetDefaultCellVerticalAlignment()
        {
            return _getDefaultCellVerticalAlignment?.Invoke() ?? GridVerticalAlignment.Center;
        }

        private GridAlignment GetDefaultHeaderHorizontalAlignment()
        {
            return _getDefaultHeaderHorizontalAlignment?.Invoke() ?? GridAlignment.Center;
        }

        private static GridAlignment? GetNextHorizontalAlignmentOverride(bool hasOverride, GridAlignment current)
        {
            if (!hasOverride)
            {
                return GridAlignment.Left;
            }

            switch (current)
            {
                case GridAlignment.Left:
                    return GridAlignment.Center;
                case GridAlignment.Center:
                    return GridAlignment.Right;
                default:
                    return null;
            }
        }

        private static GridVerticalAlignment? GetNextVerticalAlignmentOverride(bool hasOverride, GridVerticalAlignment current)
        {
            if (!hasOverride)
            {
                return GridVerticalAlignment.Top;
            }

            switch (current)
            {
                case GridVerticalAlignment.Top:
                    return GridVerticalAlignment.Center;
                case GridVerticalAlignment.Center:
                    return GridVerticalAlignment.Bottom;
                default:
                    return null;
            }
        }

        private static string CreateHorizontalAlignmentDescription(
            string target,
            GridAlignment effectiveAlignment,
            bool hasOverride)
        {
            var alignmentText = AlignmentToText(effectiveAlignment);
            return hasOverride
                ? $"{target}: {alignmentText}. Click to change."
                : $"{target}: Default ({alignmentText}). Click to change.";
        }

        private static string CreateVerticalAlignmentDescription(
            string target,
            GridVerticalAlignment effectiveAlignment,
            bool hasOverride)
        {
            var alignmentText = AlignmentToText(effectiveAlignment);
            return hasOverride
                ? $"{target}: {alignmentText}. Click to change."
                : $"{target}: Default ({alignmentText}). Click to change.";
        }

        private static string AlignmentToText(GridAlignment alignment)
        {
            switch (alignment)
            {
                case GridAlignment.Center:
                    return "Center";
                case GridAlignment.Right:
                    return "Right";
                case GridAlignment.Left:
                default:
                    return "Left";
            }
        }

        private static string AlignmentToText(GridVerticalAlignment alignment)
        {
            switch (alignment)
            {
                case GridVerticalAlignment.Top:
                    return "Top";
                case GridVerticalAlignment.Bottom:
                    return "Bottom";
                case GridVerticalAlignment.Center:
                default:
                    return "Center";
            }
        }

        private static MenuItem CreateSectionHeader(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            return new MenuItem
            {
                Header = textBlock,
                Focusable = false,
                IsHitTestVisible = false,
                Style = CreateCompactMenuItemStyle(new Thickness(8, 6, 8, 6))
            };
        }

        private static Style CreateCenteredCompactMenuItemStyle(Thickness margin)
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, margin));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateCenteredCompactMenuItemTemplate()));
            return style;
        }

        private static ControlTemplate CreateCenteredCompactMenuItemTemplate()
        {
            var template = new ControlTemplate(typeof(MenuItem));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            template.VisualTree = border;
            return template;
        }

        private static void SetTextBrushOpacity(DependencyObject target, DependencyProperty property, double opacity)
        {
            if (target == null || property == null)
            {
                return;
            }

            var brush = Application.Current?.TryFindResource("TextBrush") as Brush;
            if (brush != null)
            {
                brush = brush.CloneCurrentValue();
                brush.Opacity = opacity;
                if (brush.CanFreeze)
                {
                    brush.Freeze();
                }

                target.SetValue(property, brush);
                return;
            }

            target.SetValue(property, new SolidColorBrush(Color.FromArgb(
                (byte)Math.Max(0, Math.Min(255, opacity * 255)),
                255,
                255,
                255)));
        }

        private static Style CreateCompactAlignmentButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateCompactAlignmentButtonTemplate()));
            return style;
        }

        private static ControlTemplate CreateCompactAlignmentButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(UIElement.OpacityProperty, 0.96);
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            border.AppendChild(presenter);

            template.VisualTree = border;

            var mouseOverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            mouseOverTrigger.Setters.Add(new Setter(Border.OpacityProperty, 1.0, "Chrome"));
            template.Triggers.Add(mouseOverTrigger);

            var focusTrigger = new Trigger
            {
                Property = UIElement.IsKeyboardFocusedProperty,
                Value = true
            };
            focusTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1.6), "Chrome"));
            template.Triggers.Add(focusTrigger);

            var pressedTrigger = new Trigger
            {
                Property = ButtonBase.IsPressedProperty,
                Value = true
            };
            pressedTrigger.Setters.Add(new Setter(Border.OpacityProperty, 0.86, "Chrome"));
            template.Triggers.Add(pressedTrigger);

            return template;
        }

        private static Style CreateCompactMenuItemStyle(Thickness margin)
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, margin));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateCompactMenuItemTemplate()));
            return style;
        }

        private static ControlTemplate CreateCompactMenuItemTemplate()
        {
            var template = new ControlTemplate(typeof(MenuItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            template.VisualTree = border;
            return template;
        }

        private static FrameworkElement CreateHeaderAlignmentIcon(GridAlignment alignment, bool isDefault)
        {
            var grid = new Grid
            {
                Width = 20,
                Height = 14,
                Opacity = isDefault ? 0.62 : 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header alignment intentionally shows only the header row's top line.
            AddAlignmentLine(grid, alignment, 2, 15);
            return grid;
        }

        private static FrameworkElement CreateAlignmentIcon(GridAlignment alignment, bool isDefault)
        {
            var grid = new Grid
            {
                Width = 20,
                Height = 14,
                Opacity = isDefault ? 0.62 : 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            AddAlignmentLine(grid, alignment, 0, 14);
            AddAlignmentLine(grid, alignment, 4, 9);
            AddAlignmentLine(grid, alignment, 8, 12);
            AddAlignmentLine(grid, alignment, 12, 7);

            return grid;
        }

        private static FrameworkElement CreateVerticalAlignmentIcon(GridVerticalAlignment alignment, bool isDefault)
        {
            var grid = new Grid
            {
                Width = 20,
                Height = 16,
                Opacity = isDefault ? 0.62 : 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            double[] widths;
            double top;

            switch (alignment)
            {
                case GridVerticalAlignment.Top:
                    widths = new[] { 16.0, 12.0, 8.0 };
                    top = 1.5;
                    break;

                case GridVerticalAlignment.Bottom:
                    widths = new[] { 8.0, 12.0, 16.0 };
                    top = 5.5;
                    break;

                case GridVerticalAlignment.Center:
                default:
                    widths = new[] { 8.0, 16.0, 8.0 };
                    top = 3.5;
                    break;
            }

            AddVerticalPyramidLine(grid, top, widths[0]);
            AddVerticalPyramidLine(grid, top + 4.0, widths[1]);
            AddVerticalPyramidLine(grid, top + 8.0, widths[2]);

            return grid;
        }

        private static void AddVerticalPyramidLine(Grid grid, double top, double width)
        {
            if (grid == null)
            {
                return;
            }

            var line = new Border
            {
                Width = width,
                Height = 1.4,
                CornerRadius = new CornerRadius(0.7),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top, 0, 0)
            };

            SetTextBrushOpacity(line, Border.BackgroundProperty, 0.88);
            grid.Children.Add(line);
        }

        private static void AddAlignmentLine(Grid grid, GridAlignment alignment, double top, double width)
        {
            var line = new Border
            {
                Width = width,
                Height = 1.4,
                CornerRadius = new CornerRadius(0.7),
                HorizontalAlignment = ToHorizontalAlignment(alignment),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top, 0, 0)
            };
            SetTextBrushOpacity(line, Border.BackgroundProperty, 0.88);
            grid.Children.Add(line);
        }


        private static HorizontalAlignment ToHorizontalAlignment(GridAlignment alignment)
        {
            switch (alignment)
            {
                case GridAlignment.Center:
                    return HorizontalAlignment.Center;
                case GridAlignment.Right:
                    return HorizontalAlignment.Right;
                case GridAlignment.Left:
                default:
                    return HorizontalAlignment.Left;
            }
        }

        private static VerticalAlignment ToVerticalAlignment(GridVerticalAlignment alignment)
        {
            switch (alignment)
            {
                case GridVerticalAlignment.Top:
                    return VerticalAlignment.Top;
                case GridVerticalAlignment.Bottom:
                    return VerticalAlignment.Bottom;
                case GridVerticalAlignment.Center:
                default:
                    return VerticalAlignment.Center;
            }
        }

        private static string ResolveHeaderText(object header)
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

        private static string ResolveColumnDisplayName(DataGridColumn column)
        {
            var headerText = ResolveHeaderText(column?.Header);
            if (!string.IsNullOrWhiteSpace(headerText))
            {
                return headerText;
            }

            // Fall back to ColumnKey for columns with blank headers
            return ColumnVisibilityHelper.GetColumnKey(column) ?? string.Empty;
        }

        private static string GetColumnKey(DataGridColumn column)
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

        private static bool IsValidWidth(double width)
        {
            return !double.IsNaN(width) && !double.IsInfinity(width) && width > 0;
        }

        public void Dispose()
        {
            Detach();
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Controls
{
    public partial class GamesOverviewDataGridControl : UserControl, IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private ColumnWidthPersistenceService _columnPersistence;
        private bool _isAttached;

        private static readonly IReadOnlyDictionary<string, double> DefaultWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cover"] = 92,
                ["OverviewGameName"] = 500,
                ["OverviewPlatform"] = 40,
                ["OverviewLastPlayed"] = 240,
                ["OverviewPlaytime"] = 170,
                ["OverviewProgression"] = 360,
                ["TotalAchievements"] = 180,
                ["OverviewCollectionScore"] = 180,
                ["OverviewPrestigeScore"] = 180
            };

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable<GameOverviewItem>),
                typeof(GamesOverviewDataGridControl),
                new PropertyMetadata(null));

        public IEnumerable<GameOverviewItem> ItemsSource
        {
            get => (IEnumerable<GameOverviewItem>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(GameOverviewItem),
                typeof(GamesOverviewDataGridControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public GameOverviewItem SelectedItem
        {
            get => (GameOverviewItem)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty UseCoverImagesProperty =
            DependencyProperty.Register(
                nameof(UseCoverImages),
                typeof(bool),
                typeof(GamesOverviewDataGridControl),
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
                typeof(GamesOverviewDataGridControl),
                new PropertyMetadata(null, OnRowSizingChanged));

        public double? FixedRowHeight
        {
            get => (double?)GetValue(FixedRowHeightProperty);
            set => SetValue(FixedRowHeightProperty, value);
        }

        public static readonly DependencyProperty ShowGameMetadataProperty =
            DependencyProperty.Register(
                nameof(ShowGameMetadata),
                typeof(bool),
                typeof(GamesOverviewDataGridControl),
                new PropertyMetadata(true));

        public bool ShowGameMetadata
        {
            get => (bool)GetValue(ShowGameMetadataProperty);
            set => SetValue(ShowGameMetadataProperty, value);
        }

        public static readonly DependencyProperty ShowCompletionBorderProperty =
            DependencyProperty.Register(
                nameof(ShowCompletionBorder),
                typeof(bool),
                typeof(GamesOverviewDataGridControl),
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
                typeof(GamesOverviewDataGridControl),
                new PropertyMetadata("SidebarOverview"));

        public string ColumnSettingsKey
        {
            get => (string)GetValue(ColumnSettingsKeyProperty);
            set => SetValue(ColumnSettingsKeyProperty, value);
        }

        public static readonly DependencyProperty ShowColumnHeadersProperty =
            DependencyProperty.Register(
                nameof(ShowColumnHeaders),
                typeof(bool),
                typeof(GamesOverviewDataGridControl),
                new PropertyMetadata(true, OnShowColumnHeadersChanged));

        public bool ShowColumnHeaders
        {
            get => (bool)GetValue(ShowColumnHeadersProperty);
            set => SetValue(ShowColumnHeadersProperty, value);
        }

        public event SelectionChangedEventHandler SelectionChanged;

        public event EventHandler<DataGridSortingEventArgs> Sorting;

        public static readonly RoutedEvent RowPreviewMouseLeftButtonDownEvent =
            EventManager.RegisterRoutedEvent(
                nameof(RowPreviewMouseLeftButtonDown),
                RoutingStrategy.Bubble,
                typeof(MouseButtonEventHandler),
                typeof(GamesOverviewDataGridControl));

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
                typeof(GamesOverviewDataGridControl));

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
                typeof(GamesOverviewDataGridControl));

        public event MouseButtonEventHandler RowPreviewMouseRightButtonUp
        {
            add => AddHandler(RowPreviewMouseRightButtonUpEvent, value);
            remove => RemoveHandler(RowPreviewMouseRightButtonUpEvent, value);
        }

        public GamesOverviewDataGridControl()
        {
            InitializeComponent();
            UpdateColumnHeadersVisibility();
        }

        public DataGrid InternalDataGrid => GamesOverviewDataGrid;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isAttached)
            {
                return;
            }

            var settings = PlayniteAchievementsPlugin.Instance?.Settings;
            if (settings?.Persisted == null || GamesOverviewDataGrid == null)
            {
                return;
            }

            UpdateColumnHeadersVisibility();
            UpdateRealizedRowHeights();

            DataGridAlignmentBehavior.SetColumnCellAlignmentOverridesProvider(
                GamesOverviewDataGrid,
                () => GetAlignmentsByKey(settings));

            _columnPersistence = new ColumnWidthPersistenceService(
                GamesOverviewDataGrid,
                Logger,
                () => GetWidthsByKey(settings),
                map => SetWidthsByKey(settings, map),
                () => GetVisibilityByKey(settings),
                map => SetVisibilityByKey(settings, map),
                () => SavePluginSettings(settings),
                DefaultWidthSeeds,
                getOrder: () => GetOrderByKey(settings),
                setOrder: map => SetOrderByKey(settings, map),
                getCellAlignments: () => GetAlignmentsByKey(settings),
                setCellAlignments: map => SetAlignmentsByKey(settings, map),
                getDefaultCellAlignment: () => settings.Persisted?.GridCellAlignment ?? GridAlignment.Left,
                applyCellAlignments: () => DataGridAlignmentBehavior.Refresh(GamesOverviewDataGrid));
            _columnPersistence.ExcludedCellAlignmentKeys.Add("Cover");
            _columnPersistence.ExcludedCellAlignmentKeys.Add("OverviewPlatform");
            _columnPersistence.ExcludedCellAlignmentKeys.Add("OverviewProgression");
            _columnPersistence.Attach();
            _isAttached = true;
        }

        private static void OnShowColumnHeadersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamesOverviewDataGridControl control)
            {
                control.UpdateColumnHeadersVisibility();
            }
        }

        private void UpdateColumnHeadersVisibility()
        {
            if (GamesOverviewDataGrid != null)
            {
                GamesOverviewDataGrid.HeadersVisibility = ShowColumnHeaders
                    ? DataGridHeadersVisibility.Column
                    : DataGridHeadersVisibility.None;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private static void OnRowSizingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamesOverviewDataGridControl control)
            {
                control.UpdateRealizedRowHeights();
            }
        }

        private void GamesOverviewDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            ApplyFixedRowHeight(e.Row);
        }

        private void UpdateRealizedRowHeights()
        {
            if (GamesOverviewDataGrid == null)
            {
                return;
            }

            foreach (var item in GamesOverviewDataGrid.Items)
            {
                if (GamesOverviewDataGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
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
            if (settings?.Persisted == null)
            {
                return null;
            }

            return IsStartPageScope()
                ? settings.Persisted.StartPageGamesOverviewColumnWidths
                : settings.Persisted.GamesOverviewColumnWidths;
        }

        private void SetWidthsByKey(PlayniteAchievementsSettings settings, Dictionary<string, double> map)
        {
            if (settings?.Persisted == null)
            {
                return;
            }

            if (IsStartPageScope())
            {
                settings.Persisted.StartPageGamesOverviewColumnWidths = map;
            }
            else
            {
                settings.Persisted.GamesOverviewColumnWidths = map;
            }
        }

        private Dictionary<string, int> GetOrderByKey(PlayniteAchievementsSettings settings)
        {
            if (settings?.Persisted == null)
            {
                return null;
            }

            return IsStartPageScope()
                ? settings.Persisted.StartPageGamesOverviewColumnOrder
                : settings.Persisted.GamesOverviewColumnOrder;
        }

        private void SetOrderByKey(PlayniteAchievementsSettings settings, Dictionary<string, int> map)
        {
            if (settings?.Persisted == null)
            {
                return;
            }

            if (IsStartPageScope())
            {
                settings.Persisted.StartPageGamesOverviewColumnOrder = map;
            }
            else
            {
                settings.Persisted.GamesOverviewColumnOrder = map;
            }
        }

        private Dictionary<string, bool> GetVisibilityByKey(PlayniteAchievementsSettings settings)
        {
            if (settings?.Persisted == null)
            {
                return null;
            }

            return IsStartPageScope()
                ? settings.Persisted.StartPageGamesOverviewColumnVisibility
                : settings.Persisted.GamesOverviewColumnVisibility;
        }

        private void SetVisibilityByKey(PlayniteAchievementsSettings settings, Dictionary<string, bool> map)
        {
            if (settings?.Persisted == null)
            {
                return;
            }

            if (IsStartPageScope())
            {
                settings.Persisted.StartPageGamesOverviewColumnVisibility = map;
            }
            else
            {
                settings.Persisted.GamesOverviewColumnVisibility = map;
            }
        }

        private Dictionary<string, GridAlignment> GetAlignmentsByKey(PlayniteAchievementsSettings settings)
        {
            if (settings?.Persisted == null)
            {
                return null;
            }

            return IsStartPageScope()
                ? settings.Persisted.StartPageGamesOverviewColumnAlignments
                : settings.Persisted.GamesOverviewColumnAlignments;
        }

        private void SetAlignmentsByKey(PlayniteAchievementsSettings settings, Dictionary<string, GridAlignment> map)
        {
            if (settings?.Persisted == null)
            {
                return;
            }

            if (IsStartPageScope())
            {
                settings.Persisted.StartPageGamesOverviewColumnAlignments = map;
            }
            else
            {
                settings.Persisted.GamesOverviewColumnAlignments = map;
            }
        }

        private bool IsStartPageScope()
        {
            return string.Equals(ColumnSettingsKey, "StartPageOverview", StringComparison.OrdinalIgnoreCase);
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
                Logger.Warn(ex, "Failed to persist games overview column settings.");
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectionChanged?.Invoke(sender, e);
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            Sorting?.Invoke(sender, e);
        }

        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var forwardedEvent = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, e.ChangedButton)
            {
                RoutedEvent = RowPreviewMouseLeftButtonDownEvent,
                Source = sender
            };
            RaiseEvent(forwardedEvent);
            if (forwardedEvent.Handled)
            {
                e.Handled = true;
            }
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var forwardedEvent = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, e.ChangedButton)
            {
                RoutedEvent = RowPreviewMouseRightButtonDownEvent,
                Source = sender
            };
            RaiseEvent(forwardedEvent);
            if (forwardedEvent.Handled)
            {
                e.Handled = true;
            }
        }

        private void DataGridRow_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var forwardedEvent = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, e.ChangedButton)
            {
                RoutedEvent = RowPreviewMouseRightButtonUpEvent,
                Source = sender
            };
            RaiseEvent(forwardedEvent);
            if (forwardedEvent.Handled)
            {
                e.Handled = true;
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
            var header = FullscreenControllerNavigationService.GetFocusedDataGridColumnHeader(GamesOverviewDataGrid);
            if (header == null)
            {
                return false;
            }

            return OpenColumnVisibilityMenu(
                GamesOverviewDataGrid,
                header,
                useControllerPlacement: true);
        }

        public bool IsColumnHeaderFocusedForController()
        {
            return FullscreenControllerNavigationService.IsFocusWithinDataGridColumnHeader(GamesOverviewDataGrid);
        }

        public bool ActivateFocusedColumnHeaderForController()
        {
            return FullscreenControllerNavigationService.ActivateFocusedDataGridColumnHeader(GamesOverviewDataGrid);
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
            if (GamesOverviewDataGrid?.Columns == null)
            {
                return;
            }

            foreach (var column in GamesOverviewDataGrid.Columns)
            {
                column.SortDirection = null;
            }

            if (direction == null || string.IsNullOrWhiteSpace(sortMemberPath))
            {
                return;
            }

            var targetColumn = GamesOverviewDataGrid.Columns
                .FirstOrDefault(column => string.Equals(column?.SortMemberPath, sortMemberPath, StringComparison.Ordinal));
            if (targetColumn != null)
            {
                targetColumn.SortDirection = direction;
            }
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
            DataGridAlignmentBehavior.SetColumnCellAlignmentOverridesProvider(GamesOverviewDataGrid, null);
            _isAttached = false;
        }
    }
}

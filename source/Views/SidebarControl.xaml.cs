using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views
{
    public partial class SidebarControl : UserControl, IDisposable
    {
        private readonly SidebarViewModel _viewModel;
        private readonly ILogger _logger;
        private readonly ScanManager _achievementManager;
        private bool _isActive;
        private bool _ignoreNextSelectionChange;

        public SidebarControl()
        {
            InitializeComponent();
        }

        public SidebarControl(IPlayniteAPI api, ILogger logger, ScanManager achievementManager, PlayniteAchievementsSettings settings)
        {
            using (PlayniteAchievements.Common.PerfTrace.Measure(
                "SidebarControl.InitializeComponent",
                logger,
                settings?.Persisted?.EnableDiagnostics == true))
            {
                InitializeComponent();
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _achievementManager = achievementManager;

            _viewModel = new SidebarViewModel(achievementManager, api, logger, settings);
            DataContext = _viewModel;

            _viewModel.SetActive(false);
        }

        public void Activate()
        {
            if (_isActive)
            {
                return;
            }

            _isActive = true;
            _viewModel?.SetActive(true);

            // Kick an initial refresh without blocking sidebar open.
            _ = _viewModel?.RefreshViewAsync();
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
                _viewModel?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "SidebarControl dispose failed.");
            }
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
        }

        private void GamesOverview_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null) return;

            if (!(sender is DataGrid grid)) return;

            // Skip selection update if navigating via game name button
            if (_ignoreNextSelectionChange)
            {
                _ignoreNextSelectionChange = false;
                grid.SelectedItem = null;
                return;
            }

            if (grid.SelectedItem is GameOverviewItem item)
            {
                _viewModel.SelectedGame = item;
                ResetAchievementsScrollPosition();
            }
        }

        private void ResetAchievementsScrollPosition()
        {
            if (GameAchievementsDataGrid == null) return;

            // Use Dispatcher to wait for collection to update and layout to render
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (GameAchievementsDataGrid == null) return;

                if (GameAchievementsDataGrid.Items.Count > 0)
                {
                    GameAchievementsDataGrid.ScrollIntoView(GameAchievementsDataGrid.Items[0]);
                    GameAchievementsDataGrid.SelectedIndex = 0;
                }

                // Also try to reset via ScrollViewer for more reliable scroll reset
                if (FindVisualChild<ScrollViewer>(GameAchievementsDataGrid) is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToTop();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
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

        private void GameNameButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop event from bubbling to DataGrid row
            e.Handled = true;
        }

        private void GameNameButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Set flag to prevent SelectionChanged from updating the view
            _ignoreNextSelectionChange = true;
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

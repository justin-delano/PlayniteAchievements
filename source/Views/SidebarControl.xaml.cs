using System;
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
        private readonly AchievementManager _achievementManager;
        private bool _isActive;

        public SidebarControl()
        {
            InitializeComponent();
        }

        public SidebarControl(IPlayniteAPI api, ILogger logger, AchievementManager achievementManager, PlayniteAchievementsSettings settings)
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

            if (sender is DataGrid grid && grid.SelectedItem is GameOverviewItem item)
            {
                _viewModel.SelectedGame = item;
            }
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

            // Clear the DataGrid selection to cancel the filter after navigation completes
            if (sender is Button button)
            {
                var dataGrid = FindParent<DataGrid>(button);
                if (dataGrid != null)
                {
                    // Use Dispatcher to ensure selection clears after command executes
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        dataGrid.SelectedItem = null;
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        // Helper to find parent visual element
        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T result)
                    return result;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}

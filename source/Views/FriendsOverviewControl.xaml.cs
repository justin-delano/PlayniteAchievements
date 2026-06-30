using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PlayniteAchievements.Views
{
    public partial class FriendsOverviewControl : UserControl, IDisposable
    {
        private readonly FriendsOverviewViewModel _viewModel;
        private readonly OverviewLaunchContext _launchContext;
        private bool _loaded;

        public FriendsOverviewControl()
        {
            InitializeComponent();
        }

        internal FriendsOverviewControl(
            ILogger logger,
            IFriendCacheManager friendCache,
            RefreshEntryPoint refreshCoordinator,
            RefreshRuntime refreshRuntime,
            PlayniteAchievementsSettings settings,
            OverviewLaunchContext launchContext = OverviewLaunchContext.Sidebar)
        {
            InitializeComponent();
            _launchContext = launchContext;
            _viewModel = new FriendsOverviewViewModel(friendCache, refreshCoordinator, refreshRuntime, settings, logger);
            DataContext = _viewModel;
        }

        private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;

            if (_viewModel != null)
            {
                await _viewModel.LoadAsync().ConfigureAwait(true);
            }
        }

        public void Dispose()
        {
            FriendSummariesGridControl?.Dispose();
            FriendGameSummariesGridControl?.Dispose();
            FriendsAchievementsGrid?.Dispose();
            _viewModel?.Dispose();
        }

        private void ClearFriendSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearFriendSearch();
        }

        private void ClearGameSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearGameSearch();
        }

        private void ClearAchievementSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearAchievementSearch();
        }

        private void CloseViewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_launchContext == OverviewLaunchContext.Popout)
                {
                    Window.GetWindow(this)?.Close();
                    return;
                }

                PlayniteUiProvider.RestoreMainView();
            }
            catch (Exception ex)
            {
                LogManager.GetLogger()?.Debug(ex, "Failed to close friends overview view.");
            }
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearSelection();
            ClearGridSelection(FriendSummariesGridControl?.InternalDataGrid);
            ClearGridSelection(FriendGameSummariesGridControl?.InternalDataGrid);
            ClearGridSelection(FriendsAchievementsGrid?.InternalDataGrid);
        }

        private void ClearFriendSelection_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearFriendSelection();
            ClearGridSelection(FriendSummariesGridControl?.InternalDataGrid);
        }

        private void ClearGameSelection_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearGameSelection();
            ClearGridSelection(FriendGameSummariesGridControl?.InternalDataGrid);
        }

        private void RefreshModeSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            OpenSingleSelectRefreshModeContextMenu(
                RefreshModeSelectionButton,
                _viewModel.FriendRefreshModes,
                _viewModel.SelectedRefreshMode,
                selectedKey => _viewModel.SelectedRefreshMode = selectedKey);
        }

        private void TypeFilterSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            OpenMultiSelectFilterContextMenu(
                TypeFilterSelectionButton,
                _viewModel.TypeFilterOptions,
                option => _viewModel.IsTypeFilterSelected(option),
                (option, isSelected) => _viewModel.SetTypeFilterSelected(option, isSelected));
        }

        private void CategoryFilterSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            OpenMultiSelectFilterContextMenu(
                CategoryFilterSelectionButton,
                _viewModel.CategoryFilterOptions,
                option => _viewModel.IsCategoryFilterSelected(option),
                (option, isSelected) => _viewModel.SetCategoryFilterSelected(option, isSelected));
        }

        private static void OpenSingleSelectRefreshModeContextMenu(
            Button button,
            IEnumerable<RefreshMode> modes,
            string selectedModeKey,
            Action<string> setSelection)
        {
            if (button == null || setSelection == null)
            {
                return;
            }

            var menu = button.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();
            var itemStyle = button.TryFindResource("AchievementMultiSelectMenuItemStyle") as Style;
            foreach (var mode in modes?.Where(mode => mode != null && !string.IsNullOrWhiteSpace(mode.Key)) ?? Enumerable.Empty<RefreshMode>())
            {
                var modeKey = mode.Key;
                var item = new MenuItem
                {
                    Header = !string.IsNullOrWhiteSpace(mode.ShortDisplayName)
                        ? mode.ShortDisplayName
                        : (!string.IsNullOrWhiteSpace(mode.DisplayName) ? mode.DisplayName : modeKey),
                    IsCheckable = true,
                    IsChecked = string.Equals(modeKey, selectedModeKey, StringComparison.Ordinal)
                };
                if (itemStyle != null)
                {
                    item.Style = itemStyle;
                }

                item.Click += (_, __) => setSelection(modeKey);
                menu.Items.Add(item);
            }

            OpenSelectorContextMenu(button, menu);
        }

        private void OpenMultiSelectFilterContextMenu(
            Button button,
            IEnumerable<string> options,
            Func<string, bool> isSelected,
            Action<string, bool> setSelection)
        {
            if (button == null || isSelected == null || setSelection == null)
            {
                return;
            }

            var menu = button.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();
            var itemStyle = button.TryFindResource("AchievementMultiSelectMenuItemStyle") as Style;
            foreach (var option in options?.Where(value => !string.IsNullOrWhiteSpace(value)) ?? Enumerable.Empty<string>())
            {
                var value = option;
                var item = new MenuItem
                {
                    Header = value,
                    IsCheckable = true,
                    StaysOpenOnClick = true,
                    IsChecked = isSelected(value)
                };
                if (itemStyle != null)
                {
                    item.Style = itemStyle;
                }

                item.Click += (_, __) => setSelection(value, item.IsChecked);
                menu.Items.Add(item);
            }

            OpenSelectorContextMenu(button, menu);
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

        private void SummaryRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (!TryResolveSelectedRow(sender, e, out var row, out var grid))
            {
                return;
            }

            if (row.DataContext is FriendSummaryItem)
            {
                _viewModel.ClearFriendSelection();
            }
            else if (row.DataContext is FriendGameSummaryItem)
            {
                _viewModel.ClearGameSelection();
            }
            else
            {
                return;
            }

            ClearGridSelection(grid);

            e.Handled = true;
        }

        private void AchievementRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryResolveSelectedRow(sender, e, out var row, out var grid) ||
                !(row.DataContext is AchievementDisplayItem))
            {
                return;
            }

            ClearGridSelection(grid);
            e.Handled = true;
        }

        private static bool TryResolveSelectedRow(
            object sender,
            MouseButtonEventArgs e,
            out DataGridRow row,
            out DataGrid grid)
        {
            row = ResolveDataGridRow(sender, e);
            grid = FindParentDataGrid(row);
            if (row == null || grid == null)
            {
                return false;
            }

            return row.IsSelected ||
                   ReferenceEquals(grid.SelectedItem, row.DataContext) ||
                   ReferenceEquals(grid.CurrentItem, row.DataContext);
        }

        private static DataGridRow ResolveDataGridRow(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow senderRow)
            {
                return senderRow;
            }

            if (e?.Source is DataGridRow sourceRow)
            {
                return sourceRow;
            }

            return VisualTreeHelpers.FindVisualParent<DataGridRow>(
                e?.OriginalSource as DependencyObject ?? e?.Source as DependencyObject);
        }

        private static void ClearGridSelection(DataGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            try
            {
                grid.SelectedItem = null;
                grid.UnselectAll();
                grid.CurrentItem = null;
                Keyboard.ClearFocus();
            }
            catch
            {
                // Best effort; focus clearing should not break row toggling.
            }
        }

        private static DataGrid FindParentDataGrid(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is DataGrid grid)
                {
                    return grid;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}

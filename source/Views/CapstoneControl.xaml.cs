using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlayniteAchievements.Views
{
    public partial class CapstoneControl : UserControl
    {
        private readonly CapstoneViewModel _viewModel;

        public CapstoneControl(
            Guid gameId,
            AchievementManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            _viewModel = new CapstoneViewModel(gameId, achievementManager, playniteApi, logger, settings);
            DataContext = _viewModel;
            InitializeComponent();
            _viewModel.RequestClose += ViewModel_RequestClose;
        }

        public string WindowTitle => _viewModel?.WindowTitle ?? "Capstone Achievement";

        public event EventHandler RequestClose;

        public void Cleanup()
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
            }
        }

        private void ViewModel_RequestClose(object sender, EventArgs e)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void MarkerCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is CapstoneOptionItem item)
            {
                if (item.IsCurrentMarker)
                {
                    _viewModel?.ClearMarker();
                }
                else
                {
                    _viewModel?.SetMarker(item);
                }
            }
        }

        private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            if (source is CheckBox || VisualTreeHelpers.FindVisualParent<CheckBox>(source) != null)
            {
                return;
            }

            var row = VisualTreeHelpers.FindVisualParent<DataGridRow>(source);
            if (row == null)
            {
                return;
            }

            if (row.DataContext is CapstoneOptionItem item)
            {
                _viewModel?.ToggleReveal(item);
            }
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var sortDirection = DataGridSortingHelper.HandleSorting(sender, e);
            if (sortDirection == null)
            {
                return;
            }

            var column = e.Column;

            // Sort the collection
            var items = _viewModel.AchievementOptions.ToList();
            items.Sort((a, b) => column.SortMemberPath switch
            {
                "GlobalPercent" => sortDirection.Value == ListSortDirection.Ascending
                    ? a.GlobalPercent.CompareTo(b.GlobalPercent)
                    : b.GlobalPercent.CompareTo(a.GlobalPercent),
                "Points" => sortDirection.Value == ListSortDirection.Ascending
                    ? a.Points.CompareTo(b.Points)
                    : b.Points.CompareTo(a.Points),
                _ => 0
            });

            _viewModel.AchievementOptions.Clear();
            foreach (var item in items)
            {
                _viewModel.AchievementOptions.Add(item);
            }
        }
    }
}

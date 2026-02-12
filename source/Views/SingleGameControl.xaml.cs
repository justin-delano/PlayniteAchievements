using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    public partial class SingleGameControl : UserControl
    {
        public SingleGameControl()
        {
            InitializeComponent();
        }

        public SingleGameControl(
            Guid gameId,
            ScanManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            InitializeComponent();

            DataContext = new SingleGameControlModel(gameId, achievementManager, playniteApi, logger, settings);

            // Subscribe to settings saved event to refresh when credentials change
            PlayniteAchievementsPlugin.SettingsSaved += Plugin_SettingsSaved;
        }

        private void Plugin_SettingsSaved(object sender, EventArgs e)
        {
            RefreshView();
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
            ViewModel?.Dispose();
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

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ClearSearch();
        }
    }
}

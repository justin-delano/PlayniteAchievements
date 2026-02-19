using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using System;
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

            if (source is CheckBox || FindVisualParent<CheckBox>(source) != null)
            {
                return;
            }

            var row = FindVisualParent<DataGridRow>(source);
            if (row == null)
            {
                return;
            }

            if (row.DataContext is CapstoneOptionItem item)
            {
                _viewModel?.ToggleReveal(item);
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = child;
            while (parentObject != null)
            {
                if (parentObject is T parent)
                {
                    return parent;
                }

                parentObject = System.Windows.Media.VisualTreeHelper.GetParent(parentObject);
            }

            return null;
        }
    }
}

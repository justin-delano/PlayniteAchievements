using Playnite.SDK;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlayniteAchievements.Views
{
    public partial class CompletedMarkerControl : UserControl
    {
        private readonly CompletedMarkerViewModel _viewModel;

        public CompletedMarkerControl(
            Guid gameId,
            AchievementManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger)
        {
            _viewModel = new CompletedMarkerViewModel(gameId, achievementManager, playniteApi, logger);
            DataContext = _viewModel;
            InitializeComponent();
            _viewModel.RequestClose += ViewModel_RequestClose;
        }

        public string WindowTitle => _viewModel?.WindowTitle ?? "Completed Marker";

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

        private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;
            var row = FindVisualParent<DataGridRow>(source);
            if (row == null)
            {
                return;
            }

            if (row.DataContext is CompletedMarkerOptionItem item && item.Hidden && !item.Unlocked)
            {
                _viewModel?.ToggleRevealCommand?.Execute(item);
                e.Handled = true;
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

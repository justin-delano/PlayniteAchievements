using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views
{
    /// <summary>
    /// Interaction logic for ManualAchievementsSearchControl.xaml
    /// </summary>
    public partial class ManualAchievementsSearchControl : UserControl
    {
        private readonly ManualAchievementsSearchViewModel _viewModel;

        public ManualAchievementsSearchControl(
            ManualAchievementsSearchViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            _viewModel.RequestClose += ViewModel_RequestClose;
        }

        private void ViewModel_RequestClose(object sender, EventArgs e)
        {
            // Handled by parent window
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _viewModel.OkCommand.CanExecute(null))
            {
                _viewModel.OkCommand.Execute(null);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _viewModel.CancelCommand.CanExecute(null))
            {
                _viewModel.CancelCommand.Execute(null);
            }
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel?.SearchCommand.CanExecute(null) == true)
            {
                _viewModel.SearchCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_viewModel?.CancelCommand.CanExecute(null) == true)
                {
                    _viewModel.CancelCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.SelectedResult != null && _viewModel.OkCommand.CanExecute(null))
            {
                _viewModel.OkCommand.Execute(null);
            }
        }

        public void Cleanup()
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
                _viewModel.CancelSearch();
            }
        }
    }
}

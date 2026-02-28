using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views
{
    /// <summary>
    /// Interaction logic for ManualAchievementsEditControl.xaml
    /// </summary>
    public partial class ManualAchievementsEditControl : UserControl
    {
        private readonly ManualAchievementsEditViewModel _viewModel;

        public ManualAchievementsEditControl(
            ManualAchievementsEditViewModel viewModel)
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
            if (_viewModel != null && _viewModel.SaveCommand.CanExecute(null))
            {
                _viewModel.SaveCommand.Execute(null);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _viewModel.CancelCommand.CanExecute(null))
            {
                _viewModel.CancelCommand.Execute(null);
            }
        }

        public void Cleanup()
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
            }
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    public partial class PerGameView : UserControl
    {
        public PerGameView()
        {
            InitializeComponent();
        }

        public PerGameView(
            Guid gameId,
            AchievementManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            InitializeComponent();

            DataContext = new PerGameViewModel(gameId, achievementManager, playniteApi, logger, settings);
        }

        private PerGameViewModel ViewModel => DataContext as PerGameViewModel;

        public string WindowTitle => ViewModel?.GameName != null
            ? $"{ViewModel.GameName} - Achievements"
            : "Achievements";

        public void Cleanup()
        {
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
    }
}

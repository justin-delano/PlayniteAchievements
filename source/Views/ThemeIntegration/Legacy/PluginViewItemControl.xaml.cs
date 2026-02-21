// --SUCCESSSTORY--
using System;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible view item control for theme integration.
    /// Matches the original SuccessStory plugin styling.
    /// Displays achievement summary per game in grid/list views.
    /// </summary>
    public partial class PluginViewItemControl : PluginUserControl
    {
        private static readonly ILogger _logger = LogManager.GetLogger(nameof(PluginViewItemControl));
        private PlayniteAchievementsPlugin Plugin => PlayniteAchievementsPlugin.Instance;

        #region IntegrationViewItemWithProgressBar Property

        public static readonly DependencyProperty IntegrationViewItemWithProgressBarProperty =
            DependencyProperty.Register(
                nameof(IntegrationViewItemWithProgressBar),
                typeof(bool),
                typeof(PluginViewItemControl),
                new PropertyMetadata(false));

        public bool IntegrationViewItemWithProgressBar
        {
            get => (bool)GetValue(IntegrationViewItemWithProgressBarProperty);
            set => SetValue(IntegrationViewItemWithProgressBarProperty, value);
        }

        #endregion

        // Dependency properties for achievement data (bound in XAML)
        public static readonly DependencyProperty UnlockedCountProperty =
            DependencyProperty.Register(nameof(UnlockedCount), typeof(int), typeof(PluginViewItemControl), new PropertyMetadata(0));

        public int UnlockedCount
        {
            get => (int)GetValue(UnlockedCountProperty);
            set => SetValue(UnlockedCountProperty, value);
        }

        public static readonly DependencyProperty AchievementCountProperty =
            DependencyProperty.Register(nameof(AchievementCount), typeof(int), typeof(PluginViewItemControl), new PropertyMetadata(0));

        public int AchievementCount
        {
            get => (int)GetValue(AchievementCountProperty);
            set => SetValue(AchievementCountProperty, value);
        }

        public PluginViewItemControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TryUpdateFromDataContext();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            TryUpdateFromDataContext();
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            // GameContext can be used if DataContext doesn't provide a game
            if (newContext != null && newContext.Id != Guid.Empty)
            {
                UpdateForGame(newContext.Id);
            }
            else
            {
                TryUpdateFromDataContext();
            }
        }

        private void TryUpdateFromDataContext()
        {
            using (PerfScope.Start(_logger, "Theme.PluginViewItemControl.TryUpdateFromDataContext", thresholdMs: 16))
            {
                var game = GetGameFromDataContext(DataContext);
                if (game != null && game.Id != Guid.Empty)
                {
                    UpdateForGame(game.Id);
                }
                else
                {
                    ClearData();
                }
            }
        }

        /// <summary>
        /// Extracts a Game from various DataContext types that Playnite uses.
        /// GridView items use GamesCollectionViewEntry which wraps the Game.
        /// </summary>
        private Game GetGameFromDataContext(object dataContext)
        {
            if (dataContext == null)
                return null;

            // Direct Game reference
            if (dataContext is Game game)
                return game;

            // GamesCollectionViewEntry - the wrapper Playnite uses for GridView items
            var type = dataContext.GetType();
            if (type.Name == "GamesCollectionViewEntry")
            {
                // The Game property holds the actual game
                var gameProperty = type.GetProperty("Game");
                if (gameProperty != null)
                {
                    return gameProperty.GetValue(dataContext) as Game;
                }
            }

            return null;
        }

        private void UpdateForGame(Guid gameId)
        {
            using (PerfScope.Start(_logger, "Theme.PluginViewItemControl.UpdateForGame", thresholdMs: 16, context: gameId.ToString()))
            {
                var gameData = default(GameAchievementData);
                using (PerfScope.Start(_logger, "Theme.PluginViewItemControl.GetGameAchievementData", thresholdMs: 16, context: gameId.ToString()))
                {
                    gameData = Plugin?.AchievementManager?.GetGameAchievementData(gameId);
                }

                if (gameData == null || !gameData.HasAchievements || (gameData.Achievements?.Count ?? 0) == 0)
                {
                    ClearData();
                    return;
                }

                var achievements = gameData.Achievements;
                UnlockedCount = achievements.Count(a => a.Unlocked);
                AchievementCount = achievements.Count;
                Visibility = Visibility.Visible;
            }
        }

        private void ClearData()
        {
            UnlockedCount = 0;
            AchievementCount = 0;
            Visibility = Visibility.Collapsed;
        }
    }
}
// --END SUCCESSSTORY--

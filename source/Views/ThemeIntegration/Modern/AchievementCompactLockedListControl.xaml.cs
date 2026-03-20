using System.Windows.Controls;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.ThemeIntegration.Modern
{
    /// <summary>
    /// Desktop theme integration control displaying locked achievements in a horizontal scrolling row.
    /// Shows compact achievement icons with progress bars and rarity glow effects.
    /// Filters to show only locked (not yet unlocked) achievements.
    /// </summary>
    public partial class AchievementCompactLockedListControl : AchievementCompactListControlBase
    {
        public AchievementCompactLockedListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Returns true only for locked achievements (not unlocked).
        /// </summary>
        protected override bool FilterAchievement(AchievementDetail achievement) => !achievement.Unlocked;

        /// <summary>
        /// Refreshes the ItemsControl ItemsSource binding.
        /// </summary>
        protected override void RefreshItemsSource()
        {
            if (AchievementsList != null)
            {
                // Direct reassignment without null first - WPF detects collection changes
                // and only updates what's needed rather than rebuilding entire visual tree
                AchievementsList.ItemsSource = DisplayItems;
            }
        }
    }
}


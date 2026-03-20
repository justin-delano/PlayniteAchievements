using System.Windows.Controls;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.ThemeIntegration.Modern
{
    /// <summary>
    /// Modern theme integration control displaying all achievements in a horizontal scrolling row.
    /// Shows compact achievement icons with progress bars and rarity glow effects.
    /// Inherits filtering and data loading from AchievementCompactListControlBase.
    /// </summary>
    public partial class AchievementCompactListControl : AchievementCompactListControlBase
    {
        public AchievementCompactListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Returns true to show all achievements (no filtering).
        /// </summary>
        protected override bool FilterAchievement(AchievementDetail achievement) => true;

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


using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements progress bar control for theme integration.
    /// Displays progress bar with percentage overlay and rarity badges.
    /// </summary>
    public partial class AchievementProgressBarControl : SingleGameDataControlBase
    {
        public AchievementProgressBarControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called after data is loaded. Updates the rarity badges.
        /// </summary>
        protected override void OnDataLoaded()
        {
            Badges?.UpdateFromRarityStats(UltraRare, Rare, Uncommon, Common);
        }
    }
}

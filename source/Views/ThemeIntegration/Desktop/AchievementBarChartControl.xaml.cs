using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements bar chart control for theme integration.
    /// Displays unlock timeline using LiveCharts with the same TimelineViewModel as the sidebar.
    /// </summary>
    public partial class AchievementBarChartControl : SingleGameDataControlBase
    {
        /// <summary>
        /// Gets the timeline view model that manages chart data and state.
        /// Shared with the sidebar for consistent behavior and styling.
        /// </summary>
        public TimelineViewModel TimelineViewModel { get; } = new TimelineViewModel();

        public AchievementBarChartControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called after data is loaded. Updates the chart.
        /// </summary>
        protected override void OnDataLoaded()
        {
            if (AllAchievements == null || !AllAchievements.Any())
            {
                TimelineViewModel.SetCounts(null);
                return;
            }

            // Build counts by date from unlocked achievements
            var countsByDate = AllAchievements
                .Where(a => a.Unlocked && a.UnlockTimeUtc.HasValue)
                .GroupBy(a => a.UnlockTimeUtc.Value.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            TimelineViewModel.SetCounts(countsByDate);
        }
    }
}

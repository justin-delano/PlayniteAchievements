using System.Collections.ObjectModel;
using System.Windows;
using LiveCharts;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements pie chart control for theme integration.
    /// Displays rarity distribution as a pie chart with radial badge icons.
    /// </summary>
    public partial class AchievementPieChartControl : SingleGameDataControlBase
    {
        private readonly PieChartViewModel _viewModel = new PieChartViewModel();

        /// <summary>
        /// Gets the pie series collection for the chart.
        /// </summary>
        public SeriesCollection PieSeries => _viewModel.PieSeries;

        /// <summary>
        /// Gets the legend items for the chart.
        /// </summary>
        public ObservableCollection<LegendItem> LegendItems => _viewModel.LegendItems;

        public AchievementPieChartControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called after data is loaded. Updates the pie chart.
        /// </summary>
        protected override void OnDataLoaded()
        {
            _viewModel.SetRarityData(
                Common.Unlocked, Uncommon.Unlocked, Rare.Unlocked, UltraRare.Unlocked, LockedCount,
                Common.Total, Uncommon.Total, Rare.Total, UltraRare.Total,
                "Common", "Uncommon", "Rare", "Ultra Rare", "Locked");
        }
    }
}

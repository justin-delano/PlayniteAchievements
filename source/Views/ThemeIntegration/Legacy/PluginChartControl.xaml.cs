// --SUCCESSSTORY--
using System;
using System.Windows;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible chart control for theme integration.
    /// </summary>
    public partial class PluginChartControl : ThemeControlBase
    {
        private readonly ChartViewModel _viewModel = new ChartViewModel();

        protected override bool EnableAutomaticThemeDataUpdates => true;

        protected override bool ShouldHandleLegacyThemeDataChange(string propertyName)
        {
            return propertyName == nameof(LegacyThemeData.ListAchUnlockDateAsc);
        }

        protected override void OnThemeDataUpdated()
        {
            UpdateChart();
        }

        public PluginChartControl()
        {
            InitializeComponent();

            DataContext = _viewModel;
            _viewModel.HideChartOptions = false;
            _viewModel.AllPeriod = true;
            _viewModel.CutPeriod = false;
            _viewModel.DisableAnimations = true;
        }

        private void UpdateChart()
        {
            try
            {
                var settings = Plugin?.Settings;
                if (settings?.LegacyTheme?.ListAchUnlockDateAsc == null)
                {
                    _viewModel.UpdateFromAchievements(Array.Empty<AchievementDetail>());
                    return;
                }

                _viewModel.UpdateFromAchievements(settings.LegacyTheme.ListAchUnlockDateAsc);
            }
            catch
            {
                _viewModel.UpdateFromAchievements(Array.Empty<AchievementDetail>());
            }
        }

        private void ToggleButtonAllPeriod_Click(object sender, RoutedEventArgs e)
        {
            UpdateChart();
        }

        private void ToggleButtonCut_Click(object sender, RoutedEventArgs e)
        {
            UpdateChart();
        }
    }
}
// --END SUCCESSSTORY--

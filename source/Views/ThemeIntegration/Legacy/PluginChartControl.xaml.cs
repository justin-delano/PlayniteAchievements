// --SUCCESSSTORY--
using System;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Views.ThemeIntegration.Base;
using PlayniteAchievements.Views.ThemeIntegration.Legacy;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible chart control for theme integration.
    /// </summary>
    public partial class PluginChartControl : ThemeControlBase
    {
        private readonly ChartViewModel _viewModel = new ChartViewModel();

        public PluginChartControl()
        {
            InitializeComponent();

            DataContext = _viewModel;
            _viewModel.HideChartOptions = false;
            _viewModel.AllPeriod = true;
            _viewModel.CutPeriod = false;
            _viewModel.DisableAnimations = true;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateChart();
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            base.GameContextChanged(oldContext, newContext);
            UpdateChart();
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

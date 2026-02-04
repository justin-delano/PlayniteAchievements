// --SUCCESSSTORY--
using System;
using System.ComponentModel;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible chart control for theme integration.
    /// </summary>
    public partial class SuccessStoryPluginChartControl : SettingsAwareControlBase
    {
        private readonly SuccessStoryChartViewModel _viewModel = new SuccessStoryChartViewModel();

        public SuccessStoryPluginChartControl()
        {
            InitializeComponent();

            // Mirror SuccessStory: the control uses its own chart-focused data context.
            DataContext = _viewModel;
            _viewModel.HideChartOptions = false;
            _viewModel.AllPeriod = true;
            _viewModel.CutPeriod = false;
            _viewModel.DisableAnimations = true;

            // Watch the settings properties that affect this control
            WatchProperty("SuccessStoryTheme.ListAchUnlockDateAsc");
            WatchProperty("SuccessStoryTheme.ListAchUnlockDateDesc");
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            base.GameContextChanged(oldContext, newContext);
            UpdateFromSettings();
        }

        protected override void UpdateFromSettings()
        {
            try
            {
                var settings = Plugin?.Settings;
                if (settings?.SuccessStoryTheme?.ListAchUnlockDateAsc == null)
                {
                    _viewModel.UpdateFromAchievements(Array.Empty<AchievementDetail>());
                    return;
                }

                _viewModel.UpdateFromAchievements(settings.SuccessStoryTheme.ListAchUnlockDateAsc);
            }
            catch
            {
                _viewModel.UpdateFromAchievements(Array.Empty<AchievementDetail>());
            }
        }

        private void ToggleButtonAllPeriod_Click(object sender, RoutedEventArgs e)
        {
            UpdateFromSettings();
        }

        private void ToggleButtonCut_Click(object sender, RoutedEventArgs e)
        {
            UpdateFromSettings();
        }
    }
}
// --END SUCCESSSTORY--

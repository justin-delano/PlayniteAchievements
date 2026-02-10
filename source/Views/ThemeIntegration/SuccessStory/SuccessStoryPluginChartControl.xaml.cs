// --SUCCESSSTORY--
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
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
    public partial class SuccessStoryPluginChartControl : AchievementThemeControlBase
    {
        private readonly SuccessStoryChartViewModel _viewModel = new SuccessStoryChartViewModel();
        private bool _updatePending;
        private readonly HashSet<string> _watchedProperties = new HashSet<string>(StringComparer.Ordinal);

        public SuccessStoryPluginChartControl()
        {
            InitializeComponent();

            DataContext = _viewModel;
            _viewModel.HideChartOptions = false;
            _viewModel.AllPeriod = true;
            _viewModel.CutPeriod = false;
            _viewModel.DisableAnimations = true;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            WatchProperty("SuccessStoryTheme.ListAchUnlockDateAsc");
            WatchProperty("SuccessStoryTheme.ListAchUnlockDateDesc");
        }

        private void WatchProperty(string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName))
            {
                _watchedProperties.Add(propertyName);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings != null)
            {
                Plugin.Settings.PropertyChanged -= Settings_PropertyChanged;
                Plugin.Settings.PropertyChanged += Settings_PropertyChanged;
            }

            UpdateFromSettings();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings != null)
            {
                Plugin.Settings.PropertyChanged -= Settings_PropertyChanged;
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (_watchedProperties.Contains(e.PropertyName))
            {
                RequestUpdate();
            }
        }

        private void RequestUpdate()
        {
            if (_updatePending)
            {
                return;
            }

            _updatePending = true;
            Dispatcher?.BeginInvoke(new Action(() =>
            {
                _updatePending = false;
                UpdateFromSettings();
            }), DispatcherPriority.Background);
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            base.GameContextChanged(oldContext, newContext);
            UpdateFromSettings();
        }

        private void UpdateFromSettings()
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

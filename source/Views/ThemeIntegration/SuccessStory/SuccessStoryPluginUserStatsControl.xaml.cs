// --SUCCESSSTORY--
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible user stats control for theme integration.
    /// </summary>
    public partial class SuccessStoryPluginUserStatsControl : ThemeControlBase
    {
        private readonly SuccessStoryUserStatsViewModel _viewModel = new SuccessStoryUserStatsViewModel();

        public SuccessStoryPluginUserStatsControl()
        {
            InitializeComponent();

            DataContext = _viewModel;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
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
            if (e.PropertyName == "SuccessStoryTheme.Unlocked"
                || e.PropertyName == "SuccessStoryTheme.Locked"
                || e.PropertyName == "SuccessStoryTheme.Common"
                || e.PropertyName == "SuccessStoryTheme.NoCommon"
                || e.PropertyName == "SuccessStoryTheme.Rare"
                || e.PropertyName == "SuccessStoryTheme.UltraRare")
            {
                UpdateFromSettings();
            }
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            base.GameContextChanged(oldContext, newContext);
            UpdateFromSettings();
        }

        protected void UpdateFromSettings()
        {
            var settings = Plugin?.Settings;
            var items = new ObservableCollection<SuccessStoryUserStatsItem>();

            if (settings?.SuccessStoryTheme != null)
            {
                var theme = settings.SuccessStoryTheme;
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Total", ValueShow = $"{theme.Unlocked}/{(theme.Unlocked + theme.Locked)}" });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Unlocked", ValueShow = theme.Unlocked.ToString() });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Locked", ValueShow = theme.Locked.ToString() });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "", ValueShow = "" });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Common", ValueShow = theme.Common?.Stats ?? "0 / 0" });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Uncommon", ValueShow = theme.NoCommon?.Stats ?? "0 / 0" });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Rare", ValueShow = theme.Rare?.Stats ?? "0 / 0" });
                items.Add(new SuccessStoryUserStatsItem { NameShow = "Ultra Rare", ValueShow = theme.UltraRare?.Stats ?? "0 / 0" });
            }

            _viewModel.ItemsSource = items;
        }
    }
}
// --END SUCCESSSTORY--

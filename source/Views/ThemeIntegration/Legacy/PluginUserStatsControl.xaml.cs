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
using PlayniteAchievements.Views.ThemeIntegration.Legacy;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible user stats control for theme integration.
    /// </summary>
    public partial class PluginUserStatsControl : ThemeControlBase
    {
        private readonly UserStatsViewModel _viewModel = new UserStatsViewModel();

        public PluginUserStatsControl()
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
            if (e.PropertyName == "Unlocked"
                || e.PropertyName == "Locked"
                || e.PropertyName == "Common"
                || e.PropertyName == "Uncommon"
                || e.PropertyName == "Rare"
                || e.PropertyName == "UltraRare")
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
            var items = new ObservableCollection<UserStatsItem>();

            if (settings?.Theme != null)
            {
                var theme = settings.Theme;
                var unlocked = theme.UnlockedCount;
                var locked = theme.LockedCount;
                items.Add(new UserStatsItem { NameShow = "Total", ValueShow = $"{unlocked}/{(unlocked + locked)}" });
                items.Add(new UserStatsItem { NameShow = "Unlocked", ValueShow = unlocked.ToString() });
                items.Add(new UserStatsItem { NameShow = "Locked", ValueShow = locked.ToString() });
                items.Add(new UserStatsItem { NameShow = "", ValueShow = "" });
                items.Add(new UserStatsItem { NameShow = "Common", ValueShow = theme.Common?.Stats ?? "0 / 0" });
                items.Add(new UserStatsItem { NameShow = "Uncommon", ValueShow = theme.Uncommon?.Stats ?? "0 / 0" });
                items.Add(new UserStatsItem { NameShow = "Rare", ValueShow = theme.Rare?.Stats ?? "0 / 0" });
                items.Add(new UserStatsItem { NameShow = "Ultra Rare", ValueShow = theme.UltraRare?.Stats ?? "0 / 0" });
            }

            _viewModel.ItemsSource = items;
        }
    }
}
// --END SUCCESSSTORY--

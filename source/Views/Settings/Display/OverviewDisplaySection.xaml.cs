using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Views.Settings.Display
{
    /// <summary>
    /// Display settings: Overview section. Hosts score card, pie chart and grid options for the
    /// overview window.
    /// </summary>
    public partial class OverviewDisplaySection : UserControl
    {
        private readonly PlayniteAchievementsSettings _settings;

        public OverviewDisplaySection()
        {
            InitializeComponent();
        }

        internal OverviewDisplaySection(PlayniteAchievementsSettings settings)
            : this()
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private void ToggleOverviewGameSummariesGridSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settings?.Persisted;
            if (persisted != null)
            {
                persisted.OverviewGameSummariesGridSortDescending = !persisted.OverviewGameSummariesGridSortDescending;
            }
        }

        private void ToggleOverviewSelectedGameGridSortDescending(object sender, RoutedEventArgs e)
        {
            var persisted = _settings?.Persisted;
            if (persisted != null)
            {
                persisted.OverviewSelectedGameGridSortDescending = !persisted.OverviewSelectedGameGridSortDescending;
            }
        }
    }
}

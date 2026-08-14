using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the RecentAchievements widget: the shared achievement grid over the
    /// snapshot's recent unlocks, capped by the per-instance item count option.
    /// </summary>
    public sealed class RecentAchievementsWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private bool _showColumnHeaders = true;
        private double? _rowHeight;
        private string _columnSettingsKey = ShowcaseGridSurfaces.RecentAchievements;

        public BulkObservableCollection<AchievementDisplayItem> Items { get; } =
            new BulkObservableCollection<AchievementDisplayItem>();

        public bool ShowColumnHeaders
        {
            get => _showColumnHeaders;
            private set => SetValue(ref _showColumnHeaders, value);
        }

        public double? RowHeight
        {
            get => _rowHeight;
            private set => SetValue(ref _rowHeight, value);
        }

        /// <summary>Per-instance surface key so each placed widget keeps its own column layout.</summary>
        public string ColumnSettingsKey
        {
            get => _columnSettingsKey;
            private set => SetValue(ref _columnSettingsKey, value);
        }

        protected override void Refresh()
        {
            ShowColumnHeaders = Density != WidgetViewportDensity.Compact;
            RowHeight = Density == WidgetViewportDensity.Compact ? 30d : (double?)null;
            ColumnSettingsKey = ShowcaseGridSurfaces.ForInstance(
                ShowcaseGridSurfaces.RecentAchievements,
                Projection?.Instance?.InstanceId);
            Items.ReplaceAll(Projection?.AchievementRows ?? Array.Empty<AchievementDisplayItem>());
        }
    }
}

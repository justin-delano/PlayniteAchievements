using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the GameSummaries widget: the shared game-summaries grid over the snapshot's
    /// game summaries, sorted/filtered/capped by the per-instance options.
    /// </summary>
    public sealed class GameSummariesWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private bool _showColumnHeaders = true;
        private double? _rowHeight;
        private string _columnSettingsKey = ShowcaseGridSurfaces.GameSummaries;

        public BulkObservableCollection<GameSummaryItem> Items { get; } =
            new BulkObservableCollection<GameSummaryItem>();

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
            RowHeight = Density == WidgetViewportDensity.Compact ? 32d : (double?)null;
            ColumnSettingsKey = ShowcaseGridSurfaces.ForInstance(
                ShowcaseGridSurfaces.GameSummaries,
                Projection?.Instance?.InstanceId);
            Items.ReplaceAll(Projection?.Games ?? Array.Empty<GameSummaryItem>());
        }
    }
}

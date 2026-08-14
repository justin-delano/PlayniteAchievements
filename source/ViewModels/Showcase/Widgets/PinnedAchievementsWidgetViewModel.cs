using System;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the PinnedAchievements widget: the shared achievement grid over the pinned
    /// rows. Missing pins arrive as placeholder rows that keep the pin identity, so the
    /// grid row menu can still unpin and reorder them; blank placeholder names are
    /// substituted with the localized "unavailable" strings here.
    /// </summary>
    public sealed class PinnedAchievementsWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private bool _showColumnHeaders = true;
        private double? _rowHeight;

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

        protected override void Refresh()
        {
            ShowColumnHeaders = Density != WidgetViewportDensity.Compact;
            RowHeight = Density == WidgetViewportDensity.Compact ? 30d : (double?)null;

            var rows = Projection?.AchievementRows ?? Array.Empty<AchievementDisplayItem>();
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.DisplayName))
                {
                    row.DisplayName = ResourceProvider.GetString("LOCPlayAch_Showcase_UnavailableAchievement");
                }

                if (string.IsNullOrWhiteSpace(row.GameName))
                {
                    row.GameName = ResourceProvider.GetString("LOCPlayAch_Showcase_UnavailableGame");
                }
            }

            Items.ReplaceAll(rows);
        }
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the GameSummaries widget: the shared game-summaries grid over the snapshot's
    /// game summaries, sorted/searched/capped by the per-instance options. Sorting runs
    /// here (not in the projection) so a sort edit re-orders this widget's rows without
    /// re-projecting the whole dashboard.
    /// </summary>
    public sealed class GameSummariesWidgetViewModel
        : ShowcaseGameGridWidgetViewModelBase
    {
        protected override string BaseSurfaceKey => ShowcaseGridSurfaces.GameSummaries;

        protected override IEnumerable<GameSummaryItem> SelectItems(
            ShowcaseWidgetProjection projection) => projection?.Games;

        protected override IEnumerable<GameSummaryItem> OrderItems(IEnumerable<GameSummaryItem> items)
        {
            var list = (items ?? Enumerable.Empty<GameSummaryItem>())
                .Where(item => item != null)
                .ToList();
            var options = GridOptions as GameSummaryGridOptions;
            GameSummariesSortHelper.Sort(
                list,
                options?.SortMode ?? GameSummariesSortMode.RecentUnlock,
                options?.SortDescending == false
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending);
            return list;
        }

        protected override bool ShouldRefreshItemsFor(string propertyName)
        {
            return base.ShouldRefreshItemsFor(propertyName) ||
                propertyName == nameof(GameSummaryGridOptions.SortMode) ||
                propertyName == nameof(GameSummaryGridOptions.SortDescending);
        }
    }
}

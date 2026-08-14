using System.Collections.Generic;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the GameSummaries widget: the shared game-summaries grid over the snapshot's
    /// game summaries, sorted/filtered/capped by the per-instance options.
    /// </summary>
    public sealed class GameSummariesWidgetViewModel
        : ShowcaseGridWidgetViewModelBase<GameSummaryItem>
    {
        protected override string BaseSurfaceKey => ShowcaseGridSurfaces.GameSummaries;

        protected override IEnumerable<GameSummaryItem> SelectItems(
            ShowcaseWidgetProjection projection) => projection?.Games;
    }
}

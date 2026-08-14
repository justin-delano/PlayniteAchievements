using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the PinnedAchievements widget: the shared achievement grid over the pinned
    /// rows. Missing pins arrive as placeholder rows that keep the pin identity, so the
    /// grid row menu can still unpin and reorder them; blank placeholder names are
    /// substituted with the localized "unavailable" strings here.
    /// </summary>
    public sealed class PinnedAchievementsWidgetViewModel
        : ShowcaseAchievementGridWidgetViewModelBase
    {
        protected override string BaseSurfaceKey => ShowcaseGridSurfaces.PinnedAchievements;

        // One instance per dashboard, so the pins keep a single stable column layout.
        protected override bool UsesPerInstanceSurface => false;

        protected override IEnumerable<AchievementDisplayItem> SelectItems(
            ShowcaseWidgetProjection projection)
        {
            var rows = projection?.AchievementRows;
            if (rows == null)
            {
                return null;
            }

            foreach (var row in rows.Where(row => row != null))
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

            return rows;
        }
    }
}

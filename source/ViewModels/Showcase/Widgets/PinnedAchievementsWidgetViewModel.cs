using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs a PinnedAchievements widget instance: its grid and selected pin collection
    /// are independent from every other placement. Missing pins arrive as placeholder rows, so the
    /// grid row menu can still unpin and reorder them; blank placeholder names are
    /// substituted with the localized "unavailable" strings here.
    /// </summary>
    public sealed class PinnedAchievementsWidgetViewModel
        : ShowcaseAchievementGridWidgetViewModelBase
    {
        protected override string BaseSurfaceKey => ShowcaseGridSurfaces.PinnedAchievements;

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

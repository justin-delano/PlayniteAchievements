using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Services.Summaries
{
    /// <summary>
    /// Builds one <see cref="GameSummaryItem"/> per distinct category label from a set of
    /// achievement display items. Mirrors <see cref="GameSummaryItemBuilder"/>'s stats
    /// projection (<see cref="AchievementStatsAccumulator.FromDisplayItems"/> ->
    /// <see cref="AchievementGameStats.ApplyTo"/>) but keys on the free-form category label
    /// rather than the game, so the achievement grid can present a per-category rollup.
    /// </summary>
    internal static class CategorySummaryBuilder
    {
        public static List<GameSummaryItem> Build(IEnumerable<AchievementDisplayItem> achievements)
        {
            var result = new List<GameSummaryItem>();
            var source = achievements as IReadOnlyList<AchievementDisplayItem>
                ?? achievements?.ToList();
            if (source == null || source.Count == 0)
            {
                return result;
            }

            var groups = new Dictionary<string, List<AchievementDisplayItem>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                if (item == null)
                {
                    continue;
                }

                var label = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(item.CategoryLabel);
                if (!groups.TryGetValue(label, out var bucket))
                {
                    bucket = new List<AchievementDisplayItem>();
                    groups[label] = bucket;
                }

                bucket.Add(item);
            }

            foreach (var label in AchievementCategoryFilterOrderHelper.BuildOrderedCategoryLabels(source, i => i.CategoryLabel))
            {
                if (!groups.TryGetValue(label, out var bucket))
                {
                    continue;
                }

                var display = AchievementCategoryTypeHelper.ToCategoryLabelDisplayText(label);
                var item = new CategorySummaryItem
                {
                    CategoryLabel = label,
                    GameName = display,
                    SortingName = display,
                    GameCoverPath = ResolveSharedCover(bucket)
                };

                AchievementStatsAccumulator
                    .FromDisplayItems(bucket)
                    .ApplyTo(item);

                result.Add(item);
            }

            return result;
        }

        // A category has no cover of its own. When every achievement in the group shares a
        // single game cover (the single-game surfaces), surface it so the Cover column stays
        // meaningful; on cross-game surfaces the covers differ, so leave it blank.
        private static string ResolveSharedCover(IReadOnlyList<AchievementDisplayItem> bucket)
        {
            string cover = null;
            foreach (var item in bucket)
            {
                var candidate = item?.GameCoverPath;
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                if (cover == null)
                {
                    cover = candidate;
                }
                else if (!string.Equals(cover, candidate, System.StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return cover;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

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

            var preferredOrder = source
                .Where(item => item != null && item.CategoryOrderIndex < int.MaxValue)
                .OrderBy(item => item.CategoryOrderIndex)
                .Select(item => item.CategoryLabel)
                .ToList();

            foreach (var label in AchievementCategoryFilterOrderHelper.BuildOrderedCategoryLabels(source, i => i.CategoryLabel, preferredOrder))
            {
                if (!groups.TryGetValue(label, out var bucket))
                {
                    continue;
                }

                var display = AchievementCategoryTypeHelper.ToCategoryLabelDisplayText(label);
                var item = new CategorySummaryItem
                {
                    CategoryLabel = label,
                    PlayniteGameId = ResolveSharedGameId(bucket),
                    GameName = display,
                    SortingName = display,
                    GameLogo = ResolveSharedImage(bucket, item => item.CategoryIconPath),
                    GameCoverPath = ResolveSharedImage(bucket, item => item.CategoryCoverPath)
                };

                AchievementStatsAccumulator
                    .FromDisplayItems(bucket)
                    .ApplyTo(item);

                result.Add(item);
            }

            return result;
        }

        private static System.Guid? ResolveSharedGameId(IReadOnlyList<AchievementDisplayItem> bucket)
        {
            System.Guid? gameId = null;
            foreach (var item in bucket)
            {
                var candidate = item?.PlayniteGameId;
                if (!candidate.HasValue || candidate.Value == System.Guid.Empty)
                {
                    continue;
                }

                if (!gameId.HasValue)
                {
                    gameId = candidate.Value;
                }
                else if (gameId.Value != candidate.Value)
                {
                    return null;
                }
            }

            return gameId;
        }

        private static string ResolveSharedImage(
            IReadOnlyList<AchievementDisplayItem> bucket,
            System.Func<AchievementDisplayItem, string> selector)
        {
            if (selector == null)
            {
                return null;
            }

            string image = null;
            foreach (var item in bucket)
            {
                var candidate = selector(item);
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                if (image == null)
                {
                    image = candidate;
                }
                else if (!string.Equals(image, candidate, System.StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return image;
        }
    }
}

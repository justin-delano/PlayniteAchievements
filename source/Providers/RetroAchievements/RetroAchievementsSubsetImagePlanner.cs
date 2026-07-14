using PlayniteAchievements.Providers.RetroAchievements.Models;
using PlayniteAchievements.Services.Achievements;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal static class RetroAchievementsSubsetImagePlanner
    {
        // Plans one default image pair per subset category: (normalized label -> icon/cover URLs).
        // Base-game achievements keep the game-image fallback; only fetched subsets are passed in.
        // Dedupe is first-wins by label to match the subset achievement assignment order.
        internal static IReadOnlyList<(string Label, string IconUrl, string CoverUrl)> BuildSubsetImagePlan(
            IReadOnlyList<(string CategoryLabel, RaGameInfoUserProgress Info)> subsets)
        {
            var plan = new List<(string Label, string IconUrl, string CoverUrl)>();
            if (subsets == null || subsets.Count == 0)
            {
                return plan;
            }

            var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subset in subsets)
            {
                var iconUrl = RetroAchievementsAchievementMapper.NormalizeImageUrl(subset.Info?.ImageIcon);
                if (iconUrl == null)
                {
                    continue;
                }

                // Blank labels normalize to the Default category, which the display-side
                // resolver never serves default art for, so skip them entirely.
                var label = AchievementCategoryTypeHelper.NormalizeCategory(subset.CategoryLabel);
                if (label == null ||
                    string.Equals(label, AchievementCategoryTypeHelper.DefaultCategoryLabel, StringComparison.OrdinalIgnoreCase) ||
                    !seenLabels.Add(label))
                {
                    continue;
                }

                var coverUrl = RetroAchievementsAchievementMapper.NormalizeImageUrl(subset.Info?.ImageBoxArt);
                plan.Add((label, iconUrl, coverUrl));
            }

            return plan;
        }
    }
}

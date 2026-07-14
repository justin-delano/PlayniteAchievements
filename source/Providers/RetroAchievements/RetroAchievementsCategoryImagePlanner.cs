using PlayniteAchievements.Providers.RetroAchievements.Models;
using PlayniteAchievements.Services.Achievements;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal static class RetroAchievementsCategoryImagePlanner
    {
        // Plans one default image pair per category: (normalized label -> icon/cover URLs).
        // Sources are (label, game info) pairs for the base game and each fetched subset.
        // Dedupe is first-wins by label to match the achievement assignment order.
        internal static IReadOnlyList<(string Label, string IconUrl, string CoverUrl)> BuildCategoryImagePlan(
            IReadOnlyList<(string CategoryLabel, RaGameInfoUserProgress Info)> sources)
        {
            var plan = new List<(string Label, string IconUrl, string CoverUrl)>();
            if (sources == null || sources.Count == 0)
            {
                return plan;
            }

            var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                var iconUrl = RetroAchievementsAchievementMapper.NormalizeImageUrl(source.Info?.ImageIcon);
                if (iconUrl == null)
                {
                    continue;
                }

                // Blank labels normalize to the Default category, which the display-side
                // resolver never serves default art for, so skip them entirely.
                var label = AchievementCategoryTypeHelper.NormalizeCategory(source.CategoryLabel);
                if (label == null ||
                    string.Equals(label, AchievementCategoryTypeHelper.DefaultCategoryLabel, StringComparison.OrdinalIgnoreCase) ||
                    !seenLabels.Add(label))
                {
                    continue;
                }

                var coverUrl = RetroAchievementsAchievementMapper.NormalizeImageUrl(source.Info?.ImageBoxArt);
                plan.Add((label, iconUrl, coverUrl));
            }

            return plan;
        }
    }
}

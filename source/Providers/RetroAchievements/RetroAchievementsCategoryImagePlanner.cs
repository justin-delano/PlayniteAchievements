using PlayniteAchievements.Providers.RetroAchievements.Models;
using PlayniteAchievements.Services.Achievements;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal static class RetroAchievementsCategoryImagePlanner
    {
        // RetroAchievements serves literal placeholder art for missing media instead of
        // omitting the field: /Images/000001.png (icon) and /Images/000002.png ("No
        // Screenshot Found"). Treat them as absent.
        private static readonly string[] PlaceholderImagePaths =
        {
            "/Images/000001.png",
            "/Images/000002.png"
        };

        // Plans one default icon per category: (normalized label -> icon URL). Sources are
        // (label, game info) pairs for the base game and each fetched subset. Covers are
        // never planned: RA box art is inconsistent across sets, so the cover slot keeps
        // the natural Playnite game-cover fallback. Dedupe is first-wins by label to match
        // the achievement assignment order.
        internal static IReadOnlyList<(string Label, string IconUrl)> BuildCategoryImagePlan(
            IReadOnlyList<(string CategoryLabel, RaGameInfoUserProgress Info)> sources)
        {
            var plan = new List<(string Label, string IconUrl)>();
            if (sources == null || sources.Count == 0)
            {
                return plan;
            }

            var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                var iconUrl = NormalizeMediaUrl(source.Info?.ImageIcon);
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

                plan.Add((label, iconUrl));
            }

            return plan;
        }

        private static string NormalizeMediaUrl(string pathOrUrl)
        {
            return IsPlaceholderImage(pathOrUrl)
                ? null
                : RetroAchievementsAchievementMapper.NormalizeImageUrl(pathOrUrl);
        }

        private static bool IsPlaceholderImage(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                return false;
            }

            var value = pathOrUrl.Trim();
            foreach (var placeholder in PlaceholderImagePaths)
            {
                if (value.EndsWith(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

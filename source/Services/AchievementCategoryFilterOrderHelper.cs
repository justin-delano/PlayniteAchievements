using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services
{
    internal static class AchievementCategoryFilterOrderHelper
    {
        public static List<string> BuildOrderedCategoryLabels<T>(
            IEnumerable<T> source,
            Func<T, string> categorySelector,
            IEnumerable<string> preferredOrder = null)
        {
            var ordered = new List<string>();
            if (source == null || categorySelector == null)
            {
                return ordered;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                var normalized = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(categorySelector(item));
                if (!string.IsNullOrWhiteSpace(normalized) && !sourceLabels.ContainsKey(normalized))
                {
                    sourceLabels[normalized] = normalized;
                }
            }

            foreach (var label in preferredOrder ?? Array.Empty<string>())
            {
                var normalized = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(label);
                if (string.IsNullOrWhiteSpace(normalized) ||
                    !sourceLabels.TryGetValue(normalized, out var sourceLabel) ||
                    !seen.Add(normalized))
                {
                    continue;
                }

                ordered.Add(sourceLabel);
            }

            foreach (var item in source)
            {
                var normalized = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(categorySelector(item));
                if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                ordered.Add(normalized);
            }

            return ordered;
        }
    }
}

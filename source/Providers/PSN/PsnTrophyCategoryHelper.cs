using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.PSN
{
    // Classifies a PSN trophy by its trophy group: the canonical category type (Base/DLC) and the
    // free-text category label. Kept separate from PsnScanner so the pure group-to-category logic is
    // unit-testable without the scanner's HTTP/session dependencies.
    internal static class PsnTrophyCategoryHelper
    {
        // Maps a trophy's group id to the canonical category type. The base/default group (empty,
        // "default", "base", or "000") is "Base"; every other group is "DLC".
        internal static string MapTrophyGroupToCategoryType(string trophyGroupId)
        {
            var normalized = (trophyGroupId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "base", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "000", StringComparison.OrdinalIgnoreCase))
            {
                return "Base";
            }

            return "DLC";
        }

        // Resolves the free-text category label for a trophy from its group. The base/default group
        // maps to null (rendered as the localized "Default" label by the hydrator, consistent with the
        // Category=null convention); only DLC groups take a named label from the group title.
        internal static string ResolveCategory(
            string trophyGroupId,
            IReadOnlyDictionary<string, string> groupNameById)
        {
            if (string.Equals(MapTrophyGroupToCategoryType(trophyGroupId), "Base", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var key = PsnTrophyMatchHelper.NormalizeGroupId(trophyGroupId);
            if (groupNameById != null &&
                groupNameById.TryGetValue(key, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return null;
        }
    }
}

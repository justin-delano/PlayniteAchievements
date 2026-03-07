using PlayniteAchievements.Providers.PSN.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PlayniteAchievements.Providers.PSN
{
    internal static class PsnTrophyMatchHelper
    {
        internal static string NormalizeGroupId(string trophyGroupId)
        {
            var group = (trophyGroupId ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(group) ? "default" : group;
        }

        internal static string GetTrophyKey(PsnUserTrophy trophy)
        {
            var group = NormalizeGroupId(trophy?.TrophyGroupId);
            var trophyId = (trophy?.TrophyId ?? 0).ToString(CultureInfo.InvariantCulture);
            return $"{group}:{trophyId}";
        }

        internal static string GetTrophyKey(PsnTrophyDetail trophy)
        {
            var group = NormalizeGroupId(trophy?.TrophyGroupId);
            var trophyId = (trophy?.TrophyId ?? 0).ToString(CultureInfo.InvariantCulture);
            return $"{group}:{trophyId}";
        }

        private static string GetTrophyIdKey(PsnUserTrophy trophy)
        {
            return (trophy?.TrophyId ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string GetTrophyIdKey(PsnTrophyDetail trophy)
        {
            return (trophy?.TrophyId ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        internal static Dictionary<string, PsnUserTrophy> BuildUserTrophyLookupByGroupAndId(IEnumerable<PsnUserTrophy> trophies)
        {
            return (trophies ?? Enumerable.Empty<PsnUserTrophy>())
                .GroupBy(GetTrophyKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.FirstOrDefault(t => t != null && t.Earned) ?? g.First());
        }

        internal static Dictionary<string, PsnUserTrophy> BuildUserTrophyLookupById(IEnumerable<PsnUserTrophy> trophies)
        {
            return (trophies ?? Enumerable.Empty<PsnUserTrophy>())
                .GroupBy(GetTrophyIdKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.FirstOrDefault(t => t != null && t.Earned) ?? g.First());
        }

        internal static bool TryResolveUserTrophy(
            PsnTrophyDetail detail,
            IReadOnlyDictionary<string, PsnUserTrophy> userByGroupAndId,
            IReadOnlyDictionary<string, PsnUserTrophy> userById,
            out PsnUserTrophy userEntry,
            out bool usedIdFallback)
        {
            userEntry = null;
            usedIdFallback = false;
            if (detail == null)
            {
                return false;
            }

            var exactKey = GetTrophyKey(detail);
            if (userByGroupAndId != null &&
                userByGroupAndId.TryGetValue(exactKey, out userEntry) &&
                userEntry != null)
            {
                return true;
            }

            var idKey = GetTrophyIdKey(detail);
            if (userById != null &&
                userById.TryGetValue(idKey, out userEntry) &&
                userEntry != null)
            {
                usedIdFallback = true;
                return true;
            }

            return false;
        }
    }
}

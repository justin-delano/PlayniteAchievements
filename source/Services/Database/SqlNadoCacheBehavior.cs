using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;

namespace PlayniteAchievements.Services.Database
{
    internal static class SqlNadoCacheBehavior
    {
        // Builds fallback achievement definitions from a friend unlock scrape, used only when a game has
        // no cached definitions. Requires a stable ApiName plus a display name; rows a provider could only
        // key as unlock-status (no ApiName/name) are skipped and duplicate ApiNames are collapsed, so this
        // seeds nothing for providers that do not carry definition-quality data in their scrape.
        public static List<AchievementDetail> BuildDefinitionsFromFriendRows(IEnumerable<FriendAchievementRow> rows)
        {
            var result = new List<AchievementDetail>();
            if (rows == null)
            {
                return result;
            }

            var seenApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ApiName) || string.IsNullOrWhiteSpace(row.DisplayName))
                {
                    continue;
                }

                var apiName = row.ApiName.Trim();
                if (!seenApiNames.Add(apiName))
                {
                    continue;
                }

                result.Add(new AchievementDetail
                {
                    ApiName = apiName,
                    DisplayName = row.DisplayName,
                    Description = row.Description,
                    UnlockedIconPath = FirstNonBlank(row.UnlockedIconUrl, row.IconUrl),
                    LockedIconPath = FirstNonBlank(row.LockedIconUrl, row.IconUrl, row.UnlockedIconUrl),
                    Points = row.Points,
                    ScaledPoints = row.ScaledPoints,
                    Category = row.Category,
                    CategoryType = row.CategoryType,
                    TrophyType = row.TrophyType,
                    Hidden = row.Hidden,
                    IsCapstone = row.IsCapstone ||
                        string.Equals(row.TrophyType?.Trim(), "platinum", StringComparison.OrdinalIgnoreCase),
                    GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                    Rarity = row.Rarity ?? RarityTier.Common,
                    ProgressDenom = row.ProgressDenom
                });
            }

            return result;
        }

        private static string FirstNonBlank(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        public static List<long> ComputeStaleDefinitionIds(
            IDictionary<string, long> existingDefinitionIdsByApiName,
            IEnumerable<string> incomingApiNames)
        {
            var staleIds = new List<long>();
            if (existingDefinitionIdsByApiName == null || existingDefinitionIdsByApiName.Count == 0)
            {
                return staleIds;
            }

            var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (incomingApiNames != null)
            {
                foreach (var apiName in incomingApiNames)
                {
                    var normalized = apiName?.Trim();
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        desired.Add(normalized);
                    }
                }
            }

            foreach (var pair in existingDefinitionIdsByApiName)
            {
                var apiName = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(apiName) || desired.Contains(apiName))
                {
                    continue;
                }

                if (pair.Value > 0)
                {
                    staleIds.Add(pair.Value);
                }
            }

            return staleIds;
        }

        public static bool ShouldMarkLegacyImportDone(
            int parseFailedCount,
            int dbWriteFailedCount,
            int remainingFileCount)
        {
            return parseFailedCount <= 0 &&
                   dbWriteFailedCount <= 0 &&
                   remainingFileCount <= 0;
        }

        public static bool ShouldFallbackToProviderGameIdLookup(
            string providerKey,
            string playniteGameId,
            long? providerGameId)
        {
            if (!providerGameId.HasValue || providerGameId.Value <= 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(playniteGameId))
            {
                return false;
            }

            return true;
        }

        public static bool IsRetroAchievementsProvider(string providerKey)
        {
            return !string.IsNullOrWhiteSpace(providerKey) &&
                   string.Equals(providerKey.Trim(), "RetroAchievements", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanReclaimExophaseProxy(string incomingProviderKey)
        {
            var incomingProvider = NormalizeProviderKey(incomingProviderKey);

            if (string.IsNullOrWhiteSpace(incomingProvider))
            {
                return false;
            }

            return !string.Equals(incomingProvider, "Exophase", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(incomingProvider, "Manual", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(incomingProvider, "Unmapped", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ComputeIsCompleted(
            bool providerIsCompleted,
            int unlockedCount,
            int totalCount,
            bool markerUnlocked)
        {
            if (providerIsCompleted || markerUnlocked)
            {
                return true;
            }

            return totalCount > 0 && unlockedCount >= totalCount;
        }

        private static string NormalizeProviderKey(string providerKey)
        {
            return string.IsNullOrWhiteSpace(providerKey)
                ? null
                : providerKey.Trim();
        }
    }
}

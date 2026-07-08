using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamHuntersCategoryEnricher
    {
        internal const string BaseCategoryType = "Base";
        internal const string DlcCategoryType = "DLC";

        private readonly SteamHuntersApiClient _apiClient;
        private readonly ILogger _logger;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<int, Task<SteamHuntersAchievementGroupsResponse>> _groupsByAppId =
            new Dictionary<int, Task<SteamHuntersAchievementGroupsResponse>>();

        public SteamHuntersCategoryEnricher(
            SteamHuntersApiClient apiClient,
            ILogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger;
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _groupsByAppId.Clear();
            }
        }

        public async Task EnrichAsync(
            int appId,
            string gameName,
            IList<AchievementDetail> achievements,
            CancellationToken cancel)
        {
            if (appId <= 0 || achievements == null || achievements.Count == 0)
            {
                return;
            }

            SteamHuntersAchievementGroupsResponse response;
            try
            {
                response = await GetGroupsAsync(appId, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"[SteamHunters] Failed to enrich categories for appId={appId}.");
                return;
            }

            if (response == null)
            {
                return;
            }

            ApplyGroups(achievements, response.Groups, response.GroupBy, gameName);
        }

        internal static int ApplyGroups(
            IList<AchievementDetail> achievements,
            IList<SteamHuntersAchievementGroup> groups,
            string groupBy = null,
            string gameName = null)
        {
            if (achievements == null || achievements.Count == 0)
            {
                return 0;
            }

            var baseFallbackLabel = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();

            var achievementsByApiName = achievements
                .Where(achievement => !string.IsNullOrWhiteSpace(achievement?.ApiName))
                .GroupBy(achievement => achievement.ApiName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            if (achievementsByApiName.Count == 0)
            {
                return 0;
            }

            var updated = 0;
            foreach (var achievement in achievements)
            {
                if (achievement == null)
                {
                    continue;
                }

                if (SetCategory(achievement, BaseCategoryType, baseFallbackLabel))
                {
                    updated++;
                }
            }

            var assignedApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < (groups?.Count ?? 0); i++)
            {
                var group = groups[i];
                var type = ResolveCategoryType(groupBy, i);
                var label = NormalizeGroupLabel(group)
                    ?? (string.Equals(type, BaseCategoryType, StringComparison.Ordinal)
                        ? baseFallbackLabel
                        : null);

                foreach (var apiName in group?.AchievementApiNames ?? Enumerable.Empty<string>())
                {
                    var normalizedApiName = NormalizeApiName(apiName);
                    if (string.IsNullOrWhiteSpace(normalizedApiName) ||
                        !assignedApiNames.Add(normalizedApiName) ||
                        !achievementsByApiName.TryGetValue(normalizedApiName, out var matches))
                    {
                        continue;
                    }

                    foreach (var achievement in matches)
                    {
                        if (SetCategory(achievement, type, label))
                        {
                            updated++;
                        }
                    }
                }
            }

            return updated;
        }

        private static string ResolveCategoryType(string groupBy, int groupIndex)
        {
            if (string.Equals(groupBy, "game", StringComparison.OrdinalIgnoreCase))
            {
                return BaseCategoryType;
            }

            return groupIndex == 0 ? BaseCategoryType : DlcCategoryType;
        }

        private Task<SteamHuntersAchievementGroupsResponse> GetGroupsAsync(
            int appId,
            CancellationToken cancel)
        {
            lock (_cacheLock)
            {
                if (!_groupsByAppId.TryGetValue(appId, out var task))
                {
                    task = _apiClient.GetAchievementGroupsAsync(appId, cancel);
                    _groupsByAppId[appId] = task;
                }

                return task;
            }
        }

        private static bool SetCategory(
            AchievementDetail achievement,
            string categoryType,
            string category)
        {
            if (achievement == null)
            {
                return false;
            }

            var normalizedType = string.IsNullOrWhiteSpace(categoryType) ? null : categoryType.Trim();
            var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            var changed =
                !string.Equals(achievement.CategoryType, normalizedType, StringComparison.Ordinal) ||
                !string.Equals(achievement.Category, normalizedCategory, StringComparison.Ordinal);

            achievement.CategoryType = normalizedType;
            achievement.Category = normalizedCategory;
            return changed;
        }

        private static string NormalizeApiName(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string NormalizeGroupLabel(SteamHuntersAchievementGroup group)
        {
            var label = group?.Name;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = group?.DlcAppName;
            }

            label = label?.Trim();
            return string.IsNullOrWhiteSpace(label) ? null : label;
        }
    }
}

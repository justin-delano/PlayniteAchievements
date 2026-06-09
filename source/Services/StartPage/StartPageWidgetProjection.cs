using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Services.StartPage
{
    public static class StartPageWidgetProjection
    {
        public const int DefaultGridRowLimit = 25;
        public const int MaxGridRowLimit = 500;

        public static List<GameOverviewItem> ProjectGamesOverview(
            IEnumerable<GameOverviewItem> items,
            PersistedSettings settings,
            int rowLimit = DefaultGridRowLimit)
        {
            var list = (items ?? Enumerable.Empty<GameOverviewItem>())
                .Where(item => item != null)
                .ToList();

            GamesOverviewSortHelper.SortByConfiguredDefault(list, settings);

            return TakeLimited(list, rowLimit);
        }

        public static List<AchievementDisplayItem> ProjectRecentUnlocks(
            IEnumerable<AchievementDisplayItem> items,
            PersistedSettings settings,
            int rowLimit = DefaultGridRowLimit)
        {
            var list = (items ?? Enumerable.Empty<AchievementDisplayItem>())
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList();

            var sort = AchievementSortHelper.GetConfiguredDefaultSort(
                settings,
                AchievementSortSurface.SidebarRecentAchievements);

            if (!sort.PreservesSourceOrder)
            {
                var stableOrder = AchievementSortHelper.CreateStableOrderMap(list);
                var comparison = AchievementSortHelper.GetComparison(
                    sort.SortMemberPath,
                    sort.Direction,
                    AchievementSortScope.RecentAchievements);
                if (comparison != null)
                {
                    list.Sort(AchievementSortHelper.WithStableOrder(comparison, stableOrder));
                }
            }

            return TakeLimited(list, rowLimit);
        }

        public static string NormalizeProviderKey(string providerKey)
        {
            return string.IsNullOrWhiteSpace(providerKey)
                ? "Unknown"
                : providerKey.Trim();
        }

        private static List<T> TakeLimited<T>(List<T> list, int rowLimit)
        {
            var limit = Math.Max(1, Math.Min(MaxGridRowLimit, rowLimit));
            if (list.Count <= limit)
            {
                return list;
            }

            return list.Take(limit).ToList();
        }
    }
}

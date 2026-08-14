using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Search;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Services.StartPage
{
    public static class StartPageWidgetProjection
    {
        public static List<GameSummaryItem> FilterGameSummariesForStartPage(
            IEnumerable<GameSummaryItem> items,
            PersistedSettings settings,
            bool includeProgressScope)
        {
            var activityScope = settings?.StartPageActivityScope ??
                PersistedSettings.DefaultStartPageActivityScope;
            var progressScope = includeProgressScope
                ? settings?.StartPageProgressScope ?? PersistedSettings.DefaultStartPageProgressScope
                : GameProgressScope.None;

            return OverviewGameSummaryFilters.ApplyActivityAndProgressFilters(
                    (items ?? Enumerable.Empty<GameSummaryItem>()).Where(item => item != null),
                    activityScope,
                    progressScope)
                .ToList();
        }

        public static List<FriendAchievementDisplayItem> ProjectFriendRecentUnlocks(
            IEnumerable<FriendAchievementDisplayItem> items,
            PersistedSettings settings,
            int? rowLimit = null,
            PlayniteAchievementsSettings appearanceSettings = null)
        {
            var widgetSettings = settings?.StartPageFriendsRecentUnlocksGrid ??
                new StartPageFriendsRecentUnlocksGridSettings();

            var list = (items ?? Enumerable.Empty<FriendAchievementDisplayItem>())
                .Where(item => item != null)
                .ToList();

            var sort = new AchievementSortSpec(
                widgetSettings.SortMode,
                widgetSettings.SortDescending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending);

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

            var limited = DisplayGridRowLimitHelper.Limit(
                list,
                rowLimit ?? widgetSettings.MaxRows);

            var appearanceByGameId = appearanceSettings != null
                ? new Dictionary<Guid?, AchievementDisplayItem.AppearanceSettingsSnapshot>()
                : null;

            return limited
                .Select(item => CloneFriendAchievement(item, appearanceSettings, appearanceByGameId))
                .Where(item => item != null)
                .ToList();
        }

        public static List<FriendAchievementDisplayItem> FilterFriendRecentUnlocksBySearch(
            IEnumerable<FriendAchievementDisplayItem> items,
            SearchTextIndex<FriendAchievementDisplayItem> searchIndex,
            string searchText)
        {
            var list = (items ?? Enumerable.Empty<FriendAchievementDisplayItem>())
                .Where(item => item != null)
                .ToList();
            var query = SearchQuery.From(searchText);
            if (!query.HasValue)
            {
                return list;
            }

            var index = searchIndex ?? new SearchTextIndex<FriendAchievementDisplayItem>(item =>
                SearchTextBuilder.FromValues(item?.GameName, item?.FriendName, item?.DisplayName));
            index.Rebuild(list);
            return list.Where(item => index.Matches(item, query)).ToList();
        }

        private static FriendAchievementDisplayItem CloneFriendAchievement(
            FriendAchievementDisplayItem item,
            PlayniteAchievementsSettings appearanceSettings,
            IDictionary<Guid?, AchievementDisplayItem.AppearanceSettingsSnapshot> appearanceByGameId)
        {
            if (item == null)
            {
                return null;
            }

            var clone = new FriendAchievementDisplayItem();
            clone.UpdateFrom(
                item.Source,
                item.GameName,
                item.PlayniteGameId,
                item.ShowHiddenIcon,
                item.ShowHiddenTitle,
                item.ShowHiddenDescription,
                item.ShowHiddenSuffix,
                item.ShowLockedIcon,
                item.UseSeparateLockedIconsWhenAvailable,
                item.ShowRarityBar,
                item.SortingName,
                item.GameIconPath,
                item.GameCoverPath,
                item.CategoryOrderIndex,
                item.CategoryArtPath);
            clone.FriendName = item.FriendName;
            clone.FriendExternalUserId = item.FriendExternalUserId;
            clone.FriendAvatarPath = item.FriendAvatarPath;
            clone.ProviderKey = item.ProviderKey;
            clone.PointsValue = item.PointsValue;
            clone.CategoryType = item.CategoryType;
            clone.CategoryLabel = item.CategoryLabel;
            clone.IsRevealed = item.IsRevealed;
            clone.AppId = item.AppId;
            clone.ProviderGameKey = item.ProviderGameKey;
            clone.FriendGroupId = item.FriendGroupId;
            clone.UnlockedBySelf = item.UnlockedBySelf;
            clone.ShowFriendSpoilers = item.ShowFriendSpoilers;
            clone.SetDynamicAchievementsGameCommand = item.SetDynamicAchievementsGameCommand;
            clone.FilterDynamicLibraryAchievementsByProviderCommand = item.FilterDynamicLibraryAchievementsByProviderCommand;
            clone.OpenViewAchievementsWindow = item.OpenViewAchievementsWindow;
            clone.OpenManageAchievementsWindow = item.OpenManageAchievementsWindow;
            clone.SetDynamicFriendScopeProviderCommand = item.SetDynamicFriendScopeProviderCommand;
            clone.SetDynamicFriendScopeUserCommand = item.SetDynamicFriendScopeUserCommand;
            clone.SetDynamicFriendScopeGameCommand = item.SetDynamicFriendScopeGameCommand;

            if (appearanceSettings != null)
            {
                if (!appearanceByGameId.TryGetValue(clone.PlayniteGameId, out var snapshot))
                {
                    snapshot = AchievementDisplayItem.CreateAppearanceSettingsSnapshot(
                        appearanceSettings,
                        clone.PlayniteGameId,
                        null);
                    appearanceByGameId[clone.PlayniteGameId] = snapshot;
                }

                clone.ApplyAppearanceSettings(snapshot);
            }

            return clone;
        }

        public static string NormalizeProviderKey(string providerKey)
        {
            return string.IsNullOrWhiteSpace(providerKey)
                ? "Unknown"
                : providerKey.Trim();
        }

    }
}

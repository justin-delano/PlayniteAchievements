using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Services.Summaries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PlayniteAchievements.Services.Friends
{
    internal sealed class FriendOverviewProjection
    {
        public const string AllScopeKey = "All";

        private readonly List<FriendSummaryItem> _friends;
        private readonly List<FriendGameSummaryItem> _aggregateGames;
        private readonly List<FriendAchievementDisplayItem> _recentUnlocks;
        private readonly List<FriendAchievementDisplayItem> _allUnlockedAchievements;
        private readonly List<FriendGameLinkItem> _friendGameLinks;
        private readonly Dictionary<string, List<FriendGameSummaryItem>> _selectedFriendGamesByFriendKey;
        private readonly HashSet<string> _gameUnlockKeys;
        private readonly HashSet<string> _friendGameUnlockKeys;

        public FriendOverviewProjection(FriendsOverviewData data)
        {
            data = data ?? new FriendsOverviewData();
            _friends = data.Friends ?? new List<FriendSummaryItem>();
            _aggregateGames = data.Games ?? new List<FriendGameSummaryItem>();
            _recentUnlocks = data.RecentUnlocks ?? new List<FriendAchievementDisplayItem>();
            _allUnlockedAchievements = data.AllUnlockedAchievements ?? new List<FriendAchievementDisplayItem>();
            _friendGameLinks = data.FriendGameLinks ?? new List<FriendGameLinkItem>();
            _selectedFriendGamesByFriendKey = BuildSelectedFriendGameSummaries();
            _gameUnlockKeys = BuildGameUnlockKeys();
            _friendGameUnlockKeys = BuildFriendGameUnlockKeys();
        }

        public IReadOnlyList<FriendSummaryItem> Friends => _friends;

        public IReadOnlyList<FriendGameSummaryItem> AggregateGames => _aggregateGames;

        public IReadOnlyList<FriendAchievementDisplayItem> RecentUnlocks => _recentUnlocks;

        public IReadOnlyList<FriendAchievementDisplayItem> AllUnlockedAchievements => _allUnlockedAchievements;

        public IReadOnlyList<FriendGameLinkItem> FriendGameLinks => _friendGameLinks;

        public bool HasData =>
            _friends.Count > 0 ||
            _aggregateGames.Count > 0 ||
            _recentUnlocks.Count > 0 ||
            _allUnlockedAchievements.Count > 0;

        public IReadOnlyList<FriendGameSummaryItem> GetSelectedFriendGames(FriendSummaryItem friend)
        {
            var friendKey = BuildFriendKey(friend?.ProviderKey, friend?.ExternalUserId);
            if (!string.IsNullOrWhiteSpace(friendKey) &&
                _selectedFriendGamesByFriendKey.TryGetValue(friendKey, out var games))
            {
                return games;
            }

            return Array.Empty<FriendGameSummaryItem>();
        }

        public FriendSummaryItem FindFriend(string friendScopeKey)
        {
            if (IsAllScope(friendScopeKey))
            {
                return null;
            }

            return _friends.FirstOrDefault(friend =>
                string.Equals(GetFriendScopeKey(friend), friendScopeKey, StringComparison.OrdinalIgnoreCase));
        }

        public FriendGameSummaryItem FindGame(string gameScopeKey)
        {
            if (IsAllScope(gameScopeKey))
            {
                return null;
            }

            return _aggregateGames.FirstOrDefault(game =>
                string.Equals(GetGameScopeKey(game), gameScopeKey, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasAnyFriendUnlocks(FriendGameSummaryItem game)
        {
            if (game == null)
            {
                return false;
            }

            var gameKey = BuildGameUnlockKey(game.ProviderKey, game.AppId, game.PlayniteGameId);
            if (!string.IsNullOrWhiteSpace(gameKey) && _gameUnlockKeys.Contains(gameKey))
            {
                return true;
            }

            return _allUnlockedAchievements.Count == 0 &&
                   (game.FriendsWithUnlocksCount > 0 ||
                    game.FriendUnlockedAchievementsCount > 0 ||
                    game.LastFriendUnlockUtc.HasValue);
        }

        public bool HasUnlocksForFriendGame(FriendSummaryItem friend, FriendGameSummaryItem game)
        {
            if (friend == null || game == null)
            {
                return false;
            }

            var key = BuildFriendGameUnlockKey(friend.ProviderKey, friend.ExternalUserId, game.AppId, game.PlayniteGameId);
            if (!string.IsNullOrWhiteSpace(key) && _friendGameUnlockKeys.Contains(key))
            {
                return true;
            }

            return _allUnlockedAchievements.Count == 0 &&
                   game.FriendsWithUnlocksCount > 0 &&
                   _friendGameLinks.Any(link => IsSameFriend(link, friend) && IsSameGame(link, game));
        }

        public static bool IsAllScope(string scopeKey)
        {
            return string.IsNullOrWhiteSpace(scopeKey) ||
                   string.Equals(scopeKey, AllScopeKey, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetFriendScopeKey(FriendSummaryItem friend)
        {
            return BuildFriendKey(friend?.ProviderKey, friend?.ExternalUserId) ?? AllScopeKey;
        }

        public static string GetGameScopeKey(FriendGameSummaryItem game)
        {
            return BuildGameUnlockKey(game?.ProviderKey, game?.AppId ?? 0, game?.PlayniteGameId) ?? AllScopeKey;
        }

        public static string BuildFriendKey(string providerKey, string externalUserId)
        {
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return null;
            }

            var provider = string.IsNullOrWhiteSpace(providerKey)
                ? string.Empty
                : providerKey.Trim().ToLowerInvariant();
            return provider + "|" + externalUserId.Trim().ToLowerInvariant();
        }

        public static string BuildFriendGameUnlockKey(
            string providerKey,
            string externalUserId,
            int appId,
            Guid? playniteGameId)
        {
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return null;
            }

            var gameKey = BuildGameUnlockKey(providerKey, appId, playniteGameId);
            return string.IsNullOrWhiteSpace(gameKey)
                ? null
                : externalUserId.Trim().ToLowerInvariant() + "|" + gameKey;
        }

        public static string BuildGameUnlockKey(string providerKey, int appId, Guid? playniteGameId)
        {
            var provider = string.IsNullOrWhiteSpace(providerKey)
                ? string.Empty
                : providerKey.Trim().ToLowerInvariant();
            if (appId > 0)
            {
                return provider + "|app:" + appId.ToString("D", CultureInfo.InvariantCulture);
            }

            return playniteGameId.HasValue && playniteGameId.Value != Guid.Empty
                ? provider + "|playnite:" + playniteGameId.Value.ToString("D")
                : null;
        }

        public static bool IsSameFriend(FriendSummaryItem left, FriendSummaryItem right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(left.ProviderKey, right.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.ExternalUserId, right.ExternalUserId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSameFriend(FriendAchievementDisplayItem achievement, FriendSummaryItem friend)
        {
            return achievement != null &&
                   friend != null &&
                   string.Equals(achievement.ProviderKey, friend.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(achievement.FriendExternalUserId, friend.ExternalUserId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSameFriend(FriendGameLinkItem link, FriendSummaryItem friend)
        {
            return link != null &&
                   friend != null &&
                   string.Equals(link.ProviderKey, friend.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(link.ExternalUserId, friend.ExternalUserId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSameGame(FriendGameSummaryItem left, FriendGameSummaryItem right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (!string.Equals(left.ProviderKey, right.ProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (left.AppId > 0 && right.AppId > 0)
            {
                return left.AppId == right.AppId;
            }

            return left.PlayniteGameId.HasValue &&
                   right.PlayniteGameId.HasValue &&
                   left.PlayniteGameId.Value == right.PlayniteGameId.Value;
        }

        public static bool IsSameGame(FriendAchievementDisplayItem achievement, FriendGameSummaryItem game)
        {
            if (achievement == null || game == null)
            {
                return false;
            }

            if (!string.Equals(achievement.ProviderKey, game.ProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (achievement.AppId > 0 && game.AppId > 0)
            {
                return achievement.AppId == game.AppId;
            }

            return achievement.PlayniteGameId.HasValue &&
                   game.PlayniteGameId.HasValue &&
                   achievement.PlayniteGameId.Value == game.PlayniteGameId.Value;
        }

        public static bool IsSameGame(FriendGameLinkItem link, FriendGameSummaryItem game)
        {
            if (link == null || game == null)
            {
                return false;
            }

            if (!string.Equals(link.ProviderKey, game.ProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (link.AppId > 0 && game.AppId > 0)
            {
                return link.AppId == game.AppId;
            }

            return link.PlayniteGameId.HasValue &&
                   game.PlayniteGameId.HasValue &&
                   link.PlayniteGameId.Value == game.PlayniteGameId.Value;
        }

        private Dictionary<string, List<FriendGameSummaryItem>> BuildSelectedFriendGameSummaries()
        {
            var next = new Dictionary<string, List<FriendGameSummaryItem>>(StringComparer.OrdinalIgnoreCase);
            var gamesByKey = new Dictionary<string, FriendGameSummaryItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in _aggregateGames)
            {
                var gameKey = BuildGameUnlockKey(game?.ProviderKey, game?.AppId ?? 0, game?.PlayniteGameId);
                if (!string.IsNullOrWhiteSpace(gameKey) && !gamesByKey.ContainsKey(gameKey))
                {
                    gamesByKey[gameKey] = game;
                }
            }

            var linksByFriendGameKey = new Dictionary<string, FriendGameLinkItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in _friendGameLinks)
            {
                var key = BuildFriendGameUnlockKey(link?.ProviderKey, link?.ExternalUserId, link?.AppId ?? 0, link?.PlayniteGameId);
                if (!string.IsNullOrWhiteSpace(key) && !linksByFriendGameKey.ContainsKey(key))
                {
                    linksByFriendGameKey[key] = link;
                }
            }

            var groups = _allUnlockedAchievements
                .Where(achievement => achievement != null)
                .GroupBy(
                    achievement => BuildFriendGameUnlockKey(
                        achievement.ProviderKey,
                        achievement.FriendExternalUserId,
                        achievement.AppId,
                        achievement.PlayniteGameId),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    continue;
                }

                var sample = group.FirstOrDefault();
                var friendKey = BuildFriendKey(sample?.ProviderKey, sample?.FriendExternalUserId);
                var gameKey = BuildGameUnlockKey(sample?.ProviderKey, sample?.AppId ?? 0, sample?.PlayniteGameId);
                if (string.IsNullOrWhiteSpace(friendKey) ||
                    string.IsNullOrWhiteSpace(gameKey) ||
                    !gamesByKey.TryGetValue(gameKey, out var baseGame))
                {
                    continue;
                }

                linksByFriendGameKey.TryGetValue(group.Key, out var link);
                var row = BuildSelectedFriendGameSummary(baseGame, group, link);
                if (!next.TryGetValue(friendKey, out var rows))
                {
                    rows = new List<FriendGameSummaryItem>();
                    next[friendKey] = rows;
                }

                rows.Add(row);
            }

            return next;
        }

        private HashSet<string> BuildGameUnlockKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var achievement in _allUnlockedAchievements)
            {
                var gameKey = BuildGameUnlockKey(achievement?.ProviderKey, achievement?.AppId ?? 0, achievement?.PlayniteGameId);
                if (!string.IsNullOrWhiteSpace(gameKey))
                {
                    keys.Add(gameKey);
                }
            }

            return keys;
        }

        private HashSet<string> BuildFriendGameUnlockKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var achievement in _allUnlockedAchievements)
            {
                var friendGameKey = BuildFriendGameUnlockKey(
                    achievement?.ProviderKey,
                    achievement?.FriendExternalUserId,
                    achievement?.AppId ?? 0,
                    achievement?.PlayniteGameId);
                if (!string.IsNullOrWhiteSpace(friendGameKey))
                {
                    keys.Add(friendGameKey);
                }
            }

            return keys;
        }

        private static FriendGameSummaryItem BuildSelectedFriendGameSummary(
            FriendGameSummaryItem source,
            IEnumerable<FriendAchievementDisplayItem> achievements,
            FriendGameLinkItem link)
        {
            var unlocked = (achievements ?? Enumerable.Empty<FriendAchievementDisplayItem>())
                .Where(achievement => achievement != null)
                .GroupBy(achievement => achievement.ApiName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(achievement => achievement.UnlockTimeUtc ?? DateTime.MinValue)
                    .First())
                .ToList();

            var playtimeMinutes = Math.Max(0L, link?.PlaytimeForeverMinutes ?? 0L);
            var item = new FriendGameSummaryItem
            {
                ProviderKey = source.ProviderKey,
                Provider = source.Provider,
                ProviderIconKey = source.ProviderIconKey,
                ProviderColorHex = source.ProviderColorHex,
                AppId = source.AppId,
                PlayniteGameId = source.PlayniteGameId,
                GameName = source.GameName,
                SortingName = source.SortingName,
                GameLogo = source.GameLogo,
                GameCoverPath = source.GameCoverPath,
                PlatformText = source.PlatformText,
                Platforms = source.Platforms,
                RegionText = source.RegionText,
                PlaytimeSeconds = ToPlaytimeSeconds(playtimeMinutes),
                LastPlayed = link?.LastPlayedUtc,
                TotalAchievements = source.TotalAchievements,
                CollectionScoreTotal = source.CollectionScoreTotal,
                PrestigeScoreTotal = source.PrestigeScoreTotal,
                TotalCommonPossible = source.TotalCommonPossible,
                TotalUncommonPossible = source.TotalUncommonPossible,
                TotalRarePossible = source.TotalRarePossible,
                TotalUltraRarePossible = source.TotalUltraRarePossible,
                TrophyPlatinumTotal = source.TrophyPlatinumTotal,
                TrophyGoldTotal = source.TrophyGoldTotal,
                TrophySilverTotal = source.TrophySilverTotal,
                TrophyBronzeTotal = source.TrophyBronzeTotal,
                FriendCount = 1,
                FriendsWithUnlocksCount = unlocked.Count > 0 ? 1 : 0,
                FriendUnlockedAchievementsCount = unlocked.Count,
                UniqueFriendUnlockedAchievementsCount = unlocked.Count,
                LastFriendUnlockUtc = unlocked
                    .Select(achievement => achievement.UnlockTimeUtc)
                    .Where(unlockTime => unlockTime.HasValue)
                    .DefaultIfEmpty()
                    .Max(),
                TotalFriendPlaytimeMinutes = playtimeMinutes,
                AverageFriendPlaytimeMinutes = playtimeMinutes,
                LastFriendPlayedUtc = link?.LastPlayedUtc
            };

            var stats = AchievementStatsAccumulator.FromDisplayItems(
                unlocked,
                treatItemsAsUnlocked: true);
            item.CollectionScore = stats.CollectionScore;
            item.PrestigeScore = stats.PrestigeScore;
            item.Points = stats.Points;
            item.CommonCount = stats.CommonCount;
            item.UncommonCount = stats.UncommonCount;
            item.RareCount = stats.RareCount;
            item.UltraRareCount = stats.UltraRareCount;
            item.TrophyPlatinumCount = stats.TrophyPlatinumCount;
            item.TrophyGoldCount = stats.TrophyGoldCount;
            item.TrophySilverCount = stats.TrophySilverCount;
            item.TrophyBronzeCount = stats.TrophyBronzeCount;

            item.IsCompleted = item.TotalAchievements > 0 &&
                               item.UnlockedAchievements >= item.TotalAchievements;
            return item;
        }

        private static ulong ToPlaytimeSeconds(long minutes)
        {
            var normalized = Math.Max(0L, minutes);
            return normalized > (long)(ulong.MaxValue / 60UL)
                ? ulong.MaxValue
                : (ulong)normalized * 60UL;
        }

        private static void AccumulateRarity(RarityTier rarity, GameSummaryItem item)
        {
            if (item == null)
            {
                return;
            }

            switch (rarity)
            {
                case RarityTier.UltraRare:
                    item.UltraRareCount++;
                    break;
                case RarityTier.Rare:
                    item.RareCount++;
                    break;
                case RarityTier.Uncommon:
                    item.UncommonCount++;
                    break;
                default:
                    item.CommonCount++;
                    break;
            }
        }

        private static void AccumulateTrophy(string trophyType, GameSummaryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(trophyType))
            {
                return;
            }

            if (string.Equals(trophyType, "platinum", StringComparison.OrdinalIgnoreCase))
            {
                item.TrophyPlatinumCount++;
            }
            else if (string.Equals(trophyType, "gold", StringComparison.OrdinalIgnoreCase))
            {
                item.TrophyGoldCount++;
            }
            else if (string.Equals(trophyType, "silver", StringComparison.OrdinalIgnoreCase))
            {
                item.TrophySilverCount++;
            }
            else if (string.Equals(trophyType, "bronze", StringComparison.OrdinalIgnoreCase))
            {
                item.TrophyBronzeCount++;
            }
        }
    }
}

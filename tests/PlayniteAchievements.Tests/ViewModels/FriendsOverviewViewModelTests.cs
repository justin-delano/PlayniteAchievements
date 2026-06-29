using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Tests.ViewModels
{
    [TestClass]
    public class FriendsOverviewViewModelTests
    {
        [TestMethod]
        public void LoadAsync_NoSelectionShowsAllSummariesAndRecentUnlocks()
        {
            var viewModel = CreateViewModel(CreateData());

            viewModel.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(3, viewModel.FilteredFriends.Count);
            Assert.AreEqual(3, viewModel.FilteredGames.Count);
            CollectionAssert.AreEqual(
                new[] { "Recent Only" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void SelectedFriendFiltersGamesAndShowsAllUnlockedAchievementsForFriend()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedFriend = data.Friends[0];

            CollectionAssert.AreEquivalent(
                new[] { "Game One", "Game Two" },
                viewModel.FilteredGames.Select(item => item.GameName).ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "Recent Only", "Alice Game Two" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void SelectedGameFiltersFriendsAndShowsAllUnlockedAchievementsForGame()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedGame = data.Games[0];

            CollectionAssert.AreEquivalent(
                new[] { "Alice", "Bob" },
                viewModel.FilteredFriends.Select(item => item.DisplayName).ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "Recent Only", "Bob Game One" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void SelectedFriendAndGameFiltersAchievementsByBoth()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedFriend = data.Friends[0];
            viewModel.SelectedGame = data.Games[1];

            CollectionAssert.AreEqual(
                new[] { "Alice Game Two" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void ProviderSearchTypeAndCategoryFiltersCompose()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SetProviderFilter("Steam");
            viewModel.FriendSearchText = "ali";
            viewModel.SelectedFriend = data.Friends[0];
            viewModel.SetTypeFilterSelected("Story", true);
            viewModel.SetCategoryFilterSelected("Main", true);

            CollectionAssert.AreEqual(
                new[] { "Alice Game Two" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Alice" },
                viewModel.FilteredFriends.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void ClearingSelectedRowsRestoresBroaderState()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            viewModel.SelectedFriend = data.Friends[0];
            viewModel.SelectedGame = data.Games[1];

            viewModel.ClearGameSelection();
            viewModel.ClearFriendSelection();

            Assert.AreEqual(3, viewModel.FilteredFriends.Count);
            Assert.AreEqual(3, viewModel.FilteredGames.Count);
            CollectionAssert.AreEqual(
                new[] { "Recent Only" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        private static FriendsOverviewViewModel CreateViewModel(FriendsOverviewData data)
        {
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.EnableFriendsOverview = true;
            return new FriendsOverviewViewModel(
                new StubFriendCache(data),
                null,
                null,
                settings,
                null);
        }

        private static FriendsOverviewData CreateData()
        {
            var gameOneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var gameTwoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var gogGameId = Guid.Parse("33333333-3333-3333-3333-333333333333");

            var friends = new List<FriendSummaryItem>
            {
                new FriendSummaryItem { ProviderKey = "Steam", ExternalUserId = "alice", DisplayName = "Alice", SharedGamesCount = 2, UnlockedAchievementsCount = 2 },
                new FriendSummaryItem { ProviderKey = "Steam", ExternalUserId = "bob", DisplayName = "Bob", SharedGamesCount = 1, UnlockedAchievementsCount = 1 },
                new FriendSummaryItem { ProviderKey = "GOG", ExternalUserId = "cora", DisplayName = "Cora", SharedGamesCount = 1, UnlockedAchievementsCount = 1 }
            };

            var games = new List<FriendGameSummaryItem>
            {
                new FriendGameSummaryItem { ProviderKey = "Steam", AppId = 10, PlayniteGameId = gameOneId, GameName = "Game One", FriendCount = 2, UnlockedAchievementsCount = 2 },
                new FriendGameSummaryItem { ProviderKey = "Steam", AppId = 20, PlayniteGameId = gameTwoId, GameName = "Game Two", FriendCount = 1, UnlockedAchievementsCount = 1 },
                new FriendGameSummaryItem { ProviderKey = "GOG", AppId = 30, PlayniteGameId = gogGameId, GameName = "GOG Game", FriendCount = 1, UnlockedAchievementsCount = 1 }
            };

            var allUnlocked = new List<FriendAchievementDisplayItem>
            {
                CreateAchievement("Steam", "alice", "Alice", 10, gameOneId, "Game One", "Recent Only", "Challenge", "Side", new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)),
                CreateAchievement("Steam", "alice", "Alice", 20, gameTwoId, "Game Two", "Alice Game Two", "Story", "Main", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
                CreateAchievement("Steam", "bob", "Bob", 10, gameOneId, "Game One", "Bob Game One", "Story", "Main", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
                CreateAchievement("GOG", "cora", "Cora", 30, gogGameId, "GOG Game", "Cora GOG", "Story", "Main", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            };

            return new FriendsOverviewData
            {
                Friends = friends,
                Games = games,
                RecentUnlocks = new List<FriendAchievementDisplayItem> { allUnlocked[0] },
                AllUnlockedAchievements = allUnlocked,
                FriendGameLinks = new List<FriendGameLinkItem>
                {
                    new FriendGameLinkItem { ProviderKey = "Steam", ExternalUserId = "alice", AppId = 10, PlayniteGameId = gameOneId },
                    new FriendGameLinkItem { ProviderKey = "Steam", ExternalUserId = "alice", AppId = 20, PlayniteGameId = gameTwoId },
                    new FriendGameLinkItem { ProviderKey = "Steam", ExternalUserId = "bob", AppId = 10, PlayniteGameId = gameOneId },
                    new FriendGameLinkItem { ProviderKey = "GOG", ExternalUserId = "cora", AppId = 30, PlayniteGameId = gogGameId }
                }
            };
        }

        private static FriendAchievementDisplayItem CreateAchievement(
            string provider,
            string externalUserId,
            string friendName,
            int appId,
            Guid playniteGameId,
            string gameName,
            string achievementName,
            string categoryType,
            string categoryLabel,
            DateTime unlockTimeUtc)
        {
            return new FriendAchievementDisplayItem
            {
                ProviderKey = provider,
                FriendExternalUserId = externalUserId,
                FriendName = friendName,
                AppId = appId,
                PlayniteGameId = playniteGameId,
                GameName = gameName,
                SortingName = gameName,
                DisplayName = achievementName,
                Description = achievementName + " description",
                CategoryType = categoryType,
                CategoryLabel = categoryLabel,
                UnlockTimeUtc = unlockTimeUtc,
                Unlocked = true
            };
        }

        private sealed class StubFriendCache : IFriendCacheManager
        {
            private readonly FriendsOverviewData _data;

            public StubFriendCache(FriendsOverviewData data)
            {
                _data = data;
            }

            public FriendCacheWriteResult SaveFriendList(string providerKey, IReadOnlyList<FriendIdentity> friends) =>
                FriendCacheWriteResult.Ok();

            public FriendCacheWriteResult SaveFriendOwnership(
                string providerKey,
                string externalUserId,
                IReadOnlyList<FriendGameOwnership> ownership) =>
                FriendCacheWriteResult.Ok();

            public FriendCacheWriteResult SaveFriendGameAchievements(
                string providerKey,
                string externalUserId,
                int appId,
                FriendGameAchievements achievements) =>
                FriendCacheWriteResult.Ok();

            public List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
                string providerKey,
                FriendRefreshOptions options) =>
                new List<FriendRefreshCandidate>();

            public FriendsOverviewData LoadFriendsOverviewData(bool hideSpoilers, int recentLimit) => _data;
        }
    }
}

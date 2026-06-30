using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
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
        public void LoadAsync_IgnoresLegacyEnableFriendsOverviewFlag()
        {
            var viewModel = CreateViewModel(
                CreateData(),
                settings => settings.EnableFriendsOverview = false);

            viewModel.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(3, viewModel.FilteredFriends.Count);
            Assert.AreEqual(3, viewModel.FilteredGames.Count);
            CollectionAssert.AreEqual(
                new[] { "Recent Only" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void LoadAsync_AppliesConfiguredRowLimitsToAllThreeGrids()
        {
            var data = CreateData();
            data.RecentUnlocks = data.AllUnlockedAchievements.Take(3).ToList();
            var viewModel = CreateViewModel(
                data,
                settings =>
                {
                    settings.FriendsOverviewFriendSummariesGridMaxRows = 2;
                    settings.FriendsOverviewGameSummariesGridMaxRows = 2;
                    settings.FriendsOverviewAchievementsGridMaxRows = 2;
                });

            viewModel.LoadAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(
                new[] { "Alice", "Bob" },
                viewModel.FilteredFriends.Select(item => item.DisplayName).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Game One", "Game Two" },
                viewModel.FilteredGames.Select(item => item.GameName).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Recent Only", "Alice Game Two" },
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
        public void SelectedFriendGamesUseFriendScopedSummaryRows()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedFriend = data.Friends[0];

            var gameOne = viewModel.FilteredGames.Single(item => item.AppId == 10);
            Assert.AreNotSame(data.Games[0], gameOne);
            Assert.AreEqual(600UL * 60UL, gameOne.PlaytimeSeconds);
            Assert.AreEqual(new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc), gameOne.LastPlayed);
            Assert.AreEqual(1, gameOne.UnlockedAchievements);
            Assert.AreEqual(1, gameOne.UniqueFriendUnlockedAchievementsCount);
            Assert.AreEqual(4, gameOne.TotalAchievements);
            Assert.AreEqual(25, gameOne.Progression);
            Assert.AreEqual(new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc), gameOne.LastUnlockUtc);
            Assert.AreEqual(1, gameOne.FriendsWithUnlocksCount);
            Assert.AreEqual(600, gameOne.TotalFriendPlaytimeMinutes);
        }

        [TestMethod]
        public void SelectedFriendGamesChangeWhenSwitchingFriends()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedFriend = data.Friends[0];
            var aliceGameOne = viewModel.FilteredGames.Single(item => item.AppId == 10);

            viewModel.SelectedFriend = data.Friends[1];
            var bobGameOne = viewModel.FilteredGames.Single(item => item.AppId == 10);

            Assert.AreNotSame(aliceGameOne, bobGameOne);
            Assert.AreEqual(600UL * 60UL, aliceGameOne.PlaytimeSeconds);
            Assert.AreEqual(300UL * 60UL, bobGameOne.PlaytimeSeconds);
            Assert.AreEqual(new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc), aliceGameOne.LastUnlockUtc);
            Assert.AreEqual(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), bobGameOne.LastUnlockUtc);
        }

        [TestMethod]
        public void SelectedFriendGameSearchMatchesProjectedRows()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedFriend = data.Friends[0];
            viewModel.GameSearchText = "two";

            CollectionAssert.AreEqual(
                new[] { "Game Two" },
                viewModel.FilteredGames.Select(item => item.GameName).ToArray());
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
        public void FriendGameFiltersIgnoreOwnershipOnlyLinks()
        {
            var data = CreateData();
            var ownedOnlyGameId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var ownedOnlyGame = new FriendGameSummaryItem
            {
                ProviderKey = "Steam",
                AppId = 40,
                PlayniteGameId = ownedOnlyGameId,
                GameName = "Owned Only",
                FriendCount = 1,
                FriendsWithUnlocksCount = 0,
                FriendUnlockedAchievementsCount = 0,
                TotalAchievements = 12
            };
            data.Games.Add(ownedOnlyGame);
            data.FriendGameLinks.Add(new FriendGameLinkItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "alice",
                AppId = 40,
                PlayniteGameId = ownedOnlyGameId
            });

            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(viewModel.FilteredGames.Any(item => item.GameName == "Owned Only"));

            viewModel.SelectedFriend = data.Friends[0];

            Assert.IsFalse(viewModel.FilteredGames.Any(item => item.GameName == "Owned Only"));

            viewModel.SelectedGame = ownedOnlyGame;

            Assert.IsNull(viewModel.SelectedGame);
            CollectionAssert.AreEquivalent(
                new[] { "Recent Only", "Alice Game Two" },
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
        public void LoadAsync_ExposesEnrichedFriendAndGameProjectionFields()
        {
            var viewModel = CreateViewModel(CreateData());

            viewModel.LoadAsync().GetAwaiter().GetResult();

            var alice = viewModel.FilteredFriends.Single(item => item.ExternalUserId == "alice");
            StringAssert.Contains(alice.ProviderDisplayName, "Steam");
            Assert.AreEqual("https://cdn.example/alice.png", alice.AvatarUrl);
            Assert.AreEqual(2, alice.GamesWithUnlocksCount);
            Assert.AreEqual(1, alice.RecentUnlockCount);
            Assert.AreEqual(375, alice.CollectionScore);
            Assert.AreEqual(425, alice.PrestigeScore);
            Assert.AreEqual(new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc), alice.LastUnlockUtc);
            Assert.AreEqual(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), alice.LastRefreshedUtc);
            Assert.AreEqual(900, alice.TotalPlaytimeMinutes);

            var gameOne = viewModel.FilteredGames.Single(item => item.AppId == 10);
            Assert.AreEqual(2, gameOne.FriendCount);
            Assert.AreEqual(2, gameOne.FriendsWithUnlocksCount);
            Assert.AreEqual(2, gameOne.FriendUnlockedAchievementsCount);
            Assert.AreEqual(2, gameOne.UniqueFriendUnlockedAchievementsCount);
            Assert.AreEqual(50, gameOne.FriendCompletionPercent);
            Assert.AreEqual(1200, gameOne.TotalFriendPlaytimeMinutes);
            Assert.AreEqual(600, gameOne.AverageFriendPlaytimeMinutes);
            Assert.AreEqual("OK", gameOne.LastFriendScrapeStatusDisplay);
        }

        [TestMethod]
        public void LoadAsync_ExposesFriendAchievementIdentityFields()
        {
            var viewModel = CreateViewModel(CreateData());

            viewModel.LoadAsync().GetAwaiter().GetResult();

            var recent = viewModel.DisplayedAchievements.Single();
            Assert.AreEqual("Alice", recent.FriendName);
            Assert.AreEqual("https://cdn.example/alice.png", recent.FriendAvatarUrl);
            Assert.AreEqual(new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc), recent.FriendUnlockTimeUtc);
            Assert.IsTrue(recent.ShowUnlockDate);
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
            Assert.AreSame(data.Games[0], viewModel.FilteredGames.Single(item => item.AppId == 10));
            CollectionAssert.AreEqual(
                new[] { "Recent Only" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
        }

        private static FriendsOverviewViewModel CreateViewModel(
            FriendsOverviewData data,
            Action<PersistedSettings> configure = null)
        {
            var settings = new PlayniteAchievementsSettings();
            configure?.Invoke(settings.Persisted);
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
                new FriendSummaryItem
                {
                    ProviderKey = "Steam",
                    ExternalUserId = "alice",
                    DisplayName = "Alice",
                    AvatarUrl = "https://cdn.example/alice.png",
                    SharedGamesCount = 2,
                    GamesWithUnlocksCount = 2,
                    UnlockedAchievementsCount = 2,
                    CollectionScore = 375,
                    PrestigeScore = 425,
                    RecentUnlockCount = 1,
                    LastUnlockUtc = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc),
                    LastRefreshedUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    TotalPlaytimeMinutes = 900
                },
                new FriendSummaryItem
                {
                    ProviderKey = "Steam",
                    ExternalUserId = "bob",
                    DisplayName = "Bob",
                    AvatarUrl = "https://cdn.example/bob.png",
                    SharedGamesCount = 1,
                    GamesWithUnlocksCount = 1,
                    UnlockedAchievementsCount = 1,
                    CollectionScore = 90,
                    PrestigeScore = 140,
                    RecentUnlockCount = 0,
                    LastUnlockUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    TotalPlaytimeMinutes = 300
                },
                new FriendSummaryItem
                {
                    ProviderKey = "GOG",
                    ExternalUserId = "cora",
                    DisplayName = "Cora",
                    SharedGamesCount = 1,
                    GamesWithUnlocksCount = 1,
                    UnlockedAchievementsCount = 1,
                    CollectionScore = 30,
                    PrestigeScore = 45,
                    RecentUnlockCount = 0,
                    LastUnlockUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    TotalPlaytimeMinutes = 120
                }
            };

            var games = new List<FriendGameSummaryItem>
            {
                new FriendGameSummaryItem
                {
                    ProviderKey = "Steam",
                    AppId = 10,
                    PlayniteGameId = gameOneId,
                    GameName = "Game One",
                    FriendCount = 2,
                    FriendsWithUnlocksCount = 2,
                    FriendUnlockedAchievementsCount = 2,
                    UniqueFriendUnlockedAchievementsCount = 2,
                    TotalAchievements = 4,
                    LastFriendUnlockUtc = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc),
                    TotalFriendPlaytimeMinutes = 1200,
                    AverageFriendPlaytimeMinutes = 600,
                    LastFriendPlayedUtc = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
                    LastFriendScrapeStatus = "OK"
                },
                new FriendGameSummaryItem
                {
                    ProviderKey = "Steam",
                    AppId = 20,
                    PlayniteGameId = gameTwoId,
                    GameName = "Game Two",
                    FriendCount = 1,
                    FriendsWithUnlocksCount = 1,
                    FriendUnlockedAchievementsCount = 1,
                    UniqueFriendUnlockedAchievementsCount = 1,
                    TotalAchievements = 2,
                    LastFriendUnlockUtc = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
                },
                new FriendGameSummaryItem
                {
                    ProviderKey = "GOG",
                    AppId = 30,
                    PlayniteGameId = gogGameId,
                    GameName = "GOG Game",
                    FriendCount = 1,
                    FriendsWithUnlocksCount = 1,
                    FriendUnlockedAchievementsCount = 1,
                    UniqueFriendUnlockedAchievementsCount = 1,
                    TotalAchievements = 1,
                    LastFriendUnlockUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            };

            var allUnlocked = new List<FriendAchievementDisplayItem>
            {
                CreateAchievement("Steam", "alice", "Alice", "https://cdn.example/alice.png", 10, gameOneId, "Game One", "Recent Only", "Challenge", "Side", new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)),
                CreateAchievement("Steam", "alice", "Alice", "https://cdn.example/alice.png", 20, gameTwoId, "Game Two", "Alice Game Two", "Story", "Main", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
                CreateAchievement("Steam", "bob", "Bob", "https://cdn.example/bob.png", 10, gameOneId, "Game One", "Bob Game One", "Story", "Main", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
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
                    new FriendGameLinkItem
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "alice",
                        AppId = 10,
                        PlayniteGameId = gameOneId,
                        PlaytimeForeverMinutes = 600,
                        LastPlayedUtc = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new FriendGameLinkItem
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "alice",
                        AppId = 20,
                        PlayniteGameId = gameTwoId,
                        PlaytimeForeverMinutes = 300,
                        LastPlayedUtc = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new FriendGameLinkItem
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "bob",
                        AppId = 10,
                        PlayniteGameId = gameOneId,
                        PlaytimeForeverMinutes = 300,
                        LastPlayedUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new FriendGameLinkItem
                    {
                        ProviderKey = "GOG",
                        ExternalUserId = "cora",
                        AppId = 30,
                        PlayniteGameId = gogGameId,
                        PlaytimeForeverMinutes = 120,
                        LastPlayedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
                    }
                }
            };
        }

        private static FriendAchievementDisplayItem CreateAchievement(
            string provider,
            string externalUserId,
            string friendName,
            string friendAvatarUrl,
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
                FriendAvatarUrl = friendAvatarUrl,
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
            DateTime unlockTimeUtc) =>
            CreateAchievement(
                provider,
                externalUserId,
                friendName,
                null,
                appId,
                playniteGameId,
                gameName,
                achievementName,
                categoryType,
                categoryLabel,
                unlockTimeUtc);

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
                IReadOnlyList<FriendGameOwnership> ownership,
                FriendOwnershipSaveOptions options = null) =>
                FriendCacheWriteResult.Ok();

            public FriendCacheWriteResult SaveFriendGameDefinition(
                string providerKey,
                FriendGameDefinition definition) =>
                FriendCacheWriteResult.Ok();

            public FriendCacheWriteResult SaveProviderGameImagePaths(
                string providerKey,
                int appId,
                string iconAbsolutePath,
                string coverAbsolutePath) =>
                FriendCacheWriteResult.Ok();

            public Dictionary<int, FriendGameDefinitionState> LoadFriendGameDefinitionStates(
                string providerKey,
                IReadOnlyCollection<int> appIds) =>
                new Dictionary<int, FriendGameDefinitionState>();

            public FriendUnownedCacheStats GetUnownedFriendGameCacheStats() =>
                new FriendUnownedCacheStats();

            public FriendUnownedCacheClearResult ClearUnownedFriendGameData() =>
                new FriendUnownedCacheClearResult { Success = true };

            public FriendCacheWriteResult SaveFriendGameAchievements(
                string providerKey,
                string externalUserId,
                int appId,
                FriendGameAchievements achievements) =>
                FriendCacheWriteResult.Ok();

            public FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId) =>
                FriendCacheWriteResult.Ok();

            public List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
                string providerKey,
                FriendRefreshOptions options) =>
                new List<FriendRefreshCandidate>();

            public FriendsOverviewData LoadFriendsOverviewData(bool hideSpoilers, int recentLimit) => _data;
        }
    }
}

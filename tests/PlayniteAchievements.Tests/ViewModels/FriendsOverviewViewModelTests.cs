using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Refresh;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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
        public void FriendsOverviewDataCoordinator_InvalidatedDuringBuild_ReturnsFreshSnapshot()
        {
            var staleData = CreateData();
            staleData.Games[0].GameLogo = null;
            staleData.Games[0].GameCoverPath = null;

            var freshData = CreateData();
            freshData.Games[0].GameLogo = "icon.png";
            freshData.Games[0].GameCoverPath = "cover.png";

            using var firstLoadEntered = new ManualResetEventSlim(false);
            using var releaseFirstLoad = new ManualResetEventSlim(false);
            var cache = new StubFriendCache(staleData)
            {
                FirstLoadEntered = firstLoadEntered,
                ReleaseFirstLoad = releaseFirstLoad,
                DataAfterFirstLoad = freshData
            };

            using var coordinator = new FriendsOverviewDataCoordinator(
                cache,
                () => new PersistedSettings());
            var task = coordinator.GetSnapshotAsync(CancellationToken.None);

            Assert.IsTrue(firstLoadEntered.Wait(TimeSpan.FromSeconds(2)));
            coordinator.Invalidate();
            releaseFirstLoad.Set();

            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(2)));
            var snapshot = task.GetAwaiter().GetResult();

            Assert.AreEqual(2, cache.LoadFriendsOverviewDataCalls);
            Assert.AreEqual("icon.png", snapshot.Games[0].GameLogo);
            Assert.AreEqual("cover.png", snapshot.Games[0].GameCoverPath);
        }

        [TestMethod]
        public void FriendsOverviewDataCoordinator_Warm_InvalidatesAndBuildsSnapshot()
        {
            var cache = new StubFriendCache(CreateData());
            using var coordinator = new FriendsOverviewDataCoordinator(
                cache,
                () => new PersistedSettings(),
                warmDebounceInterval: TimeSpan.Zero);
            var invalidated = false;
            coordinator.SnapshotInvalidated += (_, __) => invalidated = true;

            coordinator.Warm();

            Assert.IsTrue(
                SpinWait.SpinUntil(() => cache.LoadFriendsOverviewDataCalls > 0, TimeSpan.FromSeconds(2)));
            Assert.IsTrue(invalidated);
        }

        [TestMethod]
        public void FriendsOverviewDataCoordinator_Warm_WithCurrentSnapshot_InvalidatesWithoutRebuilding()
        {
            var cache = new StubFriendCache(CreateData());
            using var coordinator = new FriendsOverviewDataCoordinator(
                cache,
                () => new PersistedSettings(),
                warmDebounceInterval: TimeSpan.Zero);
            coordinator.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(1, cache.LoadFriendsOverviewDataCalls);
            var invalidated = false;
            coordinator.SnapshotInvalidated += (_, __) => invalidated = true;

            coordinator.Warm();

            Assert.IsTrue(SpinWait.SpinUntil(() => invalidated, TimeSpan.FromSeconds(2)));
            Assert.IsFalse(coordinator.TryGetCurrentSnapshot(out _));
            Thread.Sleep(100);
            Assert.AreEqual(1, cache.LoadFriendsOverviewDataCalls);

            coordinator.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(2, cache.LoadFriendsOverviewDataCalls);
        }

        [TestMethod]
        public void FriendsOverviewDataCoordinator_Warm_WithBuildInFlight_DoesNotStartSecondBuild()
        {
            using var firstLoadEntered = new ManualResetEventSlim(false);
            using var releaseFirstLoad = new ManualResetEventSlim(false);
            var cache = new StubFriendCache(CreateData())
            {
                FirstLoadEntered = firstLoadEntered,
                ReleaseFirstLoad = releaseFirstLoad
            };
            using var coordinator = new FriendsOverviewDataCoordinator(
                cache,
                () => new PersistedSettings(),
                warmDebounceInterval: TimeSpan.Zero);
            var invalidated = false;
            coordinator.SnapshotInvalidated += (_, __) => invalidated = true;

            var task = coordinator.GetSnapshotAsync(CancellationToken.None);
            Assert.IsTrue(firstLoadEntered.Wait(TimeSpan.FromSeconds(2)));

            coordinator.Warm();
            Thread.Sleep(100);
            releaseFirstLoad.Set();

            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(1, cache.LoadFriendsOverviewDataCalls);
            Assert.IsFalse(invalidated);
            Assert.IsTrue(coordinator.TryGetCurrentSnapshot(out _));
        }

        [TestMethod]
        public void LoadAsync_ReloadsWhenSharedCoordinatorInvalidates()
        {
            var staleData = CreateData();
            staleData.Games[0].GameLogo = null;
            staleData.Games[0].GameCoverPath = null;

            var freshData = CreateData();
            freshData.Games[0].GameLogo = "icon.png";
            freshData.Games[0].GameCoverPath = "cover.png";

            var cache = new StubFriendCache(staleData)
            {
                DataAfterFirstLoad = freshData
            };
            var settings = new PlayniteAchievementsSettings();
            using var coordinator = new FriendsOverviewDataCoordinator(
                cache,
                () => settings.Persisted);
            using var viewModel = new FriendsOverviewViewModel(
                cache,
                null,
                null,
                settings,
                null,
                cacheInvalidationDebounceInterval: TimeSpan.Zero,
                activeRefreshInvalidationInterval: TimeSpan.Zero,
                friendsOverviewDataCoordinator: coordinator);

            viewModel.LoadAsync().GetAwaiter().GetResult();
            Assert.IsNull(viewModel.FilteredGames.First().GameLogo);

            coordinator.Invalidate();

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => string.Equals(viewModel.FilteredGames.FirstOrDefault()?.GameLogo, "icon.png", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(2)));
            Assert.AreEqual("cover.png", viewModel.FilteredGames.First().GameCoverPath);
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
        public void LockedRowsShowOnlyForSingleFriendGamePair()
        {
            var data = CreateData();
            var locked = CreateAchievement(
                "Steam",
                "alice",
                "Alice",
                "https://cdn.example/alice.png",
                10,
                data.Games[0].PlayniteGameId.Value,
                "Game One",
                "Alice Locked",
                "Story",
                "Main",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            locked.Unlocked = false;
            locked.UnlockTimeUtc = null;
            data.AllAchievements = data.AllUnlockedAchievements.Concat(new[] { locked }).ToList();

            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            // No selection: recent unlocks only.
            CollectionAssert.AreEqual(
                new[] { "Recent Only" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());

            // Friend-only selection is an aggregated view: unlocked rows only.
            viewModel.SelectedFriend = data.Friends[0];
            Assert.IsFalse(viewModel.DisplayedAchievements.Any(item => item.DisplayName == "Alice Locked"));
            CollectionAssert.AreEquivalent(
                new[] { "Recent Only", "Alice Game Two" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());

            // Single friend + single game pair: full comparison view including locked rows.
            viewModel.SelectedGame = data.Games[0];
            Assert.IsTrue(viewModel.DisplayedAchievements.Any(item =>
                item.DisplayName == "Alice Locked" &&
                !item.Unlocked));

            // Game-only selection is aggregated again: locked rows disappear.
            viewModel.ClearFriendSelection();
            Assert.IsNotNull(viewModel.SelectedGame);
            Assert.IsFalse(viewModel.DisplayedAchievements.Any(item => item.DisplayName == "Alice Locked"));
            CollectionAssert.AreEquivalent(
                new[] { "Recent Only", "Bob Game One" },
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
        public void SelectedFriendGameAllAchievements_KeepsSnapshotOrderIndependentOfDisplaySort()
        {
            var data = CreateData();
            var locked = CreateAchievement(
                "Steam",
                "alice",
                "Alice",
                "https://cdn.example/alice.png",
                10,
                data.Games[0].PlayniteGameId.Value,
                "Game One",
                "AAA Locked",
                "Story",
                "Main",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            locked.Unlocked = false;
            locked.UnlockTimeUtc = null;
            // The locked row precedes the unlocked one, mirroring the cache's definition-ordered load.
            data.AllAchievements = new[] { locked }.Concat(data.AllUnlockedAchievements).ToList();

            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            // No friend+game pair selected: the category-summary source stays empty.
            Assert.AreEqual(0, viewModel.SelectedFriendGameAllAchievements.Count);

            viewModel.SelectedFriend = data.Friends[0];
            viewModel.SelectedGame = data.Games[0];

            // The displayed grid follows the configured default sort (unlock time descending), but
            // the category-summary source preserves the definition-ordered snapshot.
            CollectionAssert.AreEqual(
                new[] { "Recent Only", "AAA Locked" },
                viewModel.DisplayedAchievements.Select(item => item.DisplayName).ToArray());
            CollectionAssert.AreEqual(
                new[] { "AAA Locked", "Recent Only" },
                viewModel.SelectedFriendGameAllAchievements.Select(item => item.DisplayName).ToArray());

            viewModel.ClearGameSelection();
            Assert.AreEqual(0, viewModel.SelectedFriendGameAllAchievements.Count);
        }

        [TestMethod]
        public void SelectedFriendGameHeaderUsesFriendUnlockFraction_NotVisibleRows()
        {
            var data = CreateData();
            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.SelectedFriend = data.Friends[0];
            viewModel.SelectedGame = data.Games[1];
            viewModel.AchievementSearchText = "does not match";

            Assert.AreEqual(0, viewModel.DisplayedAchievements.Count);
            StringAssert.Contains(viewModel.AchievementCountText, "1/2");
        }

        [TestMethod]
        public void OwnedGamesShowWithoutUnlocks_GamesWithoutPairDataClearFriendLists()
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
                PlayniteGameId = ownedOnlyGameId,
                PlaytimeForeverMinutes = 45
            });
            // A game no friend has any data for (no link, no rows) — e.g. stale aggregate row; the
            // refresh/cleanup invariant means ownership rows imply displayability, so the games list
            // shows whatever the cache holds, but no friend can pair with this game.
            var noPairDataGame = new FriendGameSummaryItem
            {
                ProviderKey = "Steam",
                AppId = 50,
                GameName = "No Pair Data",
                TotalAchievements = 3
            };
            data.Games.Add(noPairDataGame);

            var viewModel = CreateViewModel(data);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            // Owned + friend-owned with zero unlocks appears in the games overview.
            Assert.IsTrue(viewModel.FilteredGames.Any(item => item.GameName == "Owned Only"));

            // And in the selected friend's per-friend games list, as a 0/N ownership row.
            viewModel.SelectedFriend = data.Friends[0];
            var ownedRow = viewModel.FilteredGames.Single(item => item.GameName == "Owned Only");
            Assert.AreEqual(0, ownedRow.UnlockedAchievements);
            Assert.AreEqual(12, ownedRow.TotalAchievements);
            Assert.IsFalse(viewModel.FilteredGames.Any(item => item.GameName == "No Pair Data"));

            // Selecting the ownership-only pair sticks and shows an empty pair grid.
            viewModel.SelectedGame = ownedRow;
            Assert.IsNotNull(viewModel.SelectedGame);
            Assert.AreEqual(0, viewModel.DisplayedAchievements.Count);

            // A game with no pair data lists no friends when selected on its own.
            viewModel.ClearFriendSelection();
            viewModel.SelectedGame = noPairDataGame;
            Assert.AreEqual(0, viewModel.FilteredFriends.Count);
            Assert.AreEqual(0, viewModel.DisplayedAchievements.Count);
        }

        [TestMethod]
        public void FriendCacheInvalidated_DuringFriendRefresh_ReloadsOverviewData()
        {
            var initialData = CreateData();
            var updatedData = CreateData();
            updatedData.Friends.Add(new FriendSummaryItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "dana",
                DisplayName = "Dana",
                GamesWithUnlocksCount = 1,
                UnlockedAchievementsCount = 1,
                LastUnlockUtc = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc)
            });

            var cache = new StubFriendCache(initialData);
            var refreshRuntime = new RefreshRuntime();
            refreshRuntime.BeginTestRefresh(new ProgressReport { Mode = RefreshModeType.FriendsFull });
            var viewModel = CreateViewModel(
                cache,
                refreshRuntime: refreshRuntime,
                cacheInvalidationDebounceInterval: TimeSpan.Zero,
                activeRefreshInvalidationInterval: TimeSpan.Zero);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, viewModel.FilteredFriends.Count);

            cache.Data = updatedData;
            refreshRuntime.RaiseFriendCacheInvalidated();

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () =>
                    {
                        try
                        {
                            return viewModel.FilteredFriends
                                .ToList()
                                .Any(friend => friend.DisplayName == "Dana");
                        }
                        catch (InvalidOperationException)
                        {
                            return false;
                        }
                    },
                    TimeSpan.FromSeconds(2)));
        }

        [TestMethod]
        public void FriendCacheInvalidated_ReloadsGameSummariesAndAchievementsImmediately()
        {
            var initialData = CreateData();
            var updatedData = CreateData();
            var liveGameId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            updatedData.Games.Add(new FriendGameSummaryItem
            {
                ProviderKey = "Steam",
                AppId = 40,
                PlayniteGameId = liveGameId,
                GameName = "Live Game",
                FriendCount = 1,
                FriendsWithUnlocksCount = 1,
                FriendUnlockedAchievementsCount = 1,
                UniqueFriendUnlockedAchievementsCount = 1,
                TotalAchievements = 1,
                LastFriendUnlockUtc = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc)
            });
            var liveUnlock = CreateAchievement(
                "Steam",
                "alice",
                "Alice",
                "https://cdn.example/alice.png",
                40,
                liveGameId,
                "Live Game",
                "Live Unlock",
                "Story",
                "Main",
                new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc));
            updatedData.AllUnlockedAchievements.Add(liveUnlock);
            updatedData.RecentUnlocks.Insert(0, liveUnlock);
            updatedData.FriendGameLinks.Add(new FriendGameLinkItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "alice",
                AppId = 40,
                PlayniteGameId = liveGameId,
                PlaytimeForeverMinutes = 45,
                LastPlayedUtc = new DateTime(2026, 1, 9, 1, 0, 0, DateTimeKind.Utc)
            });

            var cache = new StubFriendCache(initialData);
            var refreshRuntime = new RefreshRuntime();
            refreshRuntime.BeginTestRefresh(new ProgressReport { Mode = RefreshModeType.Full });
            var viewModel = CreateViewModel(
                cache,
                refreshRuntime: refreshRuntime,
                cacheInvalidationDebounceInterval: TimeSpan.FromSeconds(10),
                activeRefreshInvalidationInterval: TimeSpan.FromSeconds(10));
            viewModel.LoadAsync().GetAwaiter().GetResult();
            var loadCountAfterInitialLoad = cache.LoadFriendsOverviewDataCalls;

            cache.Data = updatedData;
            refreshRuntime.RaiseFriendCacheInvalidated();

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => cache.LoadFriendsOverviewDataCalls > loadCountAfterInitialLoad &&
                          viewModel.FilteredGames.ToList().Any(game => game.GameName == "Live Game") &&
                          viewModel.DisplayedAchievements.ToList().Any(achievement => achievement.DisplayName == "Live Unlock"),
                    TimeSpan.FromSeconds(2)));
        }

        [TestMethod]
        public void FriendCacheInvalidated_DuringFriendRefresh_ReloadsCheckpointImmediately()
        {
            var initialData = CreateData();
            var updatedData = CreateData();
            updatedData.Friends.Add(new FriendSummaryItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "dana",
                DisplayName = "Dana",
                GamesWithUnlocksCount = 1,
                UnlockedAchievementsCount = 1,
                LastUnlockUtc = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc)
            });

            var cache = new StubFriendCache(initialData);
            var refreshRuntime = new RefreshRuntime();
            refreshRuntime.BeginTestRefresh(new ProgressReport { Mode = RefreshModeType.FriendsFull });
            var viewModel = CreateViewModel(
                cache,
                refreshRuntime: refreshRuntime,
                cacheInvalidationDebounceInterval: TimeSpan.Zero,
                activeRefreshInvalidationInterval: TimeSpan.FromSeconds(10));
            viewModel.LoadAsync().GetAwaiter().GetResult();
            var loadCountAfterInitialLoad = cache.LoadFriendsOverviewDataCalls;

            cache.Data = updatedData;
            refreshRuntime.RaiseFriendCacheInvalidated();

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => cache.LoadFriendsOverviewDataCalls > loadCountAfterInitialLoad &&
                          viewModel.FilteredFriends.ToList().Any(friend => friend.DisplayName == "Dana"),
                    TimeSpan.FromSeconds(2)));
        }

        [TestMethod]
        public void RebuildProgress_FinalFriendReport_ReloadsOverviewData()
        {
            var initialData = CreateData();
            var updatedData = CreateData();
            updatedData.Friends.Add(new FriendSummaryItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "dana",
                DisplayName = "Dana",
                GamesWithUnlocksCount = 1,
                UnlockedAchievementsCount = 1,
                LastUnlockUtc = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc)
            });

            var cache = new StubFriendCache(initialData);
            var refreshRuntime = new RefreshRuntime();
            var viewModel = CreateViewModel(
                cache,
                refreshRuntime: refreshRuntime,
                cacheInvalidationDebounceInterval: TimeSpan.Zero);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, viewModel.FilteredFriends.Count);

            cache.Data = updatedData;
            refreshRuntime.RaiseRebuildProgress(new ProgressReport
            {
                Mode = RefreshModeType.FriendsFull,
                CurrentStep = 1,
                TotalSteps = 1,
                Message = "Friends refresh complete."
            });

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () =>
                    {
                        try
                        {
                            return viewModel.FilteredFriends
                                .ToList()
                                .Any(friend => friend.DisplayName == "Dana");
                        }
                        catch (InvalidOperationException)
                        {
                            return false;
                        }
                        catch (ArgumentException)
                        {
                            return false;
                        }
                    },
                    TimeSpan.FromSeconds(2)));
        }

        [TestMethod]
        public void FriendCacheInvalidated_DuringFriendRefresh_PreservesSelectedFriendAndGame()
        {
            var initialData = CreateData();
            var updatedData = CreateData();
            updatedData.Friends[0].DisplayName = "Alice Reloaded";
            updatedData.Games[1].GameName = "Game Two Reloaded";

            var cache = new StubFriendCache(initialData);
            var refreshRuntime = new RefreshRuntime();
            refreshRuntime.BeginTestRefresh(new ProgressReport { Mode = RefreshModeType.FriendsFull });
            var viewModel = CreateViewModel(
                cache,
                refreshRuntime: refreshRuntime,
                cacheInvalidationDebounceInterval: TimeSpan.Zero,
                activeRefreshInvalidationInterval: TimeSpan.Zero);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            viewModel.SelectedFriend = initialData.Friends[0];
            viewModel.SelectedGame = initialData.Games[1];

            cache.Data = updatedData;
            refreshRuntime.RaiseFriendCacheInvalidated();

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => string.Equals(viewModel.SelectedFriend?.DisplayName, "Alice Reloaded", StringComparison.Ordinal) &&
                          string.Equals(viewModel.SelectedGame?.GameName, "Game Two Reloaded", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(2)));
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
            Assert.AreEqual("https://cdn.example/alice.png", alice.AvatarPath);
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
            Assert.AreEqual("https://cdn.example/alice.png", recent.FriendAvatarPath);
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

        [TestMethod]
        public void SelectedFriendRefreshMode_UsesSelectedShortLabel()
        {
            Assert.AreEqual(
                "LOCPlayAch_RefreshModeShort_Selected",
                RefreshModeType.FriendsSelectedGame.GetShortResourceKey());
        }

        [TestMethod]
        public void SelectedRefreshRequest_GameOnlyUsesSelectedGameMode()
        {
            var data = CreateData();

            var success = FriendsOverviewViewModel.TryBuildSelectedFriendRefreshRequest(
                data.Games[0],
                selectedFriend: null,
                selectedGame: null,
                out var request);

            Assert.IsTrue(success);
            Assert.AreEqual(RefreshModeType.FriendsSelectedGame, request.Mode);
            Assert.AreEqual(data.Games[0].PlayniteGameId, request.SingleGameId);
        }

        [TestMethod]
        public void SelectedRefreshRequest_FriendOnlyUsesCustomFullFriendScope()
        {
            var data = CreateData();

            var success = FriendsOverviewViewModel.TryBuildSelectedFriendRefreshRequest(
                data.Friends[0],
                selectedFriend: null,
                selectedGame: data.Games[0],
                out var request);

            Assert.IsTrue(success);
            Assert.AreEqual(RefreshModeType.FriendsCustom, request.Mode);
            Assert.AreEqual(RefreshSubjects.Friends, request.Options.Subjects);
            Assert.AreEqual(RefreshGameScope.All, request.Options.Scope);
            CollectionAssert.AreEqual(
                new[] { "Steam" },
                request.Options.ProviderKeys.ToList());
            CollectionAssert.AreEqual(
                new[] { "alice" },
                request.Options.FriendExternalUserIds.ToList());
            Assert.AreEqual(1, request.Options.FriendAccounts.Count);
            Assert.IsTrue(request.Options.FriendAccounts.Single().Matches("Steam", "alice"));
            Assert.IsNull(request.Options.PlayniteGameIds);
            Assert.IsNull(request.Options.ProviderAppIds);
        }

        [TestMethod]
        public void SelectedRefreshRequest_MergedFriendUsesAllMemberAccounts()
        {
            var mergedFriend = new FriendSummaryItem
            {
                ProviderKey = FriendOverviewProjection.MergedProviderKey,
                ExternalUserId = "merged-alice",
                MergedFriendId = "merged-alice",
                DisplayName = "Alice Unified",
                MemberAccounts = new List<FriendAccountRef>
                {
                    FriendAccountRef.From("Steam", "alice"),
                    FriendAccountRef.From("Exophase", "exo-alice")
                },
                MemberProviderKeys = new List<string> { "Steam", "Exophase" }
            };

            var success = FriendsOverviewViewModel.TryBuildSelectedFriendRefreshRequest(
                mergedFriend,
                selectedFriend: null,
                selectedGame: null,
                out var request);

            Assert.IsTrue(success);
            Assert.AreEqual(RefreshModeType.FriendsCustom, request.Mode);
            CollectionAssert.AreEquivalent(
                new[] { "Steam", "Exophase" },
                request.Options.ProviderKeys.ToList());
            CollectionAssert.AreEquivalent(
                new[] { "alice", "exo-alice" },
                request.Options.FriendExternalUserIds.ToList());
            var accounts = request.Options.FriendAccounts.ToList();
            Assert.AreEqual(2, accounts.Count);
            Assert.IsTrue(accounts.Any(account => account.Matches("Steam", "alice")));
            Assert.IsTrue(accounts.Any(account => account.Matches("Exophase", "exo-alice")));
        }

        [TestMethod]
        public void SelectedRefreshRequest_FriendAndGameUsesCustomPairScope()
        {
            var data = CreateData();

            var success = FriendsOverviewViewModel.TryBuildSelectedFriendRefreshRequest(
                parameter: null,
                selectedFriend: data.Friends[0],
                selectedGame: data.Games[1],
                out var request);

            Assert.IsTrue(success);
            Assert.AreEqual(RefreshModeType.FriendsCustom, request.Mode);
            Assert.AreEqual(RefreshSubjects.Friends, request.Options.Subjects);
            Assert.AreEqual(RefreshGameScope.SelectedGame, request.Options.Scope);
            CollectionAssert.AreEqual(
                new[] { "Steam" },
                request.Options.ProviderKeys.ToList());
            CollectionAssert.AreEqual(
                new[] { "alice" },
                request.Options.FriendExternalUserIds.ToList());
            Assert.AreEqual(1, request.Options.FriendAccounts.Count);
            Assert.IsTrue(request.Options.FriendAccounts.Single().Matches("Steam", "alice"));
            CollectionAssert.AreEqual(
                new[] { data.Games[1].PlayniteGameId.Value },
                request.Options.PlayniteGameIds.ToList());
        }

        [TestMethod]
        public void SelectedRefreshRequest_FriendAndProviderOnlyGameUsesProviderAppScope()
        {
            var data = CreateData();
            var providerOnlyGame = new FriendGameSummaryItem
            {
                ProviderKey = "Steam",
                AppId = 999,
                GameName = "Provider Only"
            };

            var success = FriendsOverviewViewModel.TryBuildSelectedFriendRefreshRequest(
                parameter: null,
                selectedFriend: data.Friends[0],
                selectedGame: providerOnlyGame,
                out var request);

            Assert.IsTrue(success);
            Assert.AreEqual(RefreshModeType.FriendsCustom, request.Mode);
            Assert.AreEqual(RefreshSubjects.Friends, request.Options.Subjects);
            Assert.AreEqual(RefreshGameScope.SelectedGame, request.Options.Scope);
            CollectionAssert.AreEqual(
                new[] { "alice" },
                request.Options.FriendExternalUserIds.ToList());
            Assert.AreEqual(1, request.Options.FriendAccounts.Count);
            Assert.IsTrue(request.Options.FriendAccounts.Single().Matches("Steam", "alice"));
            CollectionAssert.AreEqual(
                new[] { 999 },
                request.Options.ProviderAppIds.ToList());
        }

        private static FriendsOverviewViewModel CreateViewModel(
            FriendsOverviewData data,
            Action<PersistedSettings> configure = null)
        {
            return CreateViewModel(new StubFriendCache(data), configure);
        }

        private static FriendsOverviewViewModel CreateViewModel(
            StubFriendCache cache,
            Action<PersistedSettings> configure = null,
            RefreshRuntime refreshRuntime = null,
            TimeSpan? cacheInvalidationDebounceInterval = null,
            TimeSpan? activeRefreshInvalidationInterval = null)
        {
            var settings = new PlayniteAchievementsSettings();
            configure?.Invoke(settings.Persisted);
            return new FriendsOverviewViewModel(
                cache,
                null,
                refreshRuntime,
                settings,
                null,
                cacheInvalidationDebounceInterval: cacheInvalidationDebounceInterval,
                activeRefreshInvalidationInterval: activeRefreshInvalidationInterval);
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
                    AvatarPath = "https://cdn.example/alice.png",
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
                    AvatarPath = "https://cdn.example/bob.png",
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
            string friendAvatarPath,
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
                FriendAvatarPath = friendAvatarPath,
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
            public StubFriendCache(FriendsOverviewData data)
            {
                Data = data;
            }

            public event EventHandler FriendCacheInvalidated;

            public FriendsOverviewData Data { get; set; }

            public FriendsOverviewData DataAfterFirstLoad { get; set; }

            public ManualResetEventSlim FirstLoadEntered { get; set; }

            public ManualResetEventSlim ReleaseFirstLoad { get; set; }

            public int LoadFriendsOverviewDataCalls { get; private set; }

            public void RaiseFriendCacheInvalidated()
            {
                FriendCacheInvalidated?.Invoke(this, EventArgs.Empty);
            }

            public IFriendCacheInvalidationBatch BeginFriendCacheInvalidationBatch() =>
                NullFriendCacheInvalidationBatch.Instance;

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
                string providerGameKey,
                int appId,
                string iconAbsolutePath,
                string coverAbsolutePath) =>
                FriendCacheWriteResult.Ok();

            public Dictionary<string, FriendGameDefinitionState> LoadFriendGameDefinitionStates(
                string providerKey,
                IReadOnlyCollection<string> providerGameKeys) =>
                new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase);

            public List<string> LoadLegacyKeyedDefinitionGameKeys(
                string providerKey,
                IReadOnlyCollection<string> providerGameKeys) =>
                new List<string>();

            public FriendUnownedCacheStats GetUnownedFriendGameCacheStats() =>
                new FriendUnownedCacheStats();

            public FriendUnownedCacheClearResult ClearUnownedFriendGameData() =>
                new FriendUnownedCacheClearResult { Success = true };

            public FriendCacheWriteResult ClearUnownedFriendGame(string providerKey, int appId, string providerGameKey) =>
                FriendCacheWriteResult.Ok();

            public bool IsProviderGameMappedToPlayniteLibrary(string providerKey, int appId, string providerGameKey) =>
                true;

            public System.Collections.Generic.IReadOnlyList<FriendGameMapping> LoadFriendGameMappings(string providerKey) =>
                new System.Collections.Generic.List<FriendGameMapping>();

            public FriendCacheWriteResult PromoteProviderOnlyGameToPlayniteBacked(
                string providerKey,
                int appId,
                string providerGameKey,
                Guid playniteGameId) =>
                FriendCacheWriteResult.Ok();

            public FriendCacheWriteResult SaveFriendGameAchievements(
                string providerKey,
                string externalUserId,
                string providerGameKey,
                int appId,
                FriendGameAchievements achievements) =>
                FriendCacheWriteResult.Ok();

            public List<FriendAchievementRow> LoadFriendGameAchievements(
                string providerKey,
                string externalUserId,
                int appId,
                string providerGameKey) =>
                new List<FriendAchievementRow>();

            public FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId, bool preserveFriendRecord = false) =>
                FriendCacheWriteResult.Ok();

            public List<FriendIdentity> LoadFriendIdentities(string providerKey) =>
                new List<FriendIdentity>();

            public DateTime? GetMostRecentFriendLastRefreshedUtc() => null;

            public List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
                string providerKey,
                FriendRefreshOptions options) =>
                new List<FriendRefreshCandidate>();

            public IReadOnlyDictionary<string, FriendOwnershipRecency> LoadFriendOwnershipRecency(
                string providerKey,
                string externalUserId) =>
                new Dictionary<string, FriendOwnershipRecency>();

            public FriendsOverviewData LoadFriendsOverviewData(int recentLimit)
            {
                var data = Data;
                LoadFriendsOverviewDataCalls++;
                if (LoadFriendsOverviewDataCalls == 1 && FirstLoadEntered != null)
                {
                    FirstLoadEntered.Set();
                    ReleaseFirstLoad?.Wait(TimeSpan.FromSeconds(5));
                }

                return LoadFriendsOverviewDataCalls > 1 && DataAfterFirstLoad != null
                    ? DataAfterFirstLoad
                    : data;
            }

            public FriendsOverviewData LoadFriendGameAchievementData(Guid playniteGameId) =>
                Data;

            public FriendsOverviewData LoadFriendRecentUnlocksData(int recentLimit) =>
                Data;

            public IReadOnlyList<CurrentUserGameLabel> LoadCurrentUserGameLabels() =>
                new List<CurrentUserGameLabel>();
        }
    }
}

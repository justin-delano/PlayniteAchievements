using Microsoft.VisualStudio.TestTools.UnitTesting;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Friends;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class FriendsRefreshRuntimeTests
    {
        [TestMethod]
        public async Task RefreshAsync_Full_LoadsCurrentUserCandidatesWithoutOwnershipRefresh()
        {
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate>
                {
                    MakeCandidate("1", 100),
                    MakeCandidate("2", 200)
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    MakeFriend("1"),
                    MakeFriend("2")
                }
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Full },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.BeginCalls);
            Assert.AreEqual(1, friends.EndCalls);
            Assert.AreEqual(0, friends.GetOwnedGamesCalls);
            Assert.AreEqual(2, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(FriendRefreshScope.Full, cache.LastLoadOptions.Scope);
            Assert.AreEqual(2, payload.FriendSummary.FriendsFetched);
            Assert.AreEqual(2, payload.FriendSummary.CandidatesRefreshed);
        }

        [TestMethod]
        public async Task RefreshAsync_Shared_RefreshesOwnershipBeforeAchievements()
        {
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate>
                {
                    MakeCandidate("1", 100)
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    MakeFriend("1"),
                    MakeFriend("2")
                }
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Shared },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(2, friends.GetOwnedGamesCalls);
            Assert.AreEqual(2, cache.SaveFriendOwnershipCalls);
            Assert.AreEqual(FriendRefreshScope.Shared, cache.LastLoadOptions.Scope);
            Assert.AreEqual(1, payload.FriendSummary.CandidatesRefreshed);
        }

        [TestMethod]
        public async Task RefreshAsync_Recent_RefreshesAllLoadedCandidates()
        {
            var cache = new FakeFriendCache
            {
                Candidates = Enumerable.Range(1, 65)
                    .Select(index => MakeCandidate(index.ToString(), 1000 + index))
                    .ToList()
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(65, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(65, payload.FriendSummary.CandidatesLoaded);
            Assert.AreEqual(65, payload.FriendSummary.CandidatesRefreshed);
        }

        [TestMethod]
        public async Task RefreshAsync_RecentOwnedAndUnowned_DiscoversDefinitionsAndPersistsProviderOnlyOwnership()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Recent,
                        GameSource = FriendRefreshGameSource.OwnedAndUnowned
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, cache.SaveFriendGameDefinitionCalls);
            Assert.AreEqual(1, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_SharedOwnedAndUnowned_DoesNotDiscoverProviderOnlyDefinitions()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Shared,
                        GameSource = FriendRefreshGameSource.OwnedAndUnowned
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullOwnedAndUnowned_DiscoversUnownedOwnership()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full,
                        GameSource = FriendRefreshGameSource.OwnedAndUnowned
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(FriendRefreshGameSource.OwnedAndUnowned, cache.LastLoadOptions.GameSource);
        }

        [TestMethod]
        public async Task RefreshAsync_Recent_BoundsParallelAchievementRefresh()
        {
            var cache = new FakeFriendCache
            {
                Candidates = Enumerable.Range(1, 12)
                    .Select(index => MakeCandidate(index.ToString(), 1000 + index))
                    .ToList()
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                AchievementDelayMs = 25
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(12, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(12, payload.FriendSummary.CandidatesRefreshed);
            Assert.IsTrue(friends.MaxConcurrentAchievementCalls > 1);
            Assert.IsTrue(friends.MaxConcurrentAchievementCalls <= 4);
        }

        [TestMethod]
        public async Task RefreshAsync_Recent_ParallelDisabled_RunsAchievementRefreshSequentially()
        {
            var cache = new FakeFriendCache
            {
                Candidates = Enumerable.Range(1, 8)
                    .Select(index => MakeCandidate(index.ToString(), 1000 + index))
                    .ToList()
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                AchievementDelayMs = 10
            };

            var payload = await CreateRuntime(cache, enableParallelProviderRefresh: false)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(8, payload.FriendSummary.CandidatesRefreshed);
            Assert.AreEqual(1, friends.MaxConcurrentAchievementCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_Shared_BoundsParallelOwnershipRefresh()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = Enumerable.Range(1, 12)
                    .Select(index => MakeFriend(index.ToString()))
                    .ToList(),
                OwnershipDelayMs = 25
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Shared },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(12, friends.GetOwnedGamesCalls);
            Assert.AreEqual(12, payload.FriendSummary.OwnershipPagesRefreshed);
            Assert.IsTrue(friends.MaxConcurrentOwnershipCalls > 1);
            Assert.IsTrue(friends.MaxConcurrentOwnershipCalls <= 4);
        }

        [TestMethod]
        public async Task RefreshAsync_BeginAuthFailure_MarksAuthRequiredAndEndsRefresh()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                BeginResult = FriendsProviderResult<FriendsRefreshPreparation>.Failed("expired", authRequired: true)
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Full },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.BeginCalls);
            Assert.AreEqual(1, friends.EndCalls);
            Assert.IsTrue(payload.AuthRequired);
            CollectionAssert.Contains(payload.FailedProviderKeys, "Steam");
            Assert.AreEqual(0, cache.SaveFriendListCalls);
        }

        private static FriendsRefreshRuntime CreateRuntime(
            FakeFriendCache cache,
            bool enableParallelProviderRefresh = true)
        {
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.EnableFriendsOverview = true;
            settings.Persisted.EnableParallelProviderRefresh = enableParallelProviderRefresh;
            settings.Persisted.FriendsOverviewRefreshTtlHours = 24;
            settings.Persisted.ScanDelayMs = 0;
            settings.Persisted.MaxRetryAttempts = 0;

            return new FriendsRefreshRuntime(
                Array.Empty<IDataProvider>(),
                cache,
                providerRegistry: null,
                settings,
                logger: null);
        }

        private static FriendIdentity MakeFriend(string externalUserId) =>
            new FriendIdentity
            {
                ProviderKey = "Steam",
                ExternalUserId = externalUserId,
                DisplayName = "Friend " + externalUserId
            };

        private static FriendRefreshCandidate MakeCandidate(string externalUserId, int appId) =>
            new FriendRefreshCandidate
            {
                Friend = MakeFriend(externalUserId),
                AppId = appId,
                GameName = "Game " + appId
            };

        private sealed class FakeFriendCache : IFriendCacheManager
        {
            private int _saveFriendListCalls;
            private int _saveFriendOwnershipCalls;
            private int _saveFriendGameAchievementsCalls;
            private int _saveFriendGameDefinitionCalls;
            private int _saveProviderOnlyOwnershipCalls;

            public List<FriendRefreshCandidate> Candidates { get; set; } = new List<FriendRefreshCandidate>();
            public int SaveFriendListCalls => _saveFriendListCalls;
            public int SaveFriendOwnershipCalls => _saveFriendOwnershipCalls;
            public int SaveFriendGameAchievementsCalls => _saveFriendGameAchievementsCalls;
            public int SaveFriendGameDefinitionCalls => _saveFriendGameDefinitionCalls;
            public int SaveProviderOnlyOwnershipCalls => _saveProviderOnlyOwnershipCalls;
            public FriendRefreshOptions LastLoadOptions { get; private set; }

            public FriendCacheWriteResult SaveFriendList(string providerKey, IReadOnlyList<FriendIdentity> friends)
            {
                Interlocked.Increment(ref _saveFriendListCalls);
                var count = friends?.Count ?? 0;
                return FriendCacheWriteResult.Ok(count, count, 0);
            }

            public FriendCacheWriteResult SaveFriendOwnership(
                string providerKey,
                string externalUserId,
                IReadOnlyList<FriendGameOwnership> ownership,
                FriendOwnershipSaveOptions options = null)
            {
                Interlocked.Increment(ref _saveFriendOwnershipCalls);
                if (options?.IncludeProviderOnlyGames == true)
                {
                    Interlocked.Increment(ref _saveProviderOnlyOwnershipCalls);
                }

                var count = ownership?.Count ?? 0;
                return FriendCacheWriteResult.Ok(count, count, 0);
            }

            public FriendCacheWriteResult SaveFriendGameDefinition(
                string providerKey,
                FriendGameDefinition definition)
            {
                Interlocked.Increment(ref _saveFriendGameDefinitionCalls);
                return FriendCacheWriteResult.Ok(definition?.Achievements?.Count ?? 0, definition?.Achievements?.Count ?? 0, 0);
            }

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
                FriendGameAchievements achievements)
            {
                Interlocked.Increment(ref _saveFriendGameAchievementsCalls);
                return FriendCacheWriteResult.Ok();
            }

            public FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId) =>
                FriendCacheWriteResult.Ok();

            public List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
                string providerKey,
                FriendRefreshOptions options)
            {
                LastLoadOptions = options?.Clone();
                return Candidates.ToList();
            }

            public FriendsOverviewData LoadFriendsOverviewData(bool hideSpoilers, int recentLimit) =>
                new FriendsOverviewData();
        }

        private sealed class FakeDataProvider : IDataProvider
        {
            public FakeDataProvider(string providerKey, IFriendsProvider friends)
            {
                ProviderKey = providerKey;
                Friends = friends;
            }

            public string ProviderName => ProviderKey;
            public string ProviderKey { get; }
            public string ProviderIconKey => ProviderKey;
            public string ProviderColorHex => "#000000";
            public bool IsAuthenticated => true;
            public ISessionManager AuthSession => null;
            public IFriendsProvider Friends { get; }
            public bool IsCapable(Game game) => true;

            public Task<RebuildPayload> RefreshAsync(
                IReadOnlyList<Game> gamesToRefresh,
                Action<Game> onGameStarting,
                Func<Game, GameAchievementData, Task> onGameCompleted,
                CancellationToken cancel) =>
                Task.FromResult(new RebuildPayload());

            public IProviderSettings GetSettings() => null;
            public void ApplySettings(IProviderSettings settings) { }
            public ProviderSettingsViewBase CreateSettingsView() => null;
        }

        private sealed class FakeFriendsProvider : IFriendsProvider
        {
            private int _beginCalls;
            private int _endCalls;
            private int _getOwnedGamesCalls;
            private int _getFriendGameAchievementsCalls;
            private int _getFriendGameDefinitionCalls;
            private int _currentOwnershipCalls;
            private int _maxConcurrentOwnershipCalls;
            private int _currentAchievementCalls;
            private int _maxConcurrentAchievementCalls;

            public FakeFriendsProvider(string providerKey)
            {
                ProviderKey = providerKey;
                BeginResult = FriendsProviderResult<FriendsRefreshPreparation>
                    .FromData(new FriendsRefreshPreparation { CanRefreshAchievements = true });
            }

            public string ProviderKey { get; }
            public FriendsProviderResult<FriendsRefreshPreparation> BeginResult { get; set; }
            public IReadOnlyList<FriendIdentity> FriendsToReturn { get; set; } = Array.Empty<FriendIdentity>();
            public int OwnershipDelayMs { get; set; }
            public int AchievementDelayMs { get; set; }
            public int BeginCalls => _beginCalls;
            public int EndCalls => _endCalls;
            public int GetOwnedGamesCalls => _getOwnedGamesCalls;
            public int GetFriendGameAchievementsCalls => _getFriendGameAchievementsCalls;
            public int GetFriendGameDefinitionCalls => _getFriendGameDefinitionCalls;
            public int MaxConcurrentOwnershipCalls => _maxConcurrentOwnershipCalls;
            public int MaxConcurrentAchievementCalls => _maxConcurrentAchievementCalls;

            public Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
            {
                Interlocked.Increment(ref _beginCalls);
                return Task.FromResult(BeginResult);
            }

            public void EndRefresh()
            {
                Interlocked.Increment(ref _endCalls);
            }

            public Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel) =>
                Task.FromResult(FriendsProviderResult<IReadOnlyList<FriendIdentity>>.FromData(FriendsToReturn));

            public async Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetOwnedGamesAsync(
                FriendIdentity friend,
                CancellationToken cancel)
            {
                Interlocked.Increment(ref _getOwnedGamesCalls);
                var current = Interlocked.Increment(ref _currentOwnershipCalls);
                UpdateMaxConcurrentOwnershipCalls(current);
                try
                {
                    if (OwnershipDelayMs > 0)
                    {
                        await Task.Delay(OwnershipDelayMs, cancel).ConfigureAwait(false);
                    }

                    IReadOnlyList<FriendGameOwnership> ownedGames = new[]
                    {
                        new FriendGameOwnership
                        {
                            ExternalUserId = friend?.ExternalUserId,
                            ProviderKey = ProviderKey,
                            AppId = 100,
                            PlaytimeForeverMinutes = 1
                        }
                    };

                    return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(ownedGames);
                }
                finally
                {
                    Interlocked.Decrement(ref _currentOwnershipCalls);
                }
            }

            public async Task<FriendsProviderResult<FriendGameAchievements>> GetFriendGameAchievementsAsync(
                FriendIdentity friend,
                int appId,
                string gameName,
                CancellationToken cancel)
            {
                Interlocked.Increment(ref _getFriendGameAchievementsCalls);
                var current = Interlocked.Increment(ref _currentAchievementCalls);
                UpdateMaxConcurrentAchievementCalls(current);
                try
                {
                    if (AchievementDelayMs > 0)
                    {
                        await Task.Delay(AchievementDelayMs, cancel).ConfigureAwait(false);
                    }

                    return FriendsProviderResult<FriendGameAchievements>.FromData(
                        new FriendGameAchievements
                        {
                            Friend = friend,
                            AppId = appId,
                            LastUpdatedUtc = DateTime.UtcNow
                        });
                }
                finally
                {
                    Interlocked.Decrement(ref _currentAchievementCalls);
                }
            }

            public Task<FriendsProviderResult<FriendGameDefinition>> GetFriendGameDefinitionAsync(
                int appId,
                string gameName,
                CancellationToken cancel)
            {
                Interlocked.Increment(ref _getFriendGameDefinitionCalls);
                return Task.FromResult(FriendsProviderResult<FriendGameDefinition>.FromData(new FriendGameDefinition
                {
                    ProviderKey = ProviderKey,
                    AppId = appId,
                    GameName = gameName,
                    Status = FriendGameDefinitionStatus.Ok,
                    LastCheckedUtc = DateTime.UtcNow,
                    Achievements = new List<AchievementDetail>
                    {
                        new AchievementDetail { ApiName = "A", DisplayName = "A" }
                    }
                }));
            }

            private void UpdateMaxConcurrentAchievementCalls(int current)
            {
                while (true)
                {
                    var existing = Volatile.Read(ref _maxConcurrentAchievementCalls);
                    if (current <= existing)
                    {
                        return;
                    }

                    if (Interlocked.CompareExchange(ref _maxConcurrentAchievementCalls, current, existing) == existing)
                    {
                        return;
                    }
                }
            }

            private void UpdateMaxConcurrentOwnershipCalls(int current)
            {
                while (true)
                {
                    var existing = Volatile.Read(ref _maxConcurrentOwnershipCalls);
                    if (current <= existing)
                    {
                        return;
                    }

                    if (Interlocked.CompareExchange(ref _maxConcurrentOwnershipCalls, current, existing) == existing)
                    {
                        return;
                    }
                }
            }
        }
    }
}

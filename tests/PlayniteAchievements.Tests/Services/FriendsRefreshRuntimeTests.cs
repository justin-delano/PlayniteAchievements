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

        private static FriendsRefreshRuntime CreateRuntime(FakeFriendCache cache)
        {
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.EnableFriendsOverview = true;
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
            public List<FriendRefreshCandidate> Candidates { get; set; } = new List<FriendRefreshCandidate>();
            public int SaveFriendListCalls { get; private set; }
            public int SaveFriendOwnershipCalls { get; private set; }
            public int SaveFriendGameAchievementsCalls { get; private set; }
            public FriendRefreshOptions LastLoadOptions { get; private set; }

            public FriendCacheWriteResult SaveFriendList(string providerKey, IReadOnlyList<FriendIdentity> friends)
            {
                SaveFriendListCalls++;
                var count = friends?.Count ?? 0;
                return FriendCacheWriteResult.Ok(count, count, 0);
            }

            public FriendCacheWriteResult SaveFriendOwnership(
                string providerKey,
                string externalUserId,
                IReadOnlyList<FriendGameOwnership> ownership)
            {
                SaveFriendOwnershipCalls++;
                var count = ownership?.Count ?? 0;
                return FriendCacheWriteResult.Ok(count, count, 0);
            }

            public FriendCacheWriteResult SaveFriendGameAchievements(
                string providerKey,
                string externalUserId,
                int appId,
                FriendGameAchievements achievements)
            {
                SaveFriendGameAchievementsCalls++;
                return FriendCacheWriteResult.Ok();
            }

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
            public FakeFriendsProvider(string providerKey)
            {
                ProviderKey = providerKey;
                BeginResult = FriendsProviderResult<FriendsRefreshPreparation>
                    .FromData(new FriendsRefreshPreparation { CanRefreshAchievements = true });
            }

            public string ProviderKey { get; }
            public FriendsProviderResult<FriendsRefreshPreparation> BeginResult { get; set; }
            public IReadOnlyList<FriendIdentity> FriendsToReturn { get; set; } = Array.Empty<FriendIdentity>();
            public int BeginCalls { get; private set; }
            public int EndCalls { get; private set; }
            public int GetOwnedGamesCalls { get; private set; }
            public int GetFriendGameAchievementsCalls { get; private set; }

            public Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
            {
                BeginCalls++;
                return Task.FromResult(BeginResult);
            }

            public void EndRefresh()
            {
                EndCalls++;
            }

            public Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel) =>
                Task.FromResult(FriendsProviderResult<IReadOnlyList<FriendIdentity>>.FromData(FriendsToReturn));

            public Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetOwnedGamesAsync(
                FriendIdentity friend,
                CancellationToken cancel)
            {
                GetOwnedGamesCalls++;
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

                return Task.FromResult(FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(ownedGames));
            }

            public Task<FriendsProviderResult<FriendGameAchievements>> GetFriendGameAchievementsAsync(
                FriendIdentity friend,
                int appId,
                string gameName,
                CancellationToken cancel)
            {
                GetFriendGameAchievementsCalls++;
                return Task.FromResult(FriendsProviderResult<FriendGameAchievements>.FromData(
                    new FriendGameAchievements
                    {
                        Friend = friend,
                        AppId = appId,
                        LastUpdatedUtc = DateTime.UtcNow
                    }));
            }
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Playnite;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Images;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class RefreshRuntimeFriendTests
    {
        [TestMethod]
        public async Task RefreshAsync_Full_FetchesOwnershipForAllFriendsAndRefreshesCandidates()
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
            SeedCachedFriends(cache, "Steam", "1", "2");

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Full },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.BeginCalls);
            Assert.AreEqual(1, friends.EndCalls);
            // Full now fetches ownership for every friend (no per-friend opt-in) and builds mapped
            // scrape candidates from the fresh ownership snapshot rather than the cache loader.
            Assert.AreEqual(2, friends.GetOwnedGamesCalls);
            Assert.AreEqual(2, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(2, payload.FriendSummary.FriendsFetched);
            Assert.AreEqual(2, payload.FriendSummary.CandidatesRefreshed);
        }

        [TestMethod]
        public async Task ExecuteRefreshAsync_UnifiedOptions_InterleavesCurrentAndFriendWorkByProvider()
        {
            var events = new List<string>();
            var sync = new object();
            void Record(string value)
            {
                lock (sync)
                {
                    events.Add(value);
                }
            }

            var gameId = Guid.NewGuid();
            var game = new Game { Id = gameId, Name = "Steam Game" };
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OperationLog = Record
            };
            var provider = new FakeDataProvider("Steam", friends)
            {
                OperationLog = Record
            };
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.IncludeUnplayedGames = true;
            settings.Persisted.EnableParallelProviderRefresh = false;
            settings.Persisted.ScanDelayMs = 0;
            settings.Persisted.MaxRetryAttempts = 0;

            var runtime = new RefreshRuntime(
                cache,
                settings,
                new FakePlayniteApi(new[] { game }),
                new IDataProvider[] { provider },
                new[] { "Steam" });

            await runtime.ExecuteRefreshAsync(new RefreshRequest
            {
                Mode = RefreshModeType.Custom,
                Options = new RefreshOptions
                {
                    Subjects = RefreshSubjects.All,
                    ProviderKeys = new[] { "Steam" },
                    Scope = RefreshGameScope.Explicit,
                    PlayniteGameIds = new[] { gameId },
                    FriendExternalUserIds = new[] { "1" },
                    RunProvidersInParallelOverride = false
                }
            }).ConfigureAwait(false);

            CollectionAssert.AreEqual(
                new[] { "Steam:current", "Steam:friend-begin" },
                events.Take(2).ToArray());
            Assert.AreEqual(1, provider.RefreshCalls);
            Assert.AreEqual(1, friends.BeginCalls);
            Assert.AreEqual(1, friends.EndCalls);
        }

        [TestMethod]
        public async Task ExecuteRefreshAsync_FullCurrentUser_SkipsCachedNoAchievementGames()
        {
            var noAchievementGameId = Guid.NewGuid();
            var achievementGameId = Guid.NewGuid();
            var noAchievementGame = new Game { Id = noAchievementGameId, Name = "No Achievements" };
            var achievementGame = new Game { Id = achievementGameId, Name = "Has Achievements" };
            var cache = new FakeFriendCache
            {
                CachedGameData = new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase)
                {
                    [noAchievementGameId.ToString("D")] = new GameAchievementData
                    {
                        PlayniteGameId = noAchievementGameId,
                        ProviderKey = "Steam",
                        HasAchievements = false
                    }
                }
            };
            var provider = new FakeDataProvider("Steam", friends: null);
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.IncludeUnplayedGames = true;
            settings.Persisted.EnableParallelProviderRefresh = false;
            settings.Persisted.ScanDelayMs = 0;
            settings.Persisted.MaxRetryAttempts = 0;

            var runtime = new RefreshRuntime(
                cache,
                settings,
                new FakePlayniteApi(new[] { noAchievementGame, achievementGame }),
                new IDataProvider[] { provider },
                new[] { "Steam" });

            await runtime.ExecuteRefreshAsync(new RefreshRequest
            {
                Mode = RefreshModeType.Full
            }).ConfigureAwait(false);

            CollectionAssert.AreEqual(
                new[] { achievementGameId },
                provider.RefreshedGameIds.ToArray());
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
            SeedCachedFriends(cache, "Steam", "1", "2");

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Shared },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(2, friends.GetOwnedGamesCalls);
            Assert.AreEqual(2, cache.SaveFriendOwnershipCalls);
            // Shared builds candidates from the fresh ownership snapshot: both friends share the game,
            // so each gets a per-friend unlock scrape.
            Assert.AreEqual(2, payload.FriendSummary.CandidatesRefreshed);
        }

        [TestMethod]
        public async Task RefreshAsync_FriendSelection_OnlyRefreshesSelectedFriendOwnership()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    MakeFriend("1"),
                    MakeFriend("2"),
                    MakeFriend("3")
                }
            };
            SeedCachedFriends(cache, "Steam", "1", "2", "3");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Shared,
                        FriendExternalUserIds = new[] { "2" }
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            // Only the selected friend's library is fetched, even though three friends exist.
            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(1, cache.SaveFriendOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_Full_UsesConfiguredFriendsWithoutRosterMetadataFetchOrSave()
        {
            var cache = new FakeFriendCache();
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.AddOrUpdateFriend(
                "Steam",
                "1",
                "Configured Friend",
                "https://example.invalid/configured-avatar.png",
                "icon_cache/friendavatars/steam_1.png",
                FriendSettingsSource.Manual);
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    new FriendIdentity
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        DisplayName = "Provider Friend",
                        AvatarUrl = "https://example.invalid/provider-avatar.png"
                    }
                }
            };

            await new RefreshRuntime(cache, settings)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Full },
                    reportProgress: null)
                .ConfigureAwait(false);

            var configured = settings.Persisted.GetFriendSetting("Steam", "1");
            Assert.AreEqual(0, friends.GetFriendsCalls);
            Assert.AreEqual(0, cache.SaveFriendListCalls);
            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual("Configured Friend", configured.DisplayName);
            Assert.AreEqual("https://example.invalid/configured-avatar.png", configured.AvatarUrl);
            Assert.AreEqual("icon_cache/friendavatars/steam_1.png", configured.AvatarPath);
        }

        [TestMethod]
        public async Task RefreshAsync_Recent_UsesCachedFriendsWithoutRosterMetadataFetchOrSave()
        {
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate> { MakeCandidate("1", 100) }
            };
            SeedCachedFriends(cache, "Steam", "1");
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    new FriendIdentity
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        DisplayName = "Provider Friend",
                        AvatarUrl = "https://example.invalid/provider-avatar.png"
                    }
                }
            };

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetFriendsCalls);
            Assert.AreEqual(0, cache.SaveFriendListCalls);
            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_Full_NoConfiguredOrCachedFriendsSkipsProviderRosterDiscovery()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Full },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.BeginCalls);
            Assert.AreEqual(1, friends.EndCalls);
            Assert.AreEqual(0, friends.GetFriendsCalls);
            Assert.AreEqual(0, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, cache.SaveFriendListCalls);
            Assert.AreEqual(0, payload.FriendSummary.FriendsFetched);
        }

        [TestMethod]
        public async Task RefreshFriendRosterAsync_FetchesProviderMetadataAndSavesFriendList()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    new FriendIdentity
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        DisplayName = "Provider Friend",
                        AvatarUrl = "https://example.invalid/provider-avatar.png"
                    }
                }
            };

            var saved = await CreateRuntime(cache)
                .RefreshFriendRosterAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) })
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.BeginCalls);
            Assert.AreEqual(1, friends.GetFriendsCalls);
            Assert.AreEqual(1, friends.EndCalls);
            Assert.AreEqual(1, cache.SaveFriendListCalls);
            Assert.AreEqual(1, saved);
        }

        [TestMethod]
        public async Task RefreshAsync_SelectedGame_UsesCachedCandidateWithoutOwnershipRefresh()
        {
            var gameId = Guid.NewGuid();
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate>
                {
                    new FriendRefreshCandidate
                    {
                        Friend = MakeFriend("2"),
                        AppId = 200,
                        PlayniteGameId = gameId,
                        GameName = "Game 200"
                    }
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

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.SelectedGame,
                        PlayniteGameIds = new[] { gameId },
                        FriendExternalUserIds = new[] { "2" },
                        ForceDefinitionRefresh = true
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, friends.GetFriendsCalls);
            Assert.AreEqual(0, cache.SaveFriendListCalls);
            Assert.AreEqual(0, cache.SaveFriendOwnershipCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(FriendRefreshScope.SelectedGame, cache.LastLoadOptions.Scope);
            CollectionAssert.AreEqual(new[] { gameId }, cache.LastLoadOptions.PlayniteGameIds.ToList());
        }

        [TestMethod]
        public async Task RefreshAsync_SelectedGameForAllFriends_DoesNotLoadRosterOrOwnership()
        {
            var gameId = Guid.NewGuid();
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate>
                {
                    new FriendRefreshCandidate
                    {
                        Friend = MakeFriend("1"),
                        AppId = 200,
                        PlayniteGameId = gameId,
                        GameName = "Game 200"
                    },
                    new FriendRefreshCandidate
                    {
                        Friend = MakeFriend("2"),
                        AppId = 200,
                        PlayniteGameId = gameId,
                        GameName = "Game 200"
                    }
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    MakeFriend("1"),
                    MakeFriend("2"),
                    MakeFriend("3")
                }
            };
            SeedCachedFriends(cache, "Steam", "1", "2", "3");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.SelectedGame,
                        PlayniteGameIds = new[] { gameId },
                        ForceDefinitionRefresh = true
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetFriendsCalls);
            Assert.AreEqual(0, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, cache.SaveFriendListCalls);
            Assert.AreEqual(0, cache.SaveFriendOwnershipCalls);
            Assert.AreEqual(2, friends.GetFriendGameAchievementsCalls);
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
            SeedCachedFriends(cache, "Steam", "1");

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
        public async Task RefreshAsync_FullLibraryFriend_DiscoversDefinitionsAndPersistsProviderOnlyOwnership()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 1
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>
                {
                    new FriendAchievementRow { ApiName = "A", DisplayName = "A", Unlocked = true }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(1, cache.SaveFriendGameDefinitionCalls);
            Assert.AreEqual(1, cache.SaveProviderOnlyOwnershipCalls);
            Assert.AreEqual(1, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryFriend_SkipsProviderOnlyOwnershipForNoAchievementsDefinition()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 1
                    }
                },
                DefinitionStatusToReturn = FriendGameDefinitionStatus.NoAchievements
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, cache.SaveFriendGameDefinitionCalls);
            Assert.AreEqual(0, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryProviderOnlyGameWithZeroUnlocks_DoesNotPersistFriendData()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 0
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>()
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(0, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
            Assert.AreEqual(0, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryProviderOnlyGameWithFriendUnlocks_PersistsOwnershipAndAchievements()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 1
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>
                {
                    new FriendAchievementRow { ApiName = "A", DisplayName = "A", Unlocked = true }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(1, cache.SaveProviderOnlyOwnershipCalls);
            Assert.AreEqual(1, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryProviderOnlyGameAlreadyProbed_SkipsCandidateRescrape()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false,
                Candidates = new List<FriendRefreshCandidate>
                {
                    new FriendRefreshCandidate
                    {
                        Friend = MakeFriend("1"),
                        AppId = 100,
                        GameName = "Game 100"
                    }
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 1
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>
                {
                    new FriendAchievementRow { ApiName = "A", DisplayName = "A", Unlocked = true }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(1, cache.SaveProviderOnlyOwnershipCalls);
            Assert.AreEqual(1, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryProviderOnlyGameWithUnknownUnlockHint_ScrapesAndPersists()
        {
            // A provider that supplies no unlock hint (e.g. Steam via community page parse failure)
            // must still have its provider-only games scraped under Full scope rather than silently
            // dropped; the post-scrape guard prunes any that turn out empty.
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1
                        // AchievementUnlocksHint intentionally unset (null).
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>
                {
                    new FriendAchievementRow { ApiName = "A", DisplayName = "A", Unlocked = true }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            // The unknown-hint provider-only game is scraped (not silently dropped) and, because it
            // has unlocks, its achievements are persisted. Provider-only ownership save counts are
            // intentionally not asserted here (a separate redundancy in the ownership-save path).
            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(1, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_RetroAchievementsFullLibraryProviderOnlyGameWithZeroUnlocks_DoesNotPersistFriendData()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("RetroAchievements")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1", "RetroAchievements") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "RetroAchievements",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 0
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>()
            };
            SeedCachedFriends(cache, "RetroAchievements", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("RetroAchievements", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(0, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
            Assert.AreEqual(0, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_RetroAchievementsFullLibraryProviderOnlyGameWithFriendUnlocks_PersistsOwnershipAndAchievements()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false
            };
            var friends = new FakeFriendsProvider("RetroAchievements")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1", "RetroAchievements") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "RetroAchievements",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 1,
                        AchievementUnlocksHint = 1
                    }
                },
                AchievementRowsToReturn = new List<FriendAchievementRow>
                {
                    new FriendAchievementRow { ApiName = "A", DisplayName = "A", Unlocked = true }
                }
            };
            SeedCachedFriends(cache, "RetroAchievements", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("RetroAchievements", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(1, cache.SaveProviderOnlyOwnershipCalls);
            Assert.AreEqual(1, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_RetroAchievementsSharedOwnershipKeepsNumericAppIdAndEmptyProviderKey()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("RetroAchievements")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1", "RetroAchievements") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "RetroAchievements",
                        ExternalUserId = "1",
                        AppId = 12345,
                        ProviderGameKey = null,
                        GameName = "RA Game"
                    }
                }
            };
            SeedCachedFriends(cache, "RetroAchievements", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("RetroAchievements", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Shared },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(1, cache.SaveFriendOwnershipCalls);

            var saved = cache.SavedOwnershipRows.Single();
            Assert.AreEqual(12345, saved.AppId);
            Assert.IsTrue(string.IsNullOrWhiteSpace(saved.ProviderGameKey));
        }

        [TestMethod]
        public async Task RefreshAsync_ProviderOnlyCandidateWithZeroUnlockHint_SkipsDetailFetch()
        {
            var cache = new FakeFriendCache
            {
                ProviderGamesMappedToPlayniteLibrary = false,
                Candidates = new List<FriendRefreshCandidate>
                {
                    new FriendRefreshCandidate
                    {
                        Friend = MakeFriend("1"),
                        AppId = 100,
                        GameName = "Game 100"
                    }
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                AchievementRowsToReturn = new List<FriendAchievementRow>()
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(0, cache.SaveFriendGameAchievementsCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryFriend_RetriesCachedNoAchievementsDefinition()
        {
            var cache = new FakeFriendCache
            {
                DefinitionStates = new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["100"] = new FriendGameDefinitionState
                    {
                        ProviderKey = "Steam",
                        AppId = 100,
                        Status = FriendGameDefinitionStatus.NoAchievements,
                        LastCheckedUtc = DateTime.UtcNow
                    }
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                DefinitionStatusToReturn = FriendGameDefinitionStatus.NoAchievements
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            // The mapped game's ownership is synced by the per-friend save, not re-flagged as
            // provider-only, so no provider-only ownership write occurs.
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_CustomProviderAppIds_DiscoversOnlyRequestedProviderApps()
        {
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership { ProviderKey = "Steam", ExternalUserId = "1", AppId = 100 },
                    new FriendGameOwnership { ProviderKey = "Steam", ExternalUserId = "1", AppId = 200 }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Custom,
                        ProviderAppIds = new[] { 200 }
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
            CollectionAssert.AreEqual(new[] { 200 }, friends.DefinitionAppIds.ToList());
            Assert.AreEqual(1, cache.SavedOwnershipRows.Count);
            Assert.AreEqual(200, cache.SavedOwnershipRows.Single().AppId);
            // The requested app is mapped, so its ownership is synced by the per-friend save; no
            // separate provider-only ownership write occurs.
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_Recent_DoesNotDiscoverProviderOnlyOwnership()
        {
            // Recent is strictly playtime-delta based; it never discovers provider-only libraries.
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Recent
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_SharedScope_DoesNotDiscoverProviderOnlyDefinitions()
        {
            // The Shared scope never discovers unowned games.
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Shared
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetOwnedGamesCalls);
            Assert.AreEqual(0, friends.GetFriendGameDefinitionCalls);
            Assert.AreEqual(0, cache.SaveProviderOnlyOwnershipCalls);
        }

        [TestMethod]
        public async Task RefreshAsync_ExophaseFullScope_RefreshesSharedFriendOwnershipForMapping()
        {
            var playniteGameId = Guid.NewGuid();
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Exophase")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1"), MakeFriend("2") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Exophase",
                        ExternalUserId = "1",
                        ProviderGameKey = "origin|titanfall-2-origin",
                        ProviderPlatformKey = "EA",
                        PlayniteGameId = playniteGameId,
                        GameName = "Titanfall 2"
                    }
                }
            };
            SeedCachedFriends(cache, "Exophase", "1", "2");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Exophase", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(2, friends.GetOwnedGamesCalls);
            Assert.AreEqual(2, cache.SaveFriendOwnershipCalls);
            // Exophase scrapes the mapped game's definition once (deduplicated across both friends),
            // like Steam/RA; the two per-friend ownership saves are the only ownership writes.
            Assert.AreEqual(1, friends.GetFriendGameDefinitionCalls);
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
            SeedCachedFriends(cache, "Steam", "1");

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
            SeedCachedFriends(cache, "Steam", "1");

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
        public async Task RefreshAsync_Recent_SkipsSteamGameWithUnchangedPlaytime()
        {
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate> { MakeCandidate("1", 100) },
                OwnershipRecency = new Dictionary<string, FriendOwnershipRecency>(StringComparer.OrdinalIgnoreCase)
                {
                    ["100"] = new FriendOwnershipRecency
                    {
                        PlaytimeForeverMinutes = 50,
                        LastScrapedUtc = DateTime.UtcNow.AddDays(-1),
                        LastScrapeStatus = "ok"
                    }
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 50
                    }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(0, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(0, payload.FriendSummary.CandidatesRefreshed);
        }

        [TestMethod]
        public async Task RefreshAsync_Recent_ScrapesSteamGameWithIncreasedPlaytime()
        {
            var cache = new FakeFriendCache
            {
                Candidates = new List<FriendRefreshCandidate> { MakeCandidate("1", 100) },
                OwnershipRecency = new Dictionary<string, FriendOwnershipRecency>(StringComparer.OrdinalIgnoreCase)
                {
                    ["100"] = new FriendOwnershipRecency
                    {
                        PlaytimeForeverMinutes = 50,
                        LastScrapedUtc = DateTime.UtcNow.AddDays(-1),
                        LastScrapeStatus = "ok"
                    }
                }
            };
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") },
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "1",
                        AppId = 100,
                        PlaytimeForeverMinutes = 75
                    }
                }
            };
            SeedCachedFriends(cache, "Steam", "1");

            var payload = await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Recent },
                    reportProgress: null)
                .ConfigureAwait(false);

            Assert.AreEqual(1, friends.GetFriendGameAchievementsCalls);
            Assert.AreEqual(1, payload.FriendSummary.CandidatesRefreshed);
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
            SeedCachedFriends(cache, "Steam", Enumerable.Range(1, 12).Select(index => index.ToString()).ToArray());

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
        public async Task RefreshAsync_Shared_ReportsMonotonicProgressWithFriendAndGameNames()
        {
            var reports = new List<ProgressEvent>();
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity>
                {
                    MakeFriend("1"),
                    MakeFriend("2")
                },
                // Shared builds candidates from the fresh ownership snapshot; name the shared games so
                // the per-game progress detail reads "Friend 1 - Game 100".
                OwnedGamesToReturn = new List<FriendGameOwnership>
                {
                    new FriendGameOwnership { ProviderKey = "Steam", AppId = 100, GameName = "Game 100", PlaytimeForeverMinutes = 1 },
                    new FriendGameOwnership { ProviderKey = "Steam", AppId = 200, GameName = "Game 200", PlaytimeForeverMinutes = 1 }
                }
            };
            SeedCachedFriends(cache, "Steam", "1", "2");

            await CreateRuntime(cache, enableParallelProviderRefresh: false)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions { Scope = FriendRefreshScope.Shared },
                    reportProgress: (message, current, total) => reports.Add(new ProgressEvent(message, current, total)))
                .ConfigureAwait(false);

            Assert.IsTrue(reports.Count > 0);
            Assert.IsTrue(reports.All(report => report.Total == RefreshRuntime.FriendRefreshProgressSession.TotalUnits));
            for (var i = 1; i < reports.Count; i++)
            {
                Assert.IsTrue(
                    reports[i].Current >= reports[i - 1].Current,
                    $"Progress regressed from {reports[i - 1].Current} to {reports[i].Current}: {reports[i].Message}");
            }

            var messageText = string.Join("\n", reports.Select(report => report.Message ?? "<null>"));
            Assert.IsTrue(reports.Any(report => report.Message?.Contains("Friend 1") == true), messageText);
            Assert.IsTrue(reports.Any(report => report.Message?.Contains("Friend 1 - Game 100") == true), messageText);
        }

        [TestMethod]
        public async Task RefreshAsync_FullLibraryFriend_UsesUserFacingGameCheckProgressText()
        {
            var reports = new List<ProgressEvent>();
            var cache = new FakeFriendCache();
            var friends = new FakeFriendsProvider("Steam")
            {
                FriendsToReturn = new List<FriendIdentity> { MakeFriend("1") }
            };
            SeedCachedFriends(cache, "Steam", "1");

            await CreateRuntime(cache)
                .RefreshAsync(
                    new IDataProvider[] { new FakeDataProvider("Steam", friends) },
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.Full
                    },
                    reportProgress: (message, current, total) => reports.Add(new ProgressEvent(message, current, total)))
                .ConfigureAwait(false);

            var messages = reports
                .Select(report => report.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList();
            Assert.IsTrue(
                messages.Any(message => message.IndexOf("Refreshing friend games", StringComparison.OrdinalIgnoreCase) >= 0),
                string.Join("\n", messages));
            Assert.IsFalse(messages.Any(message => message.IndexOf("definition", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(messages.Any(message => message.IndexOf("schema", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(messages.Any(message => message.IndexOf("unlock marker", StringComparison.OrdinalIgnoreCase) >= 0));
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

        private static RefreshRuntime CreateRuntime(
            FakeFriendCache cache,
            bool enableParallelProviderRefresh = true)
        {
            var settings = new PlayniteAchievementsSettings();
            settings.Persisted.EnableParallelProviderRefresh = enableParallelProviderRefresh;
            settings.Persisted.ScanDelayMs = 0;
            settings.Persisted.MaxRetryAttempts = 0;

            return new RefreshRuntime(
                cache,
                settings);
        }

        private static FriendIdentity MakeFriend(string externalUserId) =>
            MakeFriend(externalUserId, "Steam");

        private static FriendIdentity MakeFriend(string externalUserId, string providerKey) =>
            new FriendIdentity
            {
                ProviderKey = providerKey,
                ExternalUserId = externalUserId,
                DisplayName = "Friend " + externalUserId
            };

        private static void SeedCachedFriends(
            FakeFriendCache cache,
            string providerKey,
            params string[] externalUserIds)
        {
            if (cache == null)
            {
                return;
            }

            cache.CachedFriends = (externalUserIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => MakeFriend(id.Trim(), providerKey))
                .ToList();
        }

        private static FriendRefreshCandidate MakeCandidate(string externalUserId, int appId) =>
            new FriendRefreshCandidate
            {
                Friend = MakeFriend(externalUserId),
                AppId = appId,
                GameName = "Game " + appId
            };

        private sealed class ProgressEvent
        {
            public ProgressEvent(string message, int current, int total)
            {
                Message = message;
                Current = current;
                Total = total;
            }

            public string Message { get; }
            public int Current { get; }
            public int Total { get; }
        }

        private sealed class FakeFriendCache : ICacheManager, IFriendCacheManager
        {
            private int _saveFriendListCalls;
            private int _saveFriendOwnershipCalls;
            private int _saveFriendGameAchievementsCalls;
            private int _saveFriendGameDefinitionCalls;
            private int _saveProviderOnlyOwnershipCalls;
            private int _promoteProviderOnlyGameCalls;
            private readonly List<FriendGameOwnership> _savedOwnershipRows = new List<FriendGameOwnership>();

            public List<FriendRefreshCandidate> Candidates { get; set; } = new List<FriendRefreshCandidate>();
            public Dictionary<string, FriendGameDefinitionState> DefinitionStates { get; set; } =
                new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, GameAchievementData> CachedGameData { get; set; } =
                new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase);
            public bool ProviderGamesMappedToPlayniteLibrary { get; set; } = true;
            public int SaveFriendListCalls => _saveFriendListCalls;
            public int SaveFriendOwnershipCalls => _saveFriendOwnershipCalls;
            public int SaveFriendGameAchievementsCalls => _saveFriendGameAchievementsCalls;
            public int SaveFriendGameDefinitionCalls => _saveFriendGameDefinitionCalls;
            public int SaveProviderOnlyOwnershipCalls => _saveProviderOnlyOwnershipCalls;
            public int PromoteProviderOnlyGameCalls => _promoteProviderOnlyGameCalls;
            public FriendRefreshOptions LastLoadOptions { get; private set; }
            public IReadOnlyList<FriendGameOwnership> SavedOwnershipRows
            {
                get
                {
                    lock (_savedOwnershipRows)
                    {
                        return _savedOwnershipRows.ToList();
                    }
                }
            }
            public List<FriendIdentity> CachedFriends { get; set; } = new List<FriendIdentity>();

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

                lock (_savedOwnershipRows)
                {
                    _savedOwnershipRows.AddRange((ownership ?? Array.Empty<FriendGameOwnership>())
                        .Where(item => item != null));
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

            public FriendCacheWriteResult SaveProviderGameImagePaths(
                string providerKey,
                string providerGameKey,
                int appId,
                string iconAbsolutePath,
                string coverAbsolutePath) =>
                FriendCacheWriteResult.Ok(1, 1, 0);

            public Dictionary<string, FriendGameDefinitionState> LoadFriendGameDefinitionStates(
                string providerKey,
                IReadOnlyCollection<string> providerGameKeys) =>
                DefinitionStates
                    .Where(pair => providerGameKeys?.Contains(pair.Key) == true)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            public FriendUnownedCacheStats GetUnownedFriendGameCacheStats() =>
                new FriendUnownedCacheStats();

            public FriendUnownedCacheClearResult ClearUnownedFriendGameData() =>
                new FriendUnownedCacheClearResult { Success = true };

            public FriendCacheWriteResult ClearUnownedFriendGame(string providerKey, int appId, string providerGameKey) =>
                FriendCacheWriteResult.Ok();

            public bool IsProviderGameMappedToPlayniteLibrary(string providerKey, int appId, string providerGameKey) =>
                ProviderGamesMappedToPlayniteLibrary;

            public List<FriendGameMapping> FriendGameMappings { get; set; } = new List<FriendGameMapping>();

            public IReadOnlyList<FriendGameMapping> LoadFriendGameMappings(string providerKey) =>
                FriendGameMappings.ToList();

            public FriendCacheWriteResult PromoteProviderOnlyGameToPlayniteBacked(
                string providerKey,
                int appId,
                string providerGameKey,
                Guid playniteGameId)
            {
                Interlocked.Increment(ref _promoteProviderOnlyGameCalls);
                return FriendCacheWriteResult.Ok(1, 1, 0);
            }

            public FriendCacheWriteResult SaveFriendGameAchievements(
                string providerKey,
                string externalUserId,
                string providerGameKey,
                int appId,
                FriendGameAchievements achievements)
            {
                Interlocked.Increment(ref _saveFriendGameAchievementsCalls);
                return FriendCacheWriteResult.Ok();
            }

            public FriendCacheWriteResult DeleteFriendData(string providerKey, string externalUserId, bool preserveFriendRecord = false) =>
                FriendCacheWriteResult.Ok();

            public List<FriendIdentity> LoadFriendIdentities(string providerKey) =>
                CachedFriends.ToList();

            public List<FriendRefreshCandidate> LoadFriendRefreshCandidates(
                string providerKey,
                FriendRefreshOptions options)
            {
                LastLoadOptions = options?.Clone();
                return Candidates.ToList();
            }

            public IReadOnlyDictionary<string, FriendOwnershipRecency> OwnershipRecency { get; set; } =
                new Dictionary<string, FriendOwnershipRecency>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, FriendOwnershipRecency> LoadFriendOwnershipRecency(
                string providerKey,
                string externalUserId) => OwnershipRecency;

            public FriendsOverviewData LoadFriendsOverviewData(bool hideSpoilers, int recentLimit) =>
                new FriendsOverviewData();

            public IReadOnlyList<CurrentUserGameLabel> CurrentUserGameLabels { get; set; } =
                new List<CurrentUserGameLabel>();

            public IReadOnlyList<CurrentUserGameLabel> LoadCurrentUserGameLabels() => CurrentUserGameLabels;

#pragma warning disable CS0067
            public event EventHandler<GameCacheUpdatedEventArgs> GameCacheUpdated;
            public event EventHandler<CacheDeltaEventArgs> CacheDeltaUpdated;
            public event EventHandler CacheInvalidated;
#pragma warning restore CS0067

            public void EnsureDiskCacheOrClearMemory() { }
            public bool CacheFileExists() => false;
            public bool IsCacheValid() => true;
            public DateTime? GetMostRecentLastUpdatedUtc() => null;
            public List<string> GetCachedGameIds() => new List<string>();
            public GameAchievementData LoadGameData(string key) =>
                !string.IsNullOrWhiteSpace(key) && CachedGameData.TryGetValue(key, out var data)
                    ? data
                    : null;
            public CacheWriteResult SaveGameData(string key, GameAchievementData data) => null;
            public void RemoveGameData(Guid playniteGameId) { }
            public void RemoveGameCache(Guid playniteGameId) { }
            public void NotifyCacheInvalidated() => CacheInvalidated?.Invoke(this, EventArgs.Empty);
            public void ClearCache() { }
            public string ExportDatabaseToCsv(string exportDirectory) => null;
        }

        private sealed class FakePlayniteApi : IPlayniteAPI
        {
            public FakePlayniteApi(IEnumerable<Game> games)
            {
                Database = new FakeGameDatabase(games);
            }

            public IMainViewAPI MainView => null;
            public IGameDatabaseAPI Database { get; }
            public IDialogsFactory Dialogs => null;
            public IPlaynitePathsAPI Paths => null;
            public INotificationsAPI Notifications => null;
            public IPlayniteInfoAPI ApplicationInfo => null;
            public IWebViewFactory WebViews => null;
            public IResourceProvider Resources => null;
            public IUriHandlerAPI UriHandler => null;
            public IPlayniteSettingsAPI ApplicationSettings => null;
            public IAddons Addons => null;
            public IEmulationAPI Emulation => null;

            public string ExpandGameVariables(Game game, string source) => source;
            public string ExpandGameVariables(Game game, string source, string fallbackValue) => source ?? fallbackValue;
            public GameAction ExpandGameVariables(Game game, GameAction source) => source;
            public void StartGame(Guid id) { }
            public void InstallGame(Guid id) { }
            public void UninstallGame(Guid id) { }
            public void AddCustomElementSupport(Plugin plugin, AddCustomElementSupportArgs args) { }
            public void AddSettingsSupport(Plugin plugin, AddSettingsSupportArgs args) { }
            public void AddConvertersSupport(Plugin plugin, AddConvertersSupportArgs args) { }
        }

        private sealed class FakeGameDatabase : IGameDatabaseAPI
        {
            public FakeGameDatabase(IEnumerable<Game> games)
            {
                Games = new FakeGameCollection(games);
            }

            public IItemCollection<Game> Games { get; }
            public IItemCollection<Platform> Platforms => null;
            public IItemCollection<Emulator> Emulators => null;
            public IItemCollection<Genre> Genres => null;
            public IItemCollection<Company> Companies => null;
            public IItemCollection<Tag> Tags => null;
            public IItemCollection<Category> Categories => null;
            public IItemCollection<Series> Series => null;
            public IItemCollection<AgeRating> AgeRatings => null;
            public IItemCollection<Region> Regions => null;
            public IItemCollection<GameSource> Sources => null;
            public IItemCollection<GameFeature> Features => null;
            public IItemCollection<GameScannerConfig> GameScanners => null;
            public IItemCollection<CompletionStatus> CompletionStatuses => null;
            public IItemCollection<ImportExclusionItem> ImportExclusions => null;
            public IItemCollection<FilterPreset> FilterPresets => null;
            public bool IsOpen => true;
            public string DatabasePath => string.Empty;
            public event EventHandler DatabaseOpened { add { } remove { } }
            public string AddFile(string path, Guid parentId) => path;
            public void SaveFile(string path, string sourceFile) { }
            public void RemoveFile(string path) { }
            public IDisposable BufferedUpdate() => NullDisposable.Instance;
            public void BeginBufferUpdate() { }
            public void EndBufferUpdate() { }
            public string GetFileStoragePath(Guid parentId) => string.Empty;
            public string GetFullFilePath(string path) => path;
            public Game ImportGame(GameMetadata gameMetadata) => null;
            public Game ImportGame(GameMetadata gameMetadata, LibraryPlugin libraryPlugin) => null;
            public bool GetGameMatchesFilter(Game game, FilterPresetSettings filter) => false;
            public IEnumerable<Game> GetFilteredGames(FilterPresetSettings filter) => Enumerable.Empty<Game>();
            public bool GetGameMatchesFilter(Game game, FilterPresetSettings filter, bool ignoreHidden) => false;
            public IEnumerable<Game> GetFilteredGames(FilterPresetSettings filter, bool ignoreHidden) => Enumerable.Empty<Game>();
        }

        private sealed class FakeGameCollection : IItemCollection<Game>
        {
            private readonly Dictionary<Guid, Game> _items;

            public FakeGameCollection(IEnumerable<Game> games)
            {
                _items = (games ?? Enumerable.Empty<Game>())
                    .Where(game => game != null)
                    .ToDictionary(game => game.Id, game => game);
            }

            public GameDatabaseCollection CollectionType => GameDatabaseCollection.Games;
            public int Count => _items.Count;
            public bool IsReadOnly => false;
            public Game this[Guid id] { get => Get(id); set => _items[id] = value; }
#pragma warning disable CS0067
            public event EventHandler<ItemCollectionChangedEventArgs<Game>> ItemCollectionChanged;
            public event EventHandler<ItemUpdatedEventArgs<Game>> ItemUpdated;
#pragma warning restore CS0067
            public bool ContainsItem(Guid id) => _items.ContainsKey(id);
            public Game Get(Guid id) => _items.TryGetValue(id, out var game) ? game : null;
            public List<Game> Get(IList<Guid> ids) => ids?.Select(Get).Where(item => item != null).ToList() ?? new List<Game>();
            public Game Add(string name) => throw new NotSupportedException();
            public Game Add(string name, Func<Game, string, bool> mergeAction) => throw new NotSupportedException();
            public IEnumerable<Game> Add(List<string> items) => throw new NotSupportedException();
            public Game Add(MetadataProperty item) => throw new NotSupportedException();
            public IEnumerable<Game> Add(IEnumerable<MetadataProperty> items) => throw new NotSupportedException();
            public IEnumerable<Game> Add(List<string> items, Func<Game, string, bool> mergeAction) => throw new NotSupportedException();
            public void Add(IEnumerable<Game> items) => Update(items);
            public bool Remove(Guid id) => _items.Remove(id);
            public bool Remove(IEnumerable<Game> items)
            {
                var removed = false;
                foreach (var item in items ?? Enumerable.Empty<Game>())
                {
                    removed |= item != null && _items.Remove(item.Id);
                }

                return removed;
            }

            public void Update(Game item)
            {
                if (item != null)
                {
                    _items[item.Id] = item;
                }
            }

            public void Update(IEnumerable<Game> items)
            {
                foreach (var item in items ?? Enumerable.Empty<Game>())
                {
                    Update(item);
                }
            }

            public IDisposable BufferedUpdate() => NullDisposable.Instance;
            public void BeginBufferUpdate() { }
            public void EndBufferUpdate() { }
            public IEnumerable<Game> GetClone() => _items.Values.ToList();
            public void Add(Game item) => Update(item);
            public void Clear() => _items.Clear();
            public bool Contains(Game item) => item != null && _items.ContainsKey(item.Id);
            public void CopyTo(Game[] array, int arrayIndex) => _items.Values.ToList().CopyTo(array, arrayIndex);
            public bool Remove(Game item) => item != null && _items.Remove(item.Id);
            public IEnumerator<Game> GetEnumerator() => _items.Values.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Dispose() { }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new NullDisposable();
            public void Dispose() { }
        }

        private sealed class FakeDataProvider : IDataProvider
        {
            private readonly Func<Game, bool> _isCapable;
            private int _refreshCalls;
            private readonly List<Guid> _refreshedGameIds = new List<Guid>();

            public FakeDataProvider(string providerKey, IFriendsProvider friends, Func<Game, bool> isCapable = null)
            {
                ProviderKey = providerKey;
                Friends = friends;
                _isCapable = isCapable ?? (_ => true);
            }

            public string ProviderName => ProviderKey;
            public string ProviderKey { get; }
            public string ProviderIconKey => ProviderKey;
            public string ProviderColorHex => "#000000";
            public bool IsAuthenticated => true;
            public ISessionManager AuthSession => null;
            public IFriendsProvider Friends { get; }
            public Action<string> OperationLog { get; set; }
            public int RefreshCalls => _refreshCalls;
            public IReadOnlyList<Guid> RefreshedGameIds
            {
                get
                {
                    lock (_refreshedGameIds)
                    {
                        return _refreshedGameIds.ToList();
                    }
                }
            }
            public bool IsCapable(Game game) => _isCapable(game);

            public async Task<RebuildPayload> RefreshAsync(
                IReadOnlyList<Game> gamesToRefresh,
                Action<Game> onGameStarting,
                Func<Game, GameAchievementData, Task> onGameCompleted,
                CancellationToken cancel)
            {
                Interlocked.Increment(ref _refreshCalls);
                OperationLog?.Invoke($"{ProviderKey}:current");

                var summary = new RebuildSummary();
                foreach (var game in gamesToRefresh ?? Array.Empty<Game>())
                {
                    cancel.ThrowIfCancellationRequested();
                    onGameStarting?.Invoke(game);
                    if (onGameCompleted != null)
                    {
                        await onGameCompleted(game, null).ConfigureAwait(false);
                    }

                    summary.GamesRefreshed++;
                    summary.GamesWithoutAchievements++;
                    if (game?.Id != Guid.Empty)
                    {
                        summary.RefreshedGameIds.Add(game.Id);
                        lock (_refreshedGameIds)
                        {
                            _refreshedGameIds.Add(game.Id);
                        }
                    }
                }

                return new RebuildPayload { Summary = summary };
            }

            public IProviderSettings GetSettings() => null;
            public void ApplySettings(IProviderSettings settings) { }
            public ProviderSettingsViewBase CreateSettingsView() => null;
        }

        private sealed class FakeFriendsProvider : IFriendsProvider
        {
            private int _beginCalls;
            private int _endCalls;
            private int _getFriendsCalls;
            private int _getOwnedGamesCalls;
            private int _getFriendGameAchievementsCalls;
            private int _getFriendGameDefinitionCalls;
            private int _currentOwnershipCalls;
            private int _maxConcurrentOwnershipCalls;
            private int _currentAchievementCalls;
            private int _maxConcurrentAchievementCalls;
            private readonly List<int> _definitionAppIds = new List<int>();

            public FakeFriendsProvider(string providerKey)
            {
                ProviderKey = providerKey;
                BeginResult = FriendsProviderResult<FriendsRefreshPreparation>
                    .FromData(new FriendsRefreshPreparation { CanRefreshAchievements = true });
            }

            public string ProviderKey { get; }
            public FriendsProviderResult<FriendsRefreshPreparation> BeginResult { get; set; }
            public IReadOnlyList<FriendIdentity> FriendsToReturn { get; set; } = Array.Empty<FriendIdentity>();
            public IReadOnlyList<FriendGameOwnership> OwnedGamesToReturn { get; set; }
            public FriendGameDefinitionStatus DefinitionStatusToReturn { get; set; } = FriendGameDefinitionStatus.Ok;
            public List<FriendAchievementRow> AchievementRowsToReturn { get; set; }
            public Action<string> OperationLog { get; set; }
            public int OwnershipDelayMs { get; set; }
            public int AchievementDelayMs { get; set; }
            public int BeginCalls => _beginCalls;
            public int EndCalls => _endCalls;
            public int GetFriendsCalls => _getFriendsCalls;
            public int GetOwnedGamesCalls => _getOwnedGamesCalls;
            public int GetFriendGameAchievementsCalls => _getFriendGameAchievementsCalls;
            public int GetFriendGameDefinitionCalls => _getFriendGameDefinitionCalls;
            public int MaxConcurrentOwnershipCalls => _maxConcurrentOwnershipCalls;
            public int MaxConcurrentAchievementCalls => _maxConcurrentAchievementCalls;
            public IReadOnlyList<int> DefinitionAppIds
            {
                get
                {
                    lock (_definitionAppIds)
                    {
                        return _definitionAppIds.ToList();
                    }
                }
            }

            public Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
            {
                Interlocked.Increment(ref _beginCalls);
                OperationLog?.Invoke($"{ProviderKey}:friend-begin");
                return Task.FromResult(BeginResult);
            }

            public void EndRefresh()
            {
                Interlocked.Increment(ref _endCalls);
            }

            public Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel)
            {
                Interlocked.Increment(ref _getFriendsCalls);
                return Task.FromResult(FriendsProviderResult<IReadOnlyList<FriendIdentity>>.FromData(FriendsToReturn));
            }

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

                    IReadOnlyList<FriendGameOwnership> ownedGames = OwnedGamesToReturn ?? new[]
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
                string providerGameKey,
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
                            ProviderGameKey = providerGameKey,
                            LastUpdatedUtc = DateTime.UtcNow,
                            Rows = AchievementRowsToReturn?
                                .Select(row => new FriendAchievementRow
                                {
                                    ApiName = row.ApiName,
                                    DisplayName = row.DisplayName,
                                    Description = row.Description,
                                    IconUrl = row.IconUrl,
                                    UnlockedIconUrl = row.UnlockedIconUrl,
                                    LockedIconUrl = row.LockedIconUrl,
                                    Points = row.Points,
                                    ScaledPoints = row.ScaledPoints,
                                    TrophyType = row.TrophyType,
                                    Hidden = row.Hidden,
                                    IsCapstone = row.IsCapstone,
                                    GlobalPercentUnlocked = row.GlobalPercentUnlocked,
                                    Rarity = row.Rarity,
                                    Unlocked = row.Unlocked,
                                    UnlockTimeUtc = row.UnlockTimeUtc,
                                    ProgressNum = row.ProgressNum,
                                    ProgressDenom = row.ProgressDenom
                                })
                                .ToList() ?? new List<FriendAchievementRow>()
                        });
                }
                finally
                {
                    Interlocked.Decrement(ref _currentAchievementCalls);
                }
            }

            public Task<FriendsProviderResult<FriendGameDefinition>> GetFriendGameDefinitionAsync(
                string providerGameKey,
                int appId,
                string gameName,
                CancellationToken cancel)
            {
                Interlocked.Increment(ref _getFriendGameDefinitionCalls);
                lock (_definitionAppIds)
                {
                    _definitionAppIds.Add(appId);
                }

                return Task.FromResult(FriendsProviderResult<FriendGameDefinition>.FromData(new FriendGameDefinition
                {
                    ProviderKey = ProviderKey,
                    AppId = appId,
                    ProviderGameKey = providerGameKey,
                    GameName = gameName,
                    Status = DefinitionStatusToReturn,
                    LastCheckedUtc = DateTime.UtcNow,
                    Achievements = DefinitionStatusToReturn == FriendGameDefinitionStatus.Ok
                        ? new List<AchievementDetail>
                        {
                            new AchievementDetail { ApiName = "A", DisplayName = "A" }
                        }
                        : new List<AchievementDetail>()
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

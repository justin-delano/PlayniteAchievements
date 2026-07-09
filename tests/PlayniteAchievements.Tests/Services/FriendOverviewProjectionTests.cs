using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Tests.Services
{
    [TestClass]
    public class FriendOverviewProjectionTests
    {
        [TestMethod]
        public void SelectedFriendGames_UsesFriendLinksAndUniqueUnlocks()
        {
            var gameId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var friend = new FriendSummaryItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "alice",
                DisplayName = "Alice"
            };
            var game = new FriendGameSummaryItem
            {
                ProviderKey = "Steam",
                AppId = 10,
                PlayniteGameId = gameId,
                GameName = "Game One",
                TotalAchievements = 4
            };
            var data = new FriendsOverviewData
            {
                Friends = new List<FriendSummaryItem> { friend },
                Games = new List<FriendGameSummaryItem> { game },
                AllUnlockedAchievements = new List<FriendAchievementDisplayItem>
                {
                    Achievement("Steam", "alice", 10, gameId, "Game One", "Duplicate", Utc(2026, 1, 1), RarityTier.Common),
                    Achievement("Steam", "alice", 10, gameId, "Game One", "Duplicate", Utc(2026, 1, 3), RarityTier.Rare),
                    Achievement("Steam", "alice", 10, gameId, "Game One", "Unique", Utc(2026, 1, 2), RarityTier.UltraRare)
                },
                FriendGameLinks = new List<FriendGameLinkItem>
                {
                    new FriendGameLinkItem
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "alice",
                        AppId = 10,
                        PlayniteGameId = gameId,
                        PlaytimeForeverMinutes = 90,
                        LastPlayedUtc = Utc(2026, 1, 4)
                    }
                }
            };

            var projection = new FriendOverviewProjection(data);
            var selectedRow = projection.GetSelectedFriendGames(friend).Single();

            Assert.AreNotSame(game, selectedRow);
            Assert.AreEqual(2, selectedRow.UnlockedAchievements);
            Assert.AreEqual(2, selectedRow.UniqueFriendUnlockedAchievementsCount);
            Assert.AreEqual(4, selectedRow.TotalAchievements);
            Assert.AreEqual(50, selectedRow.Progression);
            Assert.AreEqual(90UL * 60UL, selectedRow.PlaytimeSeconds);
            Assert.AreEqual(Utc(2026, 1, 4), selectedRow.LastPlayed);
            Assert.AreEqual(Utc(2026, 1, 3), selectedRow.LastUnlockUtc);
            Assert.AreEqual(90, selectedRow.TotalFriendPlaytimeMinutes);
            Assert.IsTrue(projection.HasAnyFriendUnlocks(game));
            Assert.IsTrue(projection.HasUnlocksForFriendGame(friend, game));
        }

        [TestMethod]
        public void SelectedFriendGames_IgnoresOwnershipOnlyLinks()
        {
            var gameId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var friend = new FriendSummaryItem
            {
                ProviderKey = "Steam",
                ExternalUserId = "alice",
                DisplayName = "Alice"
            };
            var game = new FriendGameSummaryItem
            {
                ProviderKey = "Steam",
                AppId = 20,
                PlayniteGameId = gameId,
                GameName = "Owned Only",
                FriendCount = 1,
                TotalAchievements = 12
            };
            var data = new FriendsOverviewData
            {
                Friends = new List<FriendSummaryItem> { friend },
                Games = new List<FriendGameSummaryItem> { game },
                FriendGameLinks = new List<FriendGameLinkItem>
                {
                    new FriendGameLinkItem
                    {
                        ProviderKey = "Steam",
                        ExternalUserId = "alice",
                        AppId = 20,
                        PlayniteGameId = gameId,
                        PlaytimeForeverMinutes = 45
                    }
                }
            };

            var projection = new FriendOverviewProjection(data);

            Assert.AreEqual(0, projection.GetSelectedFriendGames(friend).Count);
            Assert.IsFalse(projection.HasAnyFriendUnlocks(game));
            Assert.IsFalse(projection.HasUnlocksForFriendGame(friend, game));
        }

        [TestMethod]
        public void ScopeKeys_AreStableAndCaseInsensitive()
        {
            var gameId = Guid.Parse("33333333-3333-3333-3333-333333333333");

            Assert.AreEqual("steam|alice", FriendOverviewProjection.BuildFriendKey(" Steam ", " Alice "));
            Assert.AreEqual("steam|app:10", FriendOverviewProjection.BuildGameUnlockKey(" Steam ", 10, gameId));
            Assert.AreEqual(
                "alice|steam|app:10",
                FriendOverviewProjection.BuildFriendGameUnlockKey(" Steam ", " Alice ", 10, gameId));
        }

        private static FriendAchievementDisplayItem Achievement(
            string providerKey,
            string externalUserId,
            int appId,
            Guid playniteGameId,
            string gameName,
            string apiName,
            DateTime unlockTimeUtc,
            RarityTier rarity)
        {
            return new FriendAchievementDisplayItem
            {
                ProviderKey = providerKey,
                FriendExternalUserId = externalUserId,
                AppId = appId,
                PlayniteGameId = playniteGameId,
                GameName = gameName,
                SortingName = gameName,
                ApiName = apiName,
                DisplayName = apiName,
                Unlocked = true,
                UnlockTimeUtc = unlockTimeUtc,
                Rarity = rarity
            };
        }

        private static DateTime Utc(int year, int month, int day)
        {
            return DateTime.SpecifyKind(new DateTime(year, month, day, 0, 0, 0), DateTimeKind.Utc);
        }
    }
}

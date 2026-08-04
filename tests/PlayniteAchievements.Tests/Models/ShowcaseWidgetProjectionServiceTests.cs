using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.Models
{
    [TestClass]
    public class ShowcaseWidgetProjectionServiceTests
    {
        [TestMethod]
        public void Statistics_CalculateRatesPlaytimeAndStreaksFromSharedSnapshot()
        {
            var today = new DateTime(2026, 7, 31);
            var snapshot = new OverviewDataSnapshot
            {
                TotalUnlocked = 12,
                TotalGames = 3,
                CompletedGames = 1,
                GlobalProgressionPercent = 62.5,
                GlobalUnlockCountsByDate = new Dictionary<DateTime, int>
                {
                    [today] = 2,
                    [today.AddDays(-1)] = 3,
                    [today.AddDays(-4)] = 1
                },
                GameSummaries = new List<GameSummaryItem>
                {
                    new GameSummaryItem { PlaytimeSeconds = 3600, LastPlayed = today },
                    new GameSummaryItem { PlaytimeSeconds = 7200 },
                    new GameSummaryItem()
                },
                Achievements = new List<AchievementDisplayItem>
                {
                    new AchievementDisplayItem { Unlocked = true, GlobalPercentUnlocked = 10 },
                    new AchievementDisplayItem { Unlocked = true, GlobalPercentUnlocked = 30 }
                }
            };

            var statistics = ShowcaseWidgetProjectionService.BuildStatistics(snapshot, today);

            Assert.AreEqual(2, statistics.Single(item => item.Key == "currentStreak").Value);
            Assert.AreEqual(2, statistics.Single(item => item.Key == "longestStreak").Value);
            Assert.AreEqual(4, statistics.Single(item => item.Key == "activeDayRate").Value);
            Assert.AreEqual(0.2, statistics.Single(item => item.Key == "thirtyDayRate").Value, 0.001);
            Assert.AreEqual(20, statistics.Single(item => item.Key == "averageGlobalUnlock").Value);
            Assert.AreEqual(2, statistics.Single(item => item.Key == "playedGames").Value);
            Assert.AreEqual(10800, statistics.Single(item => item.Key == "playtime").Value);
            Assert.IsTrue(statistics.All(item => !string.IsNullOrWhiteSpace(item.LabelKey)));
        }

        [TestMethod]
        public void NativePoints_GroupByProviderAndGameAndAddsOtherBucket()
        {
            var snapshot = new OverviewDataSnapshot
            {
                GameSummaries = new List<GameSummaryItem>
                {
                    Game("Alpha", "Steam", "Steam", 100),
                    Game("Beta", "Steam", "Steam", 80),
                    Game("Gamma", "Xbox", "Xbox", 50),
                    Game("No points", "Xbox", "Xbox", 0)
                }
            };
            var instance = new ShowcaseWidgetInstanceSettings
            {
                Kind = ShowcaseWidgetKind.NativePoints
            };
            instance.SetOption("TopN", 1);

            var providers = ShowcaseWidgetProjectionService.BuildNativePoints(snapshot, instance);
            Assert.AreEqual(2, providers.Count);
            Assert.AreEqual("Steam", providers[0].Label);
            Assert.AreEqual(180, providers[0].Value);
            Assert.AreEqual("Other", providers[1].Key);
            Assert.AreEqual(50, providers[1].Value);
            Assert.AreEqual("LOCPlayAch_Showcase_Other", providers[1].LabelKey);

            instance.SetOption("Grouping", ShowcasePointsGrouping.Game);
            var games = ShowcaseWidgetProjectionService.BuildNativePoints(snapshot, instance);
            Assert.AreEqual("Alpha", games[0].Label);
            Assert.AreEqual(130, games[1].Value);
        }

        [TestMethod]
        public void PinsFavoritesAndMosaicPreserveConfiguredIdentityAndOrder()
        {
            var firstGameId = Guid.NewGuid();
            var secondGameId = Guid.NewGuid();
            var firstAchievement = new AchievementDisplayItem
            {
                PlayniteGameId = firstGameId,
                ApiName = "first",
                DisplayName = "First unlock",
                GameName = "First game",
                Unlocked = true,
                Rarity = RarityTier.Common,
                GlobalPercentUnlocked = 55,
                RaritySortValue = AchievementRarityResolver.GetSortValue(
                    55,
                    RarityTier.Common),
                UnlockTimeUtc = new DateTime(2026, 7, 30)
            };
            var rareAchievement = new AchievementDisplayItem
            {
                PlayniteGameId = secondGameId,
                ApiName = "rare",
                DisplayName = "Rare unlock",
                GameName = "Second game",
                Unlocked = true,
                Rarity = RarityTier.UltraRare,
                GlobalPercentUnlocked = 1,
                RaritySortValue = AchievementRarityResolver.GetSortValue(
                    1,
                    RarityTier.UltraRare),
                UnlockTimeUtc = new DateTime(2026, 7, 29)
            };
            var tierOnlyUltraRare = new AchievementDisplayItem
            {
                PlayniteGameId = Guid.NewGuid(),
                ApiName = "tier-only",
                DisplayName = "Tier-only ultra rare unlock",
                GameName = "Third game",
                Unlocked = true,
                Rarity = RarityTier.UltraRare,
                GlobalPercentUnlocked = null,
                RaritySortValue = AchievementRarityResolver.GetSortValue(
                    null,
                    RarityTier.UltraRare),
                UnlockTimeUtc = new DateTime(2026, 7, 28)
            };
            var snapshot = new OverviewDataSnapshot
            {
                Achievements = new List<AchievementDisplayItem>
                {
                    firstAchievement,
                    rareAchievement,
                    tierOnlyUltraRare
                },
                RecentAchievements = new List<AchievementDisplayItem>
                {
                    firstAchievement,
                    rareAchievement
                },
                GameSummaries = new List<GameSummaryItem>
                {
                    new GameSummaryItem
                    {
                        PlayniteGameId = firstGameId,
                        GameName = "First game",
                        IsFavorite = false
                    },
                    new GameSummaryItem
                    {
                        PlayniteGameId = secondGameId,
                        GameName = "Second game",
                        IsFavorite = true
                    }
                }
            };
            var missingGameId = Guid.NewGuid();
            var settings = new ShowcaseSettings
            {
                PinnedGameIds = new List<Guid> { secondGameId, firstGameId },
                PinnedAchievements = new List<PinnedAchievementReference>
                {
                    new PinnedAchievementReference { GameId = firstGameId, ApiName = "first" },
                    new PinnedAchievementReference
                    {
                        GameId = missingGameId,
                        ApiName = "missing",
                        LastKnownGameName = "Removed game",
                        LastKnownAchievementName = "Remembered unlock"
                    }
                }
            };

            var resolvedPins = ShowcaseWidgetProjectionService.ResolvePinnedAchievements(
                snapshot,
                settings.PinnedAchievements);
            Assert.AreSame(firstAchievement, resolvedPins[0].Achievement);
            Assert.IsTrue(resolvedPins[1].IsMissing);
            Assert.AreEqual("Remembered unlock", resolvedPins[1].Name);

            var favoriteInstance = new ShowcaseWidgetInstanceSettings
            {
                Kind = ShowcaseWidgetKind.FavoriteGames
            };
            var pinnedGames = ShowcaseWidgetProjectionService.ResolveFavoriteGames(
                snapshot,
                settings,
                favoriteInstance);
            CollectionAssert.AreEqual(
                new[] { secondGameId, firstGameId },
                pinnedGames.Select(game => game.PlayniteGameId.Value).ToArray());

            favoriteInstance.SetOption("Source", ShowcaseFavoriteGameSource.PlayniteFavorites);
            var liveFavorites = ShowcaseWidgetProjectionService.ResolveFavoriteGames(
                snapshot,
                settings,
                favoriteInstance);
            Assert.AreEqual(secondGameId, liveFavorites.Single().PlayniteGameId);

            var mosaic = new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.IconMosaic };
            mosaic.SetOption("Source", ShowcaseMosaicSource.Rarest);
            var rarest = ShowcaseWidgetProjectionService.ResolveMosaic(snapshot, settings, mosaic);
            CollectionAssert.AreEqual(
                new[] { rareAchievement, tierOnlyUltraRare, firstAchievement },
                rarest.ToArray());
            mosaic.SetOption("Source", ShowcaseMosaicSource.Pinned);
            var pinnedMosaic = ShowcaseWidgetProjectionService.ResolveMosaic(snapshot, settings, mosaic);
            CollectionAssert.AreEqual(new[] { firstAchievement }, pinnedMosaic.ToArray());
        }

        private static GameSummaryItem Game(
            string name,
            string provider,
            string providerKey,
            int points)
        {
            return new GameSummaryItem
            {
                PlayniteGameId = Guid.NewGuid(),
                GameName = name,
                Provider = provider,
                ProviderKey = providerKey,
                ProviderGameKey = name,
                Points = points
            };
        }
    }
}

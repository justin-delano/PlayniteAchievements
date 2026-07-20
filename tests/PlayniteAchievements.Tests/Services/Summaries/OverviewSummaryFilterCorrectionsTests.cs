using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Cache;
using PlayniteAchievements.Services.Summaries;

namespace PlayniteAchievements.Tests.Services.Summaries
{
    [TestClass]
    public class OverviewSummaryFilterCorrectionsTests
    {
        private static readonly Guid FilteredGameId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OtherGameId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [TestMethod]
        public void Apply_RecomputesRowRecentUnlocksAndTimelineFromFilteredProjection()
        {
            var keptUnlockTime = Utc(2026, 5, 1, 12);
            var summaryData = CreateSummaryData(keptUnlockTime);
            var visible = new List<AchievementDetail>
            {
                Achievement("kept_unlocked", RarityTier.Rare, unlocked: true, points: 30, keptUnlockTime),
                Achievement("kept_locked", RarityTier.Common, unlocked: false, points: 10, null)
            };
            var expected = AchievementStatsAccumulator.FromAchievements(visible);

            OverviewSummaryFilterCorrections.Apply(summaryData, FilteredGameId, visible, isCompleted: false);

            var row = summaryData.Games.Single(game => game.PlayniteGameId == FilteredGameId);
            Assert.AreEqual(2, row.TotalAchievements);
            Assert.AreEqual(1, row.UnlockedAchievements);
            Assert.AreEqual(1, row.RareCount);
            Assert.AreEqual(0, row.CommonCount);
            Assert.AreEqual(1, row.TotalRarePossible);
            Assert.AreEqual(1, row.TotalCommonPossible);
            Assert.AreEqual(expected.CollectionScore, row.CollectionScore);
            Assert.AreEqual(expected.PrestigeScore, row.PrestigeScore);
            Assert.AreEqual(expected.CollectionScoreTotal, row.CollectionScoreTotal);
            Assert.AreEqual(expected.PrestigeScoreTotal, row.PrestigeScoreTotal);
            Assert.AreEqual(30, row.Points);
            Assert.IsFalse(row.IsCompleted);

            // The filtered-out recent unlock is dropped; the visible one and the other game's stay.
            var recentApiNames = summaryData.RecentUnlocks.Select(recent => recent.ApiName).ToList();
            CollectionAssert.Contains(recentApiNames, "kept_unlocked");
            CollectionAssert.DoesNotContain(recentApiNames, "filtered_out");
            Assert.AreEqual(1, summaryData.RecentUnlocks.Count(recent => recent.PlayniteGameId == OtherGameId));

            // Timeline: the game's counts are replaced; the global counts keep the other game's
            // contribution on the shared date.
            var gameCounts = summaryData.UnlockCountsByDateByGame[FilteredGameId];
            Assert.AreEqual(1, gameCounts.Count);
            Assert.AreEqual(1, gameCounts[keptUnlockTime.Date]);
            Assert.AreEqual(2, summaryData.GlobalUnlockCountsByDate[keptUnlockTime.Date]);
            Assert.IsFalse(summaryData.GlobalUnlockCountsByDate.ContainsKey(Utc(2026, 4, 1, 0).Date));
        }

        [TestMethod]
        public void Apply_EmptyProjectionRemovesGameEverywhere()
        {
            var keptUnlockTime = Utc(2026, 5, 1, 12);
            var summaryData = CreateSummaryData(keptUnlockTime);

            OverviewSummaryFilterCorrections.Apply(
                summaryData,
                FilteredGameId,
                new List<AchievementDetail>(),
                isCompleted: false);

            Assert.IsFalse(summaryData.Games.Any(game => game.PlayniteGameId == FilteredGameId));
            Assert.IsFalse(summaryData.RecentUnlocks.Any(recent => recent.PlayniteGameId == FilteredGameId));
            Assert.IsFalse(summaryData.UnlockCountsByDateByGame.ContainsKey(FilteredGameId));

            // The other game's summary row, recent unlock, and timeline contribution survive.
            Assert.IsTrue(summaryData.Games.Any(game => game.PlayniteGameId == OtherGameId));
            Assert.AreEqual(1, summaryData.GlobalUnlockCountsByDate[keptUnlockTime.Date]);
        }

        [TestMethod]
        public void Apply_GameWithoutSummaryRowIsIgnored()
        {
            var keptUnlockTime = Utc(2026, 5, 1, 12);
            var summaryData = CreateSummaryData(keptUnlockTime);
            var before = summaryData.Games.Count;

            OverviewSummaryFilterCorrections.Apply(
                summaryData,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                new List<AchievementDetail>(),
                isCompleted: false);

            Assert.AreEqual(before, summaryData.Games.Count);
        }

        [TestMethod]
        public void ReplaceTimelineCounts_SwapsOneGamesContribution()
        {
            var sharedDate = Utc(2026, 5, 1, 0).Date;
            var soloDate = Utc(2026, 4, 1, 0).Date;
            var newDate = Utc(2026, 6, 1, 0).Date;
            var globalCounts = new Dictionary<DateTime, int>
            {
                [sharedDate] = 5,
                [soloDate] = 2
            };
            var countsByGame = new Dictionary<Guid, Dictionary<DateTime, int>>
            {
                [FilteredGameId] = new Dictionary<DateTime, int> { [sharedDate] = 3, [soloDate] = 2 },
                [OtherGameId] = new Dictionary<DateTime, int> { [sharedDate] = 2 }
            };

            OverviewSummaryFilterCorrections.ReplaceTimelineCounts(
                globalCounts,
                countsByGame,
                FilteredGameId,
                new Dictionary<DateTime, int> { [sharedDate] = 1, [newDate] = 4 });

            Assert.AreEqual(3, globalCounts[sharedDate]);
            Assert.IsFalse(globalCounts.ContainsKey(soloDate));
            Assert.AreEqual(4, globalCounts[newDate]);
            Assert.AreEqual(1, countsByGame[FilteredGameId][sharedDate]);
            Assert.AreEqual(4, countsByGame[FilteredGameId][newDate]);
            Assert.AreEqual(2, countsByGame[OtherGameId][sharedDate]);
        }

        [TestMethod]
        public void ReplaceTimelineCounts_NullReplacementRemovesGame()
        {
            var date = Utc(2026, 5, 1, 0).Date;
            var globalCounts = new Dictionary<DateTime, int> { [date] = 3 };
            var countsByGame = new Dictionary<Guid, Dictionary<DateTime, int>>
            {
                [FilteredGameId] = new Dictionary<DateTime, int> { [date] = 3 }
            };

            OverviewSummaryFilterCorrections.ReplaceTimelineCounts(
                globalCounts,
                countsByGame,
                FilteredGameId,
                null);

            Assert.IsFalse(globalCounts.ContainsKey(date));
            Assert.IsFalse(countsByGame.ContainsKey(FilteredGameId));
        }

        // One filtered game with an inflated stored row (three achievements, two unlocked: one
        // on keptUnlockTime.Date, one on 2026-04-01) plus one untouched other game sharing
        // keptUnlockTime.Date.
        private static CachedSummaryData CreateSummaryData(DateTime keptUnlockTime)
        {
            var filteredOutDate = Utc(2026, 4, 1, 0).Date;
            return new CachedSummaryData
            {
                Games = new List<CachedGameSummaryData>
                {
                    new CachedGameSummaryData
                    {
                        PlayniteGameId = FilteredGameId,
                        HasAchievements = true,
                        TotalAchievements = 3,
                        UnlockedAchievements = 2,
                        RareCount = 1,
                        CommonCount = 1,
                        Points = 60,
                        IsCompleted = false
                    },
                    new CachedGameSummaryData
                    {
                        PlayniteGameId = OtherGameId,
                        HasAchievements = true,
                        TotalAchievements = 1,
                        UnlockedAchievements = 1
                    }
                },
                RecentUnlocks = new List<CachedRecentUnlockData>
                {
                    new CachedRecentUnlockData
                    {
                        PlayniteGameId = FilteredGameId,
                        ApiName = "kept_unlocked",
                        UnlockTimeUtc = keptUnlockTime
                    },
                    new CachedRecentUnlockData
                    {
                        PlayniteGameId = FilteredGameId,
                        ApiName = "filtered_out",
                        UnlockTimeUtc = Utc(2026, 4, 1, 9)
                    },
                    new CachedRecentUnlockData
                    {
                        PlayniteGameId = OtherGameId,
                        ApiName = "other_game",
                        UnlockTimeUtc = keptUnlockTime
                    }
                },
                GlobalUnlockCountsByDate = new Dictionary<DateTime, int>
                {
                    [keptUnlockTime.Date] = 2,
                    [filteredOutDate] = 1
                },
                UnlockCountsByDateByGame = new Dictionary<Guid, Dictionary<DateTime, int>>
                {
                    [FilteredGameId] = new Dictionary<DateTime, int>
                    {
                        [keptUnlockTime.Date] = 1,
                        [filteredOutDate] = 1
                    },
                    [OtherGameId] = new Dictionary<DateTime, int>
                    {
                        [keptUnlockTime.Date] = 1
                    }
                }
            };
        }

        private static AchievementDetail Achievement(
            string apiName,
            RarityTier rarity,
            bool unlocked,
            int points,
            DateTime? unlockTimeUtc)
        {
            return new AchievementDetail
            {
                ApiName = apiName,
                DisplayName = apiName,
                Rarity = rarity,
                Unlocked = unlocked,
                Points = points,
                GlobalPercentUnlocked = 50,
                UnlockTimeUtc = unlockTimeUtc
            };
        }

        private static DateTime Utc(int year, int month, int day, int hour)
        {
            return new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);
        }
    }
}

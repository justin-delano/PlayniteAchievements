using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Achievements.Scoring;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Summaries;
using PlayniteAchievements.Services.ThemeIntegration;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.Models
{
    [TestClass]
    [DoNotParallelize]
    public class PlatformScoreTotalsTests
    {
        [TestMethod]
        public void Add_SumsByEffectiveProviderKey()
        {
            var totals = new PlatformScoreTotals();
            totals.Add("Xbox", 1000);
            totals.Add("xenia", 250);
            totals.Add("Epic", 1250);
            totals.Add("RetroAchievements", 400);
            totals.Add("Steam", 9999);
            totals.Add(null, 9999);
            totals.Add("Xbox", -5);

            Assert.AreEqual(1250, totals.Gamerscore);
            Assert.AreEqual(1250, totals.EpicXp);
            Assert.AreEqual(400, totals.RetroPoints);
            Assert.AreEqual(1250, totals.Get(ScoreCardType.Gamerscore));
            Assert.AreEqual(0, totals.Get(ScoreCardType.Collection));
        }

        [TestMethod]
        public void Accumulator_LeavesSoftcoreUnlocksOutOfPlatformScorePoints()
        {
            var stats = AchievementStatsAccumulator.FromAchievements(new[]
            {
                Achievement(10, unlocked: true, categoryType: "Base|Hardcore"),
                Achievement(25, unlocked: true, categoryType: "Base|Softcore"),
                Achievement(5, unlocked: true, categoryType: null),
                Achievement(50, unlocked: false, categoryType: null)
            });

            Assert.AreEqual(40, stats.Points);
            Assert.AreEqual(15, stats.PlatformScorePoints);
        }

        [TestMethod]
        public void LibraryRuntimeState_SumsPlatformScoresAcrossProviders()
        {
            var allData = new List<GameAchievementData>
            {
                Game("Xbox", null, Achievement(100, true, null), Achievement(50, false, null)),
                // Exophase-sourced Xbox 360 game: counts toward Gamerscore through its platform key.
                Game("Exophase", "Xbox", Achievement(30, true, null)),
                Game("Xenia", null, Achievement(20, true, null)),
                Game("Epic", null, Achievement(45, true, null)),
                Game("RetroAchievements", null,
                    Achievement(10, true, "Base|Hardcore"),
                    Achievement(25, true, "Base|Softcore")),
                // Exophase-sourced RetroAchievements game: no unlock mode, so every unlock counts.
                Game("Exophase", "RetroAchievements", Achievement(5, true, null)),
                Game("Steam", null, Achievement(1000, true, null))
            };

            var state = LibraryRuntimeStateBuilder.Build(
                allData,
                api: null,
                token: default,
                includeHeavyAchievementLists: false);

            Assert.AreEqual(150, state.GamerscoreScore);
            Assert.AreEqual(45, state.EpicXpScore);
            Assert.AreEqual(15, state.RetroPointsScore);
            Assert.AreEqual(1, state.GamerscoreLevel);
            Assert.AreEqual("Bronze5", state.GamerscoreRank);
            Assert.AreEqual(0, state.GamerscoreMastery);
        }

        [TestMethod]
        public void OverviewSnapshot_DeltaTotalsSumPlatformScores()
        {
            var games = new List<GameSummaryItem>
            {
                new GameSummaryItem { ProviderKey = "Xbox", PlatformScorePoints = 6000, Points = 6000 },
                new GameSummaryItem { ProviderKey = "Xenia", PlatformScorePoints = 1500, Points = 1500 },
                new GameSummaryItem { ProviderKey = "RetroAchievements", PlatformScorePoints = 400, Points = 900 },
                new GameSummaryItem { ProviderKey = "Steam", PlatformScorePoints = 70, Points = 70 }
            };

            var snapshot = new OverviewDataSnapshot();
            snapshot.ApplyGameSummaryTotals(games, (a, b) => a + b);

            Assert.AreEqual(7500, snapshot.GamerscoreScore);
            Assert.AreEqual("Silver5", snapshot.GamerscoreRank);
            Assert.AreEqual(50, snapshot.GamerscoreLevel);
            Assert.AreEqual(400, snapshot.RetroPointsScore);
            Assert.AreEqual("Bronze4", snapshot.RetroPointsRank);
            Assert.AreEqual(0, snapshot.EpicXpScore);
            Assert.AreEqual(7500, snapshot.GetScore(ScoreCardType.Gamerscore));
        }

        private static GameAchievementData Game(
            string providerKey,
            string providerPlatformKey,
            params AchievementDetail[] achievements)
        {
            var gameId = Guid.NewGuid();
            return new GameAchievementData
            {
                PlayniteGameId = gameId,
                Game = new Game { Id = gameId, Name = providerKey + " game" },
                ProviderKey = providerKey,
                ProviderPlatformKey = providerPlatformKey,
                HasAchievements = true,
                Achievements = new List<AchievementDetail>(achievements)
            };
        }

        private static AchievementDetail Achievement(int points, bool unlocked, string categoryType)
        {
            return new AchievementDetail
            {
                ApiName = Guid.NewGuid().ToString("N"),
                DisplayName = "Achievement",
                Points = points,
                Unlocked = unlocked,
                UnlockTimeUtc = unlocked ? new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc) : (DateTime?)null,
                CategoryType = categoryType
            };
        }
    }
}

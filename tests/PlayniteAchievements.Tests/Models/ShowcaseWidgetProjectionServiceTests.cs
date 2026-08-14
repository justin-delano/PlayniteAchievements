using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
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
            // Timestamp-less/imported unlocks can contribute to TotalUnlocked, but cannot be
            // assigned to an active day. The rate therefore uses the dated timeline numerator.
            Assert.AreEqual(2, statistics.Single(item => item.Key == "activeDayRate").Value);
            Assert.AreEqual(0.2, statistics.Single(item => item.Key == "thirtyDayRate").Value, 0.001);
            Assert.AreEqual(20, statistics.Single(item => item.Key == "averageGlobalUnlock").Value);
            Assert.AreEqual(2, statistics.Single(item => item.Key == "playedGames").Value);
            Assert.AreEqual(10800, statistics.Single(item => item.Key == "playtime").Value);
            Assert.IsTrue(statistics.All(item => !string.IsNullOrWhiteSpace(item.LabelKey)));

            var profile = ShowcaseWidgetProjectionService.Build(
                snapshot,
                new ShowcaseSettings(),
                new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.Profile },
                today);
            Assert.AreEqual(2, profile.Statistics.Single(item =>
                item.Key == "currentStreak").Value);
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
            var timestampLessRare = new AchievementDisplayItem
            {
                PlayniteGameId = Guid.NewGuid(),
                ApiName = "timestamp-less",
                DisplayName = "Imported rare unlock",
                GameName = "Fourth game",
                Unlocked = true,
                Rarity = RarityTier.UltraRare,
                GlobalPercentUnlocked = 0.5,
                RaritySortValue = AchievementRarityResolver.GetSortValue(
                    0.5,
                    RarityTier.UltraRare),
                UnlockTimeUtc = null
            };
            var lockedRare = new AchievementDisplayItem
            {
                PlayniteGameId = Guid.NewGuid(),
                ApiName = "locked",
                Unlocked = false,
                Rarity = RarityTier.UltraRare,
                GlobalPercentUnlocked = 0.1,
                RaritySortValue = AchievementRarityResolver.GetSortValue(
                    0.1,
                    RarityTier.UltraRare)
            };
            var snapshot = new OverviewDataSnapshot
            {
                Achievements = new List<AchievementDisplayItem>
                {
                    firstAchievement,
                    rareAchievement,
                    tierOnlyUltraRare,
                    timestampLessRare,
                    lockedRare
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
                    },
                    new PinnedAchievementReference
                    {
                        GameId = lockedRare.PlayniteGameId.Value,
                        ApiName = lockedRare.ApiName
                    }
                }
            };

            var resolvedPins = ShowcaseWidgetProjectionService.ResolvePinnedAchievements(
                snapshot,
                settings.PinnedAchievements);
            Assert.AreSame(firstAchievement, resolvedPins[0].Achievement);
            Assert.IsTrue(resolvedPins[1].IsMissing);
            Assert.AreEqual("Remembered unlock", resolvedPins[1].Name);
            Assert.AreSame(lockedRare, resolvedPins[2].Achievement);

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
                new[]
                {
                    timestampLessRare,
                    rareAchievement,
                    tierOnlyUltraRare,
                    firstAchievement
                },
                rarest.ToArray());
            mosaic.SetOption("Source", ShowcaseMosaicSource.Pinned);
            var pinnedMosaic = ShowcaseWidgetProjectionService.ResolveMosaic(snapshot, settings, mosaic);
            CollectionAssert.AreEqual(new[] { firstAchievement, lockedRare }, pinnedMosaic.ToArray());
        }

        [TestMethod]
        public void ActivityCalendar_DensifiesSundayAlignedTrailingYear()
        {
            var endDate = new DateTime(2026, 7, 31); // a Friday
            var snapshot = new OverviewDataSnapshot
            {
                GlobalUnlockCountsByDate = new Dictionary<DateTime, int>
                {
                    [endDate] = 2,
                    [endDate.AddHours(-30)] = 3,           // same day as endDate-2 once dated
                    [endDate.AddDays(-1).AddHours(6)] = 1, // duplicate-day key collapses
                    [endDate.AddDays(-1)] = 4,
                    [endDate.AddDays(-500)] = 99,          // outside the window
                    [endDate.AddDays(-3)] = -7             // negative clamps to zero
                }
            };

            var calendar = ShowcaseWidgetProjectionService.BuildActivityCalendar(
                snapshot,
                CalendarInstance(TimelineRange.OneYear),
                endDate);

            Assert.AreEqual(DayOfWeek.Sunday, calendar.StartDate.DayOfWeek);
            Assert.IsTrue(calendar.StartDate <= endDate.AddDays(-364));
            Assert.IsTrue(calendar.StartDate > endDate.AddDays(-364 - 7));
            Assert.AreEqual(endDate, calendar.EndDate);
            Assert.AreEqual((endDate - calendar.StartDate).Days + 1, calendar.Days.Count);
            Assert.AreEqual(calendar.StartDate, calendar.Days[0].Date);
            Assert.AreEqual(endDate, calendar.Days[calendar.Days.Count - 1].Date);

            var byDate = calendar.Days.ToDictionary(day => day.Date);
            Assert.AreEqual(5, byDate[endDate.AddDays(-1)].Count);
            Assert.AreEqual(2, byDate[endDate].Count);
            Assert.AreEqual(0, byDate[endDate.AddDays(-3)].Count);
            Assert.AreEqual(0, byDate[endDate.AddDays(-10)].Count);
            Assert.AreEqual(5, calendar.MaxCount);
            Assert.AreEqual(10, calendar.TotalCount);
            Assert.AreEqual(3, calendar.ActiveDayCount);
        }

        [TestMethod]
        public void ActivityCalendar_BucketsIntensityAtMaxRelativeQuartiles()
        {
            var endDate = new DateTime(2026, 7, 31);
            var snapshot = new OverviewDataSnapshot
            {
                GlobalUnlockCountsByDate = new Dictionary<DateTime, int>
                {
                    [endDate] = 8,
                    [endDate.AddDays(-1)] = 1,
                    [endDate.AddDays(-2)] = 2,
                    [endDate.AddDays(-3)] = 3,
                    [endDate.AddDays(-4)] = 5,
                    [endDate.AddDays(-5)] = 7
                }
            };

            var byDate = ShowcaseWidgetProjectionService.BuildActivityCalendar(
                    snapshot,
                    CalendarInstance(TimelineRange.OneYear),
                    endDate)
                .Days.ToDictionary(day => day.Date);

            Assert.AreEqual(4, byDate[endDate].Intensity);
            Assert.AreEqual(1, byDate[endDate.AddDays(-1)].Intensity);
            Assert.AreEqual(1, byDate[endDate.AddDays(-2)].Intensity);
            Assert.AreEqual(2, byDate[endDate.AddDays(-3)].Intensity);
            Assert.AreEqual(3, byDate[endDate.AddDays(-4)].Intensity);
            Assert.AreEqual(4, byDate[endDate.AddDays(-5)].Intensity);
            Assert.AreEqual(0, byDate[endDate.AddDays(-6)].Intensity);

            var single = ShowcaseWidgetProjectionService.BuildActivityCalendar(
                new OverviewDataSnapshot
                {
                    GlobalUnlockCountsByDate = new Dictionary<DateTime, int> { [endDate] = 1 }
                },
                CalendarInstance(TimelineRange.OneYear),
                endDate);
            Assert.AreEqual(4, single.Days.Last().Intensity);

            var built = ShowcaseWidgetProjectionService.Build(
                snapshot,
                new ShowcaseSettings(),
                CalendarInstance(TimelineRange.OneYear),
                endDate);
            Assert.AreEqual(byDate.Count, built.ActivityCalendar.Days.Count);

            // A shorter range narrows the window while keeping the Sunday alignment.
            var quarter = ShowcaseWidgetProjectionService.BuildActivityCalendar(
                snapshot,
                CalendarInstance(TimelineRange.ThreeMonths),
                endDate);
            Assert.AreEqual(DayOfWeek.Sunday, quarter.StartDate.DayOfWeek);
            Assert.IsTrue(quarter.Days.Count < 120);
        }

        private static ShowcaseWidgetInstanceSettings CalendarInstance(TimelineRange range)
        {
            var instance = new ShowcaseWidgetInstanceSettings
            {
                Kind = ShowcaseWidgetKind.ActivityCalendar
            };
            ShowcaseTimelineOptions.SetRange(instance, range);
            return instance;
        }

        [TestMethod]
        public void ScoreHistory_AccumulatesPerAchievementScoresWithUndatedBaseline()
        {
            var endDate = new DateTime(2026, 7, 31);
            var dated = new AchievementDisplayItem
            {
                Unlocked = true,
                Rarity = RarityTier.UltraRare,
                GlobalPercentUnlocked = 1,
                UnlockTimeUtc = endDate.AddDays(-2)
            };
            var datedSameDay = new AchievementDisplayItem
            {
                Unlocked = true,
                Rarity = RarityTier.Common,
                GlobalPercentUnlocked = 60,
                UnlockTimeUtc = endDate.AddDays(-2).AddHours(5)
            };
            var undated = new AchievementDisplayItem
            {
                Unlocked = true,
                Rarity = RarityTier.Rare,
                GlobalPercentUnlocked = 10,
                UnlockTimeUtc = null
            };
            var locked = new AchievementDisplayItem
            {
                Unlocked = false,
                Rarity = RarityTier.UltraRare,
                GlobalPercentUnlocked = 0.2
            };
            var snapshot = new OverviewDataSnapshot
            {
                Achievements = new List<AchievementDisplayItem> { dated, datedSameDay, undated, locked }
            };
            var instance = new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.Scores };

            var points = ShowcaseWidgetProjectionService.BuildScoreHistory(snapshot, instance, endDate);

            Assert.IsTrue(points.Count >= 2);
            var expectedCollection = dated.CollectionScore + datedSameDay.CollectionScore + undated.CollectionScore;
            var expectedPrestige = dated.PrestigeScore + datedSameDay.PrestigeScore + undated.PrestigeScore;
            var last = points[points.Count - 1];
            Assert.AreEqual(endDate, last.Date);
            Assert.AreEqual(expectedCollection, last.CollectionScore);
            Assert.AreEqual(expectedPrestige, last.PrestigeScore);
            // The undated baseline is present from the very first point.
            Assert.AreEqual(undated.CollectionScore, points[0].CollectionScore);
            // Gap days carry the cumulative value forward.
            var afterUnlock = points.First(point => point.Date >= endDate.AddDays(-1));
            Assert.AreEqual(expectedCollection, afterUnlock.CollectionScore);
        }

        [TestMethod]
        public void ScoreHistory_DownsamplesAllTimeToBoundedPointCount()
        {
            var endDate = new DateTime(2026, 7, 31);
            var achievements = new List<AchievementDisplayItem>();
            for (var i = 0; i < 60; i++)
            {
                achievements.Add(new AchievementDisplayItem
                {
                    Unlocked = true,
                    Rarity = RarityTier.Common,
                    GlobalPercentUnlocked = 70,
                    UnlockTimeUtc = endDate.AddDays(-i * 30) // spans ~5 years
                });
            }

            var snapshot = new OverviewDataSnapshot { Achievements = achievements };
            var instance = new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.Scores };
            instance.SetOption("TimelineRange", TimelineRange.All);

            var points = ShowcaseWidgetProjectionService.BuildScoreHistory(snapshot, instance, endDate);

            Assert.IsTrue(points.Count <= 130, $"Expected bounded point count, got {points.Count}");
            Assert.AreEqual(endDate, points[points.Count - 1].Date);
            Assert.AreEqual(
                achievements.Sum(item => item.CollectionScore),
                points[points.Count - 1].CollectionScore);
            for (var i = 1; i < points.Count; i++)
            {
                Assert.IsTrue(points[i].CollectionScore >= points[i - 1].CollectionScore);
            }

            Assert.AreEqual(
                0,
                ShowcaseWidgetProjectionService.BuildScoreHistory(
                    new OverviewDataSnapshot(),
                    instance,
                    endDate).Count);

            var built = ShowcaseWidgetProjectionService.Build(
                snapshot,
                new ShowcaseSettings(),
                instance,
                endDate);
            Assert.AreEqual(points.Count, built.ScoreHistory.Count);
        }

        [TestMethod]
        public void RecentAchievements_ProjectUncappedAndResolvePerInstanceGridOptions()
        {
            var items = Enumerable.Range(0, 30)
                .Select(i => new AchievementDisplayItem
                {
                    ApiName = $"a{i}",
                    Unlocked = true,
                    UnlockTimeUtc = new DateTime(2026, 7, 1).AddDays(-i)
                })
                .ToList();
            var snapshot = new OverviewDataSnapshot { RecentAchievements = items };
            var instance = new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.RecentAchievements };

            // The resolver hands back the full snapshot order uncapped; the widget view model
            // applies the MaxRows cap after its control-bar search filter.
            var rows = ShowcaseWidgetProjectionService.ResolveRecentAchievements(snapshot);
            Assert.AreEqual(items.Count, rows.Count);
            CollectionAssert.AreEqual(items, rows.ToList());

            var catalog = new GridOptionsCatalog();
            var surfaceKey = ShowcaseGridSurfaces.ForInstance(
                ShowcaseGridSurfaces.RecentAchievements,
                instance.InstanceId);
            var built = ShowcaseWidgetProjectionService.Build(
                snapshot,
                new ShowcaseSettings(),
                instance,
                gridOptions: catalog);
            Assert.AreEqual(items.Count, built.AchievementRows.Count);
            Assert.AreSame(catalog.GetAchievement(surfaceKey), built.GridWidgetOptions);
        }

        [TestMethod]
        public void GameSummaries_SortModesHideCompletedAndUncappedRows()
        {
            var oldest = new GameSummaryItem
            {
                PlayniteGameId = Guid.NewGuid(),
                GameName = "Alpha",
                UnlockedAchievements = 4,
                TotalAchievements = 10,
                PlaytimeSeconds = 50,
                LastUnlockUtc = new DateTime(2026, 1, 1)
            };
            var newest = new GameSummaryItem
            {
                PlayniteGameId = Guid.NewGuid(),
                GameName = "Beta",
                UnlockedAchievements = 9,
                TotalAchievements = 10,
                PlaytimeSeconds = 500,
                LastUnlockUtc = new DateTime(2026, 7, 1)
            };
            var completed = new GameSummaryItem
            {
                PlayniteGameId = Guid.NewGuid(),
                GameName = "Gamma",
                UnlockedAchievements = 10,
                TotalAchievements = 10,
                IsCompleted = true,
                PlaytimeSeconds = 5,
                LastUnlockUtc = new DateTime(2026, 6, 1)
            };
            var neverUnlocked = new GameSummaryItem
            {
                PlayniteGameId = Guid.NewGuid(),
                GameName = "Delta",
                TotalAchievements = 10,
                PlaytimeSeconds = 5000
            };
            var snapshot = new OverviewDataSnapshot
            {
                GameSummaries = new List<GameSummaryItem> { oldest, newest, completed, neverUnlocked }
            };
            var instance = new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.GameSummaries };
            var options = new GameSummaryGridOptions();

            var byLastUnlock = ShowcaseWidgetProjectionService.ResolveGameSummaries(snapshot, instance, options);
            CollectionAssert.AreEqual(
                new[] { newest, completed, oldest, neverUnlocked },
                byLastUnlock.ToArray());

            options.SortMode = GameSummariesSortMode.Progress;
            Assert.AreSame(completed,
                ShowcaseWidgetProjectionService.ResolveGameSummaries(snapshot, instance, options)[0]);

            options.SortMode = GameSummariesSortMode.Alphabetical;
            options.SortDescending = false;
            Assert.AreSame(oldest,
                ShowcaseWidgetProjectionService.ResolveGameSummaries(snapshot, instance, options)[0]);

            options.SortMode = GameSummariesSortMode.RecentUnlock;
            options.SortDescending = true;
            instance.SetOption("HideCompleted", true);
            var withoutCompleted = ShowcaseWidgetProjectionService.ResolveGameSummaries(snapshot, instance, options);
            Assert.IsFalse(withoutCompleted.Contains(completed));

            // MaxRows does not cap here: the widget view model applies it after its
            // control-bar filters so searching reaches rows beyond the cap.
            instance.SetOption("HideCompleted", false);
            options.MaxRows = 2;
            Assert.AreEqual(4, ShowcaseWidgetProjectionService.ResolveGameSummaries(snapshot, instance, options).Count);

            Assert.AreEqual(
                4,
                ShowcaseWidgetProjectionService.ResolveGameSummaries(snapshot, instance, null).Count);
        }

        [TestMethod]
        public void GameMosaic_SourcesFilterAndOrder()
        {
            var pinnedFirst = Guid.NewGuid();
            var pinnedSecond = Guid.NewGuid();
            var completedOld = new GameSummaryItem
            {
                PlayniteGameId = pinnedSecond,
                GameName = "Old completed",
                IsCompleted = true,
                LastUnlockUtc = new DateTime(2026, 1, 1)
            };
            var completedNew = new GameSummaryItem
            {
                PlayniteGameId = Guid.NewGuid(),
                GameName = "New completed",
                IsCompleted = true,
                LastUnlockUtc = new DateTime(2026, 7, 1)
            };
            var favorite = new GameSummaryItem
            {
                PlayniteGameId = pinnedFirst,
                GameName = "A favorite",
                IsFavorite = true,
                LastUnlockUtc = new DateTime(2026, 3, 1)
            };
            var snapshot = new OverviewDataSnapshot
            {
                GameSummaries = new List<GameSummaryItem> { completedOld, completedNew, favorite }
            };
            var settings = new ShowcaseSettings
            {
                PinnedGameIds = new List<Guid> { pinnedFirst, pinnedSecond }
            };
            var instance = new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.GameMosaic };

            var completed = ShowcaseWidgetProjectionService.ResolveGameMosaic(snapshot, settings, instance);
            CollectionAssert.AreEqual(new[] { completedNew, completedOld }, completed.ToArray());

            instance.SetOption("Source", ShowcaseGameMosaicSource.All);
            var all = ShowcaseWidgetProjectionService.ResolveGameMosaic(snapshot, settings, instance);
            Assert.AreEqual(3, all.Count);
            Assert.AreSame(completedNew, all[0]);

            instance.SetOption("Source", ShowcaseGameMosaicSource.Pinned);
            CollectionAssert.AreEqual(
                new[] { favorite, completedOld },
                ShowcaseWidgetProjectionService.ResolveGameMosaic(snapshot, settings, instance).ToArray());

            instance.SetOption("Source", ShowcaseGameMosaicSource.PlayniteFavorites);
            Assert.AreSame(favorite,
                ShowcaseWidgetProjectionService.ResolveGameMosaic(snapshot, settings, instance).Single());

            instance.SetOption("Source", ShowcaseGameMosaicSource.All);
            instance.SetOption("Count", 2);
            Assert.AreEqual(2,
                ShowcaseWidgetProjectionService.ResolveGameMosaic(snapshot, settings, instance).Count);
        }

        [TestMethod]
        public void PinRows_MaterializeMissingPinsAsPlaceholders()
        {
            var gameId = Guid.NewGuid();
            var missingGameId = Guid.NewGuid();
            var resolved = new AchievementDisplayItem
            {
                PlayniteGameId = gameId,
                ApiName = "real",
                DisplayName = "Real unlock",
                GameName = "Real game",
                Unlocked = true
            };
            var snapshot = new OverviewDataSnapshot
            {
                Achievements = new List<AchievementDisplayItem> { resolved }
            };
            var pins = new List<PinnedAchievementReference>
            {
                new PinnedAchievementReference { GameId = gameId, ApiName = "real" },
                new PinnedAchievementReference
                {
                    GameId = missingGameId,
                    ApiName = "gone",
                    LastKnownGameName = "Removed game",
                    LastKnownAchievementName = "Remembered unlock"
                }
            };

            var rows = ShowcaseWidgetProjectionService.MaterializePinRows(
                ShowcaseWidgetProjectionService.ResolvePinnedAchievements(snapshot, pins));

            Assert.AreEqual(2, rows.Count);
            Assert.AreSame(resolved, rows[0]);
            Assert.AreEqual(missingGameId, rows[1].PlayniteGameId);
            Assert.AreEqual("gone", rows[1].ApiName);
            Assert.AreEqual("Remembered unlock", rows[1].DisplayName);
            Assert.AreEqual("Removed game", rows[1].GameName);
            Assert.IsFalse(rows[1].Unlocked);

            var built = ShowcaseWidgetProjectionService.Build(
                snapshot,
                new ShowcaseSettings { PinnedAchievements = pins },
                new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.PinnedAchievements });
            Assert.AreEqual(2, built.AchievementRows.Count);
        }

        [TestMethod]
        public void Profile_ResolvedProfilePrefersProviderIdentityWithManualOverride()
        {
            var snapshot = new OverviewDataSnapshot
            {
                CurrentUserIdentities = new List<PlayniteAchievements.Models.Friends.FriendIdentity>
                {
                    new PlayniteAchievements.Models.Friends.FriendIdentity
                    {
                        ProviderKey = "Steam",
                        DisplayName = "SteamName",
                        AvatarPath = @"C:\avatar.png"
                    }
                }
            };
            var settings = new ShowcaseSettings
            {
                Profile = new ShowcaseProfileSettings { Subtitle = "Completionist" }
            };

            var built = ShowcaseWidgetProjectionService.Build(
                snapshot,
                settings,
                new ShowcaseWidgetInstanceSettings { Kind = ShowcaseWidgetKind.Profile });

            Assert.AreEqual("SteamName", built.ResolvedProfile.DisplayName);
            Assert.AreEqual(@"C:\avatar.png", built.ResolvedProfile.AvatarPath);
            Assert.AreEqual("Completionist", built.ResolvedProfile.Subtitle);
            Assert.IsTrue(built.ResolvedProfile.FromProviderIdentity);
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

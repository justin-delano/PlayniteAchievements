using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Services.Showcase
{
    public sealed class ShowcaseStatistic
    {
        public string Key { get; set; }

        public string LabelKey { get; set; }

        public double Value { get; set; }

        public bool HasValue { get; set; } = true;
    }

    public sealed class ShowcaseChartEntry
    {
        public string Key { get; set; }

        public string LabelKey { get; set; }

        public string Label { get; set; }

        public double Value { get; set; }

        public string SecondaryText { get; set; }
    }

    public sealed class ShowcaseActivityDay
    {
        public DateTime Date { get; set; }

        public int Count { get; set; }

        /// <summary>0 = no unlocks; 1..4 = quartile of the window's busiest day.</summary>
        public int Intensity { get; set; }
    }

    public sealed class ShowcaseActivityCalendar
    {
        /// <summary>Contiguous ascending days; the first entry is a Sunday and the last is the end date.</summary>
        public IReadOnlyList<ShowcaseActivityDay> Days { get; set; } =
            Array.Empty<ShowcaseActivityDay>();

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public int MaxCount { get; set; }

        public int TotalCount { get; set; }

        public int ActiveDayCount { get; set; }
    }

    public sealed class ShowcaseScorePoint
    {
        public DateTime Date { get; set; }

        /// <summary>Cumulative collection score as of the end of <see cref="Date"/>.</summary>
        public int CollectionScore { get; set; }

        /// <summary>Cumulative prestige score as of the end of <see cref="Date"/>.</summary>
        public int PrestigeScore { get; set; }
    }

    public sealed class ShowcaseAchievementItem
    {
        public PinnedAchievementReference Pin { get; set; }

        public AchievementDisplayItem Achievement { get; set; }

        public bool IsMissing => Achievement == null;

        public string Name => Achievement?.DisplayName ??
            Pin?.LastKnownAchievementName ??
            Pin?.ApiName ??
            string.Empty;

        public string GameName => Achievement?.GameName ??
            Pin?.LastKnownGameName ??
            string.Empty;

        public string IconPath => Achievement?.DisplayIcon;
    }

    public sealed class ShowcaseWidgetProjection
    {
        public ShowcaseWidgetInstanceSettings Instance { get; set; }

        public OverviewDataSnapshot Snapshot { get; set; }

        public IReadOnlyList<ShowcaseStatistic> Statistics { get; set; } =
            Array.Empty<ShowcaseStatistic>();

        public IReadOnlyList<ShowcaseChartEntry> ChartEntries { get; set; } =
            Array.Empty<ShowcaseChartEntry>();

        public IReadOnlyList<ShowcaseAchievementItem> Achievements { get; set; } =
            Array.Empty<ShowcaseAchievementItem>();

        public IReadOnlyList<AchievementDisplayItem> MosaicAchievements { get; set; } =
            Array.Empty<AchievementDisplayItem>();

        public IReadOnlyList<GameSummaryItem> Games { get; set; } =
            Array.Empty<GameSummaryItem>();

        public IReadOnlyDictionary<DateTime, int> Timeline { get; set; } =
            new Dictionary<DateTime, int>();

        public ShowcaseProfileSettings Profile { get; set; }

        /// <summary>Grid-ready achievement rows (pinned or recent, per widget kind).</summary>
        public IReadOnlyList<AchievementDisplayItem> AchievementRows { get; set; } =
            Array.Empty<AchievementDisplayItem>();

        public ShowcaseActivityCalendar ActivityCalendar { get; set; } =
            new ShowcaseActivityCalendar();

        public IReadOnlyList<ShowcaseScorePoint> ScoreHistory { get; set; } =
            Array.Empty<ShowcaseScorePoint>();

        public ShowcaseProfileProjection ResolvedProfile { get; set; }
    }

    public static class ShowcaseWidgetProjectionService
    {
        public static ShowcaseWidgetProjection Build(
            OverviewDataSnapshot snapshot,
            ShowcaseSettings settings,
            ShowcaseWidgetInstanceSettings instance,
            DateTime? now = null)
        {
            snapshot = snapshot ?? new OverviewDataSnapshot();
            settings = settings ?? new ShowcaseSettings();
            instance = instance ?? new ShowcaseWidgetInstanceSettings();

            var result = new ShowcaseWidgetProjection
            {
                Instance = instance,
                Snapshot = snapshot,
                Profile = settings.Profile ?? new ShowcaseProfileSettings()
            };

            switch (instance.Kind)
            {
                case ShowcaseWidgetKind.Profile:
                    result.Statistics = BuildStatistics(snapshot, now ?? DateTime.Now);
                    result.ResolvedProfile = ShowcaseProfileResolver.Resolve(
                        settings.Profile,
                        snapshot.CurrentUserIdentities);
                    break;
                case ShowcaseWidgetKind.Statistics:
                    result.Statistics = BuildStatistics(snapshot, now ?? DateTime.Now);
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    result.ChartEntries = BuildNativePoints(snapshot, instance);
                    break;
                case ShowcaseWidgetKind.PinnedAchievements:
                    result.Achievements = ResolvePinnedAchievements(snapshot, settings.PinnedAchievements);
                    result.AchievementRows = MaterializePinRows(result.Achievements);
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    result.Games = ResolveFavoriteGames(snapshot, settings, instance);
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    result.MosaicAchievements = ResolveMosaic(snapshot, settings, instance);
                    break;
                case ShowcaseWidgetKind.RecentAchievements:
                    result.AchievementRows = ResolveRecentAchievements(snapshot, instance);
                    break;
                case ShowcaseWidgetKind.GameSummaries:
                    result.Games = ResolveGameSummaries(snapshot, instance);
                    break;
                case ShowcaseWidgetKind.GameMosaic:
                    result.Games = ResolveGameMosaic(snapshot, settings, instance);
                    break;
                case ShowcaseWidgetKind.ActivityCalendar:
                    result.ActivityCalendar = BuildActivityCalendar(snapshot, instance, (now ?? DateTime.Now).Date);
                    break;
                case ShowcaseWidgetKind.Scores:
                    result.ScoreHistory = BuildScoreHistory(snapshot, instance, (now ?? DateTime.Now).Date);
                    break;
                case ShowcaseWidgetKind.Timeline:
                    var endDate = (now ?? DateTime.Now).Date;
                    var sourceCounts = (snapshot.GlobalUnlockCountsByDate ??
                            new Dictionary<DateTime, int>())
                        .GroupBy(pair => pair.Key.Date)
                        .ToDictionary(group => group.Key, group => group.Sum(pair => Math.Max(0, pair.Value)));
                    var range = ShowcaseTimelineOptions.GetRange(instance);
                    var minimumDate = GetTimelineStartDate(range, endDate, sourceCounts);
                    var rangeDays = Math.Max(1, (endDate - minimumDate).Days + 1);
                    result.Timeline = Enumerable.Range(0, rangeDays)
                        .Select(offset => minimumDate.AddDays(offset))
                        .ToDictionary(
                            date => date,
                            date => sourceCounts.TryGetValue(date, out var count) ? count : 0);
                    break;
            }

            return result;
        }

        private static DateTime GetTimelineStartDate(
            TimelineRange range,
            DateTime endDate,
            IReadOnlyDictionary<DateTime, int> counts)
        {
            switch (range)
            {
                case TimelineRange.SevenDays:
                    return endDate.AddDays(-6);
                case TimelineRange.FourteenDays:
                    return endDate.AddDays(-13);
                case TimelineRange.OneMonth:
                    return endDate.AddMonths(-1).AddDays(1);
                case TimelineRange.ThreeMonths:
                    return endDate.AddMonths(-3).AddDays(1);
                case TimelineRange.OneYear:
                    return endDate.AddYears(-1).AddDays(1);
                case TimelineRange.All:
                    return counts != null && counts.Count > 0
                        ? counts.Keys.Min().Date
                        : endDate;
                default:
                    return endDate.AddMonths(-3).AddDays(1);
            }
        }

        public static IReadOnlyList<ShowcaseStatistic> BuildStatistics(
            OverviewDataSnapshot snapshot,
            DateTime now)
        {
            snapshot = snapshot ?? new OverviewDataSnapshot();
            var summaries = snapshot.GameSummaries ?? new List<GameSummaryItem>();
            var achievements = snapshot.Achievements ?? new List<AchievementDisplayItem>();
            var counts = snapshot.GlobalUnlockCountsByDate ?? new Dictionary<DateTime, int>();
            var activeDays = counts.Where(pair => pair.Value > 0).Select(pair => pair.Key.Date).Distinct().Count();
            var datedUnlocks = counts.Sum(pair => Math.Max(0, pair.Value));
            var lastThirtyDays = counts
                .Where(pair => pair.Key.Date >= now.Date.AddDays(-29) && pair.Key.Date <= now.Date)
                .Sum(pair => Math.Max(0, pair.Value));
            var unlockedWithRarity = achievements
                .Where(item => item?.Unlocked == true && item.GlobalPercentUnlocked.HasValue)
                .Select(item => item.GlobalPercentUnlocked.Value)
                .ToList();
            CalculateStreaks(counts, now.Date, out var currentStreak, out var longestStreak);

            var playedGames = summaries.Count(game =>
                game != null && (game.PlaytimeSeconds > 0 || game.LastPlayed.HasValue));
            var totalPlaytime = summaries.Where(game => game != null).Sum(game => (double)game.PlaytimeSeconds);

            return new List<ShowcaseStatistic>
            {
                Stat("unlocked", "LOCPlayAch_Showcase_Stat_Unlocked", snapshot.TotalUnlocked),
                Stat("completion", "LOCPlayAch_Showcase_Stat_Completion", snapshot.GlobalProgressionPercent),
                Stat("trackedGames", "LOCPlayAch_Showcase_Stat_TrackedGames", snapshot.TotalGames),
                Stat("playedGames", "LOCPlayAch_Showcase_Stat_PlayedGames", playedGames),
                Stat("completedGames", "LOCPlayAch_Showcase_Stat_CompletedGames", snapshot.CompletedGames),
                Stat("playtime", "LOCPlayAch_Showcase_Stat_Playtime", totalPlaytime),
                Stat(
                    "activeDayRate",
                    "LOCPlayAch_Showcase_Stat_ActiveDayRate",
                    activeDays > 0 ? (double)datedUnlocks / activeDays : 0),
                Stat("thirtyDayRate", "LOCPlayAch_Showcase_Stat_ThirtyDayRate", lastThirtyDays / 30d),
                Stat(
                    "averageGlobalUnlock",
                    "LOCPlayAch_Showcase_Stat_AverageGlobalUnlock",
                    unlockedWithRarity.Count > 0 ? unlockedWithRarity.Average() : 0,
                    hasValue: unlockedWithRarity.Count > 0),
                Stat("currentStreak", "LOCPlayAch_Showcase_Stat_CurrentStreak", currentStreak),
                Stat("longestStreak", "LOCPlayAch_Showcase_Stat_LongestStreak", longestStreak)
            };
        }

        public static IReadOnlyList<ShowcaseChartEntry> BuildNativePoints(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance)
        {
            var summaries = snapshot?.GameSummaries ?? new List<GameSummaryItem>();
            var grouping = ShowcaseWidgetOptions.GetPointsGrouping(instance);
            var topN = ShowcaseWidgetOptions.GetTopN(instance);

            IEnumerable<ShowcaseChartEntry> entries;
            if (grouping == ShowcasePointsGrouping.Game)
            {
                entries = summaries
                    .Where(game => game != null && game.Points > 0)
                    .Select(game => new ShowcaseChartEntry
                    {
                        Key = game.PlayniteGameId?.ToString("D") ??
                            $"{game.ProviderKey}:{game.ProviderGameKey}",
                        Label = game.GameName,
                        Value = game.Points,
                        SecondaryText = game.Provider
                    });
            }
            else
            {
                entries = summaries
                    .Where(game => game != null && game.Points > 0)
                    .GroupBy(
                        game => string.IsNullOrWhiteSpace(game.ProviderKey)
                            ? "Unknown"
                            : game.ProviderKey,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => new ShowcaseChartEntry
                    {
                        Key = group.Key,
                        LabelKey = string.Equals(
                            group.Key,
                            "Unknown",
                            StringComparison.OrdinalIgnoreCase)
                            ? "LOCPlayAch_Showcase_UnknownProvider"
                            : null,
                        Label = group.Select(game => game.Provider)
                            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label)) ?? group.Key,
                        Value = group.Sum(game => (double)game.Points)
                    });
            }

            return ApplyTopN(entries, topN);
        }

        public static IReadOnlyList<ShowcaseAchievementItem> ResolvePinnedAchievements(
            OverviewDataSnapshot snapshot,
            IEnumerable<PinnedAchievementReference> pins)
        {
            var achievements = snapshot?.Achievements ?? new List<AchievementDisplayItem>();

            // Index by (game id, case-insensitive api name) so each pin resolves in O(1)
            // instead of scanning every achievement. The first entry wins on a key collision,
            // mirroring the prior FirstOrDefault; achievements without a Playnite game id cannot
            // match a pinned reference and are skipped.
            var lookup = new Dictionary<(Guid, string), AchievementDisplayItem>();
            foreach (var item in achievements)
            {
                if (item?.PlayniteGameId == null)
                {
                    continue;
                }

                var key = (item.PlayniteGameId.Value, item.ApiName?.ToUpperInvariant());
                if (!lookup.ContainsKey(key))
                {
                    lookup[key] = item;
                }
            }

            return (pins ?? Array.Empty<PinnedAchievementReference>())
                .Where(pin => pin != null)
                .Select(pin =>
                {
                    lookup.TryGetValue((pin.GameId, pin.ApiName?.ToUpperInvariant()), out var match);
                    return new ShowcaseAchievementItem { Pin = pin, Achievement = match };
                })
                .ToList();
        }

        public static IReadOnlyList<GameSummaryItem> ResolveFavoriteGames(
            OverviewDataSnapshot snapshot,
            ShowcaseSettings settings,
            ShowcaseWidgetInstanceSettings instance)
        {
            var summaries = snapshot?.GameSummaries ?? new List<GameSummaryItem>();
            var source = ShowcaseWidgetOptions.GetFavoriteSource(instance);
            if (source == ShowcaseFavoriteGameSource.PlayniteFavorites)
            {
                return summaries
                    .Where(game => game?.IsFavorite == true)
                    .OrderBy(game => game.GameName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            return ResolvePinnedGameSummaries(summaries, settings);
        }

        /// <summary>Pinned games in pin order; unknown ids are skipped.</summary>
        private static IReadOnlyList<GameSummaryItem> ResolvePinnedGameSummaries(
            IReadOnlyList<GameSummaryItem> summaries,
            ShowcaseSettings settings)
        {
            var byId = (summaries ?? Array.Empty<GameSummaryItem>())
                .Where(game => game?.PlayniteGameId.HasValue == true)
                .GroupBy(game => game.PlayniteGameId.Value)
                .ToDictionary(group => group.Key, group => group.First());
            return (settings?.PinnedGameIds ?? new List<Guid>())
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
        }

        /// <summary>
        /// Grid-ready rows for the pinned achievements widget. Missing pins become
        /// placeholder rows that keep the pin identity so the row menu's unpin and
        /// reorder actions still resolve them.
        /// </summary>
        public static IReadOnlyList<AchievementDisplayItem> MaterializePinRows(
            IEnumerable<ShowcaseAchievementItem> items)
        {
            return (items ?? Array.Empty<ShowcaseAchievementItem>())
                .Where(item => item != null)
                .Select(item => item.IsMissing
                    ? new AchievementDisplayItem
                    {
                        PlayniteGameId = item.Pin?.GameId,
                        ApiName = item.Pin?.ApiName,
                        DisplayName = item.Pin?.LastKnownAchievementName,
                        GameName = item.Pin?.LastKnownGameName
                    }
                    : item.Achievement)
                .ToList();
        }

        public static IReadOnlyList<AchievementDisplayItem> ResolveRecentAchievements(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance)
        {
            // RecentAchievements is already sorted upstream (AchievementSortHelper,
            // scope RecentAchievements) - do not re-sort.
            return (snapshot?.RecentAchievements ?? new List<AchievementDisplayItem>())
                .Where(item => item != null)
                .Take(ShowcaseWidgetOptions.GetRecentCount(instance))
                .ToList();
        }

        public static IReadOnlyList<GameSummaryItem> ResolveGameSummaries(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance)
        {
            var games = (snapshot?.GameSummaries ?? new List<GameSummaryItem>())
                .Where(game => game != null);
            if (ShowcaseWidgetOptions.GetHideCompleted(instance))
            {
                games = games.Where(game => !game.IsCompleted);
            }

            switch (ShowcaseWidgetOptions.GetGameListSort(instance))
            {
                case ShowcaseGameListSort.Completion:
                    games = games
                        .OrderByDescending(game => game.Progression)
                        .ThenBy(game => game.GameName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case ShowcaseGameListSort.Name:
                    games = games.OrderBy(game => game.GameName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case ShowcaseGameListSort.Playtime:
                    games = games
                        .OrderByDescending(game => game.PlaytimeSeconds)
                        .ThenBy(game => game.GameName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    games = games
                        .OrderByDescending(game => game.LastUnlockUtc ?? DateTime.MinValue)
                        .ThenBy(game => game.GameName, StringComparer.CurrentCultureIgnoreCase);
                    break;
            }

            return games.Take(ShowcaseWidgetOptions.GetGameListCount(instance)).ToList();
        }

        public static IReadOnlyList<GameSummaryItem> ResolveGameMosaic(
            OverviewDataSnapshot snapshot,
            ShowcaseSettings settings,
            ShowcaseWidgetInstanceSettings instance)
        {
            var summaries = snapshot?.GameSummaries ?? new List<GameSummaryItem>();
            var count = ShowcaseWidgetOptions.GetGameMosaicCount(instance);
            IEnumerable<GameSummaryItem> games;
            switch (ShowcaseWidgetOptions.GetGameMosaicSource(instance))
            {
                case ShowcaseGameMosaicSource.All:
                    games = summaries
                        .Where(game => game != null)
                        .OrderByDescending(game => game.LastUnlockUtc ?? DateTime.MinValue);
                    break;
                case ShowcaseGameMosaicSource.Pinned:
                    games = ResolvePinnedGameSummaries(summaries, settings);
                    break;
                case ShowcaseGameMosaicSource.PlayniteFavorites:
                    games = summaries
                        .Where(game => game?.IsFavorite == true)
                        .OrderBy(game => game.GameName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    games = summaries
                        .Where(game => game?.IsCompleted == true)
                        .OrderByDescending(game => game.LastUnlockUtc ?? DateTime.MinValue);
                    break;
            }

            return games.Take(count).ToList();
        }

        public static ShowcaseActivityCalendar BuildActivityCalendar(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance,
            DateTime endDate)
        {
            var counts = (snapshot?.GlobalUnlockCountsByDate ?? new Dictionary<DateTime, int>())
                .GroupBy(pair => pair.Key.Date)
                .ToDictionary(group => group.Key, group => group.Sum(pair => Math.Max(0, pair.Value)));

            endDate = endDate.Date;
            var range = ShowcaseTimelineOptions.GetRange(instance);
            var start = GetTimelineStartDate(range, endDate, counts);
            if (start > endDate)
            {
                start = endDate;
            }

            while (start.DayOfWeek != DayOfWeek.Sunday)
            {
                start = start.AddDays(-1);
            }

            var totalDays = (endDate - start).Days + 1;
            var days = new List<ShowcaseActivityDay>(totalDays);
            var max = 0;
            var total = 0;
            var activeDays = 0;
            for (var offset = 0; offset < totalDays; offset++)
            {
                var date = start.AddDays(offset);
                counts.TryGetValue(date, out var count);
                max = Math.Max(max, count);
                total += count;
                if (count > 0)
                {
                    activeDays++;
                }

                days.Add(new ShowcaseActivityDay { Date = date, Count = count });
            }

            foreach (var day in days)
            {
                day.Intensity = ComputeActivityIntensity(day.Count, max);
            }

            return new ShowcaseActivityCalendar
            {
                Days = days,
                StartDate = start,
                EndDate = endDate,
                MaxCount = max,
                TotalCount = total,
                ActiveDayCount = activeDays
            };
        }

        /// <summary>Quartile of the window's busiest day; 0 only for zero-count days.</summary>
        private static int ComputeActivityIntensity(int count, int max)
        {
            if (count <= 0 || max <= 0)
            {
                return 0;
            }

            var ratio = count / (double)max;
            return ratio <= 0.25 ? 1 : ratio <= 0.5 ? 2 : ratio <= 0.75 ? 3 : 4;
        }

        /// <summary>
        /// Cumulative score per day, reconstructed retroactively from unlock timestamps
        /// with today's per-achievement score values. Unlocks without a timestamp form a
        /// constant baseline so the curve ends at the true earned total.
        /// </summary>
        public static IReadOnlyList<ShowcaseScorePoint> BuildScoreHistory(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance,
            DateTime endDate)
        {
            endDate = endDate.Date;
            var unlocked = (snapshot?.Achievements ?? new List<AchievementDisplayItem>())
                .Where(item => item?.Unlocked == true)
                .ToList();
            if (unlocked.Count == 0)
            {
                return Array.Empty<ShowcaseScorePoint>();
            }

            var baselineCollection = 0;
            var baselinePrestige = 0;
            var dailyCollection = new Dictionary<DateTime, int>();
            var dailyPrestige = new Dictionary<DateTime, int>();
            foreach (var item in unlocked)
            {
                if (item.UnlockTimeUtc.HasValue)
                {
                    var day = item.UnlockTimeUtc.Value.Date;
                    dailyCollection.TryGetValue(day, out var collection);
                    dailyCollection[day] = collection + item.CollectionScore;
                    dailyPrestige.TryGetValue(day, out var prestige);
                    dailyPrestige[day] = prestige + item.PrestigeScore;
                }
                else
                {
                    baselineCollection += item.CollectionScore;
                    baselinePrestige += item.PrestigeScore;
                }
            }

            var range = ShowcaseTimelineOptions.GetRange(instance);
            var start = GetTimelineStartDate(range, endDate, dailyCollection);
            if (start > endDate)
            {
                start = endDate;
            }

            var preWindowCollection = baselineCollection +
                dailyCollection.Where(pair => pair.Key < start).Sum(pair => pair.Value);
            var preWindowPrestige = baselinePrestige +
                dailyPrestige.Where(pair => pair.Key < start).Sum(pair => pair.Value);

            var rangeDays = (endDate - start).Days + 1;
            var step = Math.Max(1, (int)Math.Ceiling(rangeDays / 366.0));
            var points = new List<ShowcaseScorePoint>();
            var cumulativeCollection = preWindowCollection;
            var cumulativePrestige = preWindowPrestige;
            for (var offset = 0; offset < rangeDays; offset++)
            {
                var date = start.AddDays(offset);
                if (dailyCollection.TryGetValue(date, out var collection))
                {
                    cumulativeCollection += collection;
                }

                if (dailyPrestige.TryGetValue(date, out var prestige))
                {
                    cumulativePrestige += prestige;
                }

                if (offset % step == 0 || offset == rangeDays - 1)
                {
                    points.Add(new ShowcaseScorePoint
                    {
                        Date = date,
                        CollectionScore = cumulativeCollection,
                        PrestigeScore = cumulativePrestige
                    });
                }
            }

            return points;
        }

        public static IReadOnlyList<AchievementDisplayItem> ResolveMosaic(
            OverviewDataSnapshot snapshot,
            ShowcaseSettings settings,
            ShowcaseWidgetInstanceSettings instance)
        {
            var source = ShowcaseWidgetOptions.GetMosaicSource(instance);
            var count = ShowcaseWidgetOptions.GetMosaicCount(instance);
            IEnumerable<AchievementDisplayItem> achievements;
            switch (source)
            {
                case ShowcaseMosaicSource.Rarest:
                    achievements = (snapshot?.Achievements ?? new List<AchievementDisplayItem>())
                        .Where(item => item?.Unlocked == true)
                        // Match every established rarity-sorted surface. The resolver keeps
                        // tier-only achievements in their correct band instead of pushing them
                        // behind all percentage-backed achievements.
                        .OrderBy(item => item.RaritySortValue)
                        .ThenByDescending(item => item.UnlockTimeUtc);
                    break;
                case ShowcaseMosaicSource.Pinned:
                    achievements = ResolvePinnedAchievements(snapshot, settings?.PinnedAchievements)
                        .Where(item => !item.IsMissing)
                        .Select(item => item.Achievement);
                    break;
                default:
                    achievements = (snapshot?.RecentAchievements ?? new List<AchievementDisplayItem>())
                        .Where(item => item != null)
                        .OrderByDescending(item => item.UnlockTimeUtc);
                    break;
            }

            return achievements.Take(count).ToList();
        }

        private static IReadOnlyList<ShowcaseChartEntry> ApplyTopN(
            IEnumerable<ShowcaseChartEntry> source,
            int topN)
        {
            var ordered = (source ?? Array.Empty<ShowcaseChartEntry>())
                .Where(entry => entry != null && entry.Value > 0)
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (ordered.Count <= topN)
            {
                return ordered;
            }

            var result = ordered.Take(topN).ToList();
            result.Add(new ShowcaseChartEntry
            {
                Key = "Other",
                LabelKey = "LOCPlayAch_Showcase_Other",
                Value = ordered.Skip(topN).Sum(entry => entry.Value)
            });
            return result;
        }

        private static void CalculateStreaks(
            IDictionary<DateTime, int> counts,
            DateTime today,
            out int current,
            out int longest)
        {
            var dates = new HashSet<DateTime>(
                (counts ?? new Dictionary<DateTime, int>())
                    .Where(pair => pair.Value > 0)
                    .Select(pair => pair.Key.Date));
            longest = 0;
            var running = 0;
            DateTime? previous = null;
            foreach (var date in dates.OrderBy(date => date))
            {
                running = previous.HasValue && date == previous.Value.AddDays(1)
                    ? running + 1
                    : 1;
                longest = Math.Max(longest, running);
                previous = date;
            }

            current = 0;
            var cursor = dates.Contains(today) ? today : today.AddDays(-1);
            while (dates.Contains(cursor))
            {
                current++;
                cursor = cursor.AddDays(-1);
            }
        }

        private static ShowcaseStatistic Stat(
            string key,
            string labelKey,
            double value,
            bool hasValue = true)
        {
            return new ShowcaseStatistic
            {
                Key = key,
                LabelKey = labelKey,
                Value = value,
                HasValue = hasValue
            };
        }
    }
}

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
                    break;
                case ShowcaseWidgetKind.Statistics:
                    result.Statistics = BuildStatistics(snapshot, now ?? DateTime.Now);
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    result.ChartEntries = BuildNativePoints(snapshot, instance);
                    break;
                case ShowcaseWidgetKind.PinnedAchievements:
                    result.Achievements = ResolvePinnedAchievements(snapshot, settings.PinnedAchievements);
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    result.Games = ResolveFavoriteGames(snapshot, settings, instance);
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    result.MosaicAchievements = ResolveMosaic(snapshot, settings, instance);
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

            var byId = summaries
                .Where(game => game?.PlayniteGameId.HasValue == true)
                .GroupBy(game => game.PlayniteGameId.Value)
                .ToDictionary(group => group.Key, group => group.First());
            return (settings?.PinnedGameIds ?? new List<Guid>())
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
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

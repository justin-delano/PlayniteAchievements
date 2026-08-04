using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Common;
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

        public string Label { get; set; }

        public double Value { get; set; }

        public string DisplayValue { get; set; }
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
                case ShowcaseWidgetKind.Pie:
                    result.ChartEntries = BuildPie(snapshot, instance);
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
                Stat("unlocked", "LOCPlayAch_Showcase_Stat_Unlocked", "Unlocked", snapshot.TotalUnlocked, snapshot.TotalUnlocked.ToString("N0", FormattingCulture.Current)),
                Stat("completion", "LOCPlayAch_Showcase_Stat_Completion", "Completion", snapshot.GlobalProgressionPercent, snapshot.GlobalProgressionPercent.ToString("N1", FormattingCulture.Current) + "%"),
                Stat("trackedGames", "LOCPlayAch_Showcase_Stat_TrackedGames", "Tracked games", snapshot.TotalGames, snapshot.TotalGames.ToString("N0", FormattingCulture.Current)),
                Stat("playedGames", "LOCPlayAch_Showcase_Stat_PlayedGames", "Played games", playedGames, playedGames.ToString("N0", FormattingCulture.Current)),
                Stat("completedGames", "LOCPlayAch_Showcase_Stat_CompletedGames", "Completed games", snapshot.CompletedGames, snapshot.CompletedGames.ToString("N0", FormattingCulture.Current)),
                Stat("playtime", "LOCPlayAch_Showcase_Stat_Playtime", "Playtime", totalPlaytime, FormatPlaytime(totalPlaytime)),
                Stat(
                    "activeDayRate",
                    "LOCPlayAch_Showcase_Stat_ActiveDayRate",
                    "Unlocks / active day",
                    activeDays > 0 ? (double)snapshot.TotalUnlocked / activeDays : 0,
                    activeDays > 0
                        ? ((double)snapshot.TotalUnlocked / activeDays).ToString("N1", FormattingCulture.Current)
                        : "0"),
                Stat("thirtyDayRate", "LOCPlayAch_Showcase_Stat_ThirtyDayRate", "30-day rate", lastThirtyDays / 30d, (lastThirtyDays / 30d).ToString("N1", FormattingCulture.Current) + "/day"),
                Stat(
                    "averageGlobalUnlock",
                    "LOCPlayAch_Showcase_Stat_AverageGlobalUnlock",
                    "Average global unlock",
                    unlockedWithRarity.Count > 0 ? unlockedWithRarity.Average() : 0,
                    unlockedWithRarity.Count > 0
                        ? unlockedWithRarity.Average().ToString("N1", FormattingCulture.Current) + "%"
                        : "—"),
                Stat("currentStreak", "LOCPlayAch_Showcase_Stat_CurrentStreak", "Current streak", currentStreak, currentStreak.ToString("N0", FormattingCulture.Current) + " days"),
                Stat("longestStreak", "LOCPlayAch_Showcase_Stat_LongestStreak", "Longest streak", longestStreak, longestStreak.ToString("N0", FormattingCulture.Current) + " days")
            };
        }

        public static IReadOnlyList<ShowcaseChartEntry> BuildNativePoints(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance)
        {
            var summaries = snapshot?.GameSummaries ?? new List<GameSummaryItem>();
            var grouping = instance?.GetOption(
                "Grouping",
                ShowcasePointsGrouping.Provider) ?? ShowcasePointsGrouping.Provider;
            var topN = Math.Max(1, Math.Min(25, instance?.GetOption("TopN", 8) ?? 8));

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
            return (pins ?? Array.Empty<PinnedAchievementReference>())
                .Where(pin => pin != null)
                .Select(pin =>
                {
                    var match = achievements.FirstOrDefault(item =>
                        item?.PlayniteGameId == pin.GameId &&
                        string.Equals(item.ApiName, pin.ApiName, StringComparison.OrdinalIgnoreCase));
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
            var source = instance?.GetOption(
                "Source",
                ShowcaseFavoriteGameSource.ShowcasePins) ??
                ShowcaseFavoriteGameSource.ShowcasePins;
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
            var source = instance?.GetOption(
                "Source",
                ShowcaseMosaicSource.Recent) ?? ShowcaseMosaicSource.Recent;
            var count = Math.Max(1, Math.Min(64, instance?.GetOption("Count", 24) ?? 24));
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

        public static IReadOnlyList<ShowcaseChartEntry> BuildPie(
            OverviewDataSnapshot snapshot,
            ShowcaseWidgetInstanceSettings instance)
        {
            snapshot = snapshot ?? new OverviewDataSnapshot();
            var mode = instance?.GetOption("Mode", ShowcasePieMode.CompletedGames) ??
                ShowcasePieMode.CompletedGames;
            switch (mode)
            {
                case ShowcasePieMode.Provider:
                    var providerLabels = (snapshot.GameSummaries ?? new List<GameSummaryItem>())
                        .Where(game => game != null && !string.IsNullOrWhiteSpace(game.ProviderKey))
                        .GroupBy(game => game.ProviderKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(game => game.Provider)
                                .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label)) ?? group.Key,
                            StringComparer.OrdinalIgnoreCase);
                    return (snapshot.UnlockedByProvider ?? new Dictionary<string, int>())
                        .OrderByDescending(pair => pair.Value)
                        .Select(pair => new ShowcaseChartEntry
                        {
                            Key = pair.Key,
                            Label = providerLabels.TryGetValue(pair.Key, out var providerLabel)
                                ? providerLabel
                                : pair.Key,
                            Value = pair.Value,
                            SecondaryText = string.Format(
                                FormattingCulture.Current,
                                "{0:N0}/{1:N0}",
                                pair.Value,
                                GetValue(snapshot.TotalByProvider, pair.Key))
                        }).ToList();
                case ShowcasePieMode.Rarity:
                    return new List<ShowcaseChartEntry>
                    {
                        Chart("common", "LOCPlayAch_Rarity_Common", "Common", snapshot.TotalCommon),
                        Chart("uncommon", "LOCPlayAch_Rarity_Uncommon", "Uncommon", snapshot.TotalUncommon),
                        Chart("rare", "LOCPlayAch_Rarity_Rare", "Rare", snapshot.TotalRare),
                        Chart("ultraRare", "LOCPlayAch_Rarity_UltraRare", "Ultra rare", snapshot.TotalUltraRare)
                    }.Where(entry => entry.Value > 0).ToList();
                case ShowcasePieMode.Trophy:
                    var games = snapshot.GameSummaries ?? new List<GameSummaryItem>();
                    return new List<ShowcaseChartEntry>
                    {
                        Chart("platinum", "LOCPlayAch_Trophy_Platinum", "Platinum", games.Sum(game => game?.TrophyPlatinumCount ?? 0)),
                        Chart("gold", "LOCPlayAch_Trophy_Gold", "Gold", games.Sum(game => game?.TrophyGoldCount ?? 0)),
                        Chart("silver", "LOCPlayAch_Trophy_Silver", "Silver", games.Sum(game => game?.TrophySilverCount ?? 0)),
                        Chart("bronze", "LOCPlayAch_Trophy_Bronze", "Bronze", games.Sum(game => game?.TrophyBronzeCount ?? 0))
                    }.Where(entry => entry.Value > 0).ToList();
                default:
                    return new List<ShowcaseChartEntry>
                    {
                        Chart("completed", "LOCPlayAch_Completed", "Completed", snapshot.CompletedGames),
                        Chart("incomplete", "LOCPlayAch_Showcase_Incomplete", "Incomplete", Math.Max(0, snapshot.TotalGames - snapshot.CompletedGames))
                    }.Where(entry => entry.Value > 0).ToList();
            }
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
                Label = "Other",
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
            string label,
            double value,
            string displayValue)
        {
            return new ShowcaseStatistic
            {
                Key = key,
                LabelKey = labelKey,
                Label = label,
                Value = value,
                DisplayValue = displayValue
            };
        }

        private static ShowcaseChartEntry Chart(
            string key,
            string labelKey,
            string label,
            double value)
        {
            return new ShowcaseChartEntry
            {
                Key = key,
                LabelKey = labelKey,
                Label = label,
                Value = value
            };
        }

        private static int GetValue(IDictionary<string, int> values, string key)
        {
            return values != null && values.TryGetValue(key, out var value) ? value : 0;
        }

        private static string FormatPlaytime(double seconds)
        {
            var hours = Math.Max(0, seconds) / 3600d;
            return hours >= 1000
                ? (hours / 1000d).ToString("N1", FormattingCulture.Current) + "k h"
                : hours.ToString("N0", FormattingCulture.Current) + " h";
        }
    }
}

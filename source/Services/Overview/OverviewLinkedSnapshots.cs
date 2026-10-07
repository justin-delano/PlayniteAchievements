using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Services.Overview
{
    /// <summary>The overview filters a linked widget can leave out: the ones it sets itself.</summary>
    [Flags]
    public enum OverviewLinkedFilter
    {
        None = 0,
        Provider = 1,
        Completeness = 2,
        UnlockSpan = 4
    }

    /// <summary>
    /// Slice identities a linked pie exchanges with the overview, independent of the labels
    /// the chart displays. Provider slices use their provider key.
    /// </summary>
    public static class OverviewLinkedSliceKeys
    {
        public const string Complete = "Complete";
        public const string Incomplete = "Incomplete";
        public const string Locked = "Locked";
    }

    /// <summary>When a linked widget narrows to the overview's selected game.</summary>
    public enum OverviewLinkedSelection
    {
        /// <summary>Stays on the filtered library.</summary>
        Ignore,

        /// <summary>Narrows whenever a game is selected.</summary>
        Always,

        /// <summary>Narrows only when the selected game has rarity data to chart.</summary>
        WhenRarityData,

        /// <summary>Narrows only when the selected game has trophies to chart.</summary>
        WhenTrophyData
    }

    /// <summary>
    /// An inclusive range of local unlock days, the shape a timeline bar or a calendar day
    /// covers. Day keys match <see cref="OverviewDataSnapshot.UnlockCountsByDateByGame"/>.
    /// </summary>
    public readonly struct UnlockDaySpan : IEquatable<UnlockDaySpan>
    {
        public UnlockDaySpan(DateTime start, DateTime end)
        {
            Start = start.Date <= end.Date ? start.Date : end.Date;
            End = start.Date <= end.Date ? end.Date : start.Date;
        }

        public DateTime Start { get; }

        public DateTime End { get; }

        public bool Contains(DateTime day) => day.Date >= Start && day.Date <= End;

        public bool Equals(UnlockDaySpan other) => Start == other.Start && End == other.End;

        public override bool Equals(object obj) => obj is UnlockDaySpan other && Equals(other);

        public override int GetHashCode() => Start.GetHashCode() * 397 ^ End.GetHashCode();
    }

    /// <summary>
    /// Snapshots for the overview's linked widgets: the overview's own snapshot cut down to the
    /// games its filters keep. Pure, so the rules can be checked without a view model.
    /// </summary>
    public static class OverviewLinkedSnapshots
    {
        /// <summary>
        /// Games with at least one unlock on a day inside <paramref name="span"/>, by the
        /// snapshot's per-game day counts.
        /// </summary>
        public static HashSet<Guid> GamesUnlockedDuring(OverviewDataSnapshot snapshot, UnlockDaySpan span)
        {
            var result = new HashSet<Guid>();
            foreach (var game in snapshot?.UnlockCountsByDateByGame ?? new Dictionary<Guid, Dictionary<DateTime, int>>())
            {
                if (game.Value != null && game.Value.Any(day => day.Value > 0 && span.Contains(day.Key)))
                {
                    result.Add(game.Key);
                }
            }

            return result;
        }

        /// <summary>
        /// Whether a linked widget with this selection rule narrows to <paramref name="selected"/>.
        /// </summary>
        public static bool NarrowsTo(GameSummaryItem selected, OverviewLinkedSelection selection)
        {
            if (selected?.PlayniteGameId.HasValue != true)
            {
                return false;
            }

            switch (selection)
            {
                case OverviewLinkedSelection.Always:
                    return true;
                case OverviewLinkedSelection.WhenRarityData:
                    return selected.HasRarityPieChartData;
                case OverviewLinkedSelection.WhenTrophyData:
                    return selected.HasTrophyPieChartData;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The part of <paramref name="source"/> that covers <paramref name="kept"/>: its
        /// summary totals, its per-day unlock counts, and its unlocked rows. When every game
        /// is kept the source itself comes back, so widgets share its derived-series cache.
        /// </summary>
        public static OverviewDataSnapshot Build(
            OverviewDataSnapshot source,
            IReadOnlyList<GameSummaryItem> kept,
            bool keptIsAll)
        {
            if (source == null)
            {
                return null;
            }

            if (keptIsAll)
            {
                return source;
            }

            var games = kept ?? Array.Empty<GameSummaryItem>();
            var snapshot = OverviewDataSnapshot.FromGameSummaries(games);
            var ids = new HashSet<Guid>(games
                .Where(game => game?.PlayniteGameId.HasValue == true)
                .Select(game => game.PlayniteGameId.Value));

            snapshot.UnlockCountsByDateByGame = (source.UnlockCountsByDateByGame ??
                    new Dictionary<Guid, Dictionary<DateTime, int>>())
                .Where(pair => pair.Value != null && ids.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            snapshot.GlobalUnlockCountsByDate = TimelinePlatformSeries.SumGames(source, ids.Contains);
            snapshot.Achievements = (source.Achievements ?? new List<AchievementDisplayItem>())
                .Where(item => item?.PlayniteGameId.HasValue == true && ids.Contains(item.PlayniteGameId.Value))
                .ToList();
            snapshot.RecentAchievements = (source.RecentAchievements ?? new List<AchievementDisplayItem>())
                .Where(item => item?.PlayniteGameId.HasValue == true && ids.Contains(item.PlayniteGameId.Value))
                .ToList();
            snapshot.CurrentUserIdentities = source.CurrentUserIdentities;
            return snapshot;
        }
    }
}

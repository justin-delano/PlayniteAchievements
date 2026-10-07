using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels
{
    // The overview's side of its mini-showcase: the snapshots its linked widgets project from,
    // the filters their clicks set, and the Achievements grid's unlock date range, which a
    // timeline column or calendar day sets as well.
    public partial class OverviewViewModel
    {
        private DateTime? _unlockRangeFrom;
        private DateTime? _unlockRangeTo;

        private readonly Dictionary<(OverviewLinkedFilter Exclude, Guid? Game), LinkedSnapshotEntry> _linkedSnapshots =
            new Dictionary<(OverviewLinkedFilter Exclude, Guid? Game), LinkedSnapshotEntry>();

        /// <summary>
        /// Raised when what <see cref="GetLinkedSnapshot"/> returns may have changed: the
        /// snapshot, a filter, or the selected game.
        /// </summary>
        public event EventHandler LinkedDataChanged;

        /// <summary>The Achievements grid's range start day, or null for an open start.</summary>
        public DateTime? UnlockRangeFrom => _unlockRangeFrom;

        /// <summary>The Achievements grid's range end day, or null for an open end.</summary>
        public DateTime? UnlockRangeTo => _unlockRangeTo;

        /// <summary>The unlock date range as one span, open ends at the calendar's limits; null when unset.</summary>
        public UnlockDaySpan? UnlockSpanFilter =>
            _unlockRangeFrom.HasValue || _unlockRangeTo.HasValue
                ? new UnlockDaySpan(_unlockRangeFrom ?? DateTime.MinValue, _unlockRangeTo ?? DateTime.MaxValue.Date)
                : (UnlockDaySpan?)null;

        /// <summary>Sets the range to <paramref name="span"/>, or clears it when it is already that span.</summary>
        public void ToggleUnlockSpanFilter(UnlockDaySpan span)
        {
            if (Nullable.Equals(UnlockSpanFilter, span))
            {
                SetUnlockRange(null, null);
            }
            else
            {
                SetUnlockRange(span.Start, span.End);
            }
        }

        /// <summary>Sets the range from the Achievements grid's pickers; either end may be open.</summary>
        public void SetUnlockRange(DateTime? from, DateTime? to)
        {
            from = from?.Date;
            to = to?.Date;
            if (Nullable.Equals(_unlockRangeFrom, from) && Nullable.Equals(_unlockRangeTo, to))
            {
                return;
            }

            _unlockRangeFrom = from;
            _unlockRangeTo = to;
            OnPropertyChanged(nameof(UnlockSpanFilter));
            // Deferred like the other filters, so the click or pick that set it finishes first.
            // The Games grid follows it too, to the games with unlocks in the range; refiltering
            // the games also refreshes the linked widgets.
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                new Action(() =>
                {
                    ApplyRightFilters();
                    ApplyLeftFilters();
                }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        /// <summary>Games with at least one unlock in the range; every game while it is unset.</summary>
        private IEnumerable<GameSummaryItem> ApplyUnlockRangeToGames(IEnumerable<GameSummaryItem> games)
        {
            var span = UnlockSpanFilter;
            if (!span.HasValue)
            {
                return games;
            }

            var kept = OverviewLinkedSnapshots.GamesUnlockedDuring(_latestSnapshot, span.Value);
            return games.Where(game => game?.PlayniteGameId.HasValue == true && kept.Contains(game.PlayniteGameId.Value));
        }

        /// <summary>Unlocked rows whose local unlock day falls in the range; every row while it is unset.</summary>
        private IEnumerable<AchievementDisplayItem> ApplyUnlockRange(IEnumerable<AchievementDisplayItem> items)
        {
            var span = UnlockSpanFilter;
            if (!span.HasValue)
            {
                return items;
            }

            return items.Where(item =>
                item?.Unlocked == true &&
                item.UnlockTimeUtc.HasValue &&
                span.Value.Contains(UnlockDayCounts.DayOf(item.UnlockTimeUtc.Value)));
        }

        /// <summary>
        /// The snapshot a linked widget projects from: the overview's snapshot narrowed by every
        /// grid filter except those in <paramref name="exclude"/>, or by the selected game when
        /// <paramref name="selection"/> says so. Null until the overview has a snapshot.
        /// A filter change that leaves a view's games as they were returns the same instance, so
        /// the widgets on that view see no new data and are left alone.
        /// </summary>
        public OverviewDataSnapshot GetLinkedSnapshot(OverviewLinkedFilter exclude, OverviewLinkedSelection selection)
        {
            var source = _latestSnapshot;
            if (source == null)
            {
                return null;
            }

            var narrowTo = GetLinkedNarrowedGame(selection)?.PlayniteGameId;
            // Narrowed to one game, the grid filters no longer apply, but the unlock range does.
            var key = (narrowTo.HasValue ? exclude & OverviewLinkedFilter.UnlockSpan : exclude, narrowTo);
            var span = (exclude & OverviewLinkedFilter.UnlockSpan) == 0 ? UnlockSpanFilter : null;
            _linkedSnapshots.TryGetValue(key, out var entry);
            if (entry != null && entry.IsCurrent)
            {
                return entry.Snapshot;
            }

            List<GameSummaryItem> kept;
            bool keptIsAll;
            if (narrowTo.HasValue)
            {
                kept = (_allGameSummaries ?? new List<GameSummaryItem>())
                    .Where(candidate => candidate?.PlayniteGameId == narrowTo)
                    .Take(1)
                    .ToList();
                keptIsAll = false;
            }
            else
            {
                var all = (_allGameSummaries ?? new List<GameSummaryItem>()).Where(game => game != null).ToList();
                kept = FilterLinkedGames(all, exclude);
                keptIsAll = kept.Count == all.Count;
            }

            var ids = new HashSet<Guid>(kept
                .Where(game => game.PlayniteGameId.HasValue)
                .Select(game => game.PlayniteGameId.Value));
            if (entry != null &&
                ReferenceEquals(entry.Source, source) &&
                Nullable.Equals(entry.Span, span) &&
                entry.KeptCount == kept.Count &&
                entry.KeptIds.SetEquals(ids))
            {
                entry.IsCurrent = true;
                return entry.Snapshot;
            }

            var snapshot = OverviewLinkedSnapshots.Build(source, kept, keptIsAll);
            if (span.HasValue)
            {
                snapshot = OverviewLinkedSnapshots.ClipToSpan(snapshot, span.Value);
            }

            _linkedSnapshots[key] = new LinkedSnapshotEntry
            {
                Source = source,
                Span = span,
                KeptCount = kept.Count,
                KeptIds = ids,
                Snapshot = snapshot,
                IsCurrent = true
            };
            return snapshot;
        }

        /// <summary>One linked view's snapshot and the games and range it was built from.</summary>
        private sealed class LinkedSnapshotEntry
        {
            public OverviewDataSnapshot Source;
            public UnlockDaySpan? Span;
            public int KeptCount;
            public HashSet<Guid> KeptIds;
            public OverviewDataSnapshot Snapshot;

            // Cleared by InvalidateLinkedSnapshots: the next request re-checks the games.
            public bool IsCurrent;
        }

        /// <summary>
        /// A linked widget's title: the selected game's name while the widget is narrowed to it,
        /// the one thing its chart cannot say for itself; empty otherwise.
        /// </summary>
        public string GetLinkedContextLabel(OverviewLinkedSelection selection) =>
            GetLinkedNarrowedGame(selection)?.GameName ?? string.Empty;

        /// <summary>The selected game a linked widget with this rule narrows to, or null.</summary>
        public GameSummaryItem GetLinkedNarrowedGame(OverviewLinkedSelection selection)
        {
            var selected = ResolveSelectedGameForChartContext(_latestSnapshot);
            return OverviewLinkedSnapshots.NarrowsTo(selected, selection) ? selected : null;
        }

        // The Games grid's own filters (ApplyLeftFilters), less the ones the widget sets itself. A
        // completions pie that leaves the progress filter out still keeps it to the games that
        // could finish, as the overview's completions pie did. The unlock range keeps the games
        // with unlocks in it here, and GetLinkedSnapshot then clips their achievements to it.
        private List<GameSummaryItem> FilterLinkedGames(List<GameSummaryItem> all, OverviewLinkedFilter exclude)
        {
            IEnumerable<GameSummaryItem> games = all;
            var searchQuery = Services.Search.SearchQuery.From(LeftSearchText);
            if (searchQuery.HasValue)
            {
                games = games.Where(game => _gameSummarySearchIndex.Matches(game, searchQuery));
            }

            if ((exclude & OverviewLinkedFilter.Provider) == 0)
            {
                games = OverviewGameSummaryFilters.ApplyProviderPlatformFilter(games, ProviderFilterGroups);
            }

            games = OverviewGameSummaryFilters.ApplyActivityAndProgressFilters(
                games,
                _selectedPlayStatusFilters,
                (exclude & OverviewLinkedFilter.Completeness) == 0
                    ? _selectedCompletenessFilters
                    : GetCompletedGamesPieProgressFilters(),
                L("LOCPlayAch_Filter_Played"),
                L("LOCPlayAch_Filter_Unplayed"),
                L("LOCPlayAch_Filter_Complete"),
                L("LOCPlayAch_Filter_InProgress"),
                L("LOCPlayAch_Filter_NoProgress"));

            if ((exclude & OverviewLinkedFilter.UnlockSpan) == 0)
            {
                games = ApplyUnlockRangeToGames(games);
            }

            return games.ToList();
        }

        /// <summary>
        /// The slices a linked pie shows as selected: the filtered platforms for the platform
        /// pie, and for the completions pie the one side the progress filter picks, if exactly one.
        /// </summary>
        public IReadOnlyCollection<string> GetLinkedSliceKeys(ShowcasePieMode mode)
        {
            switch (mode)
            {
                case ShowcasePieMode.Provider:
                    return (ProviderFilterGroups ?? Enumerable.Empty<ProviderFilterGroup>())
                        .Where(group => group.HasAnySelected && !string.IsNullOrWhiteSpace(group.ProviderKey))
                        .Select(group => group.ProviderKey)
                        .ToList();
                case ShowcasePieMode.CompletedGames:
                    var complete = _selectedCompletenessFilters.Contains(L("LOCPlayAch_Filter_Complete"));
                    var incomplete = _selectedCompletenessFilters.Contains(L("LOCPlayAch_Filter_InProgress")) ||
                                     _selectedCompletenessFilters.Contains(L("LOCPlayAch_Filter_NoProgress"));
                    if (complete == incomplete)
                    {
                        return Array.Empty<string>();
                    }

                    return new[] { complete ? OverviewLinkedSliceKeys.Complete : OverviewLinkedSliceKeys.Incomplete };
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>Applies a linked pie slice click: toggles that platform, or that side of the progress filter.</summary>
        public void ApplyLinkedSliceClick(ShowcasePieMode mode, string sliceKey)
        {
            if (string.IsNullOrWhiteSpace(sliceKey))
            {
                return;
            }

            switch (mode)
            {
                case ShowcasePieMode.Provider:
                    if (sliceKey == OverviewLinkedSliceKeys.Locked)
                    {
                        ClearProviderFilters();
                        return;
                    }

                    ProviderFilterGroups?
                        .FirstOrDefault(group => string.Equals(group.ProviderKey, sliceKey, StringComparison.OrdinalIgnoreCase))?
                        .ToggleAll();
                    return;
                case ShowcasePieMode.CompletedGames:
                    if (sliceKey == OverviewLinkedSliceKeys.Complete)
                    {
                        ToggleCompletenessFilterFromPieChart(L("LOCPlayAch_Filter_Complete"));
                    }
                    else if (sliceKey == OverviewLinkedSliceKeys.Incomplete)
                    {
                        ToggleCompletenessFilterFromPieChart(L("LOCPlayAch_Overview_Incomplete"));
                    }

                    return;
            }
        }

        /// <param name="rebuild">
        /// True when what the widgets draw changed with no change to their games (platform or
        /// rarity colors), so every view gets a fresh snapshot instead of keeping its own.
        /// </param>
        private void InvalidateLinkedSnapshots(bool rebuild = false)
        {
            if (rebuild)
            {
                _linkedSnapshots.Clear();
            }

            foreach (var entry in _linkedSnapshots.Values)
            {
                entry.IsCurrent = false;
            }

            LinkedDataChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

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
    // the filters their clicks set, and the unlock-day filter only they can set.
    public partial class OverviewViewModel
    {
        private UnlockDaySpan? _unlockSpanFilter;

        private readonly Dictionary<(OverviewLinkedFilter Exclude, Guid? Game), OverviewDataSnapshot> _linkedSnapshots =
            new Dictionary<(OverviewLinkedFilter Exclude, Guid? Game), OverviewDataSnapshot>();

        /// <summary>
        /// Raised when what <see cref="GetLinkedSnapshot"/> returns may have changed: the
        /// snapshot, a filter, or the selected game.
        /// </summary>
        public event EventHandler LinkedDataChanged;

        /// <summary>The unlock-day filter a linked timeline or calendar set, or null.</summary>
        public UnlockDaySpan? UnlockSpanFilter => _unlockSpanFilter;

        /// <summary>The control bar chip's text for the unlock-day filter; empty while it is off.</summary>
        public string UnlockSpanFilterText
        {
            get
            {
                if (!_unlockSpanFilter.HasValue)
                {
                    return string.Empty;
                }

                var span = _unlockSpanFilter.Value;
                var culture = FormattingCulture.Current;
                var days = span.Start == span.End
                    ? span.Start.ToString("d", culture)
                    : span.Start.ToString("d", culture) + " – " + span.End.ToString("d", culture);
                return string.Format(culture, L("LOCPlayAch_Filter_UnlockedDuring"), days);
            }
        }

        /// <summary>Filters the grid to games with unlocks in <paramref name="span"/>; the same span again clears it.</summary>
        public void ToggleUnlockSpanFilter(UnlockDaySpan span)
        {
            SetUnlockSpanFilter(Nullable.Equals(_unlockSpanFilter, span) ? (UnlockDaySpan?)null : span);
        }

        public void ClearUnlockSpanFilter() => SetUnlockSpanFilter(null);

        private void SetUnlockSpanFilter(UnlockDaySpan? span)
        {
            if (Nullable.Equals(_unlockSpanFilter, span))
            {
                return;
            }

            _unlockSpanFilter = span;
            OnPropertyChanged(nameof(UnlockSpanFilterText));
            // Deferred like the other filters, so the click that set it finishes first.
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                new Action(() => { ApplyLeftFilters(); UpdateAggregatePieCharts(); }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private IEnumerable<GameSummaryItem> ApplyUnlockSpanFilter(IEnumerable<GameSummaryItem> games)
        {
            if (!_unlockSpanFilter.HasValue)
            {
                return games;
            }

            var kept = OverviewLinkedSnapshots.GamesUnlockedDuring(_latestSnapshot, _unlockSpanFilter.Value);
            return games.Where(game => game?.PlayniteGameId.HasValue == true && kept.Contains(game.PlayniteGameId.Value));
        }

        /// <summary>
        /// The snapshot a linked widget projects from: the overview's snapshot narrowed by every
        /// grid filter except those in <paramref name="exclude"/>, or by the selected game when
        /// <paramref name="selection"/> says so. Null until the overview has a snapshot.
        /// Cached until <see cref="LinkedDataChanged"/>, so widgets asking for the same view
        /// share one instance.
        /// </summary>
        public OverviewDataSnapshot GetLinkedSnapshot(OverviewLinkedFilter exclude, OverviewLinkedSelection selection)
        {
            var source = _latestSnapshot;
            if (source == null)
            {
                return null;
            }

            var narrowTo = GetLinkedNarrowedGame(selection)?.PlayniteGameId;
            var key = (narrowTo.HasValue ? OverviewLinkedFilter.None : exclude, narrowTo);
            if (_linkedSnapshots.TryGetValue(key, out var cached))
            {
                return cached;
            }

            OverviewDataSnapshot snapshot;
            if (narrowTo.HasValue)
            {
                var game = (_allGameSummaries ?? new List<GameSummaryItem>())
                    .Where(candidate => candidate?.PlayniteGameId == narrowTo)
                    .Take(1)
                    .ToList();
                snapshot = OverviewLinkedSnapshots.Build(source, game, keptIsAll: false);
            }
            else
            {
                var all = (_allGameSummaries ?? new List<GameSummaryItem>()).Where(game => game != null).ToList();
                var kept = FilterLinkedGames(all, exclude);
                snapshot = OverviewLinkedSnapshots.Build(source, kept, keptIsAll: kept.Count == all.Count);
            }

            _linkedSnapshots[key] = snapshot;
            return snapshot;
        }

        /// <summary>The selected game a linked widget with this rule narrows to, or null.</summary>
        public GameSummaryItem GetLinkedNarrowedGame(OverviewLinkedSelection selection)
        {
            var selected = ResolveSelectedGameForChartContext(_latestSnapshot);
            return OverviewLinkedSnapshots.NarrowsTo(selected, selection) ? selected : null;
        }

        // The grid's own filters (ApplyLeftFilters), less the ones the widget sets itself. A
        // completions pie that leaves the progress filter out still keeps it to the games that
        // could finish, as the overview's completions pie did.
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
                games = ApplyUnlockSpanFilter(games);
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

        private void InvalidateLinkedSnapshots()
        {
            _linkedSnapshots.Clear();
            LinkedDataChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

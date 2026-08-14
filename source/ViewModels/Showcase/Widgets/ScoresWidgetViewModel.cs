using System.Collections.Generic;
using System.Linq;
using LiveCharts;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// One score card plus its cumulative score-over-time series for the mini line chart
    /// rendered under the card.
    /// </summary>
    public sealed class ScoreCardWithHistoryViewModel
    {
        public ScoreCardWithHistoryViewModel(
            ScoreCardViewModel card,
            ChartValues<int> historyValues,
            IList<string> historyLabels,
            bool showChart,
            string historyCaption,
            string historyStartText,
            string historyEndText)
        {
            Card = card;
            HistoryValues = historyValues;
            HistoryLabels = historyLabels;
            ShowChart = showChart;
            HistoryCaption = historyCaption;
            HistoryStartText = historyStartText;
            HistoryEndText = historyEndText;
        }

        public ScoreCardViewModel Card { get; }

        public ChartValues<int> HistoryValues { get; }

        /// <summary>Per-point date labels; hidden on the axis, surfaced by the hover tooltip.</summary>
        public IList<string> HistoryLabels { get; }

        public bool ShowChart { get; }

        public string HistoryCaption { get; }

        public string HistoryStartText { get; }

        public string HistoryEndText { get; }
    }

    /// <summary>
    /// Backs the Scores widget by reusing the existing <see cref="ScoreCardViewModel"/> /
    /// ScoreCardControl. Shows the collection and/or prestige card per the score mode, laid out in
    /// a UniformGrid whose orientation follows the viewport, and featured outside compact. Each
    /// card carries a cumulative score history line derived from the projection.
    /// </summary>
    public sealed class ScoresWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private int _rows = 1;
        private int _columns = 1;
        private bool _isFeatured = true;
        private double _maxCardWidth = 360;
        private double _chartHeight = 60;

        // What the cards were last built from. Rebuilding the collection makes LiveCharts throw
        // away and re-plot every series, so an unrelated refresh (a pin toggle, another widget's
        // option, a resize that keeps the same density) must not touch it.
        private IReadOnlyList<ShowcaseScorePoint> _builtHistory;
        private OverviewDataSnapshot _builtSnapshot;
        private ShowcaseScoreMode _builtMode;
        private bool _builtShowChart;

        public BulkObservableCollection<ScoreCardWithHistoryViewModel> Cards { get; } =
            new BulkObservableCollection<ScoreCardWithHistoryViewModel>();

        public int Rows { get => _rows; private set => SetValue(ref _rows, value); }

        public int Columns { get => _columns; private set => SetValue(ref _columns, value); }

        public bool IsFeatured { get => _isFeatured; private set => SetValue(ref _isFeatured, value); }

        public double MaxCardWidth { get => _maxCardWidth; private set => SetValue(ref _maxCardWidth, value); }

        public double ChartHeight { get => _chartHeight; private set => SetValue(ref _chartHeight, value); }

        protected override void Refresh()
        {
            var snapshot = Projection?.Snapshot ?? new OverviewDataSnapshot();
            var mode = ShowcaseWidgetOptions.GetScoreMode(Projection?.Instance);
            var includeCollection = mode != ShowcaseScoreMode.Prestige;
            var includePrestige = mode != ShowcaseScoreMode.Collection;
            var count = (includeCollection ? 1 : 0) + (includePrestige ? 1 : 0);
            var tall = Orientation == WidgetViewportOrientation.Tall;

            Rows = count > 1 && tall ? 2 : 1;
            Columns = count > 1 && !tall ? 2 : 1;
            IsFeatured = Density != WidgetViewportDensity.Compact;
            MaxCardWidth = Density == WidgetViewportDensity.Expanded ? 440 : 360;
            ChartHeight = Density == WidgetViewportDensity.Expanded ? 90 : 60;

            var history = Projection?.ScoreHistory ?? new List<ShowcaseScorePoint>();
            var showChart = Density != WidgetViewportDensity.Compact && history.Count >= 2;
            if (Cards.Count > 0 &&
                ReferenceEquals(_builtHistory, history) &&
                ReferenceEquals(_builtSnapshot, Projection?.Snapshot) &&
                _builtMode == mode &&
                _builtShowChart == showChart)
            {
                return;
            }

            _builtHistory = history;
            _builtSnapshot = Projection?.Snapshot;
            _builtMode = mode;
            _builtShowChart = showChart;

            var rangeCaption = TimelineRangeText.Describe(
                ShowcaseTimelineOptions.GetRange(Projection?.Instance));
            var culture = FormattingCulture.Current;
            var historyLabels = history
                .Select(point => point.Date.ToString("d", culture))
                .ToList();
            var historyStart = history.Count > 0 ? historyLabels[0] : string.Empty;
            var historyEnd = history.Count > 0 ? historyLabels[historyLabels.Count - 1] : string.Empty;

            var uniformBadges = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?
                .UseUniformRarityBadges ?? false;
            var cards = new List<ScoreCardWithHistoryViewModel>();
            if (includeCollection)
            {
                var card = new ScoreCardViewModel(ScoreCardType.Collection);
                card.Apply(
                    snapshot.CollectorScore,
                    snapshot.CollectorLevel,
                    snapshot.CollectorLevelProgress,
                    snapshot.CollectorRank,
                    uniformBadges);
                cards.Add(new ScoreCardWithHistoryViewModel(
                    card,
                    new ChartValues<int>(history.Select(point => point.CollectionScore)),
                    historyLabels,
                    showChart,
                    rangeCaption,
                    historyStart,
                    historyEnd));
            }

            if (includePrestige)
            {
                var card = new ScoreCardViewModel(ScoreCardType.Prestige);
                card.Apply(
                    snapshot.PrestigeScore,
                    snapshot.PrestigeLevel,
                    snapshot.PrestigeLevelProgress,
                    snapshot.PrestigeRank,
                    uniformBadges);
                cards.Add(new ScoreCardWithHistoryViewModel(
                    card,
                    new ChartValues<int>(history.Select(point => point.PrestigeScore)),
                    historyLabels,
                    showChart,
                    rangeCaption,
                    historyStart,
                    historyEnd));
            }

            Cards.ReplaceAll(cards);
        }

    }
}

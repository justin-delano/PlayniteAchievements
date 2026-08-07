using System.Collections.Generic;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the Scores widget by reusing the existing <see cref="ScoreCardViewModel"/> /
    /// ScoreCardControl. Shows the collection and/or prestige card per the score mode, laid out in
    /// a UniformGrid whose orientation follows the viewport, and featured outside compact.
    /// </summary>
    public sealed class ScoresWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private int _rows = 1;
        private int _columns = 1;
        private bool _isFeatured = true;
        private double _maxCardWidth = 360;

        public BulkObservableCollection<ScoreCardViewModel> Cards { get; } =
            new BulkObservableCollection<ScoreCardViewModel>();

        public int Rows { get => _rows; private set => SetValue(ref _rows, value); }

        public int Columns { get => _columns; private set => SetValue(ref _columns, value); }

        public bool IsFeatured { get => _isFeatured; private set => SetValue(ref _isFeatured, value); }

        public double MaxCardWidth { get => _maxCardWidth; private set => SetValue(ref _maxCardWidth, value); }

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

            var uniformBadges = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?
                .UseUniformRarityBadges ?? false;
            var cards = new List<ScoreCardViewModel>();
            if (includeCollection)
            {
                var card = new ScoreCardViewModel(ScoreCardType.Collection);
                card.Apply(
                    snapshot.CollectorScore,
                    snapshot.CollectorLevel,
                    snapshot.CollectorLevelProgress,
                    snapshot.CollectorRank,
                    uniformBadges);
                cards.Add(card);
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
                cards.Add(card);
            }

            Cards.ReplaceAll(cards);
        }
    }
}

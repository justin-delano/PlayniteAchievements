using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the pinned games widget: the shared game-summaries grid over the pinned
    /// (or Playnite-favorite) games. Pin reordering is only offered when the widget
    /// draws from showcase pins, whose order is user-controlled.
    /// </summary>
    public sealed class FavoriteGamesWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private bool _showColumnHeaders = true;
        private double? _rowHeight;
        private bool _pinReorderEnabled;

        public BulkObservableCollection<GameSummaryItem> Items { get; } =
            new BulkObservableCollection<GameSummaryItem>();

        public bool ShowColumnHeaders
        {
            get => _showColumnHeaders;
            private set => SetValue(ref _showColumnHeaders, value);
        }

        public double? RowHeight
        {
            get => _rowHeight;
            private set => SetValue(ref _rowHeight, value);
        }

        public bool PinReorderEnabled
        {
            get => _pinReorderEnabled;
            private set => SetValue(ref _pinReorderEnabled, value);
        }

        protected override void Refresh()
        {
            ShowColumnHeaders = Density != WidgetViewportDensity.Compact;
            RowHeight = Density == WidgetViewportDensity.Compact ? 32d : (double?)null;
            PinReorderEnabled = ShowcaseWidgetOptions.GetFavoriteSource(Projection?.Instance) ==
                ShowcaseFavoriteGameSource.ShowcasePins;
            Items.ReplaceAll(Projection?.Games ?? Array.Empty<GameSummaryItem>());
        }
    }
}

using System.Collections.Generic;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the pinned games widget: the shared game-summaries grid over the pinned
    /// (or Playnite-favorite) games. Pin reordering is only offered when the widget
    /// draws from showcase pins, whose order is user-controlled.
    /// </summary>
    public sealed class FavoriteGamesWidgetViewModel
        : ShowcaseGridWidgetViewModelBase<GameSummaryItem>
    {
        private bool _pinReorderEnabled;

        public bool PinReorderEnabled
        {
            get => _pinReorderEnabled;
            private set => SetValue(ref _pinReorderEnabled, value);
        }

        protected override string BaseSurfaceKey => ShowcaseGridSurfaces.PinnedGames;

        protected override double CompactRowHeight => 32d;

        // One instance per dashboard, so the pins keep a single stable column layout.
        protected override bool UsesPerInstanceSurface => false;

        protected override IEnumerable<GameSummaryItem> SelectItems(
            ShowcaseWidgetProjection projection)
        {
            PinReorderEnabled = ShowcaseWidgetOptions.GetFavoriteSource(projection?.Instance) ==
                ShowcaseFavoriteGameSource.ShowcasePins;
            return projection?.Games;
        }
    }
}

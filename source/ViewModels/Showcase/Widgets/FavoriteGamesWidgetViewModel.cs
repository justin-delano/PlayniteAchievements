using System.Collections.Generic;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs a pinned-games widget instance: its game-summaries grid renders the selected
    /// pin collection (or Playnite favorites). Pin reordering is only offered when the widget
    /// draws from showcase pins, whose order is user-controlled.
    /// </summary>
    public sealed class FavoriteGamesWidgetViewModel
        : ShowcaseGameGridWidgetViewModelBase
    {
        private bool _pinReorderEnabled;

        public bool PinReorderEnabled
        {
            get => _pinReorderEnabled;
            private set => SetValue(ref _pinReorderEnabled, value);
        }

        protected override string BaseSurfaceKey => ShowcaseGridSurfaces.PinnedGames;

        protected override GameSummariesSortMode DefaultSortMode => GameSummariesSortMode.PinOrder;

        protected override IEnumerable<GameSummaryItem> SelectItems(
            ShowcaseWidgetProjection projection)
        {
            PinReorderEnabled = ShowcaseWidgetOptions.GetFavoriteSource(projection?.Instance) ==
                ShowcaseFavoriteGameSource.ShowcasePins;
            return projection?.Games;
        }
    }
}

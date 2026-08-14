using System;
using System.Linq;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the GameMosaic widget: a dense wrap of game cover tiles. Tiles offer
    /// reorder/unpin context actions only when the source is showcase pins.
    /// </summary>
    public sealed class GameMosaicWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        public BulkObservableCollection<GameTileViewModel> Tiles { get; } =
            new BulkObservableCollection<GameTileViewModel>();

        protected override void Refresh()
        {
            var games = Projection?.Games ?? Array.Empty<GameSummaryItem>();
            var coverWidth = Density == WidgetViewportDensity.Compact
                ? 44d
                : Density == WidgetViewportDensity.Expanded ? 72d : 56d;
            var coverHeight = Math.Round(coverWidth * 1.4);
            var decodePixel = Math.Max(64, (int)Math.Ceiling(coverHeight * 2));
            var pinnable = ShowcaseWidgetOptions.GetGameMosaicSource(Projection?.Instance) ==
                ShowcaseGameMosaicSource.Pinned;
            Tiles.ReplaceAll(games
                .Select(game => new GameTileViewModel(
                    game,
                    pinnable,
                    coverWidth,
                    coverHeight,
                    decodePixel)));
        }
    }
}

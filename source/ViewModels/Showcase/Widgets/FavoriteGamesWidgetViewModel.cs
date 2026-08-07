using System;
using System.Linq;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// A favorite/pinned game cover tile. When the widget draws from showcase pins, the tile
    /// exposes reorder/unpin commands surfaced through a context menu; Playnite-favorites tiles
    /// are read-only.
    /// </summary>
    public sealed class GameTileViewModel
    {
        private readonly Guid? _gameId;

        public GameTileViewModel(
            GameSummaryItem game,
            bool showName,
            bool pinnable,
            double coverWidth,
            double coverHeight,
            double tileWidth,
            int decodePixel)
        {
            _gameId = game.PlayniteGameId;
            HasCover = !string.IsNullOrWhiteSpace(game.GameCoverPath);
            CoverPath = HasCover ? game.GameCoverPath : game.GameLogo;
            CoverWidth = coverWidth;
            CoverHeight = coverHeight;
            TileWidth = tileWidth;
            DecodePixel = decodePixel;
            GameName = game.GameName;
            ShowName = showName;
            IsPinnable = pinnable && game.PlayniteGameId.HasValue;

            MoveEarlierCommand = new RelayCommand(_ => Move(-1));
            MoveLaterCommand = new RelayCommand(_ => Move(1));
            UnpinCommand = new RelayCommand(_ => Unpin());
        }

        public string CoverPath { get; }

        public bool HasCover { get; }

        public double CoverWidth { get; }

        public double CoverHeight { get; }

        public double TileWidth { get; }

        public int DecodePixel { get; }

        public string GameName { get; }

        public bool ShowName { get; }

        public bool IsPinnable { get; }

        public RelayCommand MoveEarlierCommand { get; }

        public RelayCommand MoveLaterCommand { get; }

        public RelayCommand UnpinCommand { get; }

        private static ShowcaseSettings Settings =>
            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;

        private void Move(int direction)
        {
            if (_gameId.HasValue &&
                ShowcasePinService.MoveGame(Settings, _gameId.Value, direction))
            {
                ShowcaseConfigurationCommit.Commit();
            }
        }

        private void Unpin()
        {
            if (!_gameId.HasValue)
            {
                return;
            }

            ShowcasePinService.ToggleGame(Settings, _gameId.Value);
            ShowcaseConfigurationCommit.Commit();
        }
    }

    /// <summary>
    /// Backs the FavoriteGames widget: a centered wrap of cover tiles sized by density (compact
    /// shows 3 smaller covers with no name; otherwise up to 12).
    /// </summary>
    public sealed class FavoriteGamesWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        public BulkObservableCollection<GameTileViewModel> Tiles { get; } =
            new BulkObservableCollection<GameTileViewModel>();

        protected override void Refresh()
        {
            var games = Projection?.Games ?? Array.Empty<GameSummaryItem>();
            var compact = Density == WidgetViewportDensity.Compact;
            var coverWidth = compact ? 42 : Density == WidgetViewportDensity.Expanded ? 76 : 60;
            var coverHeight = Math.Round(coverWidth * 1.4);
            var tileWidth = coverWidth + 12;
            var decodePixel = Math.Max(64, (int)Math.Ceiling(Math.Max(coverWidth, coverHeight) * 2));
            var showName = !compact;
            var pinnable = ShowcaseWidgetOptions.GetFavoriteSource(Projection?.Instance) ==
                ShowcaseFavoriteGameSource.ShowcasePins;
            var limit = compact ? 3 : 12;
            Tiles.ReplaceAll(games
                .Take(limit)
                .Select(game => new GameTileViewModel(
                    game,
                    showName,
                    pinnable,
                    coverWidth,
                    coverHeight,
                    tileWidth,
                    decodePixel)));
        }
    }
}

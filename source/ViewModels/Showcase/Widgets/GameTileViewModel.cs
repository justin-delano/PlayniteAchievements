using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// A game cover tile. When the hosting widget draws from showcase pins, the tile
    /// exposes reorder/unpin commands surfaced through a context menu; other sources
    /// render read-only tiles.
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

            HasProgress = showName && game.TotalAchievements > 0;
            ProgressFraction = game.TotalAchievements > 0
                ? (double)game.UnlockedAchievements / game.TotalAchievements
                : 0;
            ProgressText = game.ProgressionCountText;
            IsCompleted = game.IsCompleted;

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

        public bool HasProgress { get; }

        public double ProgressFraction { get; }

        public string ProgressText { get; }

        public bool IsCompleted { get; }

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
}

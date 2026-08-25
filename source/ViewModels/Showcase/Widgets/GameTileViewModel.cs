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
        private readonly string _pinCollectionId;

        public GameTileViewModel(
            GameSummaryItem game,
            bool pinnable,
            string pinCollectionId,
            double coverWidth,
            double coverHeight,
            int decodePixel,
            bool useCovers = true,
            bool showCompletionGlow = false)
        {
            _gameId = game.PlayniteGameId;
            _pinCollectionId = pinCollectionId;
            // Icon tiles keep Uniform stretch (HasCover false) so icons are never cropped;
            // cover art fills its tile.
            var cover = game.GameCoverPath;
            var icon = game.GameLogo;
            HasCover = useCovers && !string.IsNullOrWhiteSpace(cover);
            CoverPath = useCovers
                ? (HasCover ? cover : icon)
                : (!string.IsNullOrWhiteSpace(icon) ? icon : cover);
            CoverWidth = coverWidth;
            CoverHeight = coverHeight;
            DecodePixel = decodePixel;
            GameName = game.GameName;
            IsPinnable = pinnable && game.PlayniteGameId.HasValue;
            ShowCompletionGlow = showCompletionGlow && game.IsCompleted;

            MoveEarlierCommand = new RelayCommand(_ => Move(-1));
            MoveLaterCommand = new RelayCommand(_ => Move(1));
            UnpinCommand = new RelayCommand(_ => Unpin());
        }

        public string CoverPath { get; }

        public bool HasCover { get; }

        public double CoverWidth { get; }

        public double CoverHeight { get; }

        public int DecodePixel { get; }

        public string GameName { get; }

        public bool IsPinnable { get; }

        /// <summary>True when the tile's game is completed and the widget shows the glow.</summary>
        public bool ShowCompletionGlow { get; }

        public RelayCommand MoveEarlierCommand { get; }

        public RelayCommand MoveLaterCommand { get; }

        public RelayCommand UnpinCommand { get; }

        private static ShowcaseSettings Settings =>
            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;

        private void Move(int direction)
        {
            if (_gameId.HasValue &&
                ShowcasePinService.MoveGame(
                    Settings,
                    _pinCollectionId,
                    _gameId.Value,
                    direction))
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

            ShowcasePinService.ToggleGame(Settings, _pinCollectionId, _gameId.Value);
            ShowcaseConfigurationCommit.Commit();
        }
    }
}

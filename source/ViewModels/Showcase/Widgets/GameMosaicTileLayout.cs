using System;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// The inputs every Game Mosaic tile is built from; a change to any of them rebuilds the tiles
    /// (see GameMosaicWidgetViewModel.RefreshLayout).
    /// </summary>
    internal sealed class GameMosaicTileLayout : IEquatable<GameMosaicTileLayout>
    {
        public GameMosaicTileLayout(
            bool pinnable,
            string pinCollectionId,
            double coverWidth,
            double coverHeight,
            int decodePixel,
            bool useCovers,
            bool showCompletionGlow,
            int spacing,
            bool showRarityBar,
            bool showCompletionFrame)
        {
            Pinnable = pinnable;
            PinCollectionId = pinCollectionId;
            CoverWidth = coverWidth;
            CoverHeight = coverHeight;
            DecodePixel = decodePixel;
            UseCovers = useCovers;
            ShowCompletionGlow = showCompletionGlow;
            Spacing = spacing;
            ShowRarityBar = showRarityBar;
            ShowCompletionFrame = showCompletionFrame;
        }

        public bool Pinnable { get; }
        public string PinCollectionId { get; }
        public double CoverWidth { get; }
        public double CoverHeight { get; }
        public int DecodePixel { get; }
        public bool UseCovers { get; }
        public bool ShowCompletionGlow { get; }
        public int Spacing { get; }
        public bool ShowRarityBar { get; }
        public bool ShowCompletionFrame { get; }

        public bool Equals(GameMosaicTileLayout other) =>
            other != null &&
            Pinnable == other.Pinnable &&
            string.Equals(PinCollectionId, other.PinCollectionId, StringComparison.Ordinal) &&
            CoverWidth.Equals(other.CoverWidth) &&
            CoverHeight.Equals(other.CoverHeight) &&
            DecodePixel == other.DecodePixel &&
            UseCovers == other.UseCovers &&
            ShowCompletionGlow == other.ShowCompletionGlow &&
            Spacing == other.Spacing &&
            ShowRarityBar == other.ShowRarityBar &&
            ShowCompletionFrame == other.ShowCompletionFrame;

        public override bool Equals(object obj) => Equals(obj as GameMosaicTileLayout);

        public override int GetHashCode() => DecodePixel ^ Spacing ^ CoverWidth.GetHashCode();
    }
}

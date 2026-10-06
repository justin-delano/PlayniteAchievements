using System;
using System.Windows;

namespace PlayniteAchievements.Views.Helpers.Gif
{
    /// <summary>
    /// A frame source <see cref="GifPlayer"/> can play: renders frames in sequence into one Bgra32
    /// buffer and reports how long each stays on screen. Not thread-safe: one owner renders at a time.
    /// </summary>
    internal interface IFrameCanvas : IDisposable
    {
        int Width { get; }

        int Height { get; }

        int FrameCount { get; }

        /// <summary>Row-major Bgra32 pixels, <see cref="Width"/> per row.</summary>
        int[] Pixels { get; }

        /// <summary>The region the last <see cref="Render"/> changed.</summary>
        Int32Rect DirtyRect { get; }

        int RenderedIndex { get; }

        int GetDelayMs(int frameIndex);

        /// <summary>
        /// Draws <paramref name="frameIndex"/>. Frames are expected in sequence; an index at or before
        /// the previous one restarts from the first frame, which is how a loop begins.
        /// </summary>
        void Render(int frameIndex);
    }
}

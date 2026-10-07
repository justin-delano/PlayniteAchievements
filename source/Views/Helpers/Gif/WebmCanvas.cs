using System.Windows;
using PlayniteAchievements.Services.Images.Webm;

namespace PlayniteAchievements.Views.Helpers.Gif
{
    /// <summary>
    /// Plays a WebM through <see cref="GifPlayer"/>. Each frame is decoded when it is rendered and
    /// replaces the whole canvas, so memory stays at one frame however long the file runs.
    /// </summary>
    internal sealed class WebmCanvas : IFrameCanvas
    {
        private readonly WebmImage _image;
        private readonly WebmFrameDecoder _decoder;

        internal WebmCanvas(WebmImage image)
        {
            _image = image;
            _decoder = new WebmFrameDecoder(image);
        }

        public int Width => _image.Width;

        public int Height => _image.Height;

        public int FrameCount => _image.Frames.Length;

        public int[] Pixels => _decoder.Pixels;

        public Int32Rect DirtyRect { get; private set; } = Int32Rect.Empty;

        public int RenderedIndex => _decoder.DecodedIndex;

        public int GetDelayMs(int frameIndex)
        {
            return _image.Frames[frameIndex].DelayMs;
        }

        public void Render(int frameIndex)
        {
            _decoder.Decode(frameIndex);
            DirtyRect = new Int32Rect(0, 0, _image.Width, _image.Height);
        }

        public void Dispose()
        {
            _decoder.Dispose();
        }
    }
}

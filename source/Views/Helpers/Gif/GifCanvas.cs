using System;
using System.Windows;

namespace PlayniteAchievements.Views.Helpers.Gif
{
    /// <summary>
    /// Composites GIF frames, in order, onto one Bgra32 canvas and reports the region each frame
    /// changed. Every buffer is allocated up front (the restore-previous save area on first use),
    /// so steady-state playback allocates nothing. Not thread-safe: one owner renders at a time.
    /// </summary>
    internal sealed class GifCanvas : IFrameCanvas
    {
        private static readonly int[][] InterlacePasses =
        {
            new[] { 0, 8 },
            new[] { 4, 8 },
            new[] { 2, 4 },
            new[] { 1, 2 }
        };

        private readonly GifImage _image;
        private readonly int[] _pixels;
        private readonly byte[] _indices;
        private readonly GifLzwDecoder _lzw = new GifLzwDecoder();

        private int[] _saved;
        private Int32Rect _savedRect = Int32Rect.Empty;
        private int _previousIndex = -1;

        internal GifCanvas(GifImage image)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            _pixels = new int[image.Width * image.Height];
            _indices = new byte[Math.Max(1, image.MaxFramePixels)];
        }

        internal GifImage Image => _image;

        public int Width => _image.Width;

        public int Height => _image.Height;

        public int FrameCount => _image.Frames.Length;

        /// <summary>Row-major Bgra32 pixels, <see cref="GifImage.Width"/> per row.</summary>
        public int[] Pixels => _pixels;

        /// <summary>The canvas region the last <see cref="Render"/> changed.</summary>
        public Int32Rect DirtyRect { get; private set; } = Int32Rect.Empty;

        public int RenderedIndex => _previousIndex;

        public int GetDelayMs(int frameIndex)
        {
            return _image.Frames[frameIndex].DelayMs;
        }

        /// <summary>
        /// Draws <paramref name="frameIndex"/>. Frames are expected in sequence; any index at or
        /// before the previous one restarts from a cleared canvas, which is how a loop begins.
        /// </summary>
        public void Render(int frameIndex)
        {
            var frames = _image.Frames;
            var frame = frames[frameIndex];
            var dirty = Int32Rect.Empty;

            if (_previousIndex < 0 || frameIndex <= _previousIndex)
            {
                Array.Clear(_pixels, 0, _pixels.Length);
                _savedRect = Int32Rect.Empty;
                dirty = new Int32Rect(0, 0, _image.Width, _image.Height);
            }
            else
            {
                var previous = frames[_previousIndex];
                if (previous.Disposal == GifDisposal.RestoreBackground)
                {
                    var rect = Clip(previous);
                    Clear(rect);
                    dirty = Union(dirty, rect);
                }
                else if (previous.Disposal == GifDisposal.RestorePrevious && !_savedRect.IsEmpty)
                {
                    CopyRegion(_saved, _savedRect, toCanvas: true);
                    dirty = Union(dirty, _savedRect);
                    _savedRect = Int32Rect.Empty;
                }
            }

            var frameRect = Clip(frame);
            if (frame.Disposal == GifDisposal.RestorePrevious && !frameRect.IsEmpty)
            {
                var area = frameRect.Width * frameRect.Height;
                if (_saved == null || _saved.Length < area)
                {
                    _saved = new int[area];
                }

                CopyRegion(_saved, frameRect, toCanvas: false);
                _savedRect = frameRect;
            }

            if (!frameRect.IsEmpty)
            {
                Draw(frame, frameRect);
                dirty = Union(dirty, frameRect);
            }

            DirtyRect = dirty;
            _previousIndex = frameIndex;
        }

        private void Draw(GifFrame frame, Int32Rect clipped)
        {
            var pixelCount = frame.Width * frame.Height;
            var decoded = _lzw.Decode(
                _image.Payload,
                frame.DataOffset,
                frame.LzwMinimumCodeSize,
                _indices,
                pixelCount);
            if (decoded <= 0)
            {
                return;
            }

            if (!frame.Interlaced)
            {
                for (var row = 0; row < clipped.Height; row++)
                {
                    if (!DrawRow(frame, clipped, row, row * frame.Width, decoded))
                    {
                        return;
                    }
                }

                return;
            }

            // Interlaced rows arrive in four passes; streamRow counts rows in arrival order.
            var streamRow = 0;
            foreach (var pass in InterlacePasses)
            {
                for (var row = pass[0]; row < frame.Height; row += pass[1], streamRow++)
                {
                    if (row >= clipped.Height)
                    {
                        continue;
                    }

                    if (!DrawRow(frame, clipped, row, streamRow * frame.Width, decoded))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>Returns false once the decoded indices run out.</summary>
        private bool DrawRow(GifFrame frame, Int32Rect clipped, int row, int source, int decoded)
        {
            if (source >= decoded)
            {
                return false;
            }

            var count = Math.Min(clipped.Width, decoded - source);
            var target = (frame.Top + row) * _image.Width + frame.Left;
            var palette = frame.Palette;
            var indices = _indices;
            var pixels = _pixels;
            var transparent = frame.TransparentIndex;

            if (transparent < 0)
            {
                for (var x = 0; x < count; x++)
                {
                    pixels[target + x] = palette[indices[source + x]];
                }
            }
            else
            {
                for (var x = 0; x < count; x++)
                {
                    var index = indices[source + x];
                    if (index != transparent)
                    {
                        pixels[target + x] = palette[index];
                    }
                }
            }

            return true;
        }

        private Int32Rect Clip(GifFrame frame)
        {
            var width = Math.Min(frame.Width, _image.Width - frame.Left);
            var height = Math.Min(frame.Height, _image.Height - frame.Top);
            return width > 0 && height > 0
                ? new Int32Rect(frame.Left, frame.Top, width, height)
                : Int32Rect.Empty;
        }

        private void Clear(Int32Rect rect)
        {
            for (var row = 0; row < rect.Height; row++)
            {
                Array.Clear(_pixels, (rect.Y + row) * _image.Width + rect.X, rect.Width);
            }
        }

        private void CopyRegion(int[] buffer, Int32Rect rect, bool toCanvas)
        {
            for (var row = 0; row < rect.Height; row++)
            {
                var canvasOffset = (rect.Y + row) * _image.Width + rect.X;
                var bufferOffset = row * rect.Width;
                if (toCanvas)
                {
                    Array.Copy(buffer, bufferOffset, _pixels, canvasOffset, rect.Width);
                }
                else
                {
                    Array.Copy(_pixels, canvasOffset, buffer, bufferOffset, rect.Width);
                }
            }
        }

        private static Int32Rect Union(Int32Rect a, Int32Rect b)
        {
            if (a.IsEmpty)
            {
                return b;
            }

            if (b.IsEmpty)
            {
                return a;
            }

            var left = Math.Min(a.X, b.X);
            var top = Math.Min(a.Y, b.Y);
            var right = Math.Max(a.X + a.Width, b.X + b.Width);
            var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new Int32Rect(left, top, right - left, bottom - top);
        }

        /// <summary>Holds only managed buffers.</summary>
        public void Dispose()
        {
        }
    }
}

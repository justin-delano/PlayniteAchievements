using System;

namespace PlayniteAchievements.Services.Images.Webm
{
    /// <summary>
    /// Turns the frames of one <see cref="WebmImage"/> into Bgra32 pixels with straight alpha.
    /// The color stream and the alpha stream each run through their own decoder; the alpha
    /// decoder's luma plane is the alpha channel. Frames decode in sequence, so asking for an
    /// earlier frame than the last one restarts from the first frame, which is how a loop begins.
    /// Not thread-safe: one owner decodes at a time.
    /// </summary>
    internal sealed class WebmFrameDecoder : IDisposable
    {
        private readonly WebmImage _image;
        private readonly WebmVideoDecoder _color;
        private readonly WebmVideoDecoder _alpha;
        private readonly int[] _pixels;

        internal WebmFrameDecoder(WebmImage image)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            _pixels = new int[image.Width * image.Height];
            _color = new WebmVideoDecoder(image.Codec, image.Width, image.Height);
            try
            {
                if (Array.Exists(image.Frames, frame => frame.HasAlpha))
                {
                    _alpha = new WebmVideoDecoder(image.Codec, image.Width, image.Height);
                }
            }
            catch
            {
                _color.Dispose();
                throw;
            }
        }

        internal WebmImage Image => _image;

        /// <summary>Row-major Bgra32 pixels, <see cref="WebmImage.Width"/> per row.</summary>
        internal int[] Pixels => _pixels;

        internal int DecodedIndex { get; private set; } = -1;

        internal void Decode(int frameIndex)
        {
            var start = DecodedIndex + 1;
            if (DecodedIndex >= 0 && frameIndex <= DecodedIndex)
            {
                _color.Reset();
                _alpha?.Reset();
                start = 0;
            }

            var frames = _image.Frames;
            var payload = _image.Payload;
            var hasAlpha = false;
            for (var index = start; index <= frameIndex; index++)
            {
                var frame = frames[index];
                _color.Decode(payload, frame.ColorOffset, frame.ColorLength);
                hasAlpha = _alpha != null && frame.HasAlpha;
                if (hasAlpha)
                {
                    _alpha.Decode(payload, frame.AlphaOffset, frame.AlphaLength);
                }

                DecodedIndex = index;
            }

            Convert(hasAlpha);
        }

        /// <summary>NV12 (BT.601, limited range) to Bgra32, cropped to the frame's own size.</summary>
        private void Convert(bool hasAlpha)
        {
            var width = Math.Min(_image.Width, _color.PlaneWidth);
            var height = Math.Min(_image.Height, _color.PlaneHeight);
            var nv12 = _color.Nv12Buffer;
            var stride = _color.Stride;
            var chromaStart = stride * _color.PlaneHeight;

            var alpha = hasAlpha ? _alpha.Nv12Buffer : null;
            var alphaStride = hasAlpha ? _alpha.Stride : 0;
            if (hasAlpha)
            {
                width = Math.Min(width, _alpha.PlaneWidth);
                height = Math.Min(height, _alpha.PlaneHeight);
            }

            var pixels = _pixels;
            var rowWidth = _image.Width;
            for (var y = 0; y < height; y++)
            {
                var lumaRow = y * stride;
                var chromaRow = chromaStart + (y >> 1) * stride;
                var alphaRow = y * alphaStride;
                var target = y * rowWidth;
                for (var x = 0; x < width; x++)
                {
                    var c = 298 * (nv12[lumaRow + x] - 16);
                    var chroma = chromaRow + (x & ~1);
                    var d = nv12[chroma] - 128;
                    var e = nv12[chroma + 1] - 128;
                    var r = Clamp((c + 409 * e + 128) >> 8);
                    var g = Clamp((c - 100 * d - 208 * e + 128) >> 8);
                    var b = Clamp((c + 516 * d + 128) >> 8);
                    var a = alpha != null ? alpha[alphaRow + x] : 255;
                    pixels[target + x] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }
        }

        private static int Clamp(int value)
        {
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }

        public void Dispose()
        {
            _color.Dispose();
            _alpha?.Dispose();
        }
    }
}

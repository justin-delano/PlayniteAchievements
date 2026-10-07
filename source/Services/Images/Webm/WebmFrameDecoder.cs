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
        private byte[] _alphaBytes;

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

        /// <summary>
        /// YUV (BT.601, limited range) to Bgra32, cropped to the frame's own size. Each chroma
        /// sample is read once for its pair of pixels and every fixed-point term comes from a
        /// table, so the inner loop is lookups and adds.
        /// </summary>
        private void Convert(bool hasAlpha)
        {
            var width = Math.Min(_image.Width, _color.PlaneWidth);
            var height = Math.Min(_image.Height, _color.PlaneHeight);
            byte[] alpha = null;
            var alphaStride = 0;
            if (hasAlpha)
            {
                width = Math.Min(width, _alpha.PlaneWidth);
                height = Math.Min(height, _alpha.PlaneHeight);
                alpha = AlphaPlane(height, out alphaStride);
            }

            if (_color.IsTenBit)
            {
                ConvertTenBit(width, height, alpha, alphaStride);
            }
            else
            {
                ConvertEightBit(width, height, alpha, alphaStride);
            }
        }

        /// <summary>The alpha decoder's luma plane as one byte per sample.</summary>
        private byte[] AlphaPlane(int height, out int stride)
        {
            if (!_alpha.IsTenBit)
            {
                stride = _alpha.Stride;
                return _alpha.Planes;
            }

            var planes = _alpha.Planes;
            var width = _alpha.PlaneWidth;
            if (_alphaBytes == null || _alphaBytes.Length < width * height)
            {
                _alphaBytes = new byte[width * height];
            }

            for (var y = 0; y < height; y++)
            {
                var source = y * _alpha.Stride;
                var target = y * width;
                for (var x = 0; x < width; x++)
                {
                    var sample = (planes[source + 2 * x] | (planes[source + 2 * x + 1] << 8)) >> 6;
                    _alphaBytes[target + x] = (byte)((sample * 255 + 511) / 1023);
                }
            }

            stride = width;
            return _alphaBytes;
        }

        private void ConvertEightBit(int width, int height, byte[] alpha, int alphaStride)
        {
            var nv12 = _color.Planes;
            var stride = _color.Stride;
            var chromaStart = stride * _color.PlaneHeight;
            var pixels = _pixels;
            var rowWidth = _image.Width;
            var luma = Tables.Luma;
            var redV = Tables.RedV;
            var greenU = Tables.GreenU;
            var greenV = Tables.GreenV;
            var blueU = Tables.BlueU;
            var clip = Tables.Clip;
            for (var y = 0; y < height; y++)
            {
                var lumaRow = y * stride;
                var chromaRow = chromaStart + (y >> 1) * stride;
                var alphaRow = y * alphaStride;
                var target = y * rowWidth;
                for (var x = 0; x < width; x += 2)
                {
                    var u = nv12[chromaRow + x];
                    var v = nv12[chromaRow + x + 1];
                    var red = redV[v];
                    var green = greenU[u] + greenV[v];
                    var blue = blueU[u];

                    // Alpha in the top byte; opaque frames use 255 for every pixel.
                    var l = luma[nv12[lumaRow + x]];
                    var a = alpha == null ? unchecked((int)0xFF000000) : alpha[alphaRow + x] << 24;
                    pixels[target + x] = a |
                                         (clip[(l + red) >> 8] << 16) |
                                         (clip[(l + green) >> 8] << 8) |
                                         clip[(l + blue) >> 8];

                    if (x + 1 < width)
                    {
                        l = luma[nv12[lumaRow + x + 1]];
                        a = alpha == null ? unchecked((int)0xFF000000) : alpha[alphaRow + x + 1] << 24;
                        pixels[target + x + 1] = a |
                                                 (clip[(l + red) >> 8] << 16) |
                                                 (clip[(l + green) >> 8] << 8) |
                                                 clip[(l + blue) >> 8];
                    }
                }
            }
        }

        /// <summary>
        /// P010 to Bgra32. The 10-bit color carries two bits below what a byte can hold; an ordered
        /// dither turns them into a fine fixed pattern instead of dropping them, so a smooth gradient
        /// keeps its in-between shades rather than stepping into bands. The pattern is tied to the
        /// pixel position, so it stays still while the image animates.
        /// </summary>
        private void ConvertTenBit(int width, int height, byte[] alpha, int alphaStride)
        {
            var p010 = _color.Planes;
            var stride = _color.Stride;
            var chromaStart = stride * _color.PlaneHeight;
            var pixels = _pixels;
            var rowWidth = _image.Width;
            var luma = Tables.Luma10;
            var redV = Tables.RedV10;
            var greenU = Tables.GreenU10;
            var greenV = Tables.GreenV10;
            var blueU = Tables.BlueU10;
            var clip = Tables.Clip;
            var dither = Tables.Dither;
            for (var y = 0; y < height; y++)
            {
                var lumaRow = y * stride;
                var chromaRow = chromaStart + (y >> 1) * stride;
                var alphaRow = y * alphaStride;
                var target = y * rowWidth;
                var ditherRow = (y & 7) << 3;
                for (var x = 0; x < width; x += 2)
                {
                    var chroma = chromaRow + 2 * x;
                    var u = (p010[chroma] | (p010[chroma + 1] << 8)) >> 6;
                    var v = (p010[chroma + 2] | (p010[chroma + 3] << 8)) >> 6;
                    var red = redV[v];
                    var green = greenU[u] + greenV[v];
                    var blue = blueU[u];

                    var sample = lumaRow + 2 * x;
                    var l = luma[(p010[sample] | (p010[sample + 1] << 8)) >> 6] + dither[ditherRow + (x & 7)];
                    var a = alpha == null ? unchecked((int)0xFF000000) : alpha[alphaRow + x] << 24;
                    pixels[target + x] = a |
                                         (clip[(l + red) >> 12] << 16) |
                                         (clip[(l + green) >> 12] << 8) |
                                         clip[(l + blue) >> 12];

                    if (x + 1 < width)
                    {
                        l = luma[(p010[sample + 2] | (p010[sample + 3] << 8)) >> 6] + dither[ditherRow + ((x + 1) & 7)];
                        a = alpha == null ? unchecked((int)0xFF000000) : alpha[alphaRow + x + 1] << 24;
                        pixels[target + x + 1] = a |
                                                 (clip[(l + red) >> 12] << 16) |
                                                 (clip[(l + green) >> 12] << 8) |
                                                 clip[(l + blue) >> 12];
                    }
                }
            }
        }

        /// <summary>
        /// BT.601 limited-range fixed-point terms. The 8-bit tables are scaled by 256 with rounding
        /// folded into luma; the 10-bit tables are scaled by 4096 and leave rounding to the dither.
        /// </summary>
        private static class Tables
        {
            /// <summary>Added to every index into <see cref="Clip"/> so negative sums stay in range.</summary>
            private const int ClipOffset = 384;

            internal static readonly int[] Luma = Build(256, value => 298 * (value - 16) + 128 + (ClipOffset << 8));
            internal static readonly int[] RedV = Build(256, value => 409 * (value - 128));
            internal static readonly int[] GreenU = Build(256, value => -100 * (value - 128));
            internal static readonly int[] GreenV = Build(256, value => -208 * (value - 128));
            internal static readonly int[] BlueU = Build(256, value => 516 * (value - 128));

            // 10-bit samples span four times the 8-bit range, hence the division by 4 into byte units.
            internal static readonly int[] Luma10 = Build(1024, value => Scale12(1.164383 * (value - 64) / 4) + (ClipOffset << 12));
            internal static readonly int[] RedV10 = Build(1024, value => Scale12(1.596027 * (value - 512) / 4));
            internal static readonly int[] GreenU10 = Build(1024, value => Scale12(-0.391762 * (value - 512) / 4));
            internal static readonly int[] GreenV10 = Build(1024, value => Scale12(-0.812968 * (value - 512) / 4));
            internal static readonly int[] BlueU10 = Build(1024, value => Scale12(2.017232 * (value - 512) / 4));

            /// <summary>8x8 Bayer thresholds, centered in each of 64 steps of one byte level (scaled by 4096).</summary>
            internal static readonly int[] Dither = BuildDither();

            /// <summary>Saturates (sum >> 8 or sum >> 12) to a byte; spans the full range the terms above can reach.</summary>
            internal static readonly int[] Clip = BuildClip();

            private static int Scale12(double value)
            {
                return (int)Math.Round(value * 4096);
            }

            private static int[] Build(int size, Func<int, int> term)
            {
                var table = new int[size];
                for (var value = 0; value < size; value++)
                {
                    table[value] = term(value);
                }

                return table;
            }

            private static int[] BuildDither()
            {
                int[] bayer =
                {
                    0, 32, 8, 40, 2, 34, 10, 42,
                    48, 16, 56, 24, 50, 18, 58, 26,
                    12, 44, 4, 36, 14, 46, 6, 38,
                    60, 28, 52, 20, 62, 30, 54, 22,
                    3, 35, 11, 43, 1, 33, 9, 41,
                    51, 19, 59, 27, 49, 17, 57, 25,
                    15, 47, 7, 39, 13, 45, 5, 37,
                    63, 31, 55, 23, 61, 29, 53, 21
                };

                var table = new int[64];
                for (var i = 0; i < 64; i++)
                {
                    table[i] = (2 * bayer[i] + 1) * 4096 / 128;
                }

                return table;
            }

            private static int[] BuildClip()
            {
                var table = new int[ClipOffset * 3];
                for (var i = 0; i < table.Length; i++)
                {
                    table[i] = Math.Max(0, Math.Min(255, i - ClipOffset));
                }

                return table;
            }
        }

        public void Dispose()
        {
            _color.Dispose();
            _alpha?.Dispose();
        }
    }
}

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Tells whether two icon files hold the exact same picture, whatever format each is saved
    /// in. Used to keep a copy of a provider's own art from being stored as a custom icon.
    /// </summary>
    /// <remarks>
    /// Exact means the same pixel size and every channel of every pixel within
    /// <see cref="MaxChannelDifference"/>, which absorbs lossless re-saves (PNG to a palette PNG,
    /// premultiplied rounding) and nothing more. A resized, retouched or re-compressed icon is the
    /// user's own art: a cleaned-up copy of a blocky JPEG differs from it by only a few dozen
    /// levels in a few places, and a tolerance loose enough to call those equal threw such
    /// replacements away.
    /// </remarks>
    public static class IconImageComparer
    {
        private const int MaxChannelDifference = 2;

        /// <summary>
        /// True when both files decode to the same pixels. False for a missing or undecodable file,
        /// or for different dimensions.
        /// </summary>
        public static bool AreSameImage(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) ||
                string.IsNullOrWhiteSpace(secondPath) ||
                !File.Exists(firstPath) ||
                !File.Exists(secondPath))
            {
                return false;
            }

            if (string.Equals(Path.GetFullPath(firstPath), Path.GetFullPath(secondPath), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                var first = Decode(firstPath, out var firstWidth, out var firstHeight);
                var second = Decode(secondPath, out var secondWidth, out var secondHeight);
                if (first == null ||
                    second == null ||
                    firstWidth != secondWidth ||
                    firstHeight != secondHeight ||
                    first.Length != second.Length)
                {
                    return false;
                }

                for (var i = 0; i < first.Length; i++)
                {
                    if (Math.Abs(first[i] - second[i]) > MaxChannelDifference)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Decode(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            BitmapSource frame;
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.OnLoad);
                frame = decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
            }

            if (frame == null || frame.PixelWidth == 0 || frame.PixelHeight == 0)
            {
                return null;
            }

            width = frame.PixelWidth;
            height = frame.PixelHeight;
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }
    }
}

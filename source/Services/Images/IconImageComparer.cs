using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Tells whether two icon files show the same picture, whatever their format, size or
    /// encoder. Used to keep art identical to a provider's own from being stored as a custom icon.
    /// </summary>
    /// <remarks>
    /// Both images are decoded, scaled to one small square and compared pixel by pixel. A byte
    /// comparison would miss the same icon re-encoded by a different pipeline, and the icon
    /// cache re-encodes and may compress what it downloads.
    /// </remarks>
    public static class IconImageComparer
    {
        private const int SampleSize = 32;

        /// <summary>The largest mean per-channel difference, out of 255, still read as the same image.</summary>
        private const double MaxMeanDifference = 4.0;

        /// <summary>
        /// True when both files decode and look the same. False for a missing or undecodable file,
        /// or for different aspect ratios.
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
                var first = Sample(firstPath, out var firstAspect);
                var second = Sample(secondPath, out var secondAspect);
                if (first == null ||
                    second == null ||
                    first.Length != second.Length ||
                    Math.Abs(firstAspect - secondAspect) > 0.02)
                {
                    return false;
                }

                long total = 0;
                for (var i = 0; i < first.Length; i++)
                {
                    total += Math.Abs(first[i] - second[i]);
                }

                return (double)total / first.Length <= MaxMeanDifference;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Sample(string path, out double aspect)
        {
            aspect = 0;
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

            aspect = (double)frame.PixelWidth / frame.PixelHeight;
            BitmapSource converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            converted = new TransformedBitmap(
                converted,
                new ScaleTransform((double)SampleSize / frame.PixelWidth, (double)SampleSize / frame.PixelHeight));

            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }
    }
}

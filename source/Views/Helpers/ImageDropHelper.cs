using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Images.Webm;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Shared helpers for image drag-and-drop targets: extracting the first droppable image
    /// file path or browser URL from an <see cref="IDataObject"/>. Extracted from the
    /// achievement icons tab so image picker rows across the app share one implementation.
    /// </summary>
    internal static class ImageDropHelper
    {
        private static readonly Regex HttpUrlRegex = new Regex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True when the drop payload contains a decodable local image file; returns its path.
        /// </summary>
        public static bool TryGetFirstImageFilePath(IDataObject data, out string imagePath)
        {
            imagePath = null;
            if (data == null)
            {
                return false;
            }

            try
            {
                if (!data.GetDataPresent(DataFormats.FileDrop))
                {
                    return false;
                }

                var files = data.GetData(DataFormats.FileDrop) as string[];
                imagePath = files?.FirstOrDefault(IsSupportedImageFile);
                return !string.IsNullOrWhiteSpace(imagePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True when the drop payload contains an http(s) URL (e.g. an image dragged from a
        /// browser); returns the URL with trailing punctuation trimmed.
        /// </summary>
        public static bool TryGetFirstBrowserUrl(IDataObject data, out string url)
        {
            url = null;
            if (data == null)
            {
                return false;
            }

            try
            {
                var text = ReadDroppedText(data, DataFormats.UnicodeText) ??
                           ReadDroppedText(data, DataFormats.Text) ??
                           ReadDroppedText(data, DataFormats.Html);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                var match = HttpUrlRegex.Match(text);
                if (!match.Success)
                {
                    return false;
                }

                url = TrimTrailingUrlPunctuation(match.Value);
                return !string.IsNullOrWhiteSpace(url);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The image a drop or paste payload carries, in order: a decodable image file, an
        /// http(s) URL, or text naming a decodable image file.
        /// </summary>
        public static bool TryGetImageSource(IDataObject data, out string source)
        {
            if (TryGetFirstImageFilePath(data, out source) || TryGetFirstBrowserUrl(data, out source))
            {
                return true;
            }

            var text = ReadDroppedText(data, DataFormats.UnicodeText) ??
                       ReadDroppedText(data, DataFormats.Text);
            var candidate = (text ?? string.Empty).Trim().Trim('"');
            if (IsSupportedImageFile(candidate))
            {
                source = candidate;
                return true;
            }

            source = null;
            return false;
        }

        /// <summary>
        /// <see cref="TryGetImageSource"/> over the clipboard. False when the clipboard holds no
        /// image source or cannot be opened.
        /// </summary>
        public static bool TryGetClipboardImageSource(out string source)
        {
            source = null;
            try
            {
                return TryGetImageSource(Clipboard.GetDataObject(), out source);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True when the path points at an existing file in a format this machine offers and that
        /// a <see cref="BitmapDecoder"/> (or, for WebM, the WebM reader) can actually open.
        /// </summary>
        public static bool IsSupportedImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            if (!ImageFormats.HasSelectableExtension(path))
            {
                return false;
            }

            if (ImageFormats.IsWebmExtension(ImageFormats.GetExtension(path)))
            {
                return WebmStill.IsReadable(path);
            }

            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ReadDroppedText(IDataObject data, string format)
        {
            if (data == null || string.IsNullOrWhiteSpace(format))
            {
                return null;
            }

            try
            {
                if (!data.GetDataPresent(format))
                {
                    return null;
                }

                return data.GetData(format) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string TrimTrailingUrlPunctuation(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('.', ',', ';', ')', ']', '}');
        }
    }
}

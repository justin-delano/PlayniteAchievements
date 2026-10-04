using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Decodes the images a preview model displays and swaps the decoded images into the model,
    /// ahead of an offscreen render. AsyncImage never starts a load on a tree that is not on
    /// screen, but applies an ImageSource value at once, so a model whose image properties hold
    /// decoded images renders complete. Decoding runs off the calling thread; the swap runs on
    /// the caller's context once every decode has finished.
    /// </summary>
    internal sealed class WorkshopPreviewImagePreloader
    {
        /// <summary>Decode size of a game data row icon (the row shows it at 40 DIPs).</summary>
        public const int GameDataIconDecodePixel = 80;

        /// <summary>Decode size of a showcase thumbnail, as the showcase preview requests it.</summary>
        public const int ThumbnailDecodePixel = 320;

        /// <summary>Decode size of a notification badge or other slot image.</summary>
        public const int BadgeDecodePixel = 160;

        /// <summary>Decode size of a notification background, as the bundled template requests it.</summary>
        public const int BackgroundDecodePixel = 768;

        // A style package carries a handful of slot images; this bounds a malformed one.
        private const int MaxStyleImages = 64;

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        };

        private readonly MemoryImageService _imageService;

        /// <param name="imageService">The plugin's image service; null decodes local files directly.</param>
        public WorkshopPreviewImagePreloader(MemoryImageService imageService)
        {
            _imageService = imageService;
        }

        /// <summary>
        /// Decodes every image <paramref name="model"/> displays and swaps the images into it. For
        /// game data that is the unlocked icons of the first <paramref name="gameDataMaxRows"/>
        /// changed rows (0 for all), the rows a preview capped at that count shows. Images that
        /// fail to decode keep their path.
        /// </summary>
        public async Task PreloadAsync(WorkshopPreviewModel model, int gameDataMaxRows, CancellationToken cancel)
        {
            switch (model)
            {
                case BundlePreviewModel bundle:
                    foreach (var part in new WorkshopPreviewModel[] { bundle.Toast, bundle.Frame, bundle.Colors, bundle.Sounds })
                    {
                        if (part != null)
                        {
                            await PreloadAsync(part, gameDataMaxRows, cancel);
                        }
                    }

                    break;
                case GameCustomDataPreviewModel gameData:
                    await PreloadGameDataAsync(gameData, gameDataMaxRows, cancel);
                    break;
                case ShowcasePagePreviewModel showcase:
                    await PreloadShowcaseAsync(showcase, cancel);
                    break;
                case NotificationStylePreviewModel style:
                    await PreloadStyleAsync(style, cancel);
                    break;
            }
        }

        /// <summary>The rows a game data preview capped at <paramref name="maxRows"/> shows, unchanged rows hidden.</summary>
        internal static IEnumerable<AchievementPreviewRow> DisplayedRows(GameCustomDataPreviewDiff diff, int maxRows)
        {
            if (diff == null)
            {
                return Enumerable.Empty<AchievementPreviewRow>();
            }

            var rows = diff.Rows
                .Concat(diff.UnchangedRows)
                .Where(row => row != null && row.Changes != AchievementPreviewChange.None);
            return maxRows > 0 ? rows.Take(maxRows) : rows;
        }

        private async Task PreloadGameDataAsync(GameCustomDataPreviewModel model, int maxRows, CancellationToken cancel)
        {
            var states = DisplayedRows(model.Diff, maxRows)
                .SelectMany(row => new[] { row.Before, row.After })
                .Where(state => state != null)
                .ToList();
            var decoded = await DecodeAllAsync(
                states.Select(state => state.UnlockedIcon as string),
                path => GameDataIconDecodePixel,
                cancel);

            foreach (var state in states)
            {
                if (state.UnlockedIcon is string path && decoded.TryGetValue(path, out var image))
                {
                    state.UnlockedIcon = image;
                }
            }
        }

        private async Task PreloadShowcaseAsync(ShowcasePagePreviewModel model, CancellationToken cancel)
        {
            var decoded = await DecodeAllAsync(model.ImagePaths, path => ThumbnailDecodePixel, cancel);
            model.Thumbnails = model.ImagePaths
                .Select(path => path != null && decoded.TryGetValue(path, out var image) ? image : (object)path)
                .ToList();
        }

        // Every slot image of a style package is extracted under the model's scratch directory,
        // so decoding what is there covers the background, the badges and any per-kind images.
        private async Task PreloadStyleAsync(NotificationStylePreviewModel model, CancellationToken cancel)
        {
            var scratch = model.ScratchDirectory;
            if (string.IsNullOrWhiteSpace(scratch) || !Directory.Exists(scratch))
            {
                return;
            }

            var files = Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories)
                .Where(file => ImageExtensions.Contains(Path.GetExtension(file)))
                .Take(MaxStyleImages)
                .ToList();
            var background = model.Style?.ToastBackgroundImagePath;
            model.PreloadedImages = await DecodeAllAsync(
                files,
                path => string.Equals(path, background, StringComparison.OrdinalIgnoreCase) ? BackgroundDecodePixel : BadgeDecodePixel,
                cancel);
        }

        private async Task<Dictionary<string, ImageSource>> DecodeAllAsync(
            IEnumerable<string> paths,
            Func<string, int> decodePixel,
            CancellationToken cancel)
        {
            var distinct = (paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var images = await Task.WhenAll(distinct.Select(path => DecodeAsync(path, decodePixel(path), cancel)));
            cancel.ThrowIfCancellationRequested();

            var result = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < distinct.Count; i++)
            {
                if (images[i] != null)
                {
                    result[distinct[i]] = images[i];
                }
            }

            return result;
        }

        private async Task<ImageSource> DecodeAsync(string path, int decodePixel, CancellationToken cancel)
        {
            try
            {
                if (_imageService != null)
                {
                    return await _imageService.GetAsync(path, decodePixel, cancel, cacheResult: false).ConfigureAwait(false);
                }

                return await Task.Run(() => DecodeFile(path, decodePixel), cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Without the plugin's image service (tests, tools): a local file only, frozen.
        private static BitmapSource DecodeFile(string path, int decodePixel)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = decodePixel;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}

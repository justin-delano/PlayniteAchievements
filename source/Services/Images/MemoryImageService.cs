using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Hybrid image loader with memory LRU cache and persistent disk backing.
    /// Memory cache provides fast access for recently used icons.
    /// Disk cache provides persistent storage across sessions.
    /// </summary>
    public sealed class MemoryImageService : IDisposable
    {
        private static readonly ILogger StaticLogger = LogManager.GetLogger(nameof(MemoryImageService));

        private readonly ILogger _logger;
        private readonly DiskImageService _diskService;
        private readonly SemaphoreSlim _downloadGate;
        private readonly HttpClient _http;

        private readonly int _maxItems;

        private readonly object _cacheLock = new object();
        private readonly LinkedList<string> _lru = new LinkedList<string>();
        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, Task<BitmapSource>> _inflight =
            new ConcurrentDictionary<string, Task<BitmapSource>>(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            public BitmapSource Value { get; set; }
            public LinkedListNode<string> Node { get; set; }
        }

        public MemoryImageService(
            ILogger logger,
            DiskImageService diskService,
            int maxItems = 512,
            int downloadConcurrency = 8)
        {
            _logger = logger ?? StaticLogger;
            _diskService = diskService ?? throw new ArgumentNullException(nameof(diskService));
            _maxItems = Math.Max(64, maxItems);
            _downloadGate = new SemaphoreSlim(Math.Max(1, downloadConcurrency), Math.Max(1, downloadConcurrency));

            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PlayniteAchievements/1.0");
        }

        public void Dispose()
        {
            try { _http?.Dispose(); } catch { }
            try { _downloadGate?.Dispose(); } catch { }
        }

        public void Clear()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
                _lru.Clear();
            }
        }

        public void ClearDiskCache()
        {
            _diskService.ClearAllCache();
        }

        private const string GrayPrefix = "gray:";

        public Task<BitmapSource> GetAsync(string uri, int decodePixel, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return Task.FromResult<BitmapSource>(null);
            }

            bool gray = TryStripGrayPrefix(ref uri);
            if (string.IsNullOrWhiteSpace(uri))
            {
                return Task.FromResult<BitmapSource>(null);
            }

            // Clamp decode size to a reasonable range. 0 means "auto", treated as 64.
            var size = decodePixel <= 0 ? 64 : Math.Max(16, Math.Min(decodePixel, 512));
            var key = $"{size}\u001f{(gray ? 1 : 0)}\u001f{uri}";

            if (TryGetCached(key, out var cached))
            {
                return Task.FromResult(cached);
            }

            // Dedupe work per key, but allow UI-level cancellation (we just won't apply the result).
            var inflight = _inflight.GetOrAdd(key, _ => LoadAndCacheAsync(key, uri, size, gray));
            return inflight.WithCancellation(cancel);
        }

        private bool TryGetCached(string key, out BitmapSource value)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry) && entry?.Value != null)
                {
                    // touch LRU
                    if (entry.Node != null)
                    {
                        _lru.Remove(entry.Node);
                        _lru.AddFirst(entry.Node);
                    }
                    value = entry.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private async Task<BitmapSource> LoadAndCacheAsync(string key, string uri, int decodePixel, bool gray)
        {
            try
            {
                BitmapSource bmp;
                if (IsHttpUrl(uri))
                {
                    bmp = await LoadRemoteAsync(uri, decodePixel).ConfigureAwait(false);
                }
                else
                {
                    bmp = LoadLocal(uri, decodePixel);
                }

                if (gray && bmp != null)
                {
                    bmp = ConvertToGrayscale(bmp);
                }

                if (bmp != null && bmp.CanFreeze)
                {
                    bmp.Freeze();
                }

                if (bmp != null)
                {
                    AddToCache(key, bmp);
                }

                return bmp;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Image load failed.");
                return null;
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }
        }

        private static bool TryStripGrayPrefix(ref string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            bool gray = false;
            while (uri.StartsWith(GrayPrefix, StringComparison.OrdinalIgnoreCase))
            {
                uri = uri.Substring(GrayPrefix.Length);
                gray = true;
            }

            return gray;
        }

        private static BitmapSource ConvertToGrayscale(BitmapSource source)
        {
            // More robust grayscale conversion that preserves alpha.
            // If conversion fails for any reason, fall back to the original image.
            try
            {
                if (source == null)
                {
                    return null;
                }

                BitmapSource bgraSource = source;
                if (bgraSource.Format != System.Windows.Media.PixelFormats.Bgra32)
                {
                    var converted = new FormatConvertedBitmap();
                    converted.BeginInit();
                    converted.Source = bgraSource;
                    converted.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
                    converted.EndInit();
                    converted.Freeze();
                    bgraSource = converted;
                }

                int width = bgraSource.PixelWidth;
                int height = bgraSource.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                bgraSource.CopyPixels(pixels, stride, 0);

                // BGRA byte order.
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    // Standard luma approximation.
                    byte gray = (byte)Math.Min(255, (int)(0.114 * b + 0.587 * g + 0.299 * r));
                    pixels[i + 0] = gray;
                    pixels[i + 1] = gray;
                    pixels[i + 2] = gray;
                }

                var grayImage = BitmapSource.Create(
                    width,
                    height,
                    bgraSource.DpiX,
                    bgraSource.DpiY,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);

                grayImage.Freeze();
                return grayImage;
            }
            catch
            {
                return source;
            }
        }

        private void AddToCache(string key, BitmapSource value)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    existing.Value = value;
                    if (existing.Node != null)
                    {
                        _lru.Remove(existing.Node);
                        _lru.AddFirst(existing.Node);
                    }
                }
                else
                {
                    var node = new LinkedListNode<string>(key);
                    _lru.AddFirst(node);
                    _cache[key] = new CacheEntry { Value = value, Node = node };
                }

                while (_cache.Count > _maxItems && _lru.Last != null)
                {
                    var toEvict = _lru.Last.Value;
                    _lru.RemoveLast();
                    _cache.Remove(toEvict);
                }
            }
        }

        private BitmapSource LoadLocal(string uri, int decodePixel)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

                if (decodePixel > 0)
                {
                    bitmap.DecodePixelWidth = decodePixel;
                }

                bitmap.UriSource = new Uri(uri, UriKind.RelativeOrAbsolute);
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private async Task<BitmapSource> LoadRemoteAsync(string url, int decodePixel)
        {
            // Bound concurrent downloads/decodes to avoid UI stalls and network thrash.
            await _downloadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await DownloadBytesAsync(url).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                using (var ms = new MemoryStream(bytes, writable: false))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (decodePixel > 0)
                    {
                        bitmap.DecodePixelWidth = decodePixel;
                    }
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                _downloadGate.Release();
            }
        }

        private async Task<byte[]> DownloadBytesAsync(string url)
        {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();

                // Hard cap to avoid huge downloads.
                const int maxBytes = 5 * 1024 * 1024;

                using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[16 * 1024];
                    int read;
                    int total = 0;

                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > maxBytes)
                        {
                            throw new InvalidOperationException("Image download exceeded size limit.");
                        }
                        ms.Write(buffer, 0, read);
                    }

                    return ms.ToArray();
                }
            }
        }

        private static bool IsHttpUrl(string url)
        {
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class TaskCancellationExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancel)
        {
            if (task == null)
            {
                return default;
            }

            if (!cancel.CanBeCanceled)
            {
                return await task.ConfigureAwait(false);
            }

            var tcs = new TaskCompletionSource<bool>();
            using (cancel.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                {
                    throw new OperationCanceledException(cancel);
                }
            }

            return await task.ConfigureAwait(false);
        }
    }
}

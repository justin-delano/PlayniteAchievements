using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using PlayniteAchievements.Services.Logging;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Persistent disk-based icon cache with URI-based organization.
    /// Downloads icons, converts to PNG, and stores on disk for fast retrieval.
    /// </summary>
    public sealed class DiskImageService : IDisposable
    {
        private static readonly ILogger StaticLogger = PluginLogger.GetLogger(nameof(DiskImageService));
        private const int MaxBytes = 5 * 1024 * 1024; // 5MB limit

        // Domains known to rate-limit concurrent requests
        private static readonly string[] RateLimitedDomains = new[]
        {
            "image-ssl.xboxlive.com",
            "xboxlive.com"
        };

        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly string _cacheRoot;
        private readonly SemaphoreSlim _downloadGate;
        private readonly SemaphoreSlim _rateLimitedDownloadGate;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathWriteLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public DiskImageService(ILogger logger, string cacheRoot, int downloadConcurrency = 4)
        {
            _logger = logger ?? StaticLogger;
            _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
            _downloadGate = new SemaphoreSlim(Math.Max(1, downloadConcurrency), Math.Max(1, downloadConcurrency));
            _rateLimitedDownloadGate = new SemaphoreSlim(2, 2); // Lower concurrency for rate-limited domains

            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PlayniteAchievements/1.0");

            EnsureIconCacheDirectory();
        }

        public void Dispose()
        {
            try { _http?.Dispose(); } catch { }
            try { _downloadGate?.Dispose(); } catch { }
            try { _rateLimitedDownloadGate?.Dispose(); } catch { }
            try { _pathWriteLocks.Clear(); } catch { }
        }

        private string IconCacheDirectory => Path.Combine(_cacheRoot, "icon_cache");

        private void EnsureIconCacheDirectory()
        {
            try
            {
                if (!Directory.Exists(IconCacheDirectory))
                {
                    Directory.CreateDirectory(IconCacheDirectory);
                    _logger?.Info($"Created icon cache directory: {IconCacheDirectory}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to create icon cache directory: {IconCacheDirectory}");
            }
        }

        /// <summary>
        /// Generate cache filename from URI using SHA256 hash.
        /// If gameId is provided, creates per-game subfolder structure.
        /// </summary>
        public string GetIconCachePathFromUri(string uri, int decodeSize, string gameId = null)
        {
            // Create hash-based filename from the URI
            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(uri));
                var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
                var sizeSuffix = decodeSize > 0 ? $"_{decodeSize}" : "";

                // Use per-game subfolder if gameId is provided
                var cacheDir = string.IsNullOrEmpty(gameId)
                    ? IconCacheDirectory
                    : Path.Combine(IconCacheDirectory, gameId);

                return Path.Combine(cacheDir, $"{hashHex}{sizeSuffix}.png");
            }
        }

        private void EnsureGameIconDirectory(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return;

            var gameDir = Path.Combine(IconCacheDirectory, gameId);
            if (!Directory.Exists(gameDir))
            {
                Directory.CreateDirectory(gameDir);
                _logger?.Debug($"Created game icon directory: {gameDir}");
            }
        }

        /// <summary>
        /// Check if an icon is cached on disk by URI.
        /// </summary>
        public bool IsIconCached(string uri, int decodeSize, string gameId = null)
        {
            var cachePath = GetIconCachePathFromUri(uri, decodeSize, gameId);
            return File.Exists(cachePath);
        }

        /// <summary>
        /// Determines if a URL belongs to a domain known to rate-limit concurrent requests.
        /// </summary>
        private static bool IsRateLimitedDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var host = new Uri(url).Host;
                foreach (var domain in RateLimitedDomains)
                {
                    if (host.EndsWith(domain, StringComparison.OrdinalIgnoreCase) ||
                        host.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Invalid URL, treat as normal
            }

            return false;
        }

        /// <summary>
        /// Get or download an icon by URI, caching to disk by URI hash.
        /// If gameId is provided, stores in per-game subfolder.
        /// Returns the file path to the cached PNG file, or null on failure.
        /// </summary>
        public async Task<string> GetOrDownloadIconAsync(
            string uri,
            int decodeSize,
            CancellationToken cancel,
            string gameId = null)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            // Ensure game directory exists if gameId provided
            if (!string.IsNullOrEmpty(gameId))
            {
                EnsureGameIconDirectory(gameId);
            }

            // Check if already cached on disk
            var cachePath = GetIconCachePathFromUri(uri, decodeSize, gameId);
            if (File.Exists(cachePath))
            {
                return cachePath;
            }

            var pathLock = _pathWriteLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            await pathLock.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring path-specific write lock.
                if (File.Exists(cachePath))
                {
                    return cachePath;
                }

                // Use appropriate gate based on domain rate-limiting behavior
                var downloadGate = IsRateLimitedDomain(uri) ? _rateLimitedDownloadGate : _downloadGate;

                // Download and cache under shared download concurrency gate.
                await downloadGate.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    if (File.Exists(cachePath))
                    {
                        return cachePath;
                    }

                    var bytes = await DownloadBytesAsync(uri, cancel).ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0)
                    {
                        return null;
                    }

                    // Convert to PNG and save
                    using (var ms = new MemoryStream(bytes, writable: false))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        if (decodeSize > 0)
                        {
                            bitmap.DecodePixelWidth = decodeSize;
                        }
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();

                        // Crop to square for consistent aspect ratio and smaller file size
                        var finalBitmap = CropToSquare(bitmap);

                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(finalBitmap));
                        await SavePngWithRetryAsync(cachePath, encoder, cancel).ConfigureAwait(false);

                        // _logger?.Debug($"Cached icon: {cachePath}");
                        return cachePath;
                    }
                }
                finally
                {
                    downloadGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Let cancellation propagate - handled by caller
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to download/cache icon from {uri}");
                return null;
            }
            finally
            {
                pathLock.Release();
            }
        }

        private static async Task SavePngWithRetryAsync(
            string cachePath,
            PngBitmapEncoder encoder,
            CancellationToken cancel,
            int maxAttempts = 3)
        {
            var attempt = 0;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                attempt++;
                try
                {
                    using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        encoder.Save(fs);
                    }
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(50 * attempt, cancel).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Crop a bitmap to square from center. If already square, returns original.
        /// For rectangular images, crops to square using center horizontally.
        /// </summary>
        private static BitmapSource CropToSquare(BitmapSource source)
        {
            if (source == null) return null;

            int width = source.PixelWidth;
            int height = source.PixelHeight;

            // Already square (or close enough)
            if (Math.Abs(width - height) <= 1) return source;

            // Calculate crop rect - center horizontally
            int cropSize = Math.Min(width, height);
            int x = 0;
            int y = 0;

            if (height < width)
            {
                // Landscape: crop sides
                x = (width - height) / 2;
                cropSize = height;
            }
            else
            {
                // Portrait: crop top/bottom (rare for achievement icons)
                y = (height - width) / 2;
                cropSize = width;
            }

            var rect = new Int32Rect(x, y, cropSize, cropSize);
            var cropped = new CroppedBitmap(source, rect);
            cropped.Freeze();
            return cropped;
        }

        private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancel)
        {
            // Create a linked token with timeout to ensure we control disposal timing.
            // This prevents ObjectDisposedException when the parent CTS is disposed
            // while async SSL stream operations are still unwinding.
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                linkedCts.CancelAfter(TimeSpan.FromSeconds(30));
                var token = linkedCts.Token;

                try
                {
                    using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();

                        using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var ms = new MemoryStream())
                        {
                            var buffer = new byte[16 * 1024];
                            int read;
                            int total = 0;

                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                            {
                                total += read;
                                if (total > MaxBytes)
                                {
                                    throw new InvalidOperationException("Image download exceeded size limit.");
                                }
                                ms.Write(buffer, 0, read);
                            }

                            return ms.ToArray();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    // User cancelled - rethrow to propagate
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // Timeout - return null
                    return null;
                }
            }
        }

        /// <summary>
        /// Load a BitmapSource from a cached file path.
        /// </summary>
        public BitmapSource LoadCachedImage(string cachePath, int decodePixel)
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
                bitmap.UriSource = new Uri(cachePath, UriKind.RelativeOrAbsolute);
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public void ClearAllCache()
        {
            try
            {
                if (Directory.Exists(IconCacheDirectory))
                {
                    Directory.Delete(IconCacheDirectory, recursive: true);
                    EnsureIconCacheDirectory();
                    _logger?.Info("Cleared all icon cache");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to clear icon cache");
            }
        }

        public void ClearGameCache(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            try
            {
                var gameDir = Path.Combine(IconCacheDirectory, gameId.Trim());
                if (Directory.Exists(gameDir))
                {
                    Directory.Delete(gameDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to clear icon cache for game '{gameId}'.");
            }
        }

        public void RemoveGameIconCache(string gameId)
        {
            ClearGameCache(gameId);
        }

        public long GetCacheSizeBytes()
        {
            try
            {
                if (!Directory.Exists(IconCacheDirectory))
                {
                    return 0;
                }

                long total = 0;
                foreach (var file in Directory.EnumerateFiles(IconCacheDirectory, "*.png", SearchOption.AllDirectories))
                {
                    total += new FileInfo(file).Length;
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }

        public string GetCacheDirectoryPath() => IconCacheDirectory;

        /// <summary>
        /// Check if a path refers to an existing local file.
        /// </summary>
        public static bool IsLocalIconPath(string path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path);

        /// <summary>
        /// Get or copy a local icon file to the cache.
        /// If gameId is provided, stores in per-game subfolder.
        /// Returns the cached file path, or null on failure.
        /// </summary>
        public async Task<string> GetOrCopyLocalIconAsync(
            string localPath,
            int decodeSize,
            CancellationToken cancel,
            string gameId = null)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                return null;
            }

            // Ensure game directory exists if gameId provided
            if (!string.IsNullOrEmpty(gameId))
            {
                EnsureGameIconDirectory(gameId);
            }

            // Check if already cached on disk
            var cachePath = GetIconCachePathFromUri(localPath, decodeSize, gameId);
            if (File.Exists(cachePath))
            {
                return cachePath;
            }

            var pathLock = _pathWriteLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            await pathLock.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (File.Exists(cachePath))
                {
                    return cachePath;
                }

                // Read local file and convert to PNG
                using (var ms = new MemoryStream(File.ReadAllBytes(localPath), writable: false))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (decodeSize > 0)
                    {
                        bitmap.DecodePixelWidth = decodeSize;
                    }
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();

                    // Crop to square for consistent aspect ratio and smaller file size
                    var finalBitmap = CropToSquare(bitmap);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(finalBitmap));
                    await SavePngWithRetryAsync(cachePath, encoder, cancel).ConfigureAwait(false);

                    return cachePath;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to copy/cache local icon from {localPath}");
                return null;
            }
            finally
            {
                pathLock.Release();
            }
        }
    }
}

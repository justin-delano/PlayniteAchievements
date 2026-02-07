using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Persistent disk-based icon cache with URI-based organization.
    /// Downloads icons, converts to PNG, and stores on disk for fast retrieval.
    /// </summary>
    public sealed class DiskImageService : IDisposable
    {
        private static readonly ILogger StaticLogger = LogManager.GetLogger(nameof(DiskImageService));
        private const int MaxBytes = 5 * 1024 * 1024; // 5MB limit

        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly string _cacheRoot;
        private readonly SemaphoreSlim _downloadGate;

        public DiskImageService(ILogger logger, string cacheRoot, int downloadConcurrency = 4)
        {
            _logger = logger ?? StaticLogger;
            _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
            _downloadGate = new SemaphoreSlim(Math.Max(1, downloadConcurrency), Math.Max(1, downloadConcurrency));

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

            // Download and cache
            await _downloadGate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
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

                    // Save as PNG
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                    {
                        encoder.Save(fs);
                    }

                    _logger?.Debug($"Cached icon: {cachePath}");
                    return cachePath;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to download/cache icon from {uri}");
                return null;
            }
            finally
            {
                _downloadGate.Release();
            }
        }

        private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancel)
        {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();

                using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[16 * 1024];
                    int read;
                    int total = 0;

                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
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
    }
}

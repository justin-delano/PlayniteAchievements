using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Persistent disk-based icon cache with game ID subfolder organization.
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

        private string GetGameIconDirectory(Guid gameId)
        {
            var gameDir = Path.Combine(IconCacheDirectory, gameId.ToString("N"));
            if (!Directory.Exists(gameDir))
            {
                Directory.CreateDirectory(gameDir);
            }
            return gameDir;
        }

        private string GetIconCachePath(Guid gameId, string achievementId, int decodeSize)
        {
            var gameDir = GetGameIconDirectory(gameId);
            var safeId = SanitizeAchievementId(achievementId);
            var sizeSuffix = decodeSize > 0 ? $"_{decodeSize}" : "";
            return Path.Combine(gameDir, $"{safeId}{sizeSuffix}.png");
        }

        private static string SanitizeAchievementId(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                return "unknown";
            }

            // Replace invalid filename characters with underscore
            var invalidChars = new char[]
            {
                '<', '>', ':', '"', '/', '\\', '|', '?', '*',
                ' ', '\t', '\r', '\n', '\0'
            };

            var safe = achievementId;
            foreach (var c in invalidChars)
            {
                safe = safe.Replace(c, '_');
            }

            // Limit length
            if (safe.Length > 100)
            {
                safe = safe.Substring(0, 100);
            }

            return safe;
        }

        public bool IsIconCached(Guid gameId, string achievementId, int decodeSize)
        {
            var cachePath = GetIconCachePath(gameId, achievementId, decodeSize);
            return File.Exists(cachePath);
        }

        public async Task<string> GetOrDownloadIconAsync(
            Guid gameId,
            string achievementId,
            string iconPath,
            int decodeSize,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            // Check if already cached on disk
            var cachePath = GetIconCachePath(gameId, achievementId, decodeSize);
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

                var bytes = await DownloadBytesAsync(iconPath, cancel).ConfigureAwait(false);
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
                _logger?.Warn(ex, $"Failed to download/cache icon from {iconPath}");
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

        public void ClearGameCache(Guid gameId)
        {
            try
            {
                var gameDir = GetGameIconDirectory(gameId);
                if (Directory.Exists(gameDir))
                {
                    Directory.Delete(gameDir, recursive: true);
                    _logger?.Info($"Cleared icon cache for game {gameId}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to clear icon cache for game {gameId}");
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
    }
}

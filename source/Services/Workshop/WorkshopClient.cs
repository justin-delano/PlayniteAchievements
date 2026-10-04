using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Reads the Workshop: the index, item READMEs and previews, and the package files from
    /// their release assets. Everything is a plain public HTTPS GET; nothing identifies the user.
    /// Packages are verified against the index's SHA-256 before anything imports them.
    /// </summary>
    public sealed class WorkshopClient
    {
        public const string DefaultIndexUrl =
            "https://raw.githubusercontent.com/justin-delano/PlayniteAchievements-Workshop/main/index/v1.json";

        private static readonly TimeSpan IndexTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromHours(2);

        private readonly Func<string> _getIndexUrl;
        private readonly string _cacheDirectory;
        private readonly ILogger _logger;
        private readonly HttpClient _http;

        /// <param name="getIndexUrl">The index URL from settings; blank falls back to <see cref="DefaultIndexUrl"/>.</param>
        /// <param name="cacheDirectory">Where previews and READMEs are cached (<c>UserData\workshop\cache</c>).</param>
        public WorkshopClient(Func<string> getIndexUrl, string cacheDirectory, ILogger logger = null, HttpMessageHandler handler = null)
        {
            _getIndexUrl = getIndexUrl;
            _cacheDirectory = cacheDirectory;
            _logger = logger;
            _http = handler == null ? HttpClientFactory.Create(DownloadTimeout) : HttpClientFactory.Create(handler, DownloadTimeout);
        }

        public string IndexUrl
        {
            get
            {
                var configured = _getIndexUrl?.Invoke();
                return string.IsNullOrWhiteSpace(configured) ? DefaultIndexUrl : configured.Trim();
            }
        }

        public async Task<WorkshopIndexFile> FetchIndexAsync(CancellationToken cancel)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(IndexTimeout);
                // A cache-busting query keeps raw.githubusercontent's short CDN cache from showing
                // a stale index right after a merge.
                var url = IndexUrl + (IndexUrl.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;
                using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var index = JsonConvert.DeserializeObject<WorkshopIndexFile>(json)
                                ?? throw new InvalidOperationException("The Workshop index is empty.");
                    if (index.SchemaVersion > WorkshopIndexFile.SupportedSchemaVersion)
                    {
                        throw new InvalidOperationException(
                            "The Workshop index is newer than this version of the extension understands. Update the extension.");
                    }

                    index.Items.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.Id) || item.Package == null);
                    return index;
                }
            }
        }

        /// <summary>
        /// The item's README text, from the per-commit CDN URL (so the cache can be keyed by URL
        /// and never goes stale). Null when the item has none or the fetch fails.
        /// </summary>
        public async Task<string> FetchReadmeAsync(WorkshopItem item, CancellationToken cancel)
        {
            var url = item?.Urls?.Readme;
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            try
            {
                var cached = CachePathFor(url, ".md");
                if (cached != null && File.Exists(cached))
                {
                    return File.ReadAllText(cached);
                }

                var text = await _http.GetStringAsync(url).ConfigureAwait(false);
                if (cached != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cached));
                    File.WriteAllText(cached, text);
                }

                return text;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed fetching Workshop README for {item.Id}.");
                return null;
            }
        }

        /// <summary>
        /// The item's preview image as a local cached file path, or null. Cached by URL; the URL
        /// is pinned to the publishing commit, so a changed preview has a new URL.
        /// </summary>
        public async Task<string> FetchPreviewAsync(WorkshopItem item, CancellationToken cancel)
        {
            var url = item?.Urls?.Preview;
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var cached = CachePathFor(url, Path.GetExtension(item.Preview ?? ".png"));
            if (cached == null)
            {
                return null;
            }

            if (File.Exists(cached))
            {
                return cached;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cached));
                var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                File.WriteAllBytes(cached, bytes);
                return cached;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed fetching Workshop preview for {item.Id}.");
                return null;
            }
        }

        /// <summary>
        /// The live download total for an item from the GitHub release API, or null when the
        /// release URL is not a GitHub release page or the request fails. The index carries the
        /// count as of its last build; the detail pane asks for this once an item is picked.
        /// </summary>
        public async Task<long?> FetchLiveDownloadsAsync(WorkshopItem item, CancellationToken cancel)
        {
            var api = ReleaseApiUrl(item?.Package?.Release?.Url);
            if (api == null)
            {
                return null;
            }

            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    timeout.CancelAfter(IndexTimeout);
                    using (var request = new HttpRequestMessage(HttpMethod.Get, api))
                    {
                        request.Headers.Accept.ParseAdd("application/vnd.github+json");
                        request.Headers.UserAgent.ParseAdd("PlayniteAchievements");
                        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                return null;
                            }

                            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return SumAssetDownloads(json);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Live download count unavailable for {item?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>The sum of download_count over a GitHub release JSON document, or null when it has no assets array.</summary>
        public static long? SumAssetDownloads(string releaseJson)
        {
            if (string.IsNullOrWhiteSpace(releaseJson))
            {
                return null;
            }

            var release = JObject.Parse(releaseJson);
            if (!(release["assets"] is JArray assets))
            {
                return null;
            }

            long total = 0;
            foreach (var asset in assets)
            {
                total += asset.Value<long?>("download_count") ?? 0;
            }

            return total;
        }

        /// <summary>
        /// github.com/{owner}/{repo}/releases/tag/{tag} as its api.github.com release-by-tag URL,
        /// or null for any other address. Tags may contain slashes and arrive percent-encoded.
        /// </summary>
        public static string ReleaseApiUrl(string releasePageUrl)
        {
            if (string.IsNullOrWhiteSpace(releasePageUrl)
                || !Uri.TryCreate(releasePageUrl.Trim(), UriKind.Absolute, out var uri)
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 5
                || !string.Equals(parts[2], "releases", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(parts[3], "tag", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tag = string.Join("/", parts.Skip(4));
            return $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/tags/{tag}";
        }

        /// <summary>
        /// Downloads the item's package to <paramref name="destinationPath"/>, reporting bytes
        /// received, and verifies its SHA-256 against the index. A mismatch deletes the file and
        /// throws, so a tampered or truncated download is never imported.
        /// </summary>
        public async Task DownloadPackageAsync(
            WorkshopItem item,
            string destinationPath,
            IProgress<long> progress,
            CancellationToken cancel)
        {
            var url = item?.Urls?.Package ?? item?.Package?.Release?.Url;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("The Workshop item has no package URL.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                using (var sha = SHA256.Create())
                {
                    var buffer = new byte[1 << 16];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                    {
                        await destination.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        total += read;
                        progress?.Report(total);
                    }

                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var actual = BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
                    var expected = (item.Package?.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
                    if (expected.Length > 0 && !string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        destination.Dispose();
                        TryDelete(destinationPath);
                        throw new InvalidOperationException(
                            "The downloaded package does not match the Workshop's checksum, so it was not installed.");
                    }
                }
            }
        }

        private string CachePathFor(string url, string extension)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory))
            {
                return null;
            }

            using (var sha = SHA1.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url))).Replace("-", string.Empty).ToLowerInvariant();
                var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant();
                return Path.Combine(_cacheDirectory, hash + safeExtension);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

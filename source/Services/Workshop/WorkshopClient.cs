using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _imageFetches =
            new ConcurrentDictionary<string, Lazy<Task<string>>>(StringComparer.OrdinalIgnoreCase);

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

        public Task<WorkshopIndexFile> FetchIndexAsync(CancellationToken cancel) => Task.Run(() => FetchIndexCoreAsync(cancel), cancel);

        /// <summary>
        /// The index the last successful fetch returned, by any caller (the update check, Browse,
        /// the Library page), or null before the first. Read where a fetch would be one too many,
        /// such as the Manage Achievements window telling whether a game's data has an update.
        /// </summary>
        public WorkshopIndexFile LastIndex => Volatile.Read(ref _lastIndex);

        private WorkshopIndexFile _lastIndex;

        private async Task<WorkshopIndexFile> FetchIndexCoreAsync(CancellationToken cancel)
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
                    Volatile.Write(ref _lastIndex, index);
                    PruneCache(index);
                    return index;
                }
            }
        }

        /// <summary>
        /// The item's README text, from the per-commit CDN URL (so the cache can be keyed by URL
        /// and never goes stale). Null when the item has none or the fetch fails.
        /// </summary>
        public Task<string> FetchReadmeAsync(WorkshopItem item, CancellationToken cancel) => Task.Run(() => FetchReadmeCoreAsync(item, cancel), cancel);

        private async Task<string> FetchReadmeCoreAsync(WorkshopItem item, CancellationToken cancel)
        {
            var url = item?.Urls?.Readme;
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            try
            {
                var cached = ReadmeCachePathFor(url);
                if (cached != null && File.Exists(cached))
                {
                    return ReadmeToPlainText(StripImageBlock(File.ReadAllText(cached)), item.Name, item.Description);
                }

                var text = await _http.GetStringAsync(url).ConfigureAwait(false);
                if (cached != null)
                {
                    WriteCacheFile(cached, new System.Text.UTF8Encoding(false).GetBytes(text));
                }

                return ReadmeToPlainText(StripImageBlock(text), item.Name, item.Description);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed fetching Workshop README for {item.Id}.");
                return null;
            }
        }

        private static readonly Regex MarkdownHeading = new Regex(@"^[ \t]{0,3}#{1,6}[ \t]+(.*?)[ \t#]*$", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownImage = new Regex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownLink = new Regex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownEmphasis = new Regex(@"(\*\*|__)(.+?)\1", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownCode = new Regex(@"`([^`]+)`", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownBullet = new Regex(@"^([ \t]*)[-*+][ \t]+", RegexOptions.CultureInvariant);
        private static readonly Regex HtmlComment = new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex HtmlTag = new Regex(@"<[^>\r\n]+>", RegexOptions.CultureInvariant);
        private static readonly Regex ExtraBlankLines = new Regex(@"(\r?\n)[ \t]*(\r?\n)([ \t]*\r?\n)+", RegexOptions.CultureInvariant);

        /// <summary>
        /// The README as plain text for the detail pane: markdown and HTML markup removed, and a
        /// heading that repeats the item's name or a paragraph that repeats its description left
        /// out, since the pane shows those above it. Null when nothing else remains.
        /// </summary>
        public static string ReadmeToPlainText(string readme, string name, string description)
        {
            if (string.IsNullOrWhiteSpace(readme))
            {
                return null;
            }

            var text = HtmlComment.Replace(readme, string.Empty);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>(lines.Length);
            foreach (var raw in lines)
            {
                var line = MarkdownImage.Replace(raw, string.Empty);
                var heading = MarkdownHeading.Match(line);
                if (heading.Success)
                {
                    line = heading.Groups[1].Value;
                    if (SameText(line, name))
                    {
                        continue;
                    }
                }

                line = MarkdownBullet.Replace(line, "$1• ");
                line = MarkdownLink.Replace(line, "$1");
                line = MarkdownEmphasis.Replace(line, "$2");
                line = MarkdownCode.Replace(line, "$1");
                line = HtmlTag.Replace(line, string.Empty);
                kept.Add(line.TrimEnd());
            }

            // Drop a paragraph that only repeats the description.
            var paragraphs = string.Join("\n", kept).Split(new[] { "\n\n" }, StringSplitOptions.None)
                .Select(paragraph => paragraph.Trim('\n'))
                .Where(paragraph => paragraph.Trim().Length > 0 && !SameText(paragraph, description));
            var result = ExtraBlankLines.Replace(string.Join("\n\n", paragraphs), "$1$2").Trim();
            return result.Length == 0 ? null : result;
        }

        private static bool SameText(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
            string.Equals(Regex.Replace(a.Trim(), @"\s+", " "), Regex.Replace(b.Trim(), @"\s+", " "), StringComparison.OrdinalIgnoreCase);

        private static readonly Regex ImageBlock = new Regex(
            @"<!--\s*workshop:images\s*-->.*?<!--\s*/workshop:images\s*-->",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LeadingBlankLines = new Regex(@"\A(?:[ \t]*\r?\n)+", RegexOptions.CultureInvariant);

        /// <summary>
        /// The README text without the Workshop's image block (the cover and preview images the
        /// repository puts between <c>&lt;!-- workshop:images --&gt;</c> markers), markers and
        /// the blank lines around it included; the pane shows those images itself. Text around a
        /// block in the middle stays one blank line apart, in the text's own line ending.
        /// </summary>
        public static string StripImageBlock(string readme)
        {
            if (string.IsNullOrEmpty(readme))
            {
                return readme;
            }

            var newline = readme.Contains("\r\n") ? "\r\n" : "\n";
            var text = readme;
            Match match;
            while ((match = ImageBlock.Match(text)).Success)
            {
                var before = text.Substring(0, match.Index).TrimEnd();
                var after = LeadingBlankLines.Replace(text.Substring(match.Index + match.Length).TrimStart(' ', '\t'), string.Empty);
                text = before.Length == 0 || after.Length == 0
                    ? before + after
                    : before + newline + newline + after;
            }

            return text;
        }

        /// <summary>
        /// The item's preview image as a local cached file path, or null. Cached by URL; the URL
        /// is pinned to the last commit that changed the file, so a changed preview has a new URL.
        /// </summary>
        public Task<string> FetchPreviewAsync(WorkshopItem item, CancellationToken cancel)
            => Task.Run(() => FetchImageCoreAsync(item, item?.Urls?.Preview, item?.Preview, "preview"), cancel);

        /// <summary>
        /// The item's optional cover image as a local cached file path, or null when it has none
        /// or the fetch fails. Cached by URL like the preview.
        /// </summary>
        public Task<string> FetchCoverAsync(WorkshopItem item, CancellationToken cancel)
            => Task.Run(() => FetchImageCoreAsync(item, item?.Urls?.Cover, item?.Cover, "cover"), cancel);

        private async Task<string> FetchImageCoreAsync(WorkshopItem item, string url, string fileName, string label)
        {
            var cached = ImageCachePathFor(url, fileName);
            if (cached == null)
            {
                return null;
            }

            if (File.Exists(cached))
            {
                return cached;
            }

            // The list prefetch and the detail pane ask for the same image; they share one download.
            var fetch = _imageFetches.GetOrAdd(cached, path => new Lazy<Task<string>>(() => DownloadImageAsync(item, url, path, label)));
            return await fetch.Value.ConfigureAwait(false);
        }

        private async Task<string> DownloadImageAsync(WorkshopItem item, string url, string cached, string label)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                WriteCacheFile(cached, bytes);
                return cached;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed fetching Workshop {label} for {item?.Id}.");
                return null;
            }
            finally
            {
                _imageFetches.TryRemove(cached, out _);
            }
        }

        /// <summary>
        /// The live download total for an item from the GitHub release API, or null when the
        /// release URL is not a GitHub release page or the request fails. The index carries the
        /// count as of its last build; the detail pane asks for this once an item is picked.
        /// </summary>
        public Task<long?> FetchLiveDownloadsAsync(WorkshopItem item, CancellationToken cancel) => Task.Run(() => FetchLiveDownloadsCoreAsync(item, cancel), cancel);

        private async Task<long?> FetchLiveDownloadsCoreAsync(WorkshopItem item, CancellationToken cancel)
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
        public Task DownloadPackageAsync(WorkshopItem item, string destinationPath, IProgress<long> progress, CancellationToken cancel)
            => Task.Run(() => DownloadPackageCoreAsync(item, destinationPath, progress, cancel), cancel);

        private async Task DownloadPackageCoreAsync(
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
                {
                    var buffer = new byte[1 << 16];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                    {
                        await destination.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        total += read;
                        progress?.Report(total);
                    }
                }
            }

            if (!VerifyPackage(item, destinationPath))
            {
                TryDelete(destinationPath);
                throw new InvalidOperationException(
                    "The downloaded package does not match the Workshop's checksum, so it was not installed.");
            }
        }

        /// <summary>
        /// True when the file at <paramref name="path"/> exists and its SHA-256 matches the
        /// index's hash for <paramref name="item"/>. An item the index lists without a hash
        /// accepts any existing file.
        /// </summary>
        public static bool VerifyPackage(WorkshopItem item, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            var expected = (item?.Package?.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
            if (expected.Length == 0)
            {
                return true;
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                return string.Equals(actual, expected, StringComparison.Ordinal);
            }
        }

        private string ImageCachePathFor(string url, string fileName) =>
            CachePathFor(url, Path.GetExtension(string.IsNullOrWhiteSpace(fileName) ? ".png" : fileName));

        private string ReadmeCachePathFor(string url) => CachePathFor(url, ".md");

        /// <summary>
        /// Writes beside the target and renames, so an interrupted write never leaves a truncated
        /// file that later reads as cached.
        /// </summary>
        private static void WriteCacheFile(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                }
            }
            catch (IOException) when (File.Exists(path))
            {
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>
        /// Deletes cached images and READMEs that no item in the index points at, so the files of
        /// an item's earlier URLs do not pile up. Partial files of downloads still running are kept.
        /// </summary>
        private void PruneCache(WorkshopIndexFile index)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory) || !Directory.Exists(_cacheDirectory))
            {
                return;
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Keep(string path)
            {
                if (path != null)
                {
                    keep.Add(Path.GetFileName(path));
                }
            }

            foreach (var item in index.Items)
            {
                Keep(ImageCachePathFor(item.Urls?.Preview, item.Preview));
                Keep(ImageCachePathFor(item.Urls?.Cover, item.Cover));
                Keep(ReadmeCachePathFor(item.Urls?.Readme));
            }

            try
            {
                var running = _imageFetches.Keys.Select(Path.GetFileName).ToList();
                foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
                {
                    var name = Path.GetFileName(file);
                    if (!keep.Contains(name) && !running.Any(target => name.StartsWith(target, StringComparison.OrdinalIgnoreCase)))
                    {
                        TryDelete(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger?.Debug(ex, "Failed pruning the Workshop cache.");
            }
        }

        private string CachePathFor(string url, string extension)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory) || string.IsNullOrWhiteSpace(url))
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

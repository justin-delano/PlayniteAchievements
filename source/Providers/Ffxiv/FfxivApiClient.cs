using HtmlAgilityPack;
using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Ffxiv
{
    /// <summary>
    /// HTTP client for the FFXIV Collect REST API plus Lodestone character lookup.
    ///
    /// FFXIV Collect supplies both the achievement catalog (definitions + icons +
    /// global ownership %) and per-character unlock data (with timestamps). It has
    /// no name-to-id search, so character resolution falls back to scraping the
    /// public Lodestone character search page.
    /// </summary>
    internal sealed class FfxivApiClient : IDisposable
    {
        private const int MaxAttempts = 5;
        private const int CatalogPageSize = 500;
        private const int MaxCatalogPages = 60;

        private static readonly Uri ApiBase = new Uri("https://ffxivcollect.com/api/");
        private static readonly Regex LodestoneIdRegex =
            new Regex("/lodestone/character/(\\d+)/", RegexOptions.Compiled);

        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly HttpClientHandler _handler;

        public FfxivApiClient(ILogger logger)
        {
            _logger = logger;

            _handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = false
            };

            _http = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        public void Dispose()
        {
            _http?.Dispose();
            _handler?.Dispose();
        }

        /// <summary>
        /// Fetches the full achievement catalog, paging until exhausted. Results are
        /// keyed by id to dedupe defensively in case the paging parameter is ignored.
        /// </summary>
        public async Task<List<FfxivAchievement>> FetchCatalogAsync(CancellationToken cancel)
        {
            var byId = new Dictionary<int, FfxivAchievement>();

            for (var page = 1; page <= MaxCatalogPages; page++)
            {
                cancel.ThrowIfCancellationRequested();

                var uri = new Uri(ApiBase, $"achievements?limit={CatalogPageSize}&page={page}");
                var json = await GetRawAsync(uri, cancel).ConfigureAwait(false);
                var response = JsonConvert.DeserializeObject<FfxivAchievementsResponse>(json);

                if (response?.Results == null || response.Results.Count == 0)
                {
                    break;
                }

                var before = byId.Count;
                foreach (var achievement in response.Results)
                {
                    if (achievement == null) continue;
                    achievement.Icon = NormalizeIconUrl(achievement.Icon);
                    byId[achievement.Id] = achievement;
                }

                // No new rows means paging is unsupported or we reached the end.
                if (byId.Count == before || response.Results.Count < CatalogPageSize)
                {
                    break;
                }
            }

            return new List<FfxivAchievement>(byId.Values);
        }

        /// <summary>
        /// Fetches a character with its obtained achievement ids and unlock times.
        /// </summary>
        public async Task<FfxivCharacter> FetchCharacterAsync(long lodestoneId, CancellationToken cancel)
        {
            var uri = new Uri(ApiBase, $"characters/{lodestoneId.ToString(CultureInfo.InvariantCulture)}?times=true");
            var json = await GetRawAsync(uri, cancel).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<FfxivCharacter>(json);
        }

        /// <summary>
        /// Resolves a Lodestone character id from a character name and world by
        /// parsing the public Lodestone search page. Returns null when no match.
        /// </summary>
        public async Task<long?> ResolveCharacterIdAsync(string name, string world, string region, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
            {
                return null;
            }

            var subdomain = NormalizeRegion(region);
            var url = $"https://{subdomain}.finalfantasyxiv.com/lodestone/character/" +
                      $"?q={Uri.EscapeDataString(name.Trim())}&worldname={Uri.EscapeDataString(world.Trim())}";

            string html;
            try
            {
                html = await GetRawAsync(new Uri(url), cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"[FFXIV] Lodestone character search failed for '{name}' @ '{world}'.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            try
            {
                // The Lodestone search is a partial match (e.g. "Mal Reynolds" also
                // returns "Malynor Reynolds"), so match each result entry on exact
                // name + world rather than taking the first character link.
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var wantName = HtmlEntity.DeEntitize(name).Trim();
                var wantWorld = world.Trim();

                var entries = doc.DocumentNode.SelectNodes("//a[contains(@class, 'entry__link')]");
                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        var href = entry.GetAttributeValue("href", string.Empty);
                        var idMatch = LodestoneIdRegex.Match(href);
                        if (!idMatch.Success)
                        {
                            continue;
                        }

                        var nameNode = entry.SelectSingleNode(".//p[contains(@class, 'entry__name')]");
                        var worldNode = entry.SelectSingleNode(".//p[contains(@class, 'entry__world')]");

                        var entryName = HtmlEntity.DeEntitize(nameNode?.InnerText ?? string.Empty).Trim();
                        var entryWorldText = HtmlEntity.DeEntitize(worldNode?.InnerText ?? string.Empty);
                        // entry__world reads like "Gilgamesh [Aether]"; keep the world.
                        var entryWorld = entryWorldText.Split('[')[0].Replace(' ', ' ').Trim();

                        if (string.Equals(entryName, wantName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(entryWorld, wantWorld, StringComparison.OrdinalIgnoreCase) &&
                            long.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        {
                            return id;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "[FFXIV] Failed to parse Lodestone search results.");
            }

            return null;
        }

        private async Task<string> GetRawAsync(Uri uri, CancellationToken cancel)
        {
            var response = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.TryAddWithoutValidation("User-Agent", "PlayniteAchievements/FFXIV");
                return req;
            }, cancel).ConfigureAwait(false);

            if (response == null)
            {
                throw new HttpRequestException("No response.");
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancel)
        {
            Exception lastException = null;
            var backoff = TimeSpan.FromSeconds(1);

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancel.ThrowIfCancellationRequested();

                try
                {
                    using (var req = requestFactory())
                    {
                        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);

                        if (resp.StatusCode == (HttpStatusCode)429)
                        {
                            var delay = GetRetryAfterDelay(resp) ?? backoff;
                            _logger?.Warn($"[FFXIV] 429 received. Backing off for {delay.TotalSeconds:0.0}s (attempt {attempt}/{MaxAttempts}).");
                            resp.Dispose();
                            await Task.Delay(delay, cancel).ConfigureAwait(false);
                            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
                            continue;
                        }

                        if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.RequestTimeout)
                        {
                            _logger?.Warn($"[FFXIV] Server error {(int)resp.StatusCode} on {req.RequestUri} (attempt {attempt}/{MaxAttempts}).");
                            resp.Dispose();
                            await Task.Delay(backoff, cancel).ConfigureAwait(false);
                            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
                            continue;
                        }

                        return resp;
                    }
                }
                catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
                {
                    lastException = ex;
                    _logger?.Warn($"[FFXIV] Timeout on attempt {attempt}/{MaxAttempts}.");
                    await Task.Delay(backoff, cancel).ConfigureAwait(false);
                    backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    _logger?.Warn(ex, $"[FFXIV] HTTP error on attempt {attempt}/{MaxAttempts}.");
                    await Task.Delay(backoff, cancel).ConfigureAwait(false);
                    backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
                }
            }

            throw new HttpRequestException("Request failed after retries.", lastException);
        }

        private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage resp)
        {
            try
            {
                if (resp?.Headers?.RetryAfter == null) return null;
                if (resp.Headers.RetryAfter.Delta.HasValue) return resp.Headers.RetryAfter.Delta.Value;
                if (resp.Headers.RetryAfter.Date.HasValue)
                {
                    var delta = resp.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        /// <summary>
        /// Rewrites the FFXIV Collect icon URL from webp to png. WPF on .NET
        /// Framework 4.6.2 cannot decode webp.
        /// </summary>
        private static string NormalizeIconUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            return url.Replace("format=webp", "format=png");
        }

        private static string NormalizeRegion(string region)
        {
            var normalized = (region ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "eu":
                case "de":
                case "fr":
                case "jp":
                case "na":
                    return normalized;
                default:
                    return "na";
            }
        }
    }
}

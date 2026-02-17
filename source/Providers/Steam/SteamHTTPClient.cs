using HtmlAgilityPack;
using PlayniteAchievements.Providers.Steam.Models;
using Playnite.SDK;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Steam
{
    public sealed class SteamHttpClient : IDisposable
    {
        private static readonly Uri CommunityBase = new Uri("https://steamcommunity.com/");
        private static readonly Uri StoreBase = new Uri("https://store.steampowered.com/");
        private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private const int MaxAttempts = 3;

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly SteamSessionManager _sessionManager;
        private readonly SteamApiClient _steamApiClient;
        private readonly CookieContainer _cookieJar = new CookieContainer();
        private readonly object _cookieLock = new object();

        private HttpClient _http;
        private HttpClientHandler _handler;
        private HttpClient _apiHttp;
        private HttpClientHandler _apiHandler;

        public HttpClient ApiHttpClient => _apiHttp;

        private readonly ConcurrentDictionary<int, Lazy<Task<bool?>>> _hasAchievementsCache =
            new ConcurrentDictionary<int, Lazy<Task<bool?>>>();

        public SteamHttpClient(IPlayniteAPI api, ILogger logger, SteamSessionManager sessionManager)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

            BuildHttpClientsOnce();
            _steamApiClient = new SteamApiClient(_apiHttp, _logger);

            LoadCookiesFromCefIntoJar();
        }

        public void Dispose()
        {
            _http?.Dispose();
            _handler?.Dispose();
            _apiHttp?.Dispose();
            _apiHandler?.Dispose();
        }

        // ---------------------------------------------------------------------
        // Session Management
        // ---------------------------------------------------------------------

        public Task<string> GetRequiredSelfSteamId64Async(CancellationToken ct) =>
            _sessionManager.GetUserSteamId64Async(ct);

        private async Task<bool> EnsureSessionAsync(CancellationToken ct, bool forceRefresh = false)
        {
            ct.ThrowIfCancellationRequested();

            if (!forceRefresh && !_sessionManager.NeedsRefresh)
            {
                lock (_cookieLock)
                {
                    if (HasCookiesInJar()) return true;
                }

                LoadCookiesFromCefIntoJar();
                lock (_cookieLock)
                {
                    if (HasCookiesInJar()) return true;
                }
            }

            _logger?.Debug($"[FAF] Refreshing Steam session (Force={forceRefresh})...");
            var refreshed = await _sessionManager.RefreshCookiesHeadlessAsync(ct).ConfigureAwait(false);

            if (refreshed)
            {
                LoadCookiesFromCefIntoJar();
            }

            return refreshed;
        }

        private void LoadCookiesFromCefIntoJar()
        {
            lock (_cookieLock)
            {
                _sessionManager.LoadCefCookiesIntoJar(_api, _cookieJar, _logger);
            }
        }

        private bool HasCookiesInJar()
        {
            bool Has(CookieCollection cc) =>
                cc.Cast<Cookie>().Any(c => c.Name.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase));

            try
            {
                return Has(_cookieJar.GetCookies(CommunityBase))
                    || Has(_cookieJar.GetCookies(StoreBase));
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------------
        // Steam Web API
        // ---------------------------------------------------------------------

        public Task<Dictionary<int, int>> GetPlaytimesAsync(
            string apiKey, string steamId64, bool includePlayedFreeGames = true)
        {
            return _steamApiClient.GetOwnedGamesAsync(apiKey, steamId64, includePlayedFreeGames);
        }

        // ---------------------------------------------------------------------
        // Profile / Achievements
        // ---------------------------------------------------------------------

        public Task<SteamPageResult> GetAchievementsPageAsync(string steamId64, int appId, string language, CancellationToken ct)
            => GetSteamPageAsync($"https://steamcommunity.com/profiles/{steamId64}/stats/{appId}/?tab=achievements&l={language ?? "english"}", true, ct);

        public Task<SteamPageResult> GetAchievementsPageByKeyAsync(string steamId64, string statsKey, string language, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(statsKey))
            {
                return Task.FromResult(new SteamPageResult());
            }

            return GetSteamPageAsync($"https://steamcommunity.com/profiles/{steamId64}/stats/{statsKey.Trim()}/?tab=achievements&l={language ?? "english"}", true, ct);
        }

        // ---------------------------------------------------------------------
        // Player Summaries
        // ---------------------------------------------------------------------

        public async Task<List<SteamPlayerSummaries>> GetPlayerSummariesAsync(string apiKey, IEnumerable<ulong> steamIds, CancellationToken ct)
        {
            var ids = steamIds?.Where(x => x > 0).Distinct().ToList() ?? new List<ulong>();
            if (ids.Count == 0) return new List<SteamPlayerSummaries>();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger?.Warn("[FAF] An API key is required to fetch Steam friend data.");
                return new List<SteamPlayerSummaries>();
            }

            var apiResults = await _steamApiClient.GetPlayerSummariesAsync(apiKey, ids, ct).ConfigureAwait(false);
            return apiResults ?? new List<SteamPlayerSummaries>();
        }

        // ---------------------------------------------------------------------
        // Parsing / Schema
        // ---------------------------------------------------------------------

        public List<ScrapedAchievement> ParseAchievements(string html, bool includeLocked, string language = "english")
        {
            var safe = html ?? string.Empty;
            if (safe.Length < 200) return new List<ScrapedAchievement>();

            var doc = new HtmlDocument();
            doc.LoadHtml(safe);

            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'achieveRow')]") ??
                        doc.DocumentNode.SelectNodes("//*[contains(@class,'achievement') and (.//h3 or .//div[contains(@class,'achieveUnlockTime')])]");

            if (nodes == null || nodes.Count == 0) return new List<ScrapedAchievement>();

            // Parse the summary progress bar and use it as the primary unlock source:
            // Steam orders unlocked achievements first, so the first N rows are unlocked.
            var hasProgressSummary = TryParseAchievementCountsFromProgressBar(doc, out var unlockedCountFromProgressBar, out var totalCountFromProgressBar);
            if (hasProgressSummary)
            {
                unlockedCountFromProgressBar = Math.Max(0, Math.Min(unlockedCountFromProgressBar, totalCountFromProgressBar));
                unlockedCountFromProgressBar = Math.Min(unlockedCountFromProgressBar, nodes.Count);
            }

            var results = new List<ScrapedAchievement>();
            int rowIndex = 0;

            foreach (var row in nodes)
            {
                var rowIndexInList = rowIndex;
                rowIndex++;

                if (row.SelectSingleNode(".//div[contains(@class,'achieveHiddenBox')]") != null) continue;

                var unlockNode = row.SelectSingleNode(".//div[contains(@class,'achieveUnlockTime')]");
                var hasUnlockMarker = unlockNode != null;
                var unlockText = ExtractUnlockText(unlockNode);
                var unlockUtc = SteamTimeParser.TryParseSteamUnlockTime(unlockText, language);

                // Primary indicator: progress summary ordering (first N rows unlocked).
                // Fallback if summary is missing: presence of Steam's unlock marker.
                bool isUnlocked = hasProgressSummary
                    ? rowIndexInList < unlockedCountFromProgressBar
                    : hasUnlockMarker;

                if (hasUnlockMarker && !unlockUtc.HasValue && !string.IsNullOrWhiteSpace(unlockText))
                {
                    var snippet = unlockText.Substring(0, Math.Min(50, unlockText.Length));
                    var message = $"[SteamAch] ParseAchievements: Failed to parse time (lang={language}) from '{unlockText}' (hex: {BitConverter.ToString(Encoding.UTF8.GetBytes(snippet))})";

                    // Parsing failures are non-fatal when we have the progress summary fallback.
                    if (hasProgressSummary)
                    {
                        _logger?.Debug(message);
                    }
                    else
                    {
                        _logger?.Warn(message);
                    }
                }

                if (!includeLocked && !isUnlocked) continue;

                var title = WebUtility.HtmlDecode(row.SelectSingleNode(".//h3")?.InnerText ?? "").Trim();
                var desc = WebUtility.HtmlDecode(row.SelectSingleNode(".//h5")?.InnerText ?? "").Trim();
                var img = row.SelectSingleNode(".//div[contains(@class,'achieveImgHolder')]//img") ??
                          row.SelectSingleNode(".//img");
                var iconUrl = img?.GetAttributeValue("src", "")?.Trim() ?? "";

                var primaryKeyPart = !string.IsNullOrWhiteSpace(title) ? title : iconUrl;
                var secondaryKeyPart = !string.IsNullOrWhiteSpace(desc) ? desc : (unlockUtc.HasValue ? unlockUtc.Value.ToString("O") : "");

                int? progressNum = null;
                int? progressDenom = null;

                var progressBar = row.SelectSingleNode(".//div[contains(@class,'achievementProgressBar')]");
                if (progressBar != null)
                {
                    var progressText = progressBar.SelectSingleNode(".//div[contains(@class,'progressText')]");
                    if (progressText != null)
                    {
                        var text = WebUtility.HtmlDecode(progressText.InnerText).Trim();
                        var match = Regex.Match(text, @"^(\d+)\s*/\s*(\d+)$");
                        if (match.Success)
                        {
                            progressNum = int.Parse(match.Groups[1].Value);
                            progressDenom = int.Parse(match.Groups[2].Value);
                        }
                    }
                }

                results.Add(new ScrapedAchievement
                {
                    Key = (primaryKeyPart + "|" + secondaryKeyPart).Trim(),
                    DisplayName = title,
                    Description = desc,
                    IconUrl = iconUrl,
                    UnlockTimeUtc = unlockUtc,
                    IsUnlocked = isUnlocked,
                    ProgressNum = progressNum,
                    ProgressDenom = progressDenom
                });
            }
            return results;
        }

        /// <summary>
        /// Parses the progress bar section to extract unlocked and total achievement counts.
        /// Steam always displays unlocked achievements first, so first N rows are unlocked.
        /// This works across languages by finding the first two numbers in the summary text.
        /// </summary>
        private static bool TryParseAchievementCountsFromProgressBar(HtmlDocument doc, out int unlockedCount, out int totalCount)
        {
            unlockedCount = 0;
            totalCount = 0;

            // Look for the achievements summary section
            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@id='topSummaryAchievements']");
            if (summaryNode == null) return false;

            // Get the first text-containing div (before the progress bar)
            var textNode = summaryNode.SelectSingleNode("./div[not(contains(@class,'achieveBar'))]");
            if (textNode == null) return false;

            var text = WebUtility.HtmlDecode(textNode.InnerText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Find first two numbers - they represent (unlocked, total) regardless of language
            // English: "12 of 15 (80%) achievements earned"
            // French: "12 sur 15 (80%) succès débloqués"
            // German: "12 von 15 (80%) Errungenschaften freigeschaltet"
            var numberMatches = Regex.Matches(text, @"\b(\d+)\b");
            if (numberMatches.Count >= 2 &&
                int.TryParse(numberMatches[0].Groups[1].Value, out var parsedUnlockedCount) &&
                int.TryParse(numberMatches[1].Groups[1].Value, out var parsedTotalCount) &&
                parsedTotalCount > 0)
            {
                unlockedCount = Math.Max(0, Math.Min(parsedUnlockedCount, parsedTotalCount));
                totalCount = parsedTotalCount;
                return true;
            }

            return false;
        }

        private static string ExtractUnlockText(HtmlNode unlockNode)
        {
            if (unlockNode == null)
            {
                return string.Empty;
            }

            var raw = WebUtility.HtmlDecode(unlockNode.InnerText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var firstLine = raw
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

            return firstLine ?? raw.Trim();
        }

        public Task<bool?> GetAppHasAchievementsAsync(string apiKey, int appId, CancellationToken ct)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(apiKey)) return Task.FromResult<bool?>(false);

            return _hasAchievementsCache.GetOrAdd(appId,
                new Lazy<Task<bool?>>(() => FetchAppHasAchievementsAsync(apiKey, appId))).Value;
        }

        private async Task<bool?> FetchAppHasAchievementsAsync(string apiKey, int appId)
        {
            return await _steamApiClient.GetSchemaForGameAsync(apiKey, appId).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------------
        // HTTP Core
        // ---------------------------------------------------------------------

        private async Task<SteamPageResult> GetSteamPageAsync(string url, bool requiresCookies, CancellationToken ct)
        {
            // _logger.Info($"[FAF] GetSteamPageAsync: Requesting {url} (Cookies={requiresCookies})");
            var result = new SteamPageResult { RequestedUrl = url, FinalUrl = url, Html = "", StatusCode = 0, WasRedirected = false };
            if (string.IsNullOrWhiteSpace(url)) return result;

            Uri uri;
            try { uri = new Uri(url); } catch { return result; }

            bool isSteamAuth = uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                               uri.Host.EndsWith("steampowered.com", StringComparison.OrdinalIgnoreCase);

            if (requiresCookies && isSteamAuth)
            {
                await EnsureSessionAsync(ct).ConfigureAwait(false);
            }

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                // _logger.Info($"[FAF] GetSteamPageAsync: Attempt {attempt} for {url}");

                using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

                    if (isSteamAuth)
                        req.Headers.Referrer = uri.Host.Contains("store") ? StoreBase : CommunityBase;

                    try
                    {
                        using (var resp = await _http.SendAsync(req, ct).ConfigureAwait(false))
                        {
                            result.StatusCode = resp.StatusCode;
                            result.FinalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? url;
                            result.WasRedirected = !string.Equals(result.FinalUrl, url, StringComparison.OrdinalIgnoreCase);

                            // _logger.Info($"[FAF] GetSteamPageAsync: Response {resp.StatusCode} for {url}. FinalUrl={result.FinalUrl}");

                            if (requiresCookies && isSteamAuth && attempt == 1)
                            {
                                bool softRedirect = result.FinalUrl.Contains("/login") || result.FinalUrl.Contains("openid");
                                if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden || softRedirect)
                                {
                                    _logger.Warn($"[FAF] Auth failed for {url} (Status={resp.StatusCode}, Url={result.FinalUrl}). Forcing session refresh.");
                                    await EnsureSessionAsync(ct, forceRefresh: true).ConfigureAwait(false);
                                    continue;
                                }
                            }

                            result.Html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"[FAF] GetSteamPageAsync: Exception on attempt {attempt} for {url}");
                        if (attempt < MaxAttempts) continue;
                        return result;
                    }
                }
            }
            return result;
        }

        private void BuildHttpClientsOnce()
        {
            _handler?.Dispose(); _http?.Dispose();
            _apiHandler?.Dispose(); _apiHttp?.Dispose();

            _cookieJar.PerDomainCapacity = 300;

            _handler = new HttpClientHandler
            {
                CookieContainer = _cookieJar,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true
            };
            _http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(30) };

            _apiHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = false
            };
            _apiHttp = new HttpClient(_apiHandler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ---------------------------------------------------------------------
        // Static Helpers
        // ---------------------------------------------------------------------

        public static bool LooksLoggedOutHeader(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;

            if (html.IndexOf("global_action_menu", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (Regex.IsMatch(html, @"<a[^>]+class\s*=\s*[""'][^""']*\bglobal_action_link\b[^""']*[""'][^>]+href\s*=\s*[""'][^""']*/login[^""']*[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                return true;

            return html.IndexOf("Sign In", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   html.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   html.IndexOf("global_action", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HasAnyAchievementRows(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;
            return html.IndexOf("achievements_list", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("achieveRow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("achieveImgHolder", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HasOnlyHiddenAchievementRows(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'achieveRow')]") ??
                        doc.DocumentNode.SelectNodes("//*[contains(@class,'achievement') and (.//h3 or .//div[contains(@class,'achieveUnlockTime')])]");

            if (nodes == null || nodes.Count == 0)
            {
                return false;
            }

            var hasHiddenRow = false;
            foreach (var row in nodes)
            {
                var isHidden = row.SelectSingleNode(".//div[contains(@class,'achieveHiddenBox')]") != null;
                if (!isHidden)
                {
                    return false;
                }

                hasHiddenRow = true;
            }

            return hasHiddenRow;
        }
    }
}

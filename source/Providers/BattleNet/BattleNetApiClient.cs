using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Common;
using PlayniteAchievements.Providers.BattleNet.Models;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class BattleNetTransientException : Exception
    {
        public BattleNetTransientException(string message) : base(message) { }
        public BattleNetTransientException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class BattleNetApiClient : IDisposable
    {
        private bool _disposed;
        private const string Sc2ProfileUrl = "https://starcraft2.com/api/sc2/profile/{0}/1/{1}?locale={2}";
        private const string Sc2AchievementsUrl = "https://starcraft2.com/api/sc2/static/profile/2?locale={0}";
        private const string WowBaseAchievementUrl = "https://worldofwarcraft.blizzard.com/{0}/character/{1}/{2}/{3}/achievements/{4}";
        private const string WowGraphQlUrl = "https://worldofwarcraft.blizzard.com/graphql";
        private const string WowStatusUrl = "https://worldofwarcraft.blizzard.com/game/status";
        private const string DefaultLocale = "en-US";

        private const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly string[] WowCategories =
        {
            "characters/model.json",
            "player-vs-player/model.json",
            "quests/model.json",
            "exploration/model.json",
            "world-events/model.json",
            "dungeons-raids/model.json",
            "professions/model.json",
            "reputation/model.json",
            "pet-battles/model.json",
            "collections/model.json",
            "expansion-features/model.json",
            "feats-of-strength/model.json",
            "legacy/model.json"
        };

        private static readonly RateLimiter RateLimiter = new RateLimiter(1000, 3);

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private string _wowSha256Hash;

        public BattleNetApiClient(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            _httpClient.DefaultRequestHeaders.Add("User-Agent", DefaultUserAgent);
            _logger.Debug("[BattleNet/API] API client initialized.");
        }

        // --- SC2 ---

        public Task<Sc2ProfileResponse> GetSc2ProfileAsync(int regionId, int profileId, string locale, CancellationToken ct)
        {
            var effectiveLocale = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale;
            _logger?.Debug($"[BattleNet/API] SC2 profile requested. region={regionId}, profileId={MaskId(profileId.ToString())}, locale={effectiveLocale}");

            var url = string.Format(Sc2ProfileUrl, regionId, profileId, effectiveLocale);
            return RateLimiter.ExecuteWithRetryAsync(
                async () => await GetJsonAsync<Sc2ProfileResponse>(url, ct).ConfigureAwait(false),
                IsTransientError, ct);
        }

        public Task<Sc2AchievementDefinitionsResponse> GetSc2AchievementDefinitionsAsync(string locale, CancellationToken ct)
        {
            var effectiveLocale = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale;
            _logger?.Debug($"[BattleNet/API] SC2 achievement definitions requested. locale={effectiveLocale}");

            var url = string.Format(Sc2AchievementsUrl, effectiveLocale);
            return RateLimiter.ExecuteWithRetryAsync(
                async () => await GetJsonAsync<Sc2AchievementDefinitionsResponse>(url, ct).ConfigureAwait(false),
                IsTransientError, ct);
        }

        // --- WoW ---

        public async Task<List<WowAchievementsData>> GetWowAllAchievementsAsync(
            string region, string realmSlug, string character, string locale, CancellationToken ct)
        {
            var results = new List<WowAchievementsData>();
            var effectiveLocale = string.IsNullOrWhiteSpace(locale) ? "en-us" : locale;
            _logger?.Info($"[BattleNet/API] WoW achievement categories requested. region={region ?? "<none>"}, realmSlug={realmSlug ?? "<none>"}, character={Presence(character)}, locale={effectiveLocale}");

            var baseUrl = string.Format(WowBaseAchievementUrl,
                effectiveLocale, region, realmSlug, Uri.EscapeDataString(character), "{0}");

            foreach (var category in WowCategories)
            {
                ct.ThrowIfCancellationRequested();
                var url = string.Format(baseUrl, category);
                try
                {
                    _logger?.Debug($"[BattleNet/API] Fetching WoW achievement category. category={category}");
                    var data = await RateLimiter.ExecuteWithRetryAsync(
                        async () => await GetJsonAsync<WowAchievementsData>(url, ct).ConfigureAwait(false),
                        IsTransientError, ct);
                    if (data != null)
                    {
                        results.Add(data);
                        _logger?.Debug($"[BattleNet/API] Fetched WoW achievement category. category={category}, displayName={data.Name ?? data.Category ?? "<none>"}");
                    }
                    else
                    {
                        _logger?.Debug($"[BattleNet/API] WoW achievement category returned no data. category={category}");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.Debug(ex, $"[BattleNet/API] Failed to fetch WoW achievement category. category={category}");
                }
            }

            _logger?.Info($"[BattleNet/API] Completed WoW achievement category fetch. region={region ?? "<none>"}, realmSlug={realmSlug ?? "<none>"}, fetched={results.Count}/{WowCategories.Length}");
            return results;
        }

        public async Task<List<WowRealm>> GetWowRealmsAsync(string region, CancellationToken ct)
        {
            _logger?.Debug($"[BattleNet/API] WoW realms requested. region={region ?? "<none>"}");
            var hash = await GetWowSha256HashAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(hash))
            {
                _logger?.Warn("[BattleNet/API] Could not obtain WoW GraphQL SHA256 hash.");
                return new List<WowRealm>();
            }

            var payload = $"{{\"operationName\":\"GetRealmStatusData\",\"variables\":{{\"input\":{{\"compoundRegionGameVersionSlug\":\"{region}\"}}}},\"extensions\":{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{hash}\"}}}}}}";

            _logger?.Debug($"[BattleNet/API] Posting WoW realms GraphQL request. region={region ?? "<none>"}, hash={Presence(hash)}");
            var response = await _httpClient.PostAsync(WowGraphQlUrl, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
            _logger?.Debug($"[BattleNet/API] WoW realms GraphQL response. status={(int)response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = Serialization.FromJson<WowRegionResult>(json);
            var realms = result?.Data?.Realms ?? new List<WowRealm>();
            _logger?.Info($"[BattleNet/API] Loaded WoW realms. region={region ?? "<none>"}, count={realms.Count}");
            return realms;
        }

        // --- Transient error detection ---

        public static bool IsTransientError(Exception ex)
        {
            if (ex is BattleNetTransientException) return true;
            if (ex is HttpRequestException) return true;
            if (ex is WebException) return true;
            if (ex is OperationCanceledException) return false;

            if (ex.GetBaseException() is WebException webEx)
            {
                if (webEx.Status == WebExceptionStatus.Timeout ||
                    webEx.Status == WebExceptionStatus.ConnectionClosed ||
                    webEx.Status == WebExceptionStatus.ConnectFailure)
                    return true;
            }

            return false;
        }

        // --- Private ---

        private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
        {
            _logger?.Debug($"[BattleNet/API] GET JSON started. url={UrlHostAndPath(url)}");
            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            _logger?.Debug($"[BattleNet/API] GET JSON response. url={UrlHostAndPath(url)}, status={(int)response.StatusCode}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger?.Debug($"[BattleNet/API] GET JSON returned 404. url={UrlHostAndPath(url)}");
                return null;
            }

            if ((int)response.StatusCode == 429 ||
                (int)response.StatusCode >= 500)
            {
                throw new BattleNetTransientException($"HTTP {(int)response.StatusCode} from {UrlHostAndPath(url)}");
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            _logger?.Debug($"[BattleNet/API] GET JSON payload read. url={UrlHostAndPath(url)}, bytes={json?.Length ?? 0}");
            var value = Serialization.FromJson<T>(json);
            _logger?.Debug($"[BattleNet/API] GET JSON parsed. url={UrlHostAndPath(url)}, hasValue={Bool(value != null)}");
            return value;
        }

        private async Task<string> GetWowSha256HashAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(_wowSha256Hash))
            {
                _logger?.Debug("[BattleNet/API] Using cached WoW GraphQL SHA256 hash.");
                return _wowSha256Hash;
            }

            try
            {
                _logger?.Debug($"[BattleNet/API] Fetching WoW status page for GraphQL hash. url={UrlHostAndPath(WowStatusUrl)}");
                var statusHtml = await _httpClient.GetStringAsync(WowStatusUrl);
                var scriptMatch = Regex.Match(statusHtml, @"<script\s+src=""([^""]+realm-status.\w*.js)"">", RegexOptions.IgnoreCase);
                if (!scriptMatch.Success)
                {
                    _logger?.Warn("[BattleNet/API] Could not find WoW realm-status script on status page.");
                    return null;
                }

                var scriptUrl = new Uri(new Uri(WowStatusUrl), scriptMatch.Groups[1].Value).ToString();
                _logger?.Debug($"[BattleNet/API] Fetching WoW realm-status script. url={UrlHostAndPath(scriptUrl)}");
                var scriptContent = await _httpClient.GetStringAsync(scriptUrl);

                var hashMatch = Regex.Match(scriptContent, @"""GetRealmStatusData""\)[^,]*,\w*\.documentId=""(\w*)""");
                if (hashMatch.Success)
                {
                    _wowSha256Hash = hashMatch.Groups[1].Value;
                    _logger?.Debug("[BattleNet/API] Extracted WoW GraphQL SHA256 hash.");
                    return _wowSha256Hash;
                }

                _logger?.Warn("[BattleNet/API] WoW realm-status script did not contain the expected GraphQL hash.");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[BattleNet/API] Failed to extract WoW GraphQL hash.");
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _logger?.Debug("[BattleNet/API] Disposing API client.");
            _httpClient?.Dispose();
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string MaskId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= 4)
            {
                return "****";
            }

            return $"{new string('*', Math.Min(8, trimmed.Length - 4))}{trimmed.Substring(trimmed.Length - 4)}";
        }

        private static string Presence(string value) => string.IsNullOrWhiteSpace(value) ? "missing" : "set";

        private static string UrlHostAndPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "<empty>";
            }

            try
            {
                var uri = new Uri(url);
                var path = RedactKnownSensitivePathSegments(uri.AbsolutePath);
                return uri.GetLeftPart(UriPartial.Authority) + path;
            }
            catch
            {
                return "<invalid-url>";
            }
        }

        private static string RedactKnownSensitivePathSegments(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "character", StringComparison.OrdinalIgnoreCase) &&
                    i + 4 < segments.Length &&
                    string.Equals(segments[i + 4], "achievements", StringComparison.OrdinalIgnoreCase))
                {
                    segments[i + 3] = "<character>";
                }

                if (string.Equals(segments[i], "profile", StringComparison.OrdinalIgnoreCase) &&
                    i + 3 < segments.Length &&
                    i > 0 &&
                    string.Equals(segments[i - 1], "sc2", StringComparison.OrdinalIgnoreCase))
                {
                    segments[i + 3] = "<profile>";
                }
            }

            return string.Join("/", segments);
        }
    }
}

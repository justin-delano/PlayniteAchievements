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

    internal sealed class BattleNetApiClient
    {
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
        }

        // --- SC2 ---

        public Task<Sc2ProfileResponse> GetSc2ProfileAsync(int regionId, int profileId, string locale, CancellationToken ct)
        {
            var url = string.Format(Sc2ProfileUrl, regionId, profileId, locale ?? DefaultLocale);
            return RateLimiter.ExecuteWithRetryAsync(
                async () => await GetJsonAsync<Sc2ProfileResponse>(url, ct).ConfigureAwait(false),
                IsTransientError, ct);
        }

        public Task<Sc2AchievementDefinitionsResponse> GetSc2AchievementDefinitionsAsync(string locale, CancellationToken ct)
        {
            var url = string.Format(Sc2AchievementsUrl, locale ?? DefaultLocale);
            return RateLimiter.ExecuteWithRetryAsync(
                async () => await GetJsonAsync<Sc2AchievementDefinitionsResponse>(url, ct).ConfigureAwait(false),
                IsTransientError, ct);
        }

        // --- WoW ---

        public async Task<List<WowAchievementsData>> GetWowAllAchievementsAsync(
            string region, string realmSlug, string character, string locale, CancellationToken ct)
        {
            var results = new List<WowAchievementsData>();
            var baseUrl = string.Format(WowBaseAchievementUrl,
                locale ?? "en-us", region, realmSlug, Uri.EscapeDataString(character), "{0}");

            foreach (var category in WowCategories)
            {
                ct.ThrowIfCancellationRequested();
                var url = string.Format(baseUrl, category);
                try
                {
                    var data = await RateLimiter.ExecuteWithRetryAsync(
                        async () => await GetJsonAsync<WowAchievementsData>(url, ct).ConfigureAwait(false),
                        IsTransientError, ct);
                    if (data != null)
                    {
                        results.Add(data);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.Debug(ex, $"[BattleNet] Failed to fetch WoW category: {category}");
                }
            }

            return results;
        }

        public async Task<List<WowRealm>> GetWowRealmsAsync(string region, CancellationToken ct)
        {
            var hash = await GetWowSha256HashAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(hash))
            {
                _logger?.Warn("[BattleNet] Could not obtain WoW GraphQL SHA256 hash.");
                return new List<WowRealm>();
            }

            var payload = $"{{\"operationName\":\"GetRealmStatusData\",\"variables\":{{\"input\":{{\"compoundRegionGameVersionSlug\":\"{region}\"}}}},\"extensions\":{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{hash}\"}}}}}}";

            var response = await _httpClient.PostAsync(WowGraphQlUrl, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = Serialization.FromJson<WowRegionResult>(json);
            return result?.Data?.Realms ?? new List<WowRealm>();
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
            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500)
            {
                throw new BattleNetTransientException($"HTTP {(int)response.StatusCode} from {url}");
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return Serialization.FromJson<T>(json);
        }

        private async Task<string> GetWowSha256HashAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(_wowSha256Hash))
                return _wowSha256Hash;

            try
            {
                var statusHtml = await _httpClient.GetStringAsync(WowStatusUrl);
                var scriptMatch = Regex.Match(statusHtml, @"<script\s+src=""([^""]+realm-status.\w*.js)"">", RegexOptions.IgnoreCase);
                if (!scriptMatch.Success) return null;

                var scriptUrl = scriptMatch.Groups[1].Value;
                var scriptContent = await _httpClient.GetStringAsync(scriptUrl);

                var hashMatch = Regex.Match(scriptContent, @"""GetRealmStatusData""\)[^,]*,\w*\.documentId=""(\w*)""");
                if (hashMatch.Success)
                {
                    _wowSha256Hash = hashMatch.Groups[1].Value;
                    return _wowSha256Hash;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[BattleNet] Failed to extract WoW GraphQL hash.");
            }

            return null;
        }
    }
}

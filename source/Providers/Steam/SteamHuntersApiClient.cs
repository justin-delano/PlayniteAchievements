using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamHuntersApiClient
    {
        private const string BaseUrl = "https://steamhunters.com/api";
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public SteamHuntersApiClient(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }

        public async Task<SteamHuntersAchievementGroupsResponse> GetAchievementGroupsAsync(
            int appId,
            CancellationToken cancel)
        {
            if (appId <= 0)
            {
                return null;
            }

            var url = BaseUrl + "/GetAchievementGroups/v1?appId=" +
                appId.ToString(CultureInfo.InvariantCulture);

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json,text/html;q=0.8,*/*;q=0.5");
                request.Headers.TryAddWithoutValidation("X-Requested-With", "PlayniteAchievements");

                using (var response = await _httpClient.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.Warn(
                            $"[SteamHunters] Group request failed for appId={appId}. Status={(int)response.StatusCode}. BodyLength={body?.Length ?? 0}");
                        return null;
                    }

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        return null;
                    }

                    try
                    {
                        return JsonConvert.DeserializeObject<SteamHuntersAchievementGroupsResponse>(body);
                    }
                    catch (JsonException ex)
                    {
                        _logger?.Warn(ex, $"[SteamHunters] Failed to parse achievement groups for appId={appId}.");
                        return null;
                    }
                }
            }
        }
    }

    internal sealed class SteamHuntersAchievementGroupsResponse
    {
        public string GroupBy { get; set; }

        public List<SteamHuntersAchievementGroup> Groups { get; set; } =
            new List<SteamHuntersAchievementGroup>();
    }

    internal sealed class SteamHuntersAchievementGroup
    {
        public string Name { get; set; }

        public int? DlcAppId { get; set; }

        public string DlcAppName { get; set; }

        public List<string> AchievementApiNames { get; set; } = new List<string>();
    }
}

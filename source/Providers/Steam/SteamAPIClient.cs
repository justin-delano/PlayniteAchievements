using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PlayniteAchievements.Providers.Steam.Models;
using Playnite.SDK;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamApiClient
    {
        private readonly HttpClient _apiHttp;
        private readonly ILogger _logger;

        public SteamApiClient(HttpClient apiHttp, ILogger logger)
        {
            _apiHttp = apiHttp ?? throw new ArgumentNullException(nameof(apiHttp));
            _logger = logger;
        }

        public async Task<SchemaAndPercentages> GetSchemaForGameDetailedAsync(string accessToken, int appId, string language, CancellationToken ct)
        {
            var result = await GetSchemaForGameDetailedInternalAsync(accessToken, appId, language, ct).ConfigureAwait(false);
            if (result == null && !string.Equals(language, "english", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Info($"Schema fetch failed for '{language}', retrying with 'english' for appId={appId}");
                result = await GetSchemaForGameDetailedInternalAsync(accessToken, appId, "english", ct).ConfigureAwait(false);
            }
            return result;
        }

        public async Task<bool?> GetGameHasAchievementsAsync(string accessToken, int appId, string language, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(accessToken) || appId <= 0)
            {
                return false;
            }

            try
            {
                language = string.IsNullOrWhiteSpace(language) ? "english" : language;
                var url = $"https://api.steampowered.com/IPlayerService/GetGameAchievements/v1/" +
                          $"?key={Uri.EscapeDataString(accessToken)}" +
                          $"&appid={appId}" +
                          $"&language={Uri.EscapeDataString(language)}";

                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                using (var resp = await _apiHttp.SendAsync(req, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return null;
                    }

                    var root = JsonConvert.DeserializeObject<GetGameAchievementsRoot>(json);
                    var achievements = root?.Response?.Achievements;
                    return achievements != null && achievements.Count > 0;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "GetGameAchievements API availability check failed for appId={appId}");
                return null;
            }
        }

        private async Task<SchemaAndPercentages> GetSchemaForGameDetailedInternalAsync(string accessToken, int appId, string language, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(accessToken) || appId <= 0)
            {
                _logger?.Warn("GetSchemaForGameDetailedInternalAsync: Invalid accessToken or appId={appId}");
                return null;
            }

            try
            {
                language = string.IsNullOrWhiteSpace(language) ? "english" : language;
                var url = $"https://api.steampowered.com/IPlayerService/GetGameAchievements/v1/" +
                          $"?key={Uri.EscapeDataString(accessToken)}" +
                          $"&appid={appId}" +
                          $"&language={Uri.EscapeDataString(language)}";

                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                using (var resp = await _apiHttp.SendAsync(req, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return null;
                    }

                    var root = JsonConvert.DeserializeObject<GetGameAchievementsRoot>(json);
                    var achievements = root?.Response?.Achievements;
                    if (achievements == null || achievements.Count == 0)
                    {
                        return null;
                    }

                    var schemaAchievements = new List<SchemaAchievement>();
                    var globalPercentages = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                    foreach (var ach in achievements)
                    {
                        schemaAchievements.Add(new SchemaAchievement
                        {
                            Name = ach.InternalName,
                            DisplayName = ach.LocalizedName,
                            Description = ach.LocalizedDesc,
                            Icon = $"https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/{appId}/{ach.Icon}",
                            IconGray = $"https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/{appId}/{ach.IconGray}",
                            Hidden = ach.Hidden ? 1 : 0
                        });

                        if (!string.IsNullOrWhiteSpace(ach.InternalName) &&
                            double.TryParse(
                                ach.PlayerPercentUnlocked,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var percent))
                        {
                            globalPercentages[ach.InternalName] = percent;
                        }
                    }

                    return new SchemaAndPercentages
                    {
                        Achievements = schemaAchievements,
                        GlobalPercentages = globalPercentages
                    };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "GetGameAchievements API request failed for appId={appId}");
                return null;
            }
        }
    }
}




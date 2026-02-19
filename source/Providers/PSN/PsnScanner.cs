using Newtonsoft.Json;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.PSN.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.PSN
{
    internal sealed class PsnScanner
    {
        private readonly ILogger _logger;
        private readonly PsnSessionManager _sessionManager;

        private const string UrlBase = "https://m.np.playstation.com/api/trophy/v1";
        private const string UrlTrophiesDetailsAll = UrlBase + "/npCommunicationIds/{0}/trophyGroups/all/trophies";
        private const string UrlTrophiesUserAll = UrlBase + "/users/me/npCommunicationIds/{0}/trophyGroups/all/trophies";
        private const string UrlTitlesWithIdsMobile = UrlBase + "/users/me/titles/trophyTitles?npTitleIds={0}";

        public PsnScanner(ILogger logger, PsnSessionManager sessionManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        public async Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            var report = progressCallback ?? (_ => { });
            var providerName = GetProviderName();

            string token;
            try
            {
                token = await _sessionManager.GetAccessTokenAsync(cancel).ConfigureAwait(false);
            }
            catch (PsnAuthRequiredException)
            {
                _logger?.Warn("[PSNAch] Not authenticated (PSN token missing).");
                report(new ProviderScanUpdate { AuthRequired = true });
                return new RebuildPayload { Summary = new RebuildSummary() };
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Failed to acquire PSN token.");
                report(new ProviderScanUpdate { AuthRequired = true });
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var summary = new RebuildSummary();
            if (gamesToScan == null || gamesToScan.Count == 0)
            {
                return new RebuildPayload { Summary = summary };
            }

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(45);
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                for (var i = 0; i < gamesToScan.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var game = gamesToScan[i];
                    report(new ProviderScanUpdate { CurrentGameName = game?.Name });

                    try
                    {
                        var data = await FetchGameDataAsync(http, game, providerName, cancel).ConfigureAwait(false);
                        if (data != null && onGameScanned != null)
                        {
                            await onGameScanned(data).ConfigureAwait(false);
                        }

                        summary.GamesScanned++;
                        if (data != null && !data.NoAchievements)
                        {
                            summary.GamesWithAchievements++;
                        }
                        else
                        {
                            summary.GamesWithoutAchievements++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, $"[PSNAch] Failed to scan {game?.Name}");
                    }
                }
            }

            report(new ProviderScanUpdate { CurrentGameName = null });
            return new RebuildPayload { Summary = summary };
        }

        private async Task<GameAchievementData> FetchGameDataAsync(HttpClient http, Game game, string providerName, CancellationToken cancel)
        {
            var npCommId = await ResolveNpCommunicationIdAsync(http, game, cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(npCommId))
            {
                return BuildNoAchievementsData(game, providerName);
            }

            var normalizedId = NormalizeGameId(game?.GameId);
            var isPs5 = normalizedId.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase);
            var serviceSuffix = isPs5 ? string.Empty : "?npServiceName=trophy";

            var urlUser = string.Format(UrlTrophiesUserAll, npCommId) + serviceSuffix;
            var urlDetails = string.Format(UrlTrophiesDetailsAll, npCommId) + serviceSuffix;

            string userJson = null;
            try
            {
                userJson = await http.GetStringAsync(urlUser).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                // Private profile or no earned progress data is non-fatal.
                _logger?.Debug(ex, $"[PSNAch] User trophies fetch failed for '{game?.Name}'");
                userJson = null;
            }

            string detailsJson;
            try
            {
                detailsJson = await http.GetStringAsync(urlDetails).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                try
                {
                    var retrySuffix = string.IsNullOrEmpty(serviceSuffix) ? "?npServiceName=trophy" : string.Empty;
                    var retryUrl = string.Format(UrlTrophiesDetailsAll, npCommId) + retrySuffix;
                    detailsJson = await http.GetStringAsync(retryUrl).ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();
                }
                catch
                {
                    _logger?.Warn(ex, $"[PSNAch] Failed to fetch trophy details for '{game?.Name}' (npCommId='{npCommId}')");
                    return BuildNoAchievementsData(game, providerName);
                }
            }

            var user = string.IsNullOrWhiteSpace(userJson)
                ? null
                : JsonConvert.DeserializeObject<PsnTrophiesUserResponse>(userJson);

            var details = JsonConvert.DeserializeObject<PsnTrophiesDetailResponse>(detailsJson);

            var userById = (user?.Trophies ?? new List<PsnUserTrophy>())
                .GroupBy(t => t.TrophyId)
                .ToDictionary(g => g.Key, g => g.First());

            var achievements = new List<AchievementDetail>();
            foreach (var detail in (details?.Trophies ?? new List<PsnTrophyDetail>())
                .GroupBy(t => t.TrophyId)
                .Select(g => g.First()))
            {
                userById.TryGetValue(detail.TrophyId, out var userEntry);

                DateTime? unlockUtc = null;
                if (userEntry != null && userEntry.Earned)
                {
                    if (!string.IsNullOrWhiteSpace(userEntry.EarnedDateTime))
                    {
                        if (DateTime.TryParse(
                            userEntry.EarnedDateTime,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var parsed))
                        {
                            unlockUtc = parsed;
                        }
                        else
                        {
                            unlockUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
                        }
                    }
                    else
                    {
                        unlockUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
                    }
                }

                achievements.Add(new AchievementDetail
                {
                    ApiName = detail.TrophyId.ToString(CultureInfo.InvariantCulture),
                    DisplayName = detail.TrophyName,
                    Description = detail.TrophyDetail,
                    UnlockedIconPath = detail.TrophyIconUrl,
                    Hidden = detail.Hidden,
                    UnlockTimeUtc = unlockUtc,
                    GlobalPercentUnlocked = userEntry?.TrophyEarnedRate,
                    TrophyType = detail.TrophyType
                });
            }

            return new GameAchievementData
            {
                ProviderName = providerName,
                LibrarySourceName = game?.Source?.Name,
                GameName = game?.Name,
                PlayniteGameId = game?.Id,
                NoAchievements = achievements.Count == 0,
                Achievements = achievements,
                LastUpdatedUtc = DateTime.UtcNow,
                PlaytimeSeconds = (ulong)(game?.Playtime ?? 0) * 60UL
            };
        }

        private static GameAchievementData BuildNoAchievementsData(Game game, string providerName)
        {
            return new GameAchievementData
            {
                ProviderName = providerName,
                LibrarySourceName = game?.Source?.Name,
                GameName = game?.Name,
                PlayniteGameId = game?.Id,
                NoAchievements = true,
                LastUpdatedUtc = DateTime.UtcNow
            };
        }

        private static string GetProviderName()
        {
            var value = ResourceProvider.GetString("LOCPlayAch_Provider_PlayStation");
            return string.IsNullOrWhiteSpace(value) ? "PlayStation" : value;
        }

        private static string NormalizeGameId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var parts = raw.Split('#');
            var normalized = parts.Length > 0 ? parts[parts.Length - 1] : raw;
            return (normalized ?? string.Empty).Trim();
        }

        private async Task<string> ResolveNpCommunicationIdAsync(HttpClient http, Game game, CancellationToken cancel)
        {
            var raw = game?.GameId?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger?.Warn($"[PSNAch] GameId is empty for '{game?.Name}'");
                return null;
            }

            var normalized = NormalizeGameId(raw);
            if (normalized.IndexOf("NPWR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return normalized;
            }

            try
            {
                var url = string.Format(UrlTitlesWithIdsMobile, Uri.EscapeDataString(normalized));
                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();

                var titles = JsonConvert.DeserializeObject<PsnTrophyTitleLookup>(json);
                var comm = titles?.Titles?.FirstOrDefault()?.TrophyTitles?.FirstOrDefault()?.NpCommunicationId;
                if (!string.IsNullOrWhiteSpace(comm))
                {
                    return comm;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[PSNAch] Lookup failed for '{normalized}' ({game?.Name})");
            }

            if (!normalized.EndsWith("_00", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var withSuffix = normalized + "_00";
                    var url2 = string.Format(UrlTitlesWithIdsMobile, Uri.EscapeDataString(withSuffix));
                    var json2 = await http.GetStringAsync(url2).ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();

                    var titles2 = JsonConvert.DeserializeObject<PsnTrophyTitleLookup>(json2);
                    var comm2 = titles2?.Titles?.FirstOrDefault()?.TrophyTitles?.FirstOrDefault()?.NpCommunicationId;
                    if (!string.IsNullOrWhiteSpace(comm2))
                    {
                        return comm2;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"[PSNAch] Lookup failed for '{normalized}_00' ({game?.Name})");
                }
            }

            _logger?.Warn($"[PSNAch] Unable to resolve npCommunicationId for '{game?.Name}' (GameId='{normalized}')");
            return null;
        }
    }
}

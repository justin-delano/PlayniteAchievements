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
        private readonly PsnLibraryBridge _bridge;

        private static readonly string UrlBase = @"https://m.np.playstation.com/api/trophy/v1";

        // SuccessStory-style endpoints (most reliable)
        private static readonly string UrlTrophiesDetailsAll = UrlBase + @"/npCommunicationIds/{0}/trophyGroups/all/trophies";
        private static readonly string UrlTrophiesUserAll = UrlBase + @"/users/me/npCommunicationIds/{0}/trophyGroups/all/trophies";

        // TitleId -> npCommunicationId
        private static readonly string UrlTitlesWithIdsMobile = UrlBase + @"/users/me/titles/trophyTitles?npTitleIds={0}";

        public PsnScanner(ILogger logger, PsnLibraryBridge bridge)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public async Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            var report = progressCallback ?? (_ => { });

            var token = await _bridge.GetAccessTokenAsync(cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger?.Warn("[PSNAch] Not authenticated (PSNLibrary token missing).");
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

                for (int i = 0; i < gamesToScan.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var game = gamesToScan[i];

                    report(new ProviderScanUpdate { CurrentGameName = game?.Name });

                    try
                    {
                        var data = await FetchGameDataAsync(http, game, cancel).ConfigureAwait(false);
                        if (data != null && onGameScanned != null)
                        {
                            await onGameScanned(data).ConfigureAwait(false);
                        }

                        summary.GamesScanned++;
                        if (data != null && !data.NoAchievements) summary.GamesWithAchievements++;
                        else summary.GamesWithoutAchievements++;
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

        private async Task<GameAchievementData> FetchGameDataAsync(HttpClient http, Game game, CancellationToken cancel)
        {
            var npCommId = await ResolveNpCommunicationIdAsync(http, game, cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(npCommId))
            {
                return new GameAchievementData
                {
                    ProviderName = "PlayStation",
                    LibrarySourceName = game?.Source?.Name,
                    GameName = game?.Name,
                    PlayniteGameId = game?.Id,
                    NoAchievements = true,
                    LastUpdatedUtc = DateTime.UtcNow
                };
            }

            // Decide PS4 vs PS5 using GameId prefix (more reliable than Platforms)
            var normalizedId = NormalizeGameId(game?.GameId);
            var isPs5 = normalizedId.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase);

            // SuccessStory behavior: PS4 often needs ?npServiceName=trophy
            var serviceSuffix = isPs5 ? string.Empty : "?npServiceName=trophy";

            var urlUser = string.Format(UrlTrophiesUserAll, npCommId) + serviceSuffix;
            var urlDetails = string.Format(UrlTrophiesDetailsAll, npCommId) + serviceSuffix;

            // User trophies (unlocked status, earnedDateTime, earned rate)
            string userJson = null;
            try
            {
                userJson = await http.GetStringAsync(urlUser).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                // Not fatal (private profile etc.)
                _logger?.Debug(ex, $"[PSNAch] User trophies fetch failed for '{game?.Name}'");
                userJson = null;
            }

            // Details trophies (name/desc/icon/hidden) - required
            string detailsJson;
            try
            {
                detailsJson = await http.GetStringAsync(urlDetails).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                // If PS5 detection was wrong, retry once with the other suffix
                try
                {
                    var retrySuffix = string.IsNullOrEmpty(serviceSuffix) ? "?npServiceName=trophy" : string.Empty;
                    var retryUrl = string.Format(UrlTrophiesDetailsAll, npCommId) + retrySuffix;

                    detailsJson = await http.GetStringAsync(retryUrl).ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();

                    _logger?.Debug($"[PSNAch] Details fetch retry succeeded for '{game?.Name}' (usedSuffix='{retrySuffix}')");
                }
                catch
                {
                    _logger?.Warn(ex, $"[PSNAch] Failed to fetch trophy details for '{game?.Name}' (npCommId='{npCommId}')");
                    return new GameAchievementData
                    {
                        ProviderName = "PlayStation",
                        LibrarySourceName = game?.Source?.Name,
                        GameName = game?.Name,
                        PlayniteGameId = game?.Id,
                        NoAchievements = true,
                        LastUpdatedUtc = DateTime.UtcNow
                    };
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
            foreach (var d in (details?.Trophies ?? new List<PsnTrophyDetail>())
                .GroupBy(t => t.TrophyId)
                .Select(g => g.First()))
            {
                userById.TryGetValue(d.TrophyId, out var u);

                DateTime? unlockUtc = null;

                // If earnedDateTime is missing, still treat as unlocked (some PSN responses omit the timestamp).
                if (u != null && u.Earned)
                {
                    if (!string.IsNullOrWhiteSpace(u.EarnedDateTime))
                    {
                        if (DateTime.TryParse(
                            u.EarnedDateTime,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var parsed))
                        {
                            unlockUtc = parsed;
                        }
                        else
                        {
                            // earned but date parse failed -> still mark unlocked
                            unlockUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
                        }
                    }
                    else
                    {
                        // earned but no date -> still mark unlocked
                        unlockUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
                    }
                }


                achievements.Add(new AchievementDetail
                {
                    ApiName = d.TrophyId.ToString(CultureInfo.InvariantCulture),
                    DisplayName = d.TrophyName,
                    Description = d.TrophyDetail,
                    IconPath = d.TrophyIconUrl,
                    Hidden = d.Hidden,
                    UnlockTimeUtc = unlockUtc,
                    GlobalPercentUnlocked = u?.TrophyEarnedRate,

                    // PSN only: bronze/silver/gold/platinum
                    TrophyType = d.TrophyType

                });
            }

            return new GameAchievementData
            {
                ProviderName = "PlayStation",
                LibrarySourceName = game?.Source?.Name,
                GameName = game?.Name,
                PlayniteGameId = game?.Id,
                NoAchievements = achievements.Count == 0,
                Achievements = achievements,
                LastUpdatedUtc = DateTime.UtcNow,
                PlaytimeSeconds = (ulong)(game?.Playtime ?? 0) * 60UL
            };
        }

        private static string NormalizeGameId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
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

            // Already NPWR
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

            // Try with "_00" suffix
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

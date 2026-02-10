using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Steam.Models;
using PlayniteAchievements.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamScanner
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly SteamHTTPClient _steamClient;
        private readonly SteamSessionManager _sessionManager;
        private readonly SteamAPIClient _apiHelper;
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, Lazy<Task<Dictionary<int, int>>>> _ownedGamePlaytimeCache =
            new ConcurrentDictionary<string, Lazy<Task<Dictionary<int, int>>>>(StringComparer.OrdinalIgnoreCase);

        public SteamScanner(
            PlayniteAchievementsSettings settings,
            SteamHTTPClient steamClient,
            SteamSessionManager sessionManager,
            SteamAPIClient apiHelper,
            IPlayniteAPI api,
            ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _steamClient = steamClient ?? throw new ArgumentNullException(nameof(steamClient));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _apiHelper = apiHelper ?? throw new ArgumentNullException(nameof(apiHelper));
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
        }

        public async Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(_settings.Persisted.SteamUserId) || string.IsNullOrWhiteSpace(_settings.Persisted.SteamApiKey))
            {
                _logger?.Warn("[SteamAch] Missing Steam credentials - cannot scan achievements.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var report = progressCallback ?? (_ => { });

            _logger?.Info("[SteamAch] Probing Steam login status before scan...");
            var (isLoggedIn, _) = await _sessionManager.ProbeLoggedInAsync(cancel).ConfigureAwait(false);
            if (!isLoggedIn)
            {
                _logger?.Warn("[SteamAch] Steam web auth check failed: not logged in. Aborting scan.");
                report(new ProviderScanUpdate { AuthRequired = true });
                return new RebuildPayload { Summary = new RebuildSummary() };
            }
            _logger?.Info("[SteamAch] Steam web auth verified.");

            if (gamesToScan is null || gamesToScan.Count == 0)
            {
                _logger?.Info("[SteamAch] No games found to scan.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var playtimes = await GetPlaytimesAsync(_settings.Persisted.SteamUserId.Trim(), cancel).ConfigureAwait(false)
                ?? new Dictionary<int, int>();

            var progress = new RebuildProgressReporter(report, gamesToScan.Count);
            var summary = new RebuildSummary();

            // Create rate limiter with exponential backoff
            var rateLimiter = new RateLimiter(
                _settings.Persisted.ScanDelayMs,
                _settings.Persisted.MaxRetryAttempts);

            int consecutiveErrors = 0;

            for (int i = 0; i < gamesToScan.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                progress.Step();

                var game = gamesToScan[i];

                if (!TryGetPlatformAppId(game, out var appId))
                {
                    _logger?.Warn($"Skipping game without valid AppId: {game.Name}");
                    continue;
                }

                progress.Emit(new ProviderScanUpdate
                {
                    CurrentGameName = game.Name
                });

                try
                {
                    var data = await rateLimiter.ExecuteWithRetryAsync(
                        () => FetchGameDataAsync(game, playtimes, cancel),
                        IsTransientError,
                        cancel).ConfigureAwait(false);

                    if (onGameScanned != null && data != null)
                    {
                        await onGameScanned(data).ConfigureAwait(false);
                    }

                    summary.GamesScanned++;

                    if (data != null && !data.NoAchievements)
                        summary.GamesWithAchievements++;
                    else
                        summary.GamesWithoutAchievements++;

                    // Reset consecutive errors on success
                    consecutiveErrors = 0;

                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger?.Debug(ex, $"[SteamAch] Failed to scan achievements for {game.Name} (appId={appId}) after {consecutiveErrors} consecutive errors");

                    // If we've hit too many consecutive errors, apply exponential backoff before continuing
                    if (consecutiveErrors >= 3)
                    {
                        await rateLimiter.DelayAfterErrorAsync(consecutiveErrors, cancel).ConfigureAwait(false);
                    }
                }
            }

            progress.Emit(new ProviderScanUpdate
            {
                CurrentGameName = null
            }, force: true);

            return new RebuildPayload { Summary = summary };
        }

        /// <summary>
        /// Determines if an exception is a transient error that should trigger retry.
        /// </summary>
        private static bool IsTransientError(Exception ex)
        {
            if (ex is OperationCanceledException) return false;

            // WebException with transient status codes
            if (ex is WebException webEx && webEx.Response is HttpWebResponse response)
            {
                var statusCode = (int)response.StatusCode;
                // 429 Too Many Requests, 503 Service Unavailable, 502 Bad Gateway, 504 Gateway Timeout
                if (statusCode == 429 || statusCode == 502 || statusCode == 503 || statusCode == 504)
                    return true;
            }

            // Network-related exceptions
            var message = ex.Message ?? string.Empty;
            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (message.IndexOf("temporarily", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private static bool TryGetPlatformAppId(Game game, out int appId)
        {
            appId = 0;
            return game != null &&
                   !string.IsNullOrWhiteSpace(game.GameId) &&
                   int.TryParse(game.GameId, out appId);
        }

        // ---------------------------------------------------------------------
        // Playtime cache
        // ---------------------------------------------------------------------

        private async Task<Dictionary<int, int>> GetPlaytimesAsync(string steamId, CancellationToken cancel)
        {
            return await GetAndCachePlaytimesAsync(steamId, cancel).ConfigureAwait(false)
                   ?? new Dictionary<int, int>();
        }

        private async Task<Dictionary<int, int>> GetAndCachePlaytimesAsync(string steamId, CancellationToken cancel)
        {
            var resolved = await ResolveSteamId64Async(steamId, cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolved))
                return new Dictionary<int, int>();

            var apiKey = _settings.Persisted.SteamApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return new Dictionary<int, int>();

            var lazyTask = _ownedGamePlaytimeCache.GetOrAdd(resolved,
                new Lazy<Task<Dictionary<int, int>>>(() => _apiHelper.GetOwnedGamesAsync(apiKey, resolved, true)));

            try
            {
                var dict = await lazyTask.Value.ConfigureAwait(false) ?? new Dictionary<int, int>();
                if (dict.Count == 0) _ownedGamePlaytimeCache.TryRemove(resolved, out _);
                return dict;
            }
            catch
            {
                _ownedGamePlaytimeCache.TryRemove(resolved, out _);
                throw;
            }
        }

        // ---------------------------------------------------------------------
        // Game data building
        // ---------------------------------------------------------------------

        private async Task<GameAchievementData> FetchGameDataAsync(
            Game game,
            Dictionary<int, int> ownedGamesPlaytime,
            CancellationToken cancel)
        {
            if (!TryGetPlatformAppId(game, out var appId))
            {
                _logger?.Warn($"Could not get AppId from game {game.Name}");
                return null;
            }

            var schema = await FetchSchemaAsync(appId, cancel).ConfigureAwait(false);
            var unlocked = await FetchUnlockedAsync(appId, ownedGamesPlaytime, schema, cancel).ConfigureAwait(false);

            var gameData = new GameAchievementData
            {
                AppId = appId,
                GameName = game.Name,
                ProviderName = "Steam",
                LibrarySourceName = game?.Source?.Name,
                PlaytimeSeconds = unlocked?.PlaytimeSeconds ?? 0,
                LastUpdatedUtc = DateTime.UtcNow,
                NoAchievements = schema?.Achievements == null || schema.Achievements.Count == 0,
                PlayniteGameId = game.Id,
                Achievements = new List<AchievementDetail>()
            };

            if (!gameData.NoAchievements)
            {
                var unlockedDict = unlocked?.UnlockTimesUtc ?? new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                var progressNumDict = unlocked?.ProgressNum ?? new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
                var progressDenomDict = unlocked?.ProgressDenom ?? new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

                foreach (var schemaAch in schema.Achievements)
                {
                    if (string.IsNullOrWhiteSpace(schemaAch.Name))
                        continue;

                    DateTime? unlockTime = null;
                    if (unlockedDict.TryGetValue(schemaAch.Name, out var time))
                    {
                        unlockTime = time;
                    }

                    double? globalPercent = null;
                    if (schema.GlobalPercentages?.TryGetValue(schemaAch.Name, out var percent) == true)
                    {
                        globalPercent = percent;
                    }

                    progressNumDict.TryGetValue(schemaAch.Name, out var progressNum);
                    progressDenomDict.TryGetValue(schemaAch.Name, out var progressDenom);

                    var detail = new AchievementDetail
                    {
                        ApiName = schemaAch.Name,
                        DisplayName = !string.IsNullOrWhiteSpace(schemaAch.DisplayName)
                            ? schemaAch.DisplayName
                            : schemaAch.Name,
                        Description = schemaAch.Description ?? string.Empty,
                        IconPath = schemaAch.Icon,
                        Hidden = schemaAch.Hidden == 1,
                        GlobalPercentUnlocked = globalPercent,
                        UnlockTimeUtc = unlockTime,
                        ProgressNum = progressNum,
                        ProgressDenom = progressDenom
                    };

                    gameData.Achievements.Add(detail);
                }
            }

            return gameData;
        }

        private Task<SchemaAndPercentages> FetchSchemaAsync(int appId, CancellationToken cancel)
        {
            var language = string.IsNullOrWhiteSpace(_settings.Persisted.SteamLanguage) ? "english" : _settings.Persisted.SteamLanguage.Trim();
            return _apiHelper.GetSchemaForGameDetailedAsync(
                _settings.Persisted.SteamApiKey.Trim(),
                appId,
                language,
                cancel);
        }

        private async Task<UserUnlockedAchievements> FetchUnlockedAsync(
            int appId,
            Dictionary<int, int> ownedGamesPlaytime,
            SchemaAndPercentages schema,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(_settings.Persisted.SteamUserId) || string.IsNullOrWhiteSpace(_settings.Persisted.SteamApiKey))
                return new UserUnlockedAchievements();

            var playtimeMinutes = 0;
            if (ownedGamesPlaytime?.ContainsKey(appId) == true)
            {
                playtimeMinutes = ownedGamesPlaytime[appId];
            }

            AchievementsScrapeResponse scraped = null;
            try
            {
                scraped = await ScrapeAchievementsAsync(_settings.Persisted.SteamUserId.Trim(), appId, cancel, includeLocked: true)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[SteamAch] User achievements scrape failed (appId={appId}).");
                return new UserUnlockedAchievements();
            }

            if (scraped == null || scraped.TransientFailure)
                return new UserUnlockedAchievements();

            var iconFileToAchievements = new Dictionary<string, List<SchemaAchievement>>(StringComparer.OrdinalIgnoreCase);

            if (schema?.Achievements != null)
            {
                // _logger?.Info($"[SteamAch] FetchUnlockedAsync: Schema has {schema.Achievements.Count} achievements for appId={appId}");

                foreach (var ach in schema.Achievements)
                {
                    if (string.IsNullOrWhiteSpace(ach.Name))
                        continue;

                    var iconFile = ExtractIconFilename(ach.Icon);
                    if (!string.IsNullOrWhiteSpace(iconFile))
                    {
                        if (!iconFileToAchievements.ContainsKey(iconFile))
                            iconFileToAchievements[iconFile] = new List<SchemaAchievement>();
                        iconFileToAchievements[iconFile].Add(ach);
                    }

                    var iconGrayFile = ExtractIconFilename(ach.IconGray);
                    if (!string.IsNullOrWhiteSpace(iconGrayFile))
                    {
                        if (!iconFileToAchievements.ContainsKey(iconGrayFile))
                            iconFileToAchievements[iconGrayFile] = new List<SchemaAchievement>();
                        iconFileToAchievements[iconGrayFile].Add(ach);
                    }
                }

                // _logger?.Info($"[SteamAch] FetchUnlockedAsync: Built iconFileToAchievements with {iconFileToAchievements.Count} icon entries");
            }
            else
            {
                // _logger?.Warn($"[SteamAch] FetchUnlockedAsync: Schema is null or has no achievements for appId={appId}");
            }

            var data = new UserUnlockedAchievements
            {
                LastUpdatedUtc = DateTime.UtcNow,
                PlaytimeSeconds = (ulong)playtimeMinutes * 60,
                AppId = appId,
                UnlockTimesUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase),
                ProgressNum = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase),
                ProgressDenom = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            };

            if (scraped.Rows != null)
            {
                // _logger?.Info($"[SteamAch] FetchUnlockedAsync: Scraped {scraped.Rows.Count} rows for appId={appId}");
                int withTime = 0, withoutTime = 0, matched = 0, fallbackMatches = 0;

                foreach (var row in scraped.Rows)
                {
                    var iconFile = ExtractIconFilename(row.IconUrl);
                    if (!string.IsNullOrWhiteSpace(iconFile) && iconFileToAchievements.TryGetValue(iconFile, out var achievements))
                    {
                        string apiName = null;

                        if (achievements.Count == 1)
                        {
                            // Icon maps to exactly one achievement - use it directly
                            apiName = achievements[0].Name;
                        }
                        else
                        {
                            // Multiple achievements share this icon - use DisplayName/Description matching as fallback
                            apiName = achievements.FirstOrDefault(a =>
                                string.Equals(a.DisplayName, row.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(a.Description, row.Description, StringComparison.OrdinalIgnoreCase)
                            )?.Name;

                            if (apiName != null)
                                fallbackMatches++;
                        }

                        if (!string.IsNullOrWhiteSpace(apiName))
                        {
                            data.ProgressNum[apiName] = row.ProgressNum;
                            data.ProgressDenom[apiName] = row.ProgressDenom;

                            if (row.IsUnlocked)
                            {
                                var unlockTime = row.UnlockTimeUtc ?? DateTime.MinValue;
                                data.UnlockTimesUtc[apiName] = unlockTime;
                                matched++;
                                if (row.UnlockTimeUtc.HasValue) withTime++; else withoutTime++;
                            }
                        }
                    }
                }
                // _logger?.Info($"[SteamAch] FetchUnlockedAsync: Processed rows - withTime={withTime}, withoutTime={withoutTime}, matched={matched}, fallbackMatches={fallbackMatches}");
            }

            return data;
        }

        private static string ExtractIconFilename(string iconUrl)
        {
            if (string.IsNullOrWhiteSpace(iconUrl))
                return null;

            var queryIndex = iconUrl.IndexOf('?');
            if (queryIndex > 0)
                iconUrl = iconUrl.Substring(0, queryIndex);

            var lastSlash = iconUrl.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash >= iconUrl.Length - 1)
                return null;

            return iconUrl.Substring(lastSlash + 1);
        }

        // ---------------------------------------------------------------------
        // Achievements scraping
        // ---------------------------------------------------------------------

        private async Task<AchievementsScrapeResponse> ScrapeAchievementsAsync(
            string steamId64,
            int appId,
            CancellationToken cancel,
            bool includeLocked = false)
        {
            var res = new AchievementsScrapeResponse();

            var resolved = await ResolveSteamId64Async(steamId64, cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                res.TransientFailure = true;
                res.SetDetail(SteamScrapeDetail.NoSteamSession);
                res.Rows = new List<ScrapedAchievement>();
                return res;
            }

            var language = string.IsNullOrWhiteSpace(_settings.Persisted.SteamLanguage) ? "english" : _settings.Persisted.SteamLanguage.Trim();
            var requested = BuildAchievementsUrl(resolved, appId, language);
            res.RequestedUrl = requested;

            var page = await _steamClient.GetAchievementsPageAsync(resolved, appId, language, cancel).ConfigureAwait(false);
            var html = page?.Html ?? string.Empty;

            res.StatusCode = page != null ? (int)page.StatusCode : 0;
            res.FinalUrl = page?.FinalUrl ?? requested;

            if (res.StatusCode == 429)
            {
                res.TransientFailure = true;
                res.SetDetail(SteamScrapeDetail.TooManyRequests);
                return res;
            }

            if (LooksLoggedOutHeuristic(html, res.FinalUrl))
            {
                if (TryGetPrivateOrUnavailable(html, out var reason))
                {
                    res.TransientFailure = false;
                    res.StatsUnavailable = true;
                    res.SetDetail(reason == SteamScrapeDetail.None ? SteamScrapeDetail.Unavailable : reason);
                    return res;
                }

                res.TransientFailure = true;
                res.SetDetail(SteamScrapeDetail.CookiesBadAfterRefresh);
                return res;
            }

            if (page?.WasRedirected == true && !IsStatsForApp(res.FinalUrl))
            {
                res.StatsUnavailable = true;
                res.SetDetail(SteamScrapeDetail.RedirectOffStats);
                await MaybeClassifyNoAchievementsBySchemaAsync(res, appId, cancel).ConfigureAwait(false);
                return res;
            }

            var result = ClassifyScrapeOrPrivate(res, html, includeLocked, language);
            await MaybeClassifyNoAchievementsBySchemaAsync(result, appId, cancel).ConfigureAwait(false);
            return result;
        }

        private static string BuildAchievementsUrl(string steamId64, int appId, string language) =>
            $"https://steamcommunity.com/profiles/{steamId64}/stats/{appId}/?tab=achievements&l={language ?? "english"}";

        private async Task MaybeClassifyNoAchievementsBySchemaAsync(AchievementsScrapeResponse res, int appId, CancellationToken ct)
        {
            if (res == null || appId <= 0) return;
            if (!res.StatsUnavailable) return;

            var apiKey = _settings.Persisted.SteamApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey)) return;

            bool? has;
            try
            {
                has = await _steamClient.GetAppHasAchievementsAsync(apiKey, appId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[FAF] Failed to check if app {appId} has achievements via API.");
                return;
            }

            if (has == false)
                res.SetDetail(SteamScrapeDetail.NoAchievements);
        }

        private AchievementsScrapeResponse ClassifyScrapeOrPrivate(
            AchievementsScrapeResponse res,
            string html,
            bool includeLocked,
            string language = "english")
        {
            res.Rows = new List<ScrapedAchievement>();
            res.TransientFailure = true;
            res.StatsUnavailable = false;
            res.SetDetail(SteamScrapeDetail.NoRowsUnknown);

            var parsed = _steamClient.ParseAchievements(html, includeLocked, language) ?? new List<ScrapedAchievement>();

            if (parsed.Count > 0)
            {
                res.Rows = parsed;
                res.TransientFailure = false;
                res.SetDetail(SteamScrapeDetail.Scraped);
                return res;
            }

            if (TryGetPrivateOrUnavailable(html, out var why))
            {
                res.TransientFailure = false;
                res.StatsUnavailable = true;
                res.SetDetail(why == SteamScrapeDetail.None ? SteamScrapeDetail.Unavailable : why);
                return res;
            }

            if (SteamHTTPClient.HasAnyAchievementRows(html))
            {
                var hasUnlockedMarkers = html.IndexOf("achieveUnlockTime", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!hasUnlockedMarkers && !includeLocked)
                {
                    res.TransientFailure = false;
                    res.SetDetail(SteamScrapeDetail.AllHidden);
                    return res;
                }

                res.SetDetail(hasUnlockedMarkers
                    ? SteamScrapeDetail.UnlockedMarkerButParseFailed
                    : SteamScrapeDetail.RowsMarkerButParseFailed);
                return res;
            }

            return res;
        }

        private static bool IsStatsForApp(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            var p = u.AbsolutePath.TrimEnd('/');
            return p.IndexOf("/stats/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (p.IndexOf("/profiles/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.IndexOf("/id/", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsLoginLikeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("openid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLoggedOutHeuristic(string html, string finalUrl)
        {
            if (IsLoginLikeUrl(finalUrl)) return true;

            if (string.IsNullOrEmpty(html)) return false;

            if (html.IndexOf("This profile is private", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (html.IndexOf("no achievements", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (html.IndexOf("global_action_menu", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (SteamHTTPClient.LooksLoggedOutHeader(html)) return true;

            if (html.IndexOf("g_steamID = \"0\"", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private static bool TryGetPrivateOrUnavailable(string html, out SteamScrapeDetail detail)
        {
            detail = SteamScrapeDetail.None;
            if (string.IsNullOrEmpty(html)) return false;

            if (html.IndexOf("This profile is private", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = SteamScrapeDetail.ProfilePrivate;
                return true;
            }

            if (html.IndexOf("You must be logged in to view this user's stats", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = SteamScrapeDetail.RequiresLoginForStats;
                return true;
            }

            if (html.IndexOf("The specified profile could not be found", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = SteamScrapeDetail.ProfileNotFound;
                return true;
            }

            if (html.IndexOf("no achievements", StringComparison.OrdinalIgnoreCase) >= 0 &&
                html.IndexOf("achievement", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = SteamScrapeDetail.NoAchievements;
                return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------
        // Persona / helpers
        // ---------------------------------------------------------------------

        private async Task<string> ResolveSteamId64Async(string steamIdMaybe, CancellationToken ct)
        {
            return InputValidator.IsValidSteamId64(steamIdMaybe)
                ? steamIdMaybe.Trim()
                : (await _steamClient.GetRequiredSelfSteamId64Async(ct).ConfigureAwait(false))?.Trim();
        }
    }
}

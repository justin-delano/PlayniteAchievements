using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.RetroAchievements.Hashing;
using PlayniteAchievements.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal sealed class RetroAchievementsScanner
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly RetroAchievementsApiClient _api;
        private readonly RetroAchievementsHashIndexStore _hashIndexStore;
        private readonly RetroAchievementsPathResolver _pathResolver;
        private readonly Dictionary<int, List<Models.RaGameListWithTitle>> _gameListCache = new();

        public RetroAchievementsScanner(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            RetroAchievementsApiClient api,
            RetroAchievementsHashIndexStore hashIndexStore,
            RetroAchievementsPathResolver pathResolver)
        {
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _hashIndexStore = hashIndexStore ?? throw new ArgumentNullException(nameof(hashIndexStore));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public async Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(_settings.Persisted.RaUsername) || string.IsNullOrWhiteSpace(_settings.Persisted.RaWebApiKey))
            {
                _logger?.Warn("[RA] Missing RetroAchievements credentials - cannot scan achievements.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            if (gamesToRefresh is null || gamesToRefresh.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var report = progressCallback ?? (_ => { });

            var progress = new RebuildProgressReporter(report, gamesToRefresh.Count);
            var summary = new RebuildSummary();

            // Create rate limiter with exponential backoff
            var rateLimiter = new RateLimiter(
                _settings.Persisted.ScanDelayMs,
                _settings.Persisted.MaxRetryAttempts);

            int consecutiveErrors = 0;

            for (var i = 0; i < gamesToRefresh.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                progress.Step();

                var game = gamesToRefresh[i];
                if (game == null) continue;

                if (!RaConsoleIdResolver.TryResolve(game, out var consoleId))
                {
                    continue;
                }

                var hasher = RaHasherFactory.Create(consoleId, _settings, _logger);
                if (hasher == null)
                {
                    continue;
                }

                progress.Emit(new ProviderRefreshUpdate
                {
                    CurrentGameName = !string.IsNullOrWhiteSpace(game.Name) ? game.Name : $"Game {game.Id}"
                });

                try
                {
                    var data = await rateLimiter.ExecuteWithRetryAsync(
                        () => ScanGameAsync(game, consoleId, hasher, cancel),
                        IsTransientError,
                        cancel).ConfigureAwait(false);

                    if (OnGameRefreshed != null && data != null)
                    {
                        await OnGameRefreshed(data).ConfigureAwait(false);
                    }

                    summary.GamesRefreshed++;
                    if (data != null && data.HasAchievements)
                    {
                        summary.GamesWithAchievements++;
                    }
                    else
                    {
                        summary.GamesWithoutAchievements++;
                    }

                    // Reset consecutive errors on success
                    consecutiveErrors = 0;

                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (CachePersistenceException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger?.Warn(ex, $"[RA] Failed to scan game '{game?.Name}' after {consecutiveErrors} consecutive errors.");

                    // If we've hit too many consecutive errors, apply exponential backoff before continuing
                    if (consecutiveErrors >= 3)
                    {
                        await rateLimiter.DelayAfterErrorAsync(consecutiveErrors, cancel).ConfigureAwait(false);
                    }
                }
            }

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

        private async Task<GameAchievementData> ScanGameAsync(Game game, int consoleId, IRaHasher hasher, CancellationToken cancel)
        {
            var candidates = _pathResolver.ResolveCandidateFilePaths(game).ToList();
            _logger?.Info($"[RA] Scanning '{game?.Name}' consoleId={consoleId} hasher={hasher.Name} candidates={candidates.Count}.");

            try
            {
                var index = await _hashIndexStore.GetHashIndexAsync(consoleId, cancel).ConfigureAwait(false);

                foreach (var candidate in candidates)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(candidate)) continue;

                    // CSO files need to be decompressed before hashing
                    if (ArchiveUtils.IsCsoPath(candidate) && _settings.Persisted.EnableArchiveScanning)
                    {
                        if (!File.Exists(candidate))
                        {
                            continue;
                        }

                        _logger?.Info($"[RA] Decompressing CSO file: '{candidate}'");
                        try
                        {
                            using (var tmpIso = CsoUtils.DecompressToTempFile(candidate))
                            {
                                var hashes = await hasher.ComputeHashesAsync(tmpIso.Path, cancel).ConfigureAwait(false);
                                var match = TryMatchHash(index, hashes, out var matchedHash, out var gameId);
                                _logger?.Info($"[RA] CSO file '{candidate}' hashes={FormatHashesForLog(hashes)} matched={match} gameId={gameId}");

                                if (match)
                                {
                                    return await FetchGameInfoAsync(game, gameId, cancel).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Warn(ex, $"[RA] Failed to decompress CSO file '{candidate}': {ex.Message}");
                        }
                        continue;
                    }

                    // RVZ files need to be decompressed before hashing
                    if (ArchiveUtils.IsRvzPath(candidate) && _settings.Persisted.EnableArchiveScanning)
                    {
                        if (!File.Exists(candidate))
                        {
                            continue;
                        }

                        _logger?.Info($"[RA] Decompressing RVZ file: '{candidate}'");
                        try
                        {
                            using (var tmpIso = RvzUtils.DecompressToTempFile(candidate))
                            {
                                var hashes = await hasher.ComputeHashesAsync(tmpIso.Path, cancel).ConfigureAwait(false);
                                var match = TryMatchHash(index, hashes, out var matchedHash, out var gameId);
                                _logger?.Info($"[RA] RVZ file '{candidate}' hashes={FormatHashesForLog(hashes)} matched={match} gameId={gameId}");

                                if (match)
                                {
                                    return await FetchGameInfoAsync(game, gameId, cancel).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Warn(ex, $"[RA] Failed to decompress RVZ file '{candidate}': {ex.Message}");
                        }
                        continue;
                    }

                    // Standard archive handling (zip, 7z, rar)
                    if (ArchiveUtils.IsArchivePath(candidate) && _settings.Persisted.EnableArchiveScanning)
                    {
                        // Arcade hashing is based on filename; no need to inspect entries.
                        if (hasher is Hashing.Hashers.ArcadeFilenameHasher)
                        {
                            var hashes = await hasher.ComputeHashesAsync(candidate, cancel).ConfigureAwait(false);
                            var match = TryMatchHash(index, hashes, out var matchedHash, out var gameId);
                            _logger?.Info($"[RA] Archive '{candidate}' hashes={FormatHashesForLog(hashes)} matched={match} gameId={gameId}");
                            if (match)
                            {
                                return await FetchGameInfoAsync(game, gameId, cancel).ConfigureAwait(false);
                            }

                            continue;
                        }

                        var entries = ArchiveUtils.GetCandidateEntries(candidate);
                        foreach (var entry in entries)
                        {
                            cancel.ThrowIfCancellationRequested();

                            using (var tmp = ArchiveUtils.ExtractEntryToTempFile(candidate, entry))
                            {
                                var hashes = await hasher.ComputeHashesAsync(tmp.Path, cancel).ConfigureAwait(false);
                                var match = TryMatchHash(index, hashes, out var matchedHash, out var gameId);
                                _logger?.Info($"[RA] ArchiveEntry '{entry.Key}' hashes={FormatHashesForLog(hashes)} matched={match} gameId={gameId}");

                                if (match)
                                {
                                    return await FetchGameInfoAsync(game, gameId, cancel).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!File.Exists(candidate))
                        {
                            continue;
                        }

                        var hashes = await hasher.ComputeHashesAsync(candidate, cancel).ConfigureAwait(false);
                        var match = TryMatchHash(index, hashes, out var matchedHash, out var gameId);
                        _logger?.Info($"[RA] File '{candidate}' hashes={FormatHashesForLog(hashes)} matched={match} gameId={gameId}");

                        if (match)
                        {
                            return await FetchGameInfoAsync(game, gameId, cancel).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"[RA] Hash scanning failed for '{game.Name}': {ex.Message}");
            }

            // Try name-based fallback if enabled
            if (_settings.Persisted.EnableRaNameFallback)
            {
                var nameMatchId = await TryMatchGameByNameAsync(game, consoleId, cancel).ConfigureAwait(false);
                if (nameMatchId > 0)
                {
                    _logger?.Info($"[RA] Name-based fallback matched gameId={nameMatchId} for '{game.Name}'");
                    return await FetchGameInfoAsync(game, nameMatchId, cancel).ConfigureAwait(false);
                }
                _logger?.Info($"[RA] Name-based fallback found no match for '{game.Name}'");
            }

            return BuildNoAchievements(game, appId: 0);
        }

        private async Task<GameAchievementData> FetchGameInfoAsync(Game game, int gameId, CancellationToken cancel)
        {
            try
            {
                var gameInfo = await _api.GetGameInfoAndUserProgressAsync(gameId, cancel).ConfigureAwait(false);
                var achievements = ParseAchievements(gameInfo, _settings.Persisted.RaRarityStats);

                _logger?.Info($"[RA] Parsed {achievements.Count} achievements for '{gameInfo?.GameTitle}'.");

                return new GameAchievementData
                {
                    AppId = gameId,
                    GameName = game?.Name,
                    ProviderName = "RetroAchievements",
                    LibrarySourceName = game?.Source?.Name,
                    LastUpdatedUtc = DateTime.UtcNow,
                    HasAchievements = achievements.Count > 0,
                    PlayniteGameId = game?.Id,
                    Achievements = achievements
                };
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RA] Failed to fetch game info for gameId={gameId}: {ex.Message}");
                return BuildNoAchievements(game, appId: gameId);
            }
        }

        private static GameAchievementData BuildNoAchievements(Game game, int appId)
        {
            return new GameAchievementData
            {
                AppId = appId,
                GameName = game?.Name,
                ProviderName = "RetroAchievements",
                LibrarySourceName = game?.Source?.Name,
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = false,
                PlayniteGameId = game?.Id,
                Achievements = new List<AchievementDetail>()
            };
        }

        private static bool TryMatchHash(Dictionary<string, int> index, IReadOnlyList<string> hashes, out string matchedHash, out int gameId)
        {
            matchedHash = null;
            gameId = 0;

            if (index == null || hashes == null)
            {
                return false;
            }

            foreach (var h in hashes)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                var key = h.Trim().ToLowerInvariant();

                if (index.TryGetValue(key, out gameId))
                {
                    matchedHash = key;
                    return true;
                }
            }

            return false;
        }

        private static string FormatHashesForLog(IReadOnlyList<string> hashes)
        {
            if (hashes == null || hashes.Count == 0) return "(none)";
            return string.Join(",", hashes.Select(h => string.IsNullOrWhiteSpace(h) ? "?" : h));
        }

        private static List<AchievementDetail> ParseAchievements(Models.RaGameInfoUserProgress gameInfo, string rarityStats)
        {
            var list = new List<AchievementDetail>();

            if (gameInfo?.Achievements == null)
            {
                return list;
            }

            var useHardcoreRarity = string.Equals(rarityStats, "hardcore", StringComparison.OrdinalIgnoreCase);
            var useCombinedRarity = string.Equals(rarityStats, "combined", StringComparison.OrdinalIgnoreCase);

            var distinctPlayers = Math.Max(gameInfo.NumDistinctPlayers, 0);
            var distinctPlayersCasual = Math.Max(gameInfo.NumDistinctPlayersCasual, 0);
            var distinctPlayersHardcore = Math.Max(gameInfo.NumDistinctPlayersHardcore, 0);

            var distinctForPercent =
                distinctPlayersCasual > 0 ? distinctPlayersCasual :
                distinctPlayers > 0 ? distinctPlayers :
                distinctPlayersHardcore;

            var distinctHardcoreForPercent =
                distinctPlayersHardcore > 0 ? distinctPlayersHardcore :
                distinctPlayers > 0 ? distinctPlayers :
                distinctPlayersCasual;

            var distinctCombinedForPercent =
                distinctPlayers > 0 ? distinctPlayers :
                distinctPlayersCasual > 0 ? distinctPlayersCasual :
                distinctPlayersHardcore;

            foreach (var kvp in gameInfo.Achievements)
            {
                var achId = kvp.Key;
                var ach = kvp.Value;
                if (ach == null) continue;

                var title = ach.Title ?? achId;
                var desc = ach.Description ?? string.Empty;
                var badge = ach.BadgeName ?? string.Empty;

                DateTime? unlockUtc = null;
                if (!string.IsNullOrWhiteSpace(ach.DateEarnedHardcore))
                {
                    unlockUtc = ParseRaUtcTimestamp(ach.DateEarnedHardcore);
                }
                else if (!string.IsNullOrWhiteSpace(ach.DateEarned))
                {
                    unlockUtc = ParseRaUtcTimestamp(ach.DateEarned);
                }

                double? globalPercent = null;
                if (useCombinedRarity)
                {
                    var numAwarded = Math.Max(ach.NumAwarded, 0);
                    if (numAwarded > 0 && distinctCombinedForPercent > 0)
                    {
                        globalPercent = 100.0 * numAwarded / distinctCombinedForPercent;
                    }
                    else
                    {
                        var numAwardedHardcore = Math.Max(ach.NumAwardedHardcore, 0);
                        if (numAwardedHardcore > 0 && distinctHardcoreForPercent > 0)
                        {
                            globalPercent = 100.0 * numAwardedHardcore / distinctHardcoreForPercent;
                        }
                    }
                }
                else if (useHardcoreRarity)
                {
                    var numAwardedHardcore = Math.Max(ach.NumAwardedHardcore, 0);
                    if (numAwardedHardcore > 0 && distinctHardcoreForPercent > 0)
                    {
                        globalPercent = 100.0 * numAwardedHardcore / distinctHardcoreForPercent;
                    }
                    else
                    {
                        var numAwarded = Math.Max(ach.NumAwarded, 0);
                        if (numAwarded > 0 && distinctForPercent > 0)
                        {
                            globalPercent = 100.0 * numAwarded / distinctForPercent;
                        }
                    }
                }
                else
                {
                    var numAwarded = Math.Max(ach.NumAwarded, 0);
                    if (numAwarded > 0 && distinctForPercent > 0)
                    {
                        globalPercent = 100.0 * numAwarded / distinctForPercent;
                    }
                    else
                    {
                        var numAwardedHardcore = Math.Max(ach.NumAwardedHardcore, 0);
                        if (numAwardedHardcore > 0 && distinctHardcoreForPercent > 0)
                        {
                            globalPercent = 100.0 * numAwardedHardcore / distinctHardcoreForPercent;
                        }
                    }
                }

                if (globalPercent.HasValue)
                {
                    globalPercent = Math.Max(0, Math.Min(100, globalPercent.Value));
                }

                var detail = new AchievementDetail
                {
                    ApiName = achId,
                    DisplayName = title,
                    Description = desc,
                    UnlockedIconPath = string.IsNullOrWhiteSpace(badge) ? null : $"https://i.retroachievements.org/Badge/{badge}.png",
                    LockedIconPath = string.IsNullOrWhiteSpace(badge) ? null : $"https://i.retroachievements.org/Badge/{badge}_lock.png",
                    Points = ach.Points,
                    Category = null,
                    UnlockTimeUtc = unlockUtc,
                    GlobalPercentUnlocked = globalPercent,
                    Hidden = false
                };

                list.Add(detail);
            }

            return list;
        }

        private static DateTime? ParseRaUtcTimestamp(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            return null;
        }

        private async Task<int> TryMatchGameByNameAsync(Game game, int consoleId, CancellationToken cancel)
        {
            var games = await GetOrFetchGameListAsync(consoleId, cancel).ConfigureAwait(false);
            if (games == null || games.Count == 0)
            {
                return 0;
            }

            var normalizedName = NormalizeGameName(game.Name);

            // Name fallback is intentionally restricted to base sets only.
            // Subset/event-style sets are excluded to avoid incorrect non-base matches.
            var orderedGames = games.OrderByDescending(g => g.Title?.Length ?? 0).ToList();
            var baseSetGames = orderedGames.Where(g => !IsSubsetLikeTitle(g?.Title)).ToList();

            var baseSetMatch = TryFindNameMatch(game, normalizedName, baseSetGames);
            if (baseSetMatch > 0)
            {
                return baseSetMatch;
            }

            return 0;
        }

        /// <summary>
        /// Minimum similarity threshold for fuzzy matching (0.0 to 1.0).
        /// A value of 0.85 means titles must be 85% similar to be considered a match.
        /// </summary>
        private const double FuzzyMatchThreshold = 0.85;

        private int TryFindNameMatch(Game game, string normalizedName, IReadOnlyList<Models.RaGameListWithTitle> games)
        {
            if (games == null || games.Count == 0)
            {
                return 0;
            }

            // Track best fuzzy match in case no exact match is found
            int bestFuzzyMatchId = 0;
            double bestFuzzyScore = 0;
            string bestFuzzyTitle = null;

            foreach (var raGame in games)
            {
                if (string.IsNullOrWhiteSpace(raGame.Title)) continue;

                var normalizedRaTitle = NormalizeGameName(raGame.Title);

                // Try exact normalized match first
                if (string.Equals(normalizedName, normalizedRaTitle, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.Info($"[RA] Name fallback: Matched '{game.Name}' -> '{raGame.Title}' (ID={raGame.ID})");
                    return raGame.ID;
                }

                // Try splitting by '|' for alternative titles
                var titleParts = raGame.Title.Split('|');
                if (titleParts.Length > 1)
                {
                    foreach (var part in titleParts)
                    {
                        var normalizedPart = NormalizeGameName(part);
                        if (string.Equals(normalizedName, normalizedPart, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger?.Info($"[RA] Name fallback: Matched '{game.Name}' -> '{part.Trim()}' (ID={raGame.ID})");
                            return raGame.ID;
                        }
                    }
                }

                // Try fuzzy match with Jaro-Winkler similarity
                if (!string.IsNullOrWhiteSpace(normalizedRaTitle) && !string.IsNullOrWhiteSpace(normalizedName))
                {
                    var similarity = StringSimilarity.JaroWinklerSimilarityIgnoreCase(normalizedName, normalizedRaTitle);
                    if (similarity >= FuzzyMatchThreshold && similarity > bestFuzzyScore)
                    {
                        bestFuzzyScore = similarity;
                        bestFuzzyMatchId = raGame.ID;
                        bestFuzzyTitle = raGame.Title;
                    }
                }
            }

            // Return best fuzzy match if one was found above threshold
            if (bestFuzzyMatchId > 0)
            {
                _logger?.Info($"[RA] Name fallback: Fuzzy matched '{game.Name}' -> '{bestFuzzyTitle}' (ID={bestFuzzyMatchId}, score={bestFuzzyScore:F2})");
                return bestFuzzyMatchId;
            }

            return 0;
        }

        private static bool IsSubsetLikeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var titleLower = title.ToLowerInvariant();
            return titleLower.Contains("[subset") ||
                   titleLower.Contains("[tournament") ||
                   titleLower.Contains("[event") ||
                   titleLower.Contains("[bonus") ||
                   titleLower.Contains("[hub") ||
                   titleLower.Contains("[specialty") ||
                   titleLower.Contains("[exclusive") ||
                   titleLower.Contains("(subset");
        }

        private async Task<List<Models.RaGameListWithTitle>> GetOrFetchGameListAsync(int consoleId, CancellationToken cancel)
        {
            if (_gameListCache.TryGetValue(consoleId, out var cached))
            {
                return cached;
            }

            try
            {
                // Use the same API call as hash index store to get raw JSON
                var json = await _api.GetGameListPageAsync(consoleId, offset: 0, count: 99999, cancel).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<Models.RaGameListWithTitle>();
                }

                // Try to deserialize as array first (API returns array when h=1 is used)
                List<Models.RaGameListWithTitle> games = null;
                try
                {
                    var arrayItems = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.RaGameListWithTitle[]>(json);
                    if (arrayItems != null && arrayItems.Length > 0)
                    {
                        games = new List<Models.RaGameListWithTitle>(arrayItems);
                    }
                }
                catch (Exception)
                {
                    // Try object format
                }

                // Try as object if array failed
                if (games == null)
                {
                    try
                    {
                        var response = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.RaGameListWithTitleResponse>(json);
                        if (response?.Results != null && response.Results.Count > 0)
                        {
                            games = response.Results;
                        }
                        else if (response?.GameList != null && response.GameList.Count > 0)
                        {
                            games = response.GameList;
                        }
                    }
                    catch (Exception)
                    {
                        // Failed to parse
                    }
                }

                if (games == null)
                {
                    return new List<Models.RaGameListWithTitle>();
                }

                if (games.Count > 0)
                {
                    _gameListCache[consoleId] = games;
                    _logger?.Info($"[RA] Cached {games.Count} games for consoleId={consoleId}");
                }

                return games;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"[RA] Failed to fetch game list for consoleId={consoleId}: {ex.Message}");
                return new List<Models.RaGameListWithTitle>();
            }
        }

        private static string NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Remove common edition suffixes and trim
            var normalized = name.Trim();

            // Handle RetroAchievements homebrew prefix convention: "~Homebrew~ Game Name"
            // Remove the "~Homebrew~" prefix entirely for matching purposes
            if (normalized.StartsWith("~Homebrew~", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("~Homebrew~".Length).Trim();
            }

            // Handle RetroAchievements hack prefix/suffix convention: "~Hack~ Game Name" or "Game Name ~Hack~"
            // Remove the "~Hack~" prefix or suffix entirely for matching purposes
            if (normalized.StartsWith("~Hack~", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("~Hack~".Length).Trim();
            }
            if (normalized.EndsWith("~Hack~", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - "~Hack~".Length).Trim();
            }

            // Remove text in parentheses (like "(USA)", "(Europe)", etc.)
            var parenIndex = normalized.IndexOf('(');
            if (parenIndex > 0)
            {
                normalized = normalized.Substring(0, parenIndex).Trim();
            }

            // Remove text in brackets (like "[!]", "[T+Eng]", etc.)
            var bracketIndex = normalized.IndexOf('[');
            if (bracketIndex > 0)
            {
                normalized = normalized.Substring(0, bracketIndex).Trim();
            }

            // Remove common symbols and extra whitespace
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s\-_:~\.]+", " ").Trim();

            return normalized;
        }
    }
}

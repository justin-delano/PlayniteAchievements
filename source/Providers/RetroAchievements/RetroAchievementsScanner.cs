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
using System.Text;
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
        private readonly Dictionary<int, List<Models.RaGameListWithTitle>> _gameListCache = new();

        public RetroAchievementsScanner(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            RetroAchievementsApiClient api,
            RetroAchievementsHashIndexStore hashIndexStore)
        {
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _hashIndexStore = hashIndexStore ?? throw new ArgumentNullException(nameof(hashIndexStore));
        }

        public async Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Action<GameAchievementData> onGameScanned,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(_settings.Persisted.RaUsername) || string.IsNullOrWhiteSpace(_settings.Persisted.RaWebApiKey))
            {
                _logger?.Warn("[RA] Missing RetroAchievements credentials - cannot scan achievements.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            if (gamesToScan is null || gamesToScan.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var report = progressCallback ?? (_ => { });

            var progress = new RebuildProgressReporter(report, gamesToScan.Count);
            var summary = new RebuildSummary();

            // Create rate limiter with exponential backoff
            var rateLimiter = new RateLimiter(
                _settings.Persisted.ScanDelayMs,
                _settings.Persisted.MaxRetryAttempts);

            int consecutiveErrors = 0;

            for (var i = 0; i < gamesToScan.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                progress.Step();

                var game = gamesToScan[i];
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

                progress.Emit(new ProviderScanUpdate
                {
                    CurrentGameName = game.Name
                });

                try
                {
                    var data = await rateLimiter.ExecuteWithRetryAsync(
                        () => ScanGameAsync(game, consoleId, hasher, cancel),
                        IsTransientError,
                        cancel).ConfigureAwait(false);

                    onGameScanned?.Invoke(data);

                    summary.GamesScanned++;
                    if (data != null && !data.NoAchievements)
                    {
                        summary.GamesWithAchievements++;
                    }
                    else
                    {
                        summary.GamesWithoutAchievements++;
                    }

                    // Reset consecutive errors on success
                    consecutiveErrors = 0;

                    // Rate limit protection: add delay before next request
                    if (i < gamesToScan.Count - 1)
                    {
                        await rateLimiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
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
            var candidates = ResolveCandidateFilePaths(game).ToList();
            _logger?.Info($"[RA] Scanning '{game?.Name}' consoleId={consoleId} hasher={hasher.Name} candidates={candidates.Count}.");

            try
            {
                var index = await _hashIndexStore.GetHashIndexAsync(consoleId, cancel).ConfigureAwait(false);

                foreach (var candidate in candidates)
            {
                cancel.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(candidate)) continue;

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
                    PlaytimeSeconds = (ulong)Math.Max(0, game?.Playtime ?? 0) * 60u,
                    LastUpdatedUtc = DateTime.UtcNow,
                    NoAchievements = achievements.Count == 0,
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
                PlaytimeSeconds = (ulong)Math.Max(0, game?.Playtime ?? 0) * 60u,
                LastUpdatedUtc = DateTime.UtcNow,
                NoAchievements = true,
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
                    IconUrl = string.IsNullOrWhiteSpace(badge) ? null : $"https://i.retroachievements.org/Badge/{badge}.png",
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

        private IEnumerable<string> ResolveCandidateFilePaths(Game game)
        {
            // Prefer game.Roms[*].Path
            if (game?.Roms != null)
            {
                foreach (var rom in game.Roms)
                {
                    var p = ResolvePath(game, rom?.Path);
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        yield return p;
                    }
                }
            }

            // Fallback: look for file-based actions.
            if (game?.GameActions != null)
            {
                foreach (var act in game.GameActions)
                {
                    var p = ResolvePath(game, act?.Path);
                    if (!string.IsNullOrWhiteSpace(p) && !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return p;
                    }
                }
            }
        }

        private static string ResolvePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var p = path.Trim().Trim('"');

            try
            {
                if (p.IndexOf("{InstallDir}", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrWhiteSpace(game?.InstallDirectory))
                {
                    p = ReplaceInsensitive(p, "{InstallDir}", game.InstallDirectory);
                }

                if (!Path.IsPathRooted(p) && !string.IsNullOrWhiteSpace(game?.InstallDirectory))
                {
                    p = Path.Combine(game.InstallDirectory, p);
                }

                return p;
            }
            catch
            {
                return null;
            }
        }

        private static string ReplaceInsensitive(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            {
                return input;
            }

            var idx = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return input;
            }

            var sb = new StringBuilder(input.Length);
            var start = 0;
            while (idx >= 0)
            {
                sb.Append(input.Substring(start, idx - start));
                sb.Append(newValue ?? string.Empty);
                start = idx + oldValue.Length;
                idx = input.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(input.Substring(start));
            return sb.ToString();
        }

        private async Task<int> TryMatchGameByNameAsync(Game game, int consoleId, CancellationToken cancel)
        {
            var games = await GetOrFetchGameListAsync(consoleId, cancel).ConfigureAwait(false);
            if (games == null || games.Count == 0)
            {
                return 0;
            }

            var normalizedName = NormalizeGameName(game.Name);

            // Sort by title length descending to prioritize exact/longer matches
            var sortedGames = games.OrderByDescending(g => g.Title?.Length ?? 0).ToList();

            foreach (var raGame in sortedGames)
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

                // Try splitting by '-' for alternative titles
                var dashParts = raGame.Title.Split('-');
                if (dashParts.Length > 1)
                {
                    foreach (var part in dashParts)
                    {
                        var normalizedPart = NormalizeGameName(part);
                        if (string.Equals(normalizedName, normalizedPart, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger?.Info($"[RA] Name fallback: Matched '{game.Name}' -> '{part.Trim()}' (ID={raGame.ID})");
                            return raGame.ID;
                        }
                    }
                }
            }

            return 0;
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

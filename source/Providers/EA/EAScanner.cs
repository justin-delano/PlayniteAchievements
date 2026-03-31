using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.EA
{
    internal sealed class EAScanner
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly EAApiClient _apiClient;
        private readonly EASessionManager _sessionManager;
        private readonly ILogger _logger;

        public EAScanner(
            PlayniteAchievementsSettings settings,
            EAApiClient apiClient,
            EASessionManager sessionManager,
            ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger;
        }

        public async Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            var probeResult = await _sessionManager.ProbeAuthStateAsync(cancel).ConfigureAwait(false);
            if (!probeResult.IsSuccess)
            {
                _logger?.Warn("[EAAch] EA not authenticated - cannot scan achievements.");
                return new RebuildPayload
                {
                    Summary = new RebuildSummary(),
                    AuthRequired = true
                };
            }

            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var rateLimiter = new RateLimiter(
                _settings.Persisted.ScanDelayMs,
                _settings.Persisted.MaxRetryAttempts);

            return await ProviderRefreshExecutor.RunProviderGamesAsync(
                gamesToRefresh,
                onGameStarting,
                async (game, token) =>
                {
                    var gameId = game?.GameId?.Trim();
                    if (string.IsNullOrWhiteSpace(gameId))
                    {
                        return ProviderRefreshExecutor.ProviderGameResult.Skipped();
                    }

                    var data = await rateLimiter.ExecuteWithRetryAsync(
                        () => FetchGameDataAsync(game, gameId, token),
                        EAApiClient.IsTransientError,
                        token).ConfigureAwait(false);

                    return new ProviderRefreshExecutor.ProviderGameResult
                    {
                        Data = data
                    };
                },
                onGameCompleted,
                isAuthRequiredException: ex => ex is EaAuthRequiredException,
                onGameError: (game, ex, consecutiveErrors) =>
                {
                    _logger?.Debug(ex, $"[EAAch] Failed to scan {game?.Name} after {consecutiveErrors} consecutive errors.");
                },
                delayBetweenGamesAsync: (index, token) => rateLimiter.DelayBeforeNextAsync(token),
                delayAfterErrorAsync: (consecutiveErrors, token) => rateLimiter.DelayAfterErrorAsync(consecutiveErrors, token),
                cancel).ConfigureAwait(false);
        }

        private async Task<GameAchievementData> FetchGameDataAsync(Game game, string gameId, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            var playerPsd = _sessionManager.GetPlayerSubId();
            if (string.IsNullOrWhiteSpace(playerPsd))
            {
                throw new EaAuthRequiredException("EA player sub ID not available. Please authenticate.");
            }

            try
            {
                var ownedGames = await _apiClient.GetOwnedGamesAsync(cancel).ConfigureAwait(false);
                var matched = MatchGame(ownedGames, game, gameId);

                string offerId;

                if (matched != null)
                {
                    offerId = matched.OriginOfferId;
                }
                else
                {
                    // Fallback: extract the offer ID directly from the Playnite game ID.
                    // EA/Origin games in Playnite have IDs like "Origin.OFR.50.0001456"
                    // where the part after "Origin." is the EA offer ID.
                    offerId = ExtractOfferIdFromGameId(gameId);
                    if (string.IsNullOrWhiteSpace(offerId))
                    {
                        _logger?.Debug($"[EAAch] No EA owned game matched and could not extract offer ID from gameId={gameId}, game={game?.Name}.");
                        return null;
                    }

                    _logger?.Debug($"[EAAch] Owned game lookup had no match for gameId={gameId}. Falling back to extracted offer ID={offerId}.");
                }

                var items = await _apiClient.GetAchievementsAsync(
                    offerId, playerPsd, cancel).ConfigureAwait(false);

                return MapToGameData(game, gameId, items);
            }
            catch (EaAuthRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"[EAAch] Error fetching data for {game?.Name} (gameId={gameId}).");
                return null;
            }
        }

        private static string ExtractOfferIdFromGameId(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return null;
            }

            var trimmed = gameId.Trim();

            // Playnite's Origin library uses "Origin." prefix for game IDs.
            // The EA offer ID is the part after the prefix.
            var prefix = "Origin.";
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var offerId = trimmed.Substring(prefix.Length).Trim();
                return string.IsNullOrWhiteSpace(offerId) ? null : offerId;
            }

            // If no prefix, the ID itself might be the offer ID (e.g. "OFR.50.0001456").
            return trimmed;
        }

        private EaOwnedGame MatchGame(List<EaOwnedGame> ownedGames, Game game, string gameId)
        {
            if (ownedGames == null || ownedGames.Count == 0)
            {
                return null;
            }

            var gameIdTrimmed = gameId.Trim();

            var byOfferId = ownedGames.FirstOrDefault(g =>
                string.Equals(g.OriginOfferId?.Trim(), gameIdTrimmed, StringComparison.OrdinalIgnoreCase));

            if (byOfferId != null)
            {
                return byOfferId;
            }

            var bySlug = ownedGames.FirstOrDefault(g =>
                string.Equals(g.GameSlug?.Trim(), gameIdTrimmed, StringComparison.OrdinalIgnoreCase));

            if (bySlug != null)
            {
                return bySlug;
            }

            var gameName = game?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return null;
            }

            return ownedGames.FirstOrDefault(g =>
                string.Equals(g.ProductName?.Trim(), gameName, StringComparison.OrdinalIgnoreCase));
        }

        private static GameAchievementData MapToGameData(Game game, string gameId, List<EaAchievementItem> items)
        {
            var data = new GameAchievementData
            {
                AppId = 0,
                GameName = game?.Name,
                ProviderKey = "EA",
                LibrarySourceName = game?.Source?.Name,
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = items != null && items.Count > 0,
                PlayniteGameId = game != null ? game.Id : Guid.Empty,
                Achievements = new List<AchievementDetail>()
            };

            if (items == null)
            {
                return data;
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.AchievementId))
                {
                    continue;
                }

                data.Achievements.Add(new AchievementDetail
                {
                    ApiName = item.AchievementId,
                    DisplayName = string.IsNullOrWhiteSpace(item.Title) ? item.AchievementId : item.Title,
                    Description = item.Description ?? string.Empty,
                    UnlockedIconPath = string.Empty,
                    LockedIconPath = string.Empty,
                    Points = null,
                    Category = null,
                    Hidden = false,
                    UnlockTimeUtc = item.UnlockTimeUtc,
                    Rarity = RarityTier.Common,
                    GlobalPercentUnlocked = null
                });
            }

            data.HasAchievements = data.Achievements.Count > 0;
            return data;
        }
    }
}

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

            var ownedGames = await _apiClient.GetOwnedGamesAsync(cancel).ConfigureAwait(false);
            var matched = EAProviderSupport.MatchGame(ownedGames, game, gameId);
            var offerId = matched?.OriginOfferId;

            if (string.IsNullOrWhiteSpace(offerId))
            {
                offerId = EAProviderSupport.ExtractOfferIdFromGameId(gameId);
            }

            if (string.IsNullOrWhiteSpace(offerId))
            {
                _logger?.Debug($"[EAAch] No EA owned game matched and could not extract offer ID from gameId={gameId}, game={game?.Name}.");
                return null;
            }

            if (matched == null)
            {
                _logger?.Debug($"[EAAch] Owned game lookup had no match for gameId={gameId}. Falling back to offer ID={offerId}.");
            }

            var items = await _apiClient.GetAchievementsAsync(
                offerId, playerPsd, cancel).ConfigureAwait(false);

            return EAProviderSupport.MapToGameData(game, items);
        }
    }
}

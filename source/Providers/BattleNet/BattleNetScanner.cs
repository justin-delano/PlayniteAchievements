using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class BattleNetScanner
    {
        private readonly Sc2GameStrategy _sc2;
        private readonly WowGameStrategy _wow;
        private readonly OverwatchGameStrategy _overwatch;
        private readonly BattleNetSessionManager _session;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;

        public BattleNetScanner(
            BattleNetApiClient client,
            BattleNetSessionManager session,
            PlayniteAchievementsSettings settings,
            ILogger logger)
        {
            _sc2 = new Sc2GameStrategy(client, session, logger);
            _wow = new WowGameStrategy(client, logger);
            _overwatch = new OverwatchGameStrategy(session, logger);
            _session = session;
            _settings = settings;
            _logger = logger;
        }

        public async Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            var probeResult = await _session.ProbeAuthStateAsync(cancel).ConfigureAwait(false);
            var isAuthenticated = probeResult.IsSuccess;

            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                _logger?.Info("[BattleNet] No Battle.net games found to scan.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var locale = _settings?.Persisted?.GlobalLanguage ?? "en-US";

            return await ProviderRefreshExecutor.RunProviderGamesAsync(
                gamesToRefresh,
                onGameStarting,
                async (game, token) =>
                {
                    var data = await FetchForGameAsync(game, locale, isAuthenticated, token);
                    return new ProviderRefreshExecutor.ProviderGameResult { Data = data };
                },
                onGameCompleted,
                isAuthRequiredException: ex => ex is AuthRequiredException,
                onGameError: (game, ex, consecutiveErrors) =>
                {
                    _logger?.Debug(ex, $"[BattleNet] Failed to scan {game?.Name} after {consecutiveErrors} consecutive errors");
                },
                delayBetweenGamesAsync: (index, token) => Task.CompletedTask,
                delayAfterErrorAsync: (consecutiveErrors, token) => Task.Delay(Math.Min(1000 * consecutiveErrors, 5000), token),
                cancel).ConfigureAwait(false);
        }

        private async Task<GameAchievementData> FetchForGameAsync(Game game, string locale, bool isAuthenticated, CancellationToken ct)
        {
            if (_wow.MatchesGame(game))
            {
                return await _wow.FetchAchievementsAsync(game, locale, ct);
            }

            if (_sc2.MatchesGame(game))
            {
                if (!isAuthenticated)
                {
                    _logger?.Debug($"[BattleNet] Skipping {game.Name} - SC2 requires authentication.");
                    return null;
                }
                return await _sc2.FetchAchievementsAsync(game, locale, ct);
            }

            if (_overwatch.MatchesGame(game))
            {
                if (!isAuthenticated)
                {
                    _logger?.Debug($"[BattleNet] Skipping {game.Name} - Overwatch requires authentication.");
                    return null;
                }
                return await _overwatch.FetchAchievementsAsync(game, locale, ct);
            }

            return null;
        }
    }
}

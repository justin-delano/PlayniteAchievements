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
            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                _logger?.Info("[BattleNet] No Battle.net games found to scan.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var locale = _settings?.Persisted?.GlobalLanguage ?? "en-US";
            var hasAuthRequiredGames = gamesToRefresh.Any(game => _sc2.MatchesGame(game) || _overwatch.MatchesGame(game));
            var isAuthenticated = false;

            _logger?.Info($"[BattleNet] Refresh started. games={gamesToRefresh.Count}, locale={locale}, hasAuthRequiredGames={Bool(hasAuthRequiredGames)}");

            if (hasAuthRequiredGames)
            {
                _logger?.Debug("[BattleNet] Probing authentication before SC2/Overwatch refresh.");
                var probeResult = await _session.ProbeAuthStateAsync(cancel).ConfigureAwait(false);
                isAuthenticated = probeResult.IsSuccess;
                _logger?.Info($"[BattleNet] Authentication probe completed for refresh. authenticated={Bool(isAuthenticated)}, outcome={probeResult.Outcome}");
            }
            else
            {
                _logger?.Debug("[BattleNet] Skipping authentication probe because selected games do not require Battle.net auth.");
            }

            var payload = await ProviderRefreshExecutor.RunProviderGamesAsync(
                gamesToRefresh,
                onGameStarting,
                async (game, token) =>
                {
                    var data = await FetchForGameAsync(game, locale, isAuthenticated, token);
                    return new ProviderRefreshExecutor.ProviderGameResult { Data = data };
                },
                onGameCompleted,
                isAuthRequiredException: _ => false,
                onGameError: (game, ex, consecutiveErrors) =>
                {
                    _logger?.Debug(ex, $"[BattleNet] Failed to scan {GameLabel(game)} after {consecutiveErrors} consecutive errors");
                },
                delayBetweenGamesAsync: (index, token) => Task.CompletedTask,
                delayAfterErrorAsync: (consecutiveErrors, token) => Task.Delay(Math.Min(1000 * consecutiveErrors, 5000), token),
                cancel).ConfigureAwait(false);

            _logger?.Info($"[BattleNet] Refresh completed. refreshed={payload?.Summary?.GamesRefreshed ?? 0}, withAchievements={payload?.Summary?.GamesWithAchievements ?? 0}, withoutAchievements={payload?.Summary?.GamesWithoutAchievements ?? 0}, authRequired={Bool(payload?.AuthRequired ?? false)}");
            return payload;
        }

        private async Task<GameAchievementData> FetchForGameAsync(Game game, string locale, bool isAuthenticated, CancellationToken ct)
        {
            if (_wow.MatchesGame(game))
            {
                _logger?.Debug($"[BattleNet] Matched WoW strategy. game={GameLabel(game)}");
                return await _wow.FetchAchievementsAsync(game, locale, ct);
            }

            if (_sc2.MatchesGame(game))
            {
                _logger?.Debug($"[BattleNet] Matched SC2 strategy. game={GameLabel(game)}, authenticated={Bool(isAuthenticated)}");
                if (!isAuthenticated)
                {
                    _logger?.Debug($"[BattleNet] Skipping {GameLabel(game)} - SC2 requires authentication.");
                    return null;
                }
                return await _sc2.FetchAchievementsAsync(game, locale, ct);
            }

            if (_overwatch.MatchesGame(game))
            {
                _logger?.Debug($"[BattleNet] Matched Overwatch strategy. game={GameLabel(game)}, authenticated={Bool(isAuthenticated)}");
                if (!isAuthenticated)
                {
                    _logger?.Debug($"[BattleNet] Skipping {GameLabel(game)} - Overwatch requires authentication.");
                    return null;
                }
                return await _overwatch.FetchAchievementsAsync(game, locale, ct);
            }

            _logger?.Debug($"[BattleNet] No Battle.net strategy matched game. game={GameLabel(game)}");
            return null;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string GameLabel(Game game)
        {
            if (game == null)
            {
                return "<null>";
            }

            return $"{game.Name ?? "<unnamed>"} ({game.Id})";
        }
    }
}

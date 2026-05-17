using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.BattleNet.Models;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class OverwatchGameStrategy
    {
        private readonly BattleNetSessionManager _session;
        private readonly ILogger _logger;

        public OverwatchGameStrategy(BattleNetSessionManager session, ILogger logger)
        {
            _session = session;
            _logger = logger;
        }

        public bool MatchesGame(Game game)
        {
            return game?.Name != null &&
                game.Name.IndexOf("overwatch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public Task<GameAchievementData> FetchAchievementsAsync(Game game, string locale, CancellationToken ct)
        {
            _logger?.Info($"[BattleNet/Overwatch] Fetch started. game={GameLabel(game)}, locale={(string.IsNullOrWhiteSpace(locale) ? "en-US" : locale)}, authenticated={Bool(_session.IsAuthenticated)}");

            if (!_session.IsAuthenticated)
            {
                _logger?.Warn($"[BattleNet/Overwatch] Not authenticated. game={GameLabel(game)}");
                return Task.FromResult(CreateEmptyData(game));
            }

            // Overwatch achievements require web scraping from the career page.
            // The reference implementation used playoverwatch.com but it is currently broken.
            // Attempting to use the newer overwatch.blizzard.com career page.
            // This strategy returns empty data until the scraping is validated against the current site.

            _logger?.Warn("[BattleNet/Overwatch] Overwatch scraping is not yet validated against current site structure.");
            _logger?.Info($"[BattleNet/Overwatch] Fetch completed with empty data. game={GameLabel(game)}");
            return Task.FromResult(CreateEmptyData(game));
        }

        private static GameAchievementData CreateEmptyData(Game game)
        {
            return new GameAchievementData
            {
                ProviderKey = "BattleNet",
                GameName = game.Name,
                PlayniteGameId = game.Id,
                AppId = StableAppId("Overwatch"),
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = false
            };
        }

        private static int StableAppId(string id)
        {
            int hash = 0;
            foreach (char c in id)
            {
                hash = (hash << 5) - hash + c;
            }
            return Math.Abs(hash);
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

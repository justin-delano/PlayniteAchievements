using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Xbox
{
    /// <summary>
    /// Data provider for Xbox achievement data.
    /// Supports Xbox One/Series X|S, Xbox 360, and PC Game Pass games.
    /// </summary>
    internal sealed class XboxDataProvider : IDataProvider
    {
        // Xbox library plugin ID from Playnite
        internal static readonly Guid XboxLibraryPluginId = Guid.Parse("7e4fbb5b-4594-4c5a-8a69-1e3f41b39c52");

        private readonly XboxSessionManager _sessionManager;
        private readonly XboxScanner _scanner;
        private readonly ILogger _logger;

        public XboxDataProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            XboxSessionManager sessionManager)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (sessionManager == null) throw new ArgumentNullException(nameof(sessionManager));

            _logger = logger;
            _sessionManager = sessionManager;

            var apiClient = new XboxApiClient(logger);
            _scanner = new XboxScanner(settings, sessionManager, apiClient, logger);

            _ = _sessionManager.PrimeAuthenticationStateAsync(CancellationToken.None);
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_Xbox");
        public string ProviderKey => "Xbox";
        public string ProviderIconKey => "ProviderIconXbox";
        public string ProviderColorHex => "#107C10";  // Xbox green

        /// <summary>
        /// Checks if Xbox authentication is properly configured.
        /// </summary>
        public bool IsAuthenticated => _sessionManager.IsAuthenticated;

        /// <summary>
        /// Determines if this provider can handle the specified game.
        /// </summary>
        public bool IsCapable(Game game)
        {
            if (game == null) return false;

            // Console games: GameId = "CONSOLE_{titleId}"
            if (game.GameId?.StartsWith("CONSOLE_") == true)
            {
                return true;
            }

            // Xbox library plugin
            if (game.PluginId == XboxLibraryPluginId)
            {
                return true;
            }

            // Source name matching for PC games
            var source = game.Source?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(source)) return false;

            return string.Equals(source, "Xbox", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, "Xbox Game Pass", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, "Microsoft Store", StringComparison.OrdinalIgnoreCase);
        }

        public Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, progressCallback, OnGameRefreshed, cancel);
        }
    }
}

using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamDataProvider : IDataProvider, IDisposable
    {
        internal static readonly Guid SteamPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        private readonly SteamHTTPClient _steamClient;
        private readonly SteamScanner _scanner;
        private readonly SteamAPIClient _apiHelper;
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;

        public SteamDataProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            SteamHTTPClient steamClient,
            SteamSessionManager sessionManager,
            SteamAPIClient apiHelper,
            IPlayniteAPI api)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (steamClient == null) throw new ArgumentNullException(nameof(steamClient));
            if (sessionManager == null) throw new ArgumentNullException(nameof(sessionManager));
            if (apiHelper == null) throw new ArgumentNullException(nameof(apiHelper));
            if (api == null) throw new ArgumentNullException(nameof(api));

            _steamClient = steamClient;
            _apiHelper = apiHelper;
            _api = api;
            _logger = logger;

            _scanner = new SteamScanner(settings, steamClient, sessionManager, apiHelper, api, logger);
        }

        public string ProviderName => "Steam";

        public Guid PlatformPluginId => SteamPluginId;

        /// <summary>
        /// Checks if Steam authentication is properly configured.
        /// Requires both SteamUserId and SteamApiKey to be present.
        /// </summary>
        public bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_settings.Persisted.SteamUserId) &&
            !string.IsNullOrWhiteSpace(_settings.Persisted.SteamApiKey);

        public bool IsCapable(Game game) =>
            IsSteamCapable(game);

        public static bool IsSteamCapable(Game game)
        {
            return game.PluginId == SteamPluginId;
        }

        public Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            return _scanner.ScanAsync(gamesToScan, progressCallback, onGameScanned, cancel);
        }

        public void Dispose()
        {
            _steamClient?.Dispose();
        }
    }
}

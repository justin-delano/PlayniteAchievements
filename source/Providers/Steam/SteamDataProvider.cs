using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
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
        private readonly PlayniteAchievementsSettings _settings;
        private readonly SteamSessionManager _sessionManager;

        /// <summary>
        /// Gets the session manager for Steam authentication operations.
        /// Exposed for settings UI to perform Steam authentication.
        /// </summary>
        public SteamSessionManager SessionManager => _sessionManager;

        private SteamDataProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            IPlayniteAPI api,
            string pluginUserDataPath)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (api == null) throw new ArgumentNullException(nameof(api));

            _logger = logger;
            _settings = settings;
            _api = api;

            // Create Steam-specific dependencies
            _sessionManager = new SteamSessionManager(api, logger, pluginUserDataPath, settings);
            _steamClient = new SteamHTTPClient(api, logger, _sessionManager);
            _apiHelper = new SteamAPIClient(_steamClient.ApiHttpClient, logger);

            _scanner = new SteamScanner(settings, _steamClient, _sessionManager, _apiHelper, api, logger);
        }

        /// <summary>
        /// Factory method to create a SteamDataProvider with all its dependencies.
        /// </summary>
        public static SteamDataProvider Create(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            IPlayniteAPI api,
            string pluginUserDataPath)
        {
            return new SteamDataProvider(logger, settings, api, pluginUserDataPath);
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_Steam");
        public string ProviderIconKey => "ProviderIconSteam";
        public string ProviderColorHex => "#B0B0B0";

        public Guid PlatformPluginId => SteamPluginId;

        /// <summary>
        /// Checks if Steam authentication is properly configured.
        /// Requires SteamUserId, SteamApiKey, and web session auth (cached SteamId64).
        /// </summary>
        public bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_settings.Persisted.SteamUserId) &&
            !string.IsNullOrWhiteSpace(_settings.Persisted.SteamApiKey) &&
            !string.IsNullOrWhiteSpace(_sessionManager.GetCachedSteamId64());

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

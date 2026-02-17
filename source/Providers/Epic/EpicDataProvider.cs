using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Epic
{
    public sealed class EpicDataProvider : IDataProvider, IDisposable
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly EpicSessionManager _sessionManager;
        private readonly EpicScanner _scanner;
        private readonly HttpClient _httpClient;

        private static readonly Guid EpicPluginId = ResolveEpicPluginId();
        internal static readonly Guid LegendaryPluginId = Guid.Parse("EAD65C3B-2F8F-4E37-B4E6-B3DE6BE540C6");

        public EpicDataProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            IPlayniteAPI playniteApi,
            EpicSessionManager sessionManager)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (playniteApi == null) throw new ArgumentNullException(nameof(playniteApi));
            if (sessionManager == null) throw new ArgumentNullException(nameof(sessionManager));

            _settings = settings;
            _httpClient = new HttpClient();
            _sessionManager = sessionManager;

            var apiClient = new EpicApiClient(_httpClient, logger, sessionManager, settings.Persisted);
            _scanner = new EpicScanner(settings, apiClient, sessionManager, logger);

            _ = _sessionManager.PrimeAuthenticationStateAsync(CancellationToken.None);
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_Epic");

        public string ProviderKey => "Epic";

        public string ProviderIconKey => "ProviderIconEpic";

        public string ProviderColorHex => "#26BBFF";

        public bool IsAuthenticated => _sessionManager.IsAuthenticated;

        public bool IsCapable(Game game) => IsEpicCapable(game);

        public static bool IsEpicCapable(Game game)
        {
            return game != null && (game.PluginId == EpicPluginId || game.PluginId == LegendaryPluginId);
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
            _httpClient?.Dispose();
        }

        private static Guid ResolveEpicPluginId()
        {
            try
            {
                return BuiltinExtensions.GetIdFromExtension(BuiltinExtension.EpicLibrary);
            }
            catch
            {
                return Guid.Empty;
            }
        }
    }
}

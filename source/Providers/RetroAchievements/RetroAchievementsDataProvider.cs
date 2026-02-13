using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.RetroAchievements.Hashing;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal sealed class RetroAchievementsDataProvider : IDataProvider, IDisposable
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly string _pluginUserDataPath;
        private readonly RetroAchievementsPathResolver _pathResolver;

        private readonly object _initLock = new object();
        private RetroAchievementsApiClient _apiClient;
        private RetroAchievementsHashIndexStore _hashIndexStore;
        private RetroAchievementsScanner _scanner;

        private string _clientUsername;
        private string _clientApiKey;

        public RetroAchievementsDataProvider(ILogger logger, PlayniteAchievementsSettings settings, string pluginUserDataPath, IPlayniteAPI playniteApi)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _pluginUserDataPath = pluginUserDataPath ?? string.Empty;
            _pathResolver = new RetroAchievementsPathResolver(playniteApi);
        }
        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_RetroAchievements");
        public string ProviderIconKey => "ProviderIconRetroAchievements";
        public string ProviderColorHex => "#FFD700";

        /// <summary>
        /// Checks if RetroAchievements authentication is properly configured.
        /// Requires both RaUsername and RaWebApiKey to be present.
        /// </summary>
        public bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_settings.Persisted.RaUsername) &&
            !string.IsNullOrWhiteSpace(_settings.Persisted.RaWebApiKey);

        public bool IsCapable(Game game)
        {
            if (game == null) return false;

            if (string.IsNullOrWhiteSpace(_settings.Persisted.RaUsername) || string.IsNullOrWhiteSpace(_settings.Persisted.RaWebApiKey))
            {
                return false;
            }

            if (!RaConsoleIdResolver.TryResolve(game, out var consoleId))
            {
                return false;
            }

            var hasher = RaHasherFactory.Create(consoleId, _settings, _logger);
            if (hasher == null)
            {
                return false;
            }

            return _pathResolver.ResolveCandidateFilePaths(game).Any(p =>
                !string.IsNullOrWhiteSpace(p) &&
                (File.Exists(p) || ArchiveUtils.IsArchivePath(p)));
        }

        public Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            EnsureInitialized();
            return _scanner.ScanAsync(gamesToScan, progressCallback, onGameScanned, cancel);
        }

        private void EnsureInitialized()
        {
            var username = _settings.Persisted.RaUsername?.Trim() ?? string.Empty;
            var apiKey = _settings.Persisted.RaWebApiKey?.Trim() ?? string.Empty;

            lock (_initLock)
            {
                if (_scanner != null && string.Equals(username, _clientUsername, StringComparison.Ordinal) &&
                    string.Equals(apiKey, _clientApiKey, StringComparison.Ordinal))
                {
                    return;
                }

                _apiClient?.Dispose();
                _apiClient = new RetroAchievementsApiClient(_logger, username, apiKey);
                _hashIndexStore = new RetroAchievementsHashIndexStore(_logger, _settings, _apiClient, _pluginUserDataPath);
                _scanner = new RetroAchievementsScanner(_logger, _settings, _apiClient, _hashIndexStore, _pathResolver);

                _clientUsername = username;
                _clientApiKey = apiKey;
            }
        }

        // private bool TryResolveConsoleId(Game game, out int consoleId)
        //     => RaConsoleIdResolver.TryResolve(game, out consoleId);

        public void Dispose()
        {
            _apiClient?.Dispose();
        }
    }
}

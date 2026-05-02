using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Settings;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Hoyoverse
{
    internal sealed class HoyoverseDataProvider : IDataProvider, IDisposable
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly IPlayniteAPI _playniteApi;
        private readonly string _pluginUserDataPath;
        private readonly HttpClient _httpClient;
        private HoyoverseSettings _providerSettings;

        public HoyoverseDataProvider(ILogger logger, PlayniteAchievementsSettings settings, IPlayniteAPI playniteApi)
            : this(logger, settings, playniteApi, string.Empty)
        {
        }

        public HoyoverseDataProvider(ILogger logger, PlayniteAchievementsSettings settings, IPlayniteAPI playniteApi, string pluginUserDataPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playniteApi = playniteApi;
            _pluginUserDataPath = pluginUserDataPath ?? string.Empty;
            _providerSettings = ProviderRegistry.Settings<HoyoverseSettings>();
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public string ProviderName
        {
            get
            {
                var value = ResourceProvider.GetString("LOCPlayAch_Provider_Hoyoverse");
                return string.IsNullOrWhiteSpace(value) ? "HoYoverse" : value;
            }
        }

        public string ProviderKey => HoyoverseConstants.ProviderKey;

        public string ProviderIconKey => HoyoverseConstants.ProviderIconKey;

        public string ProviderColorHex => HoyoverseConstants.ProviderColorHex;

        public bool IsAuthenticated => true;

        public ISessionManager AuthSession => null;

        public bool IsCapable(Game game)
        {
            return HoyoverseGameCatalog.TryResolve(game, _providerSettings, out _);
        }

        public Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            var scanner = new HoyoverseScanner(
                _logger,
                _settings,
                _providerSettings,
                _pluginUserDataPath,
                new HoyoverseDefinitionClient(_httpClient, _logger, _pluginUserDataPath));

            return scanner.RefreshAsync(gamesToRefresh, onGameStarting, onGameCompleted, cancel);
        }

        public IProviderSettings GetSettings() => _providerSettings;

        public void ApplySettings(IProviderSettings settings)
        {
            if (settings is HoyoverseSettings hoyoverseSettings)
            {
                _providerSettings.CopyFrom(hoyoverseSettings);
            }
        }

        public ProviderSettingsViewBase CreateSettingsView() => new HoyoverseSettingsView(_playniteApi);

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}

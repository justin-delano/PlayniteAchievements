using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.BattleNet
{
    public sealed class BattleNetDataProvider : IDataProvider, IDisposable
    {
        internal static readonly Guid BattleNetPluginId = Guid.Parse("E3C26A3D-D695-4CB7-A769-5FF7612C7EDD");

        private readonly BattleNetSessionManager _sessionManager;
        private readonly BattleNetApiClient _apiClient;
        private readonly BattleNetScanner _scanner;
        private readonly ILogger _logger;
        private BattleNetSettings _providerSettings;

        public BattleNetDataProvider(ILogger logger, PlayniteAchievementsSettings settings, IPlayniteAPI playniteApi)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (playniteApi == null) throw new ArgumentNullException(nameof(playniteApi));

            _logger = logger;
            _sessionManager = new BattleNetSessionManager(playniteApi, logger);
            _apiClient = new BattleNetApiClient(logger);
            _scanner = new BattleNetScanner(_apiClient, _sessionManager, settings, logger);
            _providerSettings = ProviderRegistry.Settings<BattleNetSettings>();
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_BattleNet");
        public string ProviderKey => "BattleNet";
        public string ProviderIconKey => "ProviderIconBattleNet";
        public string ProviderColorHex => "#14B8A6";

        public bool IsAuthenticated => _sessionManager.IsAuthenticated;

        public ISessionManager AuthSession => _sessionManager;

        public bool IsCapable(Game game) =>
            game != null && game.PluginId == BattleNetPluginId;

        public Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, onGameStarting, onGameCompleted, cancel);
        }

        public IProviderSettings GetSettings() => _providerSettings;

        public void ApplySettings(IProviderSettings settings)
        {
            if (settings is BattleNetSettings battleNetSettings)
            {
                _providerSettings.CopyFrom(battleNetSettings);
            }
        }

        public ProviderSettingsViewBase CreateSettingsView() => new BattleNetSettingsView(_sessionManager, _apiClient, _logger);

        public void Dispose()
        {
            _apiClient?.Dispose();
        }
    }
}

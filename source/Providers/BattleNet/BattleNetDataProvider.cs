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
        internal static readonly Guid BattleNetPluginId = BattleNetGameSupport.BattleNetPluginId;

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
            _apiClient = new BattleNetApiClient(logger);
            _sessionManager = new BattleNetSessionManager(playniteApi, logger);
            _scanner = new BattleNetScanner(_apiClient, _sessionManager, settings, logger);
            _providerSettings = ProviderRegistry.Settings<BattleNetSettings>();

            _logger.Info($"[BattleNet] Provider initialized. {SettingsSummary(_providerSettings)}");
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_BattleNet");
        public string ProviderKey => "BattleNet";
        public string ProviderIconKey => "ProviderIconBattleNet";
        public string ProviderColorHex => "#14B8A6";

        public bool IsAuthenticated => true;

        public ISessionManager AuthSession => null;

        public bool IsCapable(Game game) =>
            BattleNetGameSupport.IsSupported(game, _providerSettings);

        public Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            _logger.Info($"[BattleNet] Refresh requested. games={gamesToRefresh?.Count ?? 0}, authenticated={Bool(IsAuthenticated)}, {SettingsSummary(_providerSettings)}");
            return _scanner.RefreshAsync(gamesToRefresh, onGameStarting, onGameCompleted, cancel);
        }

        public IProviderSettings GetSettings() => _providerSettings;

        public void ApplySettings(IProviderSettings settings)
        {
            if (settings is BattleNetSettings battleNetSettings)
            {
                _logger.Info($"[BattleNet] Applying provider settings. incoming={SettingsSummary(battleNetSettings)}");
                _providerSettings.CopyFrom(battleNetSettings);
                _logger.Debug($"[BattleNet] Provider settings applied. current={SettingsSummary(_providerSettings)}");
            }
            else
            {
                _logger.Warn($"[BattleNet] Ignored incompatible provider settings object: {settings?.GetType().FullName ?? "<null>"}");
            }
        }

        public ProviderSettingsViewBase CreateSettingsView()
        {
            _logger.Debug("[BattleNet] Creating settings view.");
            return new BattleNetSettingsView(_sessionManager, _apiClient, _logger);
        }

        public void Dispose()
        {
            _logger.Debug("[BattleNet] Disposing provider resources.");
            _apiClient?.Dispose();
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string MaskId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= 4)
            {
                return "****";
            }

            return $"{new string('*', Math.Min(8, trimmed.Length - 4))}{trimmed.Substring(trimmed.Length - 4)}";
        }

        private static string Presence(string value) => string.IsNullOrWhiteSpace(value) ? "missing" : "set";

        private static string SettingsSummary(BattleNetSettings settings)
        {
            if (settings == null)
            {
                return "<null settings>";
            }

            return string.Format(
                "enabled={0}, userId={1}, battleTag={2}, apiClientId={3}, apiClientSecret={4}, oauthRedirectUri={5}, oauthAccessToken={6}, oauthRefreshToken={7}, oauthExpiresUtc={8}, sc2Region={9}, sc2Realm={10}, sc2Profile={11}, wowRegion={12}, wowRealmSlug={13}, wowCharacter={14}",
                Bool(settings.IsEnabled),
                MaskId(settings.BattleNetUserId),
                Presence(settings.BattleTag),
                Presence(settings.BattleNetClientId),
                Presence(settings.BattleNetClientSecret),
                Presence(settings.BattleNetOAuthRedirectUri),
                Presence(settings.BattleNetAccessToken),
                Presence(settings.BattleNetRefreshToken),
                settings.BattleNetTokenExpiresUtc == default ? "<none>" : settings.BattleNetTokenExpiresUtc.ToString("O"),
                settings.Sc2RegionId,
                settings.Sc2RealmId,
                settings.Sc2ProfileId > 0 ? MaskId(settings.Sc2ProfileId.ToString()) : "<none>",
                string.IsNullOrWhiteSpace(settings.WowRegion) ? "<none>" : settings.WowRegion,
                string.IsNullOrWhiteSpace(settings.WowRealmSlug) ? "<none>" : settings.WowRealmSlug,
                Presence(settings.WowCharacter));
        }
    }
}

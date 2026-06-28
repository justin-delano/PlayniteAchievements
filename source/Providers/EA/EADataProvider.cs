using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Overrides;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.EA
{
    public sealed class EADataProvider : IDataProvider, IProviderOverride, IDisposable
    {
        public ProviderOverrideDescriptor OverrideDescriptor { get; } = ProviderOverrideDescriptor.Text(
            "LOCPlayAch_ManageAchievements_Overrides_ProviderValueLabel_EA",
            "EA Offer ID",
            ProviderOverrideValidators.RequiredText);

        private static readonly Guid EaPluginId = ResolveEaPluginId();

        private readonly EASessionManager _sessionManager;
        private readonly EAScanner _scanner;
        private readonly HttpClient _httpClient;
        private EASettings _providerSettings;

        public EADataProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            IPlayniteAPI playniteApi,
            string pluginUserDataPath)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _ = settings ?? throw new ArgumentNullException(nameof(settings));
            _ = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));

            _httpClient = new HttpClient();
            _sessionManager = new EASessionManager(playniteApi, logger, _httpClient);
            _providerSettings = ProviderRegistry.Settings<EASettings>();

            var apiClient = new EAApiClient(_httpClient, logger, _sessionManager);
            _scanner = new EAScanner(
                settings,
                _providerSettings,
                apiClient,
                _sessionManager,
                logger,
                playniteApi,
                pluginUserDataPath);
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_EA");
        public string ProviderKey => "EA";
        public string ProviderIconKey => "ProviderIconEA";
        public string ProviderColorHex => "#E11D48";

        public bool IsAuthenticated => _sessionManager.IsAuthenticated;

        public ISessionManager AuthSession => _sessionManager;

        public bool IsCapable(Game game) =>
            EAProviderSupport.IsEaCapable(game, EaPluginId) ||
            (game != null && GameCustomDataLookup.TryGetProviderOverrideValue(game.Id, "EA", out _));

        public Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, onGameStarting, onGameCompleted, cancel);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public IProviderSettings GetSettings() => _providerSettings;

        public void ApplySettings(IProviderSettings settings)
        {
            if (settings is EASettings eaSettings)
            {
                _providerSettings.CopyFrom(eaSettings);
            }
        }

        public ProviderSettingsViewBase CreateSettingsView() => new EASettingsView(_sessionManager);

        private static Guid ResolveEaPluginId()
        {
            try
            {
                return BuiltinExtensions.GetIdFromExtension(BuiltinExtension.OriginLibrary);
            }
            catch
            {
                return Guid.Empty;
            }
        }
    }
}

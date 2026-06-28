using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Overrides;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamDataProvider : IDataProvider, IAchievementPageLinkProvider, IProviderOverride, IDisposable
    {
        internal static readonly Guid SteamPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        public ProviderOverrideDescriptor OverrideDescriptor { get; } = ProviderOverrideDescriptor.Text(
            "LOCPlayAch_ManageAchievements_Overrides_ProviderValueLabel_Steam",
            "Steam AppID",
            raw =>
            {
                if (int.TryParse((raw ?? string.Empty).Trim(), out var appId) && appId > 0)
                {
                    return ProviderOverrideValidation.Valid(appId.ToString(CultureInfo.InvariantCulture));
                }

                return ProviderOverrideValidation.Invalid(
                    "LOCPlayAch_Menu_SteamAppId_InvalidId",
                    "Please enter a valid positive integer Steam AppID.");
            });

        private readonly SteamHttpClient _steamClient;
        private readonly SteamScanner _scanner;
        private readonly SteamSessionManager _sessionManager;
        private SteamSettings _providerSettings;

        public SteamDataProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            IPlayniteAPI api,
            string pluginUserDataPath)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (api == null) throw new ArgumentNullException(nameof(api));

            _sessionManager = new SteamSessionManager(api, logger);

            // Initialize provider settings from persisted settings dictionary
            _providerSettings = ProviderRegistry.Settings<SteamSettings>();

            // Create Steam-specific dependencies
            _steamClient = new SteamHttpClient(api, logger, _sessionManager, pluginUserDataPath);
            _sessionManager.SetClearInMemoryAuthState(_steamClient.ClearInMemoryAuthState);
            var steamApiClient = new SteamApiClient(_steamClient.ApiHttpClient, logger);
            var tokenResolver = new SteamWebApiTokenResolver(_sessionManager, logger);
            _scanner = new SteamScanner(settings, _steamClient, steamApiClient, tokenResolver, api, logger);
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_Steam");
        public string ProviderKey => "Steam";
        public string ProviderIconKey => "ProviderIconSteam";
        public string ProviderColorHex => "#B0B0B0";

        /// <summary>
        /// Snapshot of the last known persisted Steam auth state.
        /// AuthSession is the authoritative auth check for runtime flows.
        /// </summary>
        public bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_providerSettings.SteamUserId);

        public ISessionManager AuthSession => _sessionManager;

        public bool IsCapable(Game game) =>
            IsSteamCapable(game);

        public bool CanResolveAchievementPageUrl(AchievementPageLinkContext context)
        {
            return TryBuildAchievementPageUrl(context, out _);
        }

        public Task<string> GetAchievementPageUrlAsync(
            AchievementPageLinkContext context,
            CancellationToken cancel)
        {
            return Task.FromResult(
                TryBuildAchievementPageUrl(context, out var url)
                    ? url
                    : null);
        }

        internal static bool TryBuildAchievementPageUrl(
            AchievementPageLinkContext context,
            out string url)
        {
            url = null;
            if (!TryResolveSteamAppId(context, out var appId))
            {
                return false;
            }

            url = $"https://steamcommunity.com/stats/{appId.ToString(CultureInfo.InvariantCulture)}/achievements";
            return true;
        }

        private static bool IsSteamCapable(Game game)
        {
            return game != null &&
                   (game.PluginId == SteamPluginId ||
                    GameCustomDataLookup.TryGetSteamAppIdOverride(game.Id, out _));
        }

        private static bool TryResolveSteamAppId(
            AchievementPageLinkContext context,
            out int appId)
        {
            appId = 0;
            if (context?.Game != null &&
                GameCustomDataLookup.TryGetSteamAppIdOverride(context.Game.Id, out appId))
            {
                return true;
            }

            if (string.Equals(context?.ManualLink?.SourceKey, "Steam", StringComparison.OrdinalIgnoreCase) &&
                TryGetPositiveId(context.ManualLink.SourceGameId, out appId))
            {
                return true;
            }

            var cachedAppId = context?.BestGameData?.AppId ?? 0;
            if (cachedAppId > 0)
            {
                appId = cachedAppId;
                return true;
            }

            return TryGetPositiveId(context?.Game?.GameId, out appId);
        }

        private static bool TryGetPositiveId(string value, out int id)
        {
            return int.TryParse(
                       (value ?? string.Empty).Trim(),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out id) &&
                   id > 0;
        }

        public Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, onGameStarting, onGameCompleted, cancel);
        }

        /// <inheritdoc />
        public IProviderSettings GetSettings() => _providerSettings;

        /// <inheritdoc />
        public void ApplySettings(IProviderSettings settings)
        {
            if (settings is SteamSettings steamSettings)
            {
                _providerSettings.CopyFrom(steamSettings);
            }
        }

        /// <inheritdoc />
        public ProviderSettingsViewBase CreateSettingsView() => new SteamSettingsView(_sessionManager);

        public void Dispose()
        {
            _steamClient?.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services;

namespace PlayniteAchievements.Providers.Manual
{
    /// <summary>
    /// Data provider for manually linked achievements.
    /// Implements IDataProvider to integrate with the achievement refresh system.
    /// </summary>
    public sealed class ManualAchievementsProvider : IDataProvider
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ManualSourceRegistry _manualSourceRegistry;
        private ManualSettings _providerSettings;

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_Manual");
        public string ProviderKey => "Manual";
        public string ProviderIconKey => "ProviderIconManual";
        public string ProviderColorHex => "#ff652c";

        /// <summary>
        /// Manual provider is always authenticated (no credentials needed).
        /// </summary>
        public bool IsAuthenticated => true;

        public ISessionManager AuthSession => null;

        public ManualAchievementsProvider(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            IPlayniteAPI playniteApi,
            ManualSourceRegistry manualSourceRegistry)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _manualSourceRegistry = manualSourceRegistry ?? throw new ArgumentNullException(nameof(manualSourceRegistry));
            _providerSettings = ProviderRegistry.Settings<ManualSettings>();
        }

        /// <summary>
        /// Checks if a game has a manual achievement link configured.
        /// </summary>
        public bool IsCapable(Game game)
        {
            if (game == null || game.Id == Guid.Empty)
            {
                return false;
            }

            return _providerSettings.AchievementLinks.ContainsKey(game.Id);
        }

        /// <summary>
        /// Refreshes achievement data for all games with manual links.
        /// Fetches schema from the source and applies stored unlock times.
        /// </summary>
        public async Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var links = _providerSettings.AchievementLinks;
            var linkedGames = gamesToRefresh
                .Where(game => game != null && game.Id != Guid.Empty && links.ContainsKey(game.Id))
                .ToList();

            if (linkedGames.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var language = _settings.Persisted.GlobalLanguage ?? "english";

            return await ProviderRefreshExecutor.RunProviderGamesAsync(
                linkedGames,
                onGameStarting,
                async (game, token) =>
                {
                    if (!links.TryGetValue(game.Id, out var link) || link == null)
                    {
                        return ProviderRefreshExecutor.ProviderGameResult.Skipped();
                    }

                    var data = await BuildGameAchievementDataAsync(game, link, language, token).ConfigureAwait(false);
                    return new ProviderRefreshExecutor.ProviderGameResult
                    {
                        Data = data
                    };
                },
                onGameCompleted,
                isAuthRequiredException: ex => ex is ManualSourceAuthenticationException,
                onGameError: (game, ex, consecutiveErrors) =>
                {
                    _logger?.Error(ex, $"Failed to refresh manual achievements for game '{game?.Name}' ({game?.Id})");
                },
                delayBetweenGamesAsync: null,
                delayAfterErrorAsync: null,
                cancel).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds GameAchievementData for a game with a manual link.
        /// Fetches the achievement schema from the source and applies stored unlock times.
        /// </summary>
        private async Task<GameAchievementData> BuildGameAchievementDataAsync(
            Game game,
            ManualAchievementLink link,
            string language,
            CancellationToken cancel)
        {
            if (link == null || string.IsNullOrWhiteSpace(link.SourceGameId))
            {
                return null;
            }

            var source = _manualSourceRegistry.GetSourceByKey(link.SourceKey);
            if (source == null)
            {
                _logger?.Warn($"Unknown manual source key: {link.SourceKey}");
                return null;
            }

            await ManualSourceAuthentication.EnsureAuthenticatedAsync(source, cancel).ConfigureAwait(false);

            // Fetch achievements directly as AchievementDetail list
            var achievements = await source.GetAchievementsAsync(link.SourceGameId, language, cancel);
            if (achievements == null || achievements.Count == 0)
            {
                _logger?.Debug($"No achievements found for manual link: source={link.SourceKey}, gameId={link.SourceGameId}");
                return new GameAchievementData
                {
                    LastUpdatedUtc = DateTime.UtcNow,
                    ProviderKey = ProviderKey,
                    LibrarySourceName = game.PluginId.ToString(),
                    HasAchievements = false,
                    GameName = game.Name,
                    PlayniteGameId = game.Id,
                    Game = game,
                    Achievements = new List<AchievementDetail>()
                };
            }

            // Apply unlock times from link to each achievement
            foreach (var detail in achievements)
            {
                if (detail == null || string.IsNullOrWhiteSpace(detail.ApiName))
                {
                    continue;
                }

                var unlockedState = false;
                var hasState = link.UnlockStates != null &&
                               link.UnlockStates.TryGetValue(detail.ApiName, out unlockedState);

                DateTime? unlockTime = null;
                var hasTime = link.UnlockTimes != null &&
                              link.UnlockTimes.TryGetValue(detail.ApiName, out unlockTime);

                var isUnlocked = hasState
                    ? unlockedState
                    : (hasTime && unlockTime.HasValue);

                if (isUnlocked)
                {
                    detail.Unlocked = true;
                    detail.UnlockTimeUtc = hasTime && unlockTime.HasValue
                        ? unlockTime
                        : null;
                }
            }

            return new GameAchievementData
            {
                LastUpdatedUtc = DateTime.UtcNow,
                ProviderKey = ProviderKey,
                LibrarySourceName = game.PluginId.ToString(),
                HasAchievements = true,
                GameName = game.Name,
                AppId = int.TryParse(link.SourceGameId, out var appId) ? appId : 0,
                PlayniteGameId = game.Id,
                Game = game,
                Achievements = achievements
            };
        }

        /// <inheritdoc />
        public IProviderSettings GetSettings() => _providerSettings;

        /// <inheritdoc />
        public void ApplySettings(IProviderSettings settings)
        {
            if (settings is ManualSettings manualSettings)
            {
                _providerSettings.CopyFrom(manualSettings);
            }
        }

        /// <inheritdoc />
        public ProviderSettingsViewBase CreateSettingsView() => new ManualSettingsView(_playniteApi, _logger, _settings);
    }
}






using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Epic
{
    internal sealed class EpicScanner
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly EpicApiClient _apiClient;
        private readonly EpicSessionManager _sessionManager;
        private readonly ILogger _logger;

        public EpicScanner(
            PlayniteAchievementsSettings settings,
            EpicApiClient apiClient,
            EpicSessionManager sessionManager,
            ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger;
        }

        public async Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            var report = progressCallback ?? (_ => { });

            var probeResult = await _sessionManager.ProbeAuthenticationAsync(cancel).ConfigureAwait(false);
            if (!probeResult.IsSuccess)
            {
                _logger?.Warn("[EpicAch] Epic not authenticated - cannot scan achievements.");
                report(new ProviderScanUpdate { AuthRequired = true });
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            if (gamesToScan == null || gamesToScan.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var progress = new RebuildProgressReporter(report, gamesToScan.Count);
            var summary = new RebuildSummary();
            var rateLimiter = new RateLimiter(
                _settings.Persisted.ScanDelayMs,
                _settings.Persisted.MaxRetryAttempts);

            int consecutiveErrors = 0;

            for (int i = 0; i < gamesToScan.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                progress.Step();

                var game = gamesToScan[i];
                var gameId = game?.GameId?.Trim();
                if (string.IsNullOrWhiteSpace(gameId))
                {
                    continue;
                }

                progress.Emit(new ProviderScanUpdate
                {
                    CurrentGameName = !string.IsNullOrWhiteSpace(game.Name) ? game.Name : gameId
                });

                try
                {
                    var data = await rateLimiter.ExecuteWithRetryAsync(
                        () => FetchGameDataAsync(game, gameId, cancel),
                        EpicApiClient.IsTransientError,
                        cancel).ConfigureAwait(false);

                    if (onGameScanned != null && data != null)
                    {
                        await onGameScanned(data).ConfigureAwait(false);
                    }

                    summary.GamesScanned++;
                    if (data != null && !data.NoAchievements)
                    {
                        summary.GamesWithAchievements++;
                    }
                    else
                    {
                        summary.GamesWithoutAchievements++;
                    }

                    consecutiveErrors = 0;

                    if (i < gamesToScan.Count - 1)
                    {
                        await rateLimiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
                    }
                }
                catch (EpicAuthRequiredException)
                {
                    report(new ProviderScanUpdate { AuthRequired = true });
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger?.Debug(ex, $"[EpicAch] Failed to scan {game.Name} after {consecutiveErrors} consecutive errors.");

                    if (consecutiveErrors >= 3)
                    {
                        await rateLimiter.DelayAfterErrorAsync(consecutiveErrors, cancel).ConfigureAwait(false);
                    }
                }
            }

            progress.Emit(new ProviderScanUpdate
            {
                CurrentGameName = null
            }, force: true);

            return new RebuildPayload { Summary = summary };
        }

        private async Task<GameAchievementData> FetchGameDataAsync(Game game, string gameId, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            var accountId = _sessionManager.GetAccountId();
            if (string.IsNullOrWhiteSpace(accountId))
            {
                throw new EpicAuthRequiredException("Epic account ID not available. Please authenticate.");
            }

            try
            {
                var items = await _apiClient.GetAchievementsAsync(gameId, accountId, cancel).ConfigureAwait(false);
                return MapToGameData(game, gameId, items);
            }
            catch (EpicApiNotAvailableException)
            {
                _logger?.Debug($"[EpicAch] API unavailable for {game?.Name}; using web fallback.");
                return await FetchViaWebFallbackAsync(game, gameId, cancel).ConfigureAwait(false);
            }
        }

        private Task<GameAchievementData> FetchViaWebFallbackAsync(Game game, string gameId, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            return Task.FromResult(new GameAchievementData
            {
                AppId = 0,
                GameName = game?.Name,
                ProviderName = ResourceProvider.GetString("LOCPlayAch_Provider_Epic"),
                LibrarySourceName = game?.Source?.Name,
                PlaytimeSeconds = 0,
                LastUpdatedUtc = DateTime.UtcNow,
                NoAchievements = true,
                PlayniteGameId = game != null ? game.Id : Guid.Empty,
                Achievements = new List<AchievementDetail>()
            });
        }

        private static GameAchievementData MapToGameData(Game game, string gameId, List<EpicAchievementItem> items)
        {
            var data = new GameAchievementData
            {
                AppId = 0,
                GameName = game?.Name,
                ProviderName = ResourceProvider.GetString("LOCPlayAch_Provider_Epic"),
                LibrarySourceName = game?.Source?.Name,
                PlaytimeSeconds = 0,
                LastUpdatedUtc = DateTime.UtcNow,
                NoAchievements = items == null || items.Count == 0,
                PlayniteGameId = game != null ? game.Id : Guid.Empty,
                Achievements = new List<AchievementDetail>()
            };

            if (items == null)
            {
                return data;
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.AchievementId))
                {
                    continue;
                }

                data.Achievements.Add(new AchievementDetail
                {
                    ApiName = item.AchievementId,
                    DisplayName = string.IsNullOrWhiteSpace(item.Title) ? item.AchievementId : item.Title,
                    Description = item.Description ?? string.Empty,
                    IconPath = item.IconUrl ?? string.Empty,
                    Hidden = item.Hidden,
                    UnlockTimeUtc = item.UnlockTimeUtc,
                    GlobalPercentUnlocked = item.RarityPercent
                });
            }

            data.NoAchievements = data.Achievements.Count == 0;
            return data;
        }
    }
}

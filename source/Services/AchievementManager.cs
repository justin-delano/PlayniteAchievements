using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Steam;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Images;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Services
{
    /// <summary>
    /// Manages user achievement scanning and caching operations.
    /// </summary>
    public class AchievementManager : IDisposable
    {
        private readonly object _runLock = new object();
        private CancellationTokenSource _activeRunCts;

        public event EventHandler<ProgressReport> RebuildProgress;

        private ProgressReport _lastProgress;
        private string _lastStatus;

        public ProgressReport GetLastRebuildProgress() => _lastProgress;
        public string GetLastRebuildStatus() => _lastStatus;

        public bool IsRebuilding
        {
            get { lock (_runLock) return _activeRunCts != null; }
        }

        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ICacheManager _cacheService;
        private readonly DiskImageService _diskImageService;

        // Dependencies that need disposal
        private readonly IReadOnlyList<IDataProvider> _providers;
        private readonly RebuildProgressMapper _progressMapper;

        public ICacheManager Cache => _cacheService;

        /// <summary>
        /// Checks if at least one provider has valid authentication credentials configured.
        /// </summary>
        public bool HasAnyAuthenticatedProvider() => _providers.Any(p => p.IsAuthenticated);

        /// <summary>
        /// Gets the list of available data providers.
        /// </summary>
        public IReadOnlyList<IDataProvider> GetProviders() => _providers;

        public event EventHandler<GameCacheUpdatedEventArgs> GameCacheUpdated
        {
            add => _cacheService.GameCacheUpdated += value;
            remove => _cacheService.GameCacheUpdated -= value;
        }

        public event EventHandler CacheInvalidated
        {
            add => _cacheService.CacheInvalidated += value;
            remove => _cacheService.CacheInvalidated -= value;
        }

        public AchievementManager(
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            PlayniteAchievementsPlugin plugin,
            IEnumerable<IDataProvider> providers,
            DiskImageService diskImageService)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            if (providers == null) throw new ArgumentNullException(nameof(providers));
            _diskImageService = diskImageService ?? throw new ArgumentNullException(nameof(diskImageService));

            _cacheService = new CacheManager(api, logger, _plugin);
            _providers = providers.ToList();
            _progressMapper = new RebuildProgressMapper();
        }

        public void Dispose()
        {
            // SettingsPersister removed - user settings are now only saved via ISettings.EndEdit()
        }

        // -----------------------------
        // UI helpers
        // -----------------------------

        private void PostToUi(Action action)
        {
            _api?.MainView?.UIDispatcher?.InvokeIfNeeded(action, DispatcherPriority.Background);
        }

        private void Report(string message, int current = 0, int total = 0, bool canceled = false)
        {
            var report = new ProgressReport
            {
                Message = message,
                CurrentStep = current,
                TotalSteps = total,
                IsCanceled = canceled
            };

            _lastProgress = report;
            if (!string.IsNullOrWhiteSpace(message))
                _lastStatus = message;

            var handler = RebuildProgress;
            if (handler == null) return;

            PostToUi(() =>
            {
                try { handler(this, report); }
                catch (Exception e)
                {
                    _logger?.Error(e, ResourceProvider.GetString("LOCPlayAch_Error_NotifySubscribers"));
                }
            });
        }

        // -----------------------------
        // Scan option builders
        // -----------------------------

        private CacheScanOptions FullRefreshOptions()
        {
            return new CacheScanOptions
            {
                QuickRefreshMode = false,
                IgnoreUnplayedGames = _settings.Persisted.IgnoreUnplayedGames
            };
        }

        private CacheScanOptions SingleGameOptions(Guid playniteGameId)
        {
            return new CacheScanOptions
            {
                PlayniteGameIds = new[] { playniteGameId },
                IgnoreUnplayedGames = false
            };
        }

        private CacheScanOptions QuickRefreshOptions()
        {
            return new CacheScanOptions
            {
                QuickRefreshMode = true,
                QuickRefreshRecentGamesCount = _settings?.Persisted?.QuickRefreshRecentGamesCount ?? 10,
                IgnoreUnplayedGames = _settings.Persisted.IgnoreUnplayedGames
            };
        }

        // -----------------------------
        // Centralized progress mapping
        // -----------------------------

        private void HandleUpdate(RebuildUpdate update)
        {
            var mapped = _progressMapper.Map(update);
            if (mapped != null)
            {
                Report(mapped.Message, mapped.CurrentStep, mapped.TotalSteps, mapped.IsCanceled);
            }
        }

        // -----------------------------
        // Managed scan runner
        // -----------------------------

        private bool TryBeginRun(out CancellationTokenSource cts)
        {
            lock (_runLock)
            {
                if (_activeRunCts != null)
                {
                    _logger.Info("TryBeginRun: Scan already in progress.");
                    cts = null;
                    Report(_lastStatus ?? ResourceProvider.GetString("LOCPlayAch_Status_UpdatingCache"), 0, 1);
                    return false;
                }

                _logger.Info("TryBeginRun: Starting new scan.");
                _activeRunCts = new CancellationTokenSource();
                cts = _activeRunCts;
                return true;
            }
        }

        private void EndRun()
        {
            _logger.Info("EndRun called.");
            lock (_runLock)
            {
                _activeRunCts?.Dispose();
                _activeRunCts = null;
            }
        }

        private async Task RunManagedAsync(
            Func<CancellationToken, Task<RebuildPayload>> runner,
            Func<RebuildPayload, string> finalMessage,
            string errorLogMessage)
        {
            _logger.Info("RunManagedAsync starting. Error msg template: " + errorLogMessage);
            if (!TryBeginRun(out var cts))
                return;

            _progressMapper.Reset();

            // Report immediately so UI updates buttons before any async work
            var startMsg = ResourceProvider.GetString("LOCPlayAch_Status_Starting");
            Report(startMsg, 0, 1);

            try
            {
                var payload = await runner(cts.Token).ConfigureAwait(false);

                var msg = finalMessage?.Invoke(payload) ?? ResourceProvider.GetString("LOCPlayAch_Status_Ready");
                Report(msg, 1, 1);

                PostToUi(() =>
                {
                    _api?.Notifications?.Add(new NotificationMessage(
                        "SteamAch_RebuildComplete",
                        msg,
                        NotificationType.Info));
                });
            }
            catch (OperationCanceledException)
            {
                _logger.Info("User achievement scan was canceled.");
                Report(ResourceProvider.GetString("LOCPlayAch_Status_Canceled"), 0, 1, true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, errorLogMessage);
                var errorMsg = ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed");
                Report(errorMsg, 0, 1);

                PostToUi(() =>
                {
                    _api?.Notifications?.Add(new NotificationMessage(
                        "SteamAch_RebuildError",
                        $"{errorMsg}: {ex.Message}",
                        NotificationType.Error));
                });
            }
            finally
            {
                EndRun();
            }
        }

        private List<Game> GetGamesToScan(CacheScanOptions options)
        {
            if (options.PlayniteGameIds?.Count > 0)
            {
                return options.PlayniteGameIds
                    .Select(id => _api.Database.Games.Get(id))
                    .Where(g => g != null && _providers.Any(p => p.IsCapable(g)))
                    .ToList();
            }

            var allGames = _api.Database.Games.ToList();

            // QuickRefreshMode: Use Playnite's LastActivity to get most recently played games
            if (options.QuickRefreshMode)
            {
                return allGames
                    .Where(g => g.LastActivity != null)
                    .OrderByDescending(g => g.LastActivity)
                    .Where(g => _providers.Any(p => p.IsCapable(g)))
                    .Take(options.QuickRefreshRecentGamesCount)
                    .ToList();
            }

            // IgnoreUnplayedGames: Use Playnite's Playtime property
            if (options.IgnoreUnplayedGames)
            {
                return allGames
                    .Where(g => g.Playtime > 0 && _providers.Any(p => p.IsCapable(g)))
                    .ToList();
            }

            return allGames
                .Where(g => _providers.Any(p => p.IsCapable(g)))
                .ToList();
        }

        private async Task<RebuildPayload> ScanAsync(
            CacheScanOptions options,
            Action<RebuildUpdate> progressCallback,
            CancellationToken cancel)
        {
            options ??= new CacheScanOptions();

            var gamesToScan = GetGamesToScan(options);
            var gamesWithProviders = gamesToScan
                .Select(g => new { Game = g, Provider = _providers.FirstOrDefault(p => p.IsCapable(g)) })
                .ToList();

            var gamesByProvider = gamesWithProviders
                .Where(x => x.Provider != null)
                .GroupBy(x => x.Provider)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Game).ToList());

            // log games by providers to check, list all games and all providers
            _logger.Debug($"[Scan] Games to scan: {gamesToScan.Count}, Providers: {_providers.Count}, Grouped providers: {gamesByProvider.Count}");
            // _logger.Debug($"[Scan] Games with providers: {string.Join(", ", gamesWithProviders.Where(x => x.Provider != null).Select(x => x.Game.Name + " => " + x.Provider.ProviderName))}");

            if (gamesByProvider.Count == 0 && gamesWithProviders.All(x => x.Provider == null))
            {
                _logger?.Warn("[Scan] No matching providers available for scan options.");
                return new RebuildPayload();
            }

            var totalGames = gamesToScan.Count;
            var summary = new RebuildSummary();
            var scannedSoFar = 0;

            foreach (var kvp in gamesByProvider)
            {
                var provider = kvp.Key;
                var games = kvp.Value;

                // Create a localized callback for this provider
                Action<ProviderScanUpdate> wrappedCallback = (u) =>
                {
                    if (u == null) return;

                    if (u.AuthRequired)
                    {
                        progressCallback?.Invoke(new RebuildUpdate { Kind = RebuildUpdateKind.AuthRequired });
                        return;
                    }

                    // Map provider-local index to global index
                    var localIndex = Math.Min(u.CurrentIndex, games.Count);
                    
                    // localIndex is 1-based (from progress reporter steps), so we just add the offset
                    var globalIndex = scannedSoFar + localIndex;

                    var update = new RebuildUpdate
                    {
                        Kind = RebuildUpdateKind.UserProgress,
                        Stage = RebuildStage.RefreshingUserAchievements,
                        
                        // Provider details
                        // Use global counts for the text display so it doesn't look like it resets
                        // UserAppIndex is 0-based for the text formatter
                        UserAppIndex = globalIndex - 1,
                        UserAppCount = totalGames,
                        CurrentGameName = u.CurrentGameName,
                        
                        // Global details
                        OverallIndex = globalIndex,
                        OverallCount = totalGames
                    };

                    progressCallback?.Invoke(update);
                };

                cancel.ThrowIfCancellationRequested();
                var payload = await provider
                    .ScanAsync(games, wrappedCallback, data => OnGameScanned(provider, data, cancel), cancel)
                    .ConfigureAwait(false);

                scannedSoFar += games.Count;

                if (payload?.Summary == null)
                    continue;

                summary.GamesScanned += payload.Summary.GamesScanned;
                summary.GamesWithAchievements += payload.Summary.GamesWithAchievements;
                summary.GamesWithoutAchievements += payload.Summary.GamesWithoutAchievements;
            }

            return new RebuildPayload { Summary = summary };
        }

        private async Task OnGameScanned(IDataProvider provider, GameAchievementData data, CancellationToken cancel = default)
        {
            if (data?.PlayniteGameId == null) return;

            // Ensure provider metadata is persisted for diagnostics and future multi-provider caching.
            try
            {
                if (string.IsNullOrWhiteSpace(data.ProviderName))
                {
                    data.ProviderName = provider?.ProviderName;
                }
            }
            catch
            {
            }

            // Download achievement icons and update IconPath to local file paths
            if (data.Achievements != null)
            {
                var gameIdStr = data.PlayniteGameId?.ToString();
                foreach (var achievement in data.Achievements)
                {
                    if (string.IsNullOrWhiteSpace(achievement.IconPath))
                        continue;

                    bool isHttpUrl = achievement.IconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                    achievement.IconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                    if (!isHttpUrl)
                        continue;

                    // Skip async call if already cached
                    if (_diskImageService.IsIconCached(achievement.IconPath, 0, gameIdStr))
                    {
                        var cachedPath = _diskImageService.GetIconCachePathFromUri(achievement.IconPath, 0, gameIdStr);
                        if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
                        {
                            achievement.IconPath = cachedPath;
                            continue;
                        }
                    }

                    var localPath = await _diskImageService.GetOrDownloadIconAsync(
                        achievement.IconPath, 0, cancel, gameIdStr).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(localPath))
                    {
                        achievement.IconPath = localPath;
                    }
                }
            }

            var key = data.PlayniteGameId.Value.ToString();

            if (!string.IsNullOrWhiteSpace(key))
            {
                _cacheService.SaveGameData(key, data);
            }
        }

        // -----------------------------
        // Public scan methods
        // -----------------------------

        public Task StartManagedRebuildAsync()
        {
            return RunManagedAsync(
                cancel => ScanAsync(FullRefreshOptions(), HandleUpdate, cancel),
                payload => string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_RebuildComplete"),
                    payload?.Summary?.GamesScanned ?? 0),
                "Full achievement scan failed."
            );
        }

        public Task StartManagedSingleGameScanAsync(Guid playniteGameId)
        {
            return RunManagedAsync(
                cancel => ScanAsync(SingleGameOptions(playniteGameId), HandleUpdate, cancel),
                payload => ResourceProvider.GetString("LOCPlayAch_Status_SingleGameComplete"),
                "Single game scan failed."
            );
        }

        public Task StartManagedQuickRefreshAsync()
        {
            return RunManagedAsync(
                cancel => ScanAsync(QuickRefreshOptions(), HandleUpdate, cancel),
                payload => string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_QuickRefreshComplete"),
                    payload?.Summary?.GamesScanned ?? 0),
                "Quick refresh scan failed."
            );
        }

        public void CancelCurrentRebuild()
        {
            _logger.Info($"CancelCurrentRebuild requested.");
            lock (_runLock)
            {
                _activeRunCts?.Cancel();
            }
        }

        // -----------------------------
        // Get combined achievement data
        // -----------------------------

        /// <summary>
        /// Gets combined achievement data (schema + unlocked) for a single game.
        /// </summary>
        public GameAchievementData GetGameAchievementData(string playniteGameId)
        {
            if (string.IsNullOrWhiteSpace(playniteGameId))
                return null;

            try
            {
                return _cacheService.LoadGameData(playniteGameId);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get achievement data for gameId={playniteGameId}");
                return null;
            }
        }

        /// <summary>
        /// Gets combined achievement data (schema + unlocked) for a single game by Playnite Game ID.
        /// </summary>
        public GameAchievementData GetGameAchievementData(Guid playniteGameId)
        {
            return GetGameAchievementData(playniteGameId.ToString());
        }

        /// <summary>
        /// Gets combined achievement data for all cached games.
        /// </summary>
        public List<GameAchievementData> GetAllGameAchievementData()
        {
            try
            {
                var gameIds = _cacheService.GetCachedGameIds();
                var result = new List<GameAchievementData>();
                foreach(var gameId in gameIds)
                {
                    var gameData = _cacheService.LoadGameData(gameId);
                    if (gameData != null)
                    {
                        result.Add(gameData);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get all achievement data");
                return new();
            }
        }
    }
}

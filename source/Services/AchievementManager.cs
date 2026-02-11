using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Steam;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Images;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;

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
        private int _savedGamesInCurrentRun;
        private long _lastCacheInvalidationTimestamp = -1;
        private const long CacheInvalidationThrottleMs = 500;

        // Dependencies that need disposal
        private readonly IReadOnlyList<IDataProvider> _providers;
        private readonly RebuildProgressMapper _progressMapper;

        public ICacheManager Cache => _cacheService;

        /// <summary>
        /// Predefined scan modes available in the plugin.
        /// Generated automatically from ScanModeType enum.
        /// </summary>
        private static readonly ScanMode[] PredefinedScanModes = ((ScanModeType[])Enum.GetValues(typeof(ScanModeType)))
            .Select(m => new ScanMode(m, m.GetResourceKey(), m.GetShortResourceKey()))
            .ToArray();

        /// <summary>
        /// Checks if at least one provider has valid authentication credentials configured.
        /// </summary>
        public bool HasAnyAuthenticatedProvider() => _providers.Any(p => p.IsAuthenticated);

        /// <summary>
        /// Validates that a scan can proceed. Returns true if authenticated, otherwise shows dialog.
        /// Call this before showing any progress UI.
        /// </summary>
        public bool ValidateCanStartScan()
        {
            if (HasAnyAuthenticatedProvider())
            {
                return true;
            }

            _logger.Info("Scan attempted but no providers are authenticated.");
            _api.Dialogs.ShowMessage(
                ResourceProvider.GetString("LOCPlayAch_Error_NoAuthenticatedProviders"),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        /// <summary>
        /// Gets the list of available data providers.
        /// </summary>
        public IReadOnlyList<IDataProvider> GetProviders() => _providers;

        /// <summary>
        /// Gets the list of available scan modes with localized display names.
        /// </summary>
        public IReadOnlyList<ScanMode> GetScanModes()
        {
            foreach (var mode in PredefinedScanModes)
            {
                mode.DisplayName = ResourceProvider.GetString(mode.DisplayNameResourceKey) ?? mode.Key;
                mode.ShortDisplayName = ResourceProvider.GetString(mode.ShortDisplayNameResourceKey) ?? mode.Key;
            }
            return PredefinedScanModes;
        }

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
            if (!HasAnyAuthenticatedProvider())
            {
                _logger.Info("Scan requested but no providers are authenticated.");
                Report(ResourceProvider.GetString("LOCPlayAch_Error_NoAuthenticatedProviders"), 0, 1);
                return;
            }

            if (!TryBeginRun(out var cts))
                return;

            _progressMapper.Reset();
            Interlocked.Exchange(ref _savedGamesInCurrentRun, 0);
            Interlocked.Exchange(ref _lastCacheInvalidationTimestamp, -1);

            // Report immediately so UI updates buttons before any async work
            var startMsg = ResourceProvider.GetString("LOCPlayAch_Status_Starting");
            Report(startMsg, 0, 1);

            try
            {
                var payload = await runner(cts.Token).ConfigureAwait(false);

                var msg = finalMessage?.Invoke(payload) ?? ResourceProvider.GetString("LOCPlayAch_Status_Ready");
                Report(msg, 1, 1);
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
            }
            finally
            {
                var hasSavedGames = Interlocked.Exchange(ref _savedGamesInCurrentRun, 0) > 0;
                EndRun();

                if (hasSavedGames)
                {
                    NotifyCacheInvalidatedThrottled(force: true);
                }
            }
        }

        private sealed class ScanGameTarget
        {
            public Game Game { get; set; }
            public IDataProvider Provider { get; set; }
        }

        private IReadOnlyList<IDataProvider> GetAuthenticatedProviders()
        {
            return _providers
                .Where(p => p != null && p.IsAuthenticated)
                .ToList();
        }

        private IDataProvider ResolveProviderForGame(Game game, IReadOnlyList<IDataProvider> providers)
        {
            if (game == null || providers == null || providers.Count == 0)
            {
                return null;
            }

            foreach (var provider in providers)
            {
                try
                {
                    if (provider.IsCapable(game))
                    {
                        return provider;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Provider capability check failed for game '{game?.Name}'.");
                }
            }

            return null;
        }

        private List<ScanGameTarget> GetScanTargets(CacheScanOptions options, IReadOnlyList<IDataProvider> providers)
        {
            options ??= new CacheScanOptions();

            IEnumerable<Game> candidates;
            if (options.PlayniteGameIds?.Count > 0)
            {
                candidates = options.PlayniteGameIds
                    .Select(id => _api.Database.Games.Get(id))
                    .Where(g => g != null);
            }
            else
            {
                var allGames = _api.Database.Games.ToList();
                if (options.QuickRefreshMode)
                {
                    candidates = allGames
                        .Where(g => g != null && g.LastActivity != null)
                        .OrderByDescending(g => g.LastActivity);
                }
                else if (options.IgnoreUnplayedGames)
                {
                    candidates = allGames.Where(g => g != null && g.Playtime > 0);
                }
                else
                {
                    candidates = allGames.Where(g => g != null);
                }
            }

            var targets = new List<ScanGameTarget>();
            var seenGameIds = new HashSet<Guid>();
            var quickLimit = Math.Max(1, options.QuickRefreshRecentGamesCount);

            foreach (var game in candidates)
            {
                if (game == null || !seenGameIds.Add(game.Id))
                {
                    continue;
                }

                var provider = ResolveProviderForGame(game, providers);
                if (provider == null)
                {
                    continue;
                }

                targets.Add(new ScanGameTarget { Game = game, Provider = provider });

                if (options.QuickRefreshMode && targets.Count >= quickLimit)
                {
                    break;
                }
            }

            return targets;
        }

        private async Task<RebuildPayload> ScanAsync(
            CacheScanOptions options,
            Action<RebuildUpdate> progressCallback,
            CancellationToken cancel)
        {
            options ??= new CacheScanOptions();

            var authenticatedProviders = GetAuthenticatedProviders();
            if (authenticatedProviders.Count == 0)
            {
                _logger?.Warn("[Scan] No authenticated providers available.");
                return new RebuildPayload();
            }

            var scanTargets = GetScanTargets(options, authenticatedProviders);
            var gamesToScan = scanTargets.Select(x => x.Game).ToList();
            var gamesByProvider = scanTargets
                .GroupBy(x => x.Provider)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Game).ToList());

            // log games by providers to check, list all games and all providers
            _logger.Debug($"[Scan] Games to scan: {gamesToScan.Count}, Providers: {_providers.Count}, Grouped providers: {gamesByProvider.Count}");
            // _logger.Debug($"[Scan] Games with providers: {string.Join(", ", scanTargets.Select(x => x.Game.Name + " => " + x.Provider.ProviderName))}");

            if (gamesByProvider.Count == 0)
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

            await PopulateAchievementIconCacheAsync(data, cancel).ConfigureAwait(false);

            var key = data.PlayniteGameId.Value.ToString();

            if (!string.IsNullOrWhiteSpace(key))
            {
                _cacheService.SaveGameData(key, data);
                Interlocked.Increment(ref _savedGamesInCurrentRun);
                NotifyCacheInvalidatedThrottled(force: false);
            }
        }

        private void NotifyCacheInvalidatedThrottled(bool force)
        {
            var nowTimestamp = Stopwatch.GetTimestamp();
            if (!force)
            {
                while (true)
                {
                    var lastTimestamp = Interlocked.Read(ref _lastCacheInvalidationTimestamp);
                    if (lastTimestamp >= 0)
                    {
                        var elapsedMs = (nowTimestamp - lastTimestamp) * 1000L / Stopwatch.Frequency;
                        if (elapsedMs < CacheInvalidationThrottleMs)
                        {
                            return;
                        }
                    }

                    if (Interlocked.CompareExchange(ref _lastCacheInvalidationTimestamp, nowTimestamp, lastTimestamp) == lastTimestamp)
                    {
                        break;
                    }
                }
            }
            else
            {
                Interlocked.Exchange(ref _lastCacheInvalidationTimestamp, nowTimestamp);
            }

            _cacheService.NotifyCacheInvalidated();
        }

        private static bool IsHttpIconPath(string iconPath)
        {
            return !string.IsNullOrWhiteSpace(iconPath) &&
                   (iconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    iconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private async Task PopulateAchievementIconCacheAsync(GameAchievementData data, CancellationToken cancel)
        {
            if (data?.Achievements == null || data.Achievements.Count == 0)
            {
                return;
            }

            var gameIdStr = data.PlayniteGameId?.ToString();
            var groupedByIcon = new Dictionary<string, List<AchievementDetail>>(StringComparer.OrdinalIgnoreCase);

            foreach (var achievement in data.Achievements)
            {
                if (achievement == null || !IsHttpIconPath(achievement.IconPath))
                {
                    continue;
                }

                if (!groupedByIcon.TryGetValue(achievement.IconPath, out var grouped))
                {
                    grouped = new List<AchievementDetail>();
                    groupedByIcon[achievement.IconPath] = grouped;
                }

                grouped.Add(achievement);
            }

            if (groupedByIcon.Count == 0)
            {
                return;
            }

            var iconTasks = groupedByIcon.Keys
                .Select(iconPath => ResolveIconPathAsync(iconPath, gameIdStr, cancel))
                .ToArray();

            var resolvedIconPaths = await Task.WhenAll(iconTasks).ConfigureAwait(false);
            foreach (var resolved in resolvedIconPaths)
            {
                if (string.IsNullOrWhiteSpace(resolved.OriginalPath) ||
                    string.IsNullOrWhiteSpace(resolved.LocalPath))
                {
                    continue;
                }

                if (!groupedByIcon.TryGetValue(resolved.OriginalPath, out var grouped))
                {
                    continue;
                }

                foreach (var achievement in grouped)
                {
                    achievement.IconPath = resolved.LocalPath;
                }
            }
        }

        private async Task<(string OriginalPath, string LocalPath)> ResolveIconPathAsync(string originalPath, string gameIdStr, CancellationToken cancel)
        {
            if (!IsHttpIconPath(originalPath))
            {
                return default;
            }

            try
            {
                if (_diskImageService.IsIconCached(originalPath, 0, gameIdStr))
                {
                    var cachedPath = _diskImageService.GetIconCachePathFromUri(originalPath, 0, gameIdStr);
                    if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
                    {
                        return (originalPath, cachedPath);
                    }
                }

                var localPath = await _diskImageService
                    .GetOrDownloadIconAsync(originalPath, 0, cancel, gameIdStr)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    return (originalPath, localPath);
                }

                return default;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to resolve achievement icon path for {originalPath}.");
                return default;
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
                payload => ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
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

        public Task StartManagedScanAsync(List<Guid> gameIds)
        {
            return RunManagedAsync(
                cancel => ScanAsync(new CacheScanOptions { PlayniteGameIds = gameIds, IgnoreUnplayedGames = false }, HandleUpdate, cancel),
                payload => ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
                "Game list scan failed."
            );
        }

        /// <summary>
        /// Executes a scan based on the specified scan mode key.
        /// </summary>
        public Task ExecuteScanAsync(string modeKey, Guid? singleGameId = null)
        {
            // Parse string to enum, default to Quick if invalid
            if (!Enum.TryParse<ScanModeType>(modeKey, out var mode))
            {
                _logger.Warn($"Unknown scan mode: {modeKey}, falling back to Quick.");
                mode = ScanModeType.Quick;
            }

            return ExecuteScanAsync(mode, singleGameId);
        }

        /// <summary>
        /// Executes a scan based on the specified scan mode type.
        /// </summary>
        public Task ExecuteScanAsync(ScanModeType mode, Guid? singleGameId = null)
        {
            switch (mode)
            {
                case ScanModeType.Quick:
                    return StartManagedQuickRefreshAsync();

                case ScanModeType.Full:
                    return StartManagedRebuildAsync();

                case ScanModeType.Installed:
                    return StartManagedInstalledGamesScanAsync();

                case ScanModeType.Favorites:
                    return StartManagedFavoritesScanAsync();

                case ScanModeType.Single:
                    if (singleGameId.HasValue)
                        return StartManagedSingleGameScanAsync(singleGameId.Value);
                    _logger.Info("Single scan mode requested but no game ID provided.");
                    return Task.CompletedTask;

                case ScanModeType.LibrarySelected:
                    return StartManagedLibrarySelectedGamesScanAsync();

                case ScanModeType.Missing:
                    return StartManagedMissingScanAsync();

                default:
                    _logger.Warn($"Unknown scan mode: {mode}, falling back to Quick.");
                    return StartManagedQuickRefreshAsync();
            }
        }

        private Task StartManagedInstalledGamesScanAsync()
        {
            var gameIds = _api.Database.Games
                .Where(g => g != null && g.IsInstalled)
                .Select(g => g.Id)
                .ToList();

            if (gameIds.Count == 0)
            {
                _logger.Info("No installed games found for scan.");
                return Task.CompletedTask;
            }

            return RunManagedAsync(
                cancel => ScanAsync(new CacheScanOptions { PlayniteGameIds = gameIds, IgnoreUnplayedGames = false }, HandleUpdate, cancel),
                payload => string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
                    payload?.Summary?.GamesScanned ?? 0),
                "Installed games scan failed."
            );
        }

        private Task StartManagedFavoritesScanAsync()
        {
            var gameIds = _api.Database.Games
                .Where(g => g != null && g.Favorite)
                .Select(g => g.Id)
                .ToList();

            if (gameIds.Count == 0)
            {
                _logger.Info("No favorite games found for scan.");
                return Task.CompletedTask;
            }

            return RunManagedAsync(
                cancel => ScanAsync(new CacheScanOptions { PlayniteGameIds = gameIds, IgnoreUnplayedGames = false }, HandleUpdate, cancel),
                payload => string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
                    payload?.Summary?.GamesScanned ?? 0),
                "Favorites scan failed."
            );
        }

        private Task StartManagedLibrarySelectedGamesScanAsync()
        {
            var selectedGames = _api.MainView.SelectedGames?
                .Where(g => g != null)
                .ToList();

            if (selectedGames == null || selectedGames.Count == 0)
            {
                _logger.Info("No games selected in Playnite library for scan.");
                return Task.CompletedTask;
            }

            var gameIds = selectedGames.Select(g => g.Id).ToList();
            return RunManagedAsync(
                cancel => ScanAsync(new CacheScanOptions { PlayniteGameIds = gameIds, IgnoreUnplayedGames = false }, HandleUpdate, cancel),
                payload => string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
                    payload?.Summary?.GamesScanned ?? 0),
                "Library selected games scan failed."
            );
        }

        private Task StartManagedMissingScanAsync()
        {
            var authenticatedProviders = GetAuthenticatedProviders();
            if (authenticatedProviders.Count == 0)
            {
                _logger.Info("No authenticated providers available for missing scan.");
                return Task.CompletedTask;
            }

            var cachedGameIds = new HashSet<string>(_cacheService.GetCachedGameIds(), StringComparer.OrdinalIgnoreCase);
            var allGames = _api.Database.Games.ToList();

            var missingGameIds = new List<Guid>();
            foreach (var game in allGames)
            {
                if (game == null)
                {
                    continue;
                }

                var provider = ResolveProviderForGame(game, authenticatedProviders);
                if (provider == null)
                {
                    continue;
                }

                if (!cachedGameIds.Contains(game.Id.ToString()))
                {
                    missingGameIds.Add(game.Id);
                }
            }

            if (missingGameIds.Count == 0)
            {
                _logger.Info("No games missing achievement data found.");
                return Task.CompletedTask;
            }

            _logger.Info($"Found {missingGameIds.Count} games missing achievement data.");

            return RunManagedAsync(
                cancel => ScanAsync(new CacheScanOptions { PlayniteGameIds = missingGameIds, IgnoreUnplayedGames = false }, HandleUpdate, cancel),
                payload => string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
                    payload?.Summary?.GamesScanned ?? 0),
                "Missing games scan failed."
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

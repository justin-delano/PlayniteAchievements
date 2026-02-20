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
    /// Manages user achievement refreshing and caching operations.
    /// </summary>
    public class AchievementManager : IDisposable
    {
        private readonly object _runLock = new object();
        private readonly object _pointsColumnVisibilityLock = new object();
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
        private readonly ProviderRegistry _providerRegistry;
        private int _savedGamesInCurrentRun;
        private long _lastCacheInvalidationTimestamp = -1;
        private const long CacheInvalidationThrottleMs = 500;
        private const string PointsColumnKey = "Points";
        private const string EpicProviderKey = "Epic";

        // Dependencies that need disposal
        private readonly IReadOnlyList<IDataProvider> _providers;
        private readonly RebuildProgressMapper _progressMapper;

        public ICacheManager Cache => _cacheService;

        /// <summary>
        /// Gets the provider registry for checking/modifying provider enabled state.
        /// </summary>
        public ProviderRegistry ProviderRegistry => _providerRegistry;

        /// <summary>
        /// Predefined refresh modes available in the plugin.
        /// Generated automatically from RefreshModeType enum.
        /// </summary>
        private static readonly RefreshMode[] PredefinedRefreshModes = ((RefreshModeType[])Enum.GetValues(typeof(RefreshModeType)))
            .Select(m => new RefreshMode(m, m.GetResourceKey(), m.GetShortResourceKey()))
            .ToArray();

        /// <summary>
        /// Checks if at least one provider is enabled and has valid authentication credentials configured.
        /// </summary>
        public bool HasAnyAuthenticatedProvider() => _providers.Any(p =>
            _providerRegistry.IsProviderEnabled(p.ProviderKey) && p.IsAuthenticated);

        /// <summary>
        /// Validates that a refresh can proceed. Returns true if authenticated, otherwise shows dialog.
        /// Call this before showing any progress UI.
        /// </summary>
        public bool ValidateCanStartRefresh()
        {
            if (HasAnyAuthenticatedProvider())
            {
                return true;
            }

            _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshAttemptedNoProviders"));
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
        /// Gets the list of available refresh modes with localized display names.
        /// </summary>
        public IReadOnlyList<RefreshMode> GetRefreshModes()
        {
            foreach (var mode in PredefinedRefreshModes)
            {
                mode.DisplayName = ResourceProvider.GetString(mode.DisplayNameResourceKey) ?? mode.Key;
                mode.ShortDisplayName = ResourceProvider.GetString(mode.ShortDisplayNameResourceKey) ?? mode.Key;
            }
            return PredefinedRefreshModes;
        }

        public event EventHandler<GameCacheUpdatedEventArgs> GameCacheUpdated
        {
            add => _cacheService.GameCacheUpdated += value;
            remove => _cacheService.GameCacheUpdated -= value;
        }

        public event EventHandler<CacheDeltaEventArgs> CacheDeltaUpdated
        {
            add => _cacheService.CacheDeltaUpdated += value;
            remove => _cacheService.CacheDeltaUpdated -= value;
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
            DiskImageService diskImageService,
            ProviderRegistry providerRegistry)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            if (providers == null) throw new ArgumentNullException(nameof(providers));
            _diskImageService = diskImageService ?? throw new ArgumentNullException(nameof(diskImageService));
            _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));

            _cacheService = new CacheManager(api, logger, _plugin);
            _providers = providers.ToList();
            _progressMapper = new RebuildProgressMapper();

            _ = Task.Run(() =>
            {
                try
                {
                    using (PerfScope.Start(_logger, "AchievementManager.InitializePointsColumnVisibilityDefaults.Async", thresholdMs: 25))
                    {
                        InitializePointsColumnVisibilityDefaults();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "Failed to initialize Points-column visibility defaults asynchronously.");
                }
            });
        }

        public void Dispose()
        {
            try
            {
                if (_cacheService is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
            }

            // SettingsPersister removed - user settings are now only saved via ISettings.EndEdit()
        }

        // -----------------------------
        // UI helpers
        // -----------------------------

        private void PostToUi(Action action)
        {
            var dispatcher = _api?.MainView?.UIDispatcher;
            dispatcher.InvokeIfNeeded(action, DispatcherPriority.Background);
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
                foreach (EventHandler<ProgressReport> subscriber in handler.GetInvocationList())
                {
                    try
                    {
                        subscriber(this, report);
                    }
                    catch (Exception e)
                    {
                        _logger?.Error(e, ResourceProvider.GetString("LOCPlayAch_Error_NotifySubscribers"));
                    }
                }
            });
        }

        /// <summary>
        /// Calculates percentage for a refresh progress report.
        /// </summary>
        public double CalculateProgressPercent(ProgressReport report)
        {
            if (report == null)
            {
                return 0;
            }

            var pct = report.PercentComplete;
            if ((pct <= 0 || double.IsNaN(pct)) && report.TotalSteps > 0)
            {
                pct = Math.Max(0, Math.Min(100, (report.CurrentStep * 100.0) / report.TotalSteps));
            }

            if (double.IsNaN(pct))
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, pct));
        }

        /// <summary>
        /// Determines if the provided report represents a final refresh state.
        /// </summary>
        public bool IsFinalProgressReport(ProgressReport report)
        {
            return IsFinalProgressReport(report, CalculateProgressPercent(report));
        }

        /// <summary>
        /// Resolves the user-facing refresh status message from report + manager state.
        /// </summary>
        public string ResolveProgressMessage(ProgressReport report = null)
        {
            var effectiveReport = report ?? _lastProgress;
            var isFinal = IsFinalProgressReport(effectiveReport);
            return ResolveProgressMessage(effectiveReport, isFinal);
        }

        /// <summary>
        /// Gets a centralized refresh status snapshot for UI consumers.
        /// </summary>
        public RefreshStatusSnapshot GetRefreshStatusSnapshot(ProgressReport report = null)
        {
            var effectiveReport = report ?? _lastProgress;
            var progressPercent = CalculateProgressPercent(effectiveReport);
            var isFinal = IsFinalProgressReport(effectiveReport, progressPercent);

            return new RefreshStatusSnapshot
            {
                IsRefreshing = IsRebuilding,
                IsFinal = isFinal,
                IsCanceled = effectiveReport?.IsCanceled == true,
                ProgressPercent = progressPercent,
                Message = ResolveProgressMessage(effectiveReport, isFinal)
            };
        }

        /// <summary>
        /// Gets a transient "starting refresh" snapshot for immediate UI updates.
        /// </summary>
        public RefreshStatusSnapshot GetStartingRefreshStatusSnapshot()
        {
            return new RefreshStatusSnapshot
            {
                IsRefreshing = IsRebuilding,
                IsFinal = false,
                IsCanceled = false,
                ProgressPercent = 0,
                Message = ResourceProvider.GetString("LOCPlayAch_Status_Starting")
            };
        }

        private bool IsFinalProgressReport(ProgressReport report, double progressPercent)
        {
            if (report == null)
            {
                return false;
            }

            return report.IsCanceled ||
                   (report.TotalSteps > 0 && report.CurrentStep >= report.TotalSteps) ||
                   progressPercent >= 100;
        }

        private string ResolveProgressMessage(ProgressReport report, bool isFinal)
        {
            if (!string.IsNullOrWhiteSpace(report?.Message))
            {
                return report.Message;
            }

            if (report?.IsCanceled == true)
            {
                return ResourceProvider.GetString("LOCPlayAch_Status_Canceled");
            }

            if (isFinal)
            {
                return ResourceProvider.GetString("LOCPlayAch_Status_RefreshComplete");
            }

            if (!string.IsNullOrWhiteSpace(_lastStatus))
            {
                return _lastStatus;
            }

            return ResourceProvider.GetString("LOCPlayAch_Status_Starting");
        }

        // -----------------------------
        // Refresh option builders
        // -----------------------------

        private CacheRefreshOptions FullRefreshOptions()
        {
            return new CacheRefreshOptions
            {
                QuickRefreshMode = false,
                IncludeUnplayedGames = _settings.Persisted.IncludeUnplayedGames
            };
        }

        private CacheRefreshOptions SingleGameOptions(Guid playniteGameId)
        {
            return new CacheRefreshOptions
            {
                PlayniteGameIds = new[] { playniteGameId },
                IncludeUnplayedGames = true
            };
        }

        private CacheRefreshOptions QuickRefreshOptions()
        {
            return new CacheRefreshOptions
            {
                QuickRefreshMode = true,
                QuickRefreshRecentGamesCount = _settings?.Persisted?.QuickRefreshRecentGamesCount ?? 10,
                IncludeUnplayedGames = _settings.Persisted.IncludeUnplayedGames
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
        // Managed refresh runner
        // -----------------------------

        private bool TryBeginRun(out CancellationTokenSource cts)
        {
            lock (_runLock)
            {
                if (_activeRunCts != null)
                {
                    _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshAlreadyInProgress"));
                    cts = null;
                    Report(_lastStatus ?? ResourceProvider.GetString("LOCPlayAch_Status_UpdatingCache"), 0, 1);
                    return false;
                }

                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshStarting"));
                _activeRunCts = new CancellationTokenSource();
                cts = _activeRunCts;
                return true;
            }
        }

        private void EndRun()
        {
            _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshEndRun"));
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
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshRequestedNoProviders"));
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

            RebuildPayload payload = null;
            try
            {
                // Run refresh setup/execution on background thread so UI commands are never blocked
                // by synchronous pre-refresh work (game filtering, capability checks, etc.).
                payload = await Task.Run(
                    async () => await runner(cts.Token).ConfigureAwait(false),
                    cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshCanceled"));
                Report(ResourceProvider.GetString("LOCPlayAch_Status_Canceled"), 0, 1, true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, errorLogMessage);
                Report(ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed"), 0, 1);
            }
            finally
            {
                var hasSavedGames = Interlocked.Exchange(ref _savedGamesInCurrentRun, 0) > 0;
                var wasCanceled = cts.IsCancellationRequested;
                EndRun();

                // Send final completion report AFTER EndRun so IsRebuilding is false when UI processes it
                if (!wasCanceled && payload != null)
                {
                    var msg = ResolveFinalSuccessMessage(payload, finalMessage);
                    Report(msg, 1, 1);
                }

                if (hasSavedGames)
                {
                    NotifyCacheInvalidatedThrottled(force: true);
                }
            }
        }

        private sealed class RefreshGameTarget
        {
            public Game Game { get; set; }
            public IDataProvider Provider { get; set; }
        }

        private IReadOnlyList<IDataProvider> GetAuthenticatedProviders()
        {
            return _providers
                .Where(p => p != null &&
                    _providerRegistry.IsProviderEnabled(p.ProviderKey) &&
                    p.IsAuthenticated)
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
                    _logger?.Debug(ex, string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshProviderCapabilityCheckFailed"),
                        game?.Name));
                }
            }

            return null;
        }

        private List<RefreshGameTarget> GetrefreshTargets(CacheRefreshOptions options, IReadOnlyList<IDataProvider> providers)
        {
            options ??= new CacheRefreshOptions();

            // Pre-load NoAchievements games for efficient skipping (only for bulk refreshes, not single-game)
            HashSet<string> noAchievementsGameIds = null;
            if (options.SkipNoAchievementsGames && (options.PlayniteGameIds == null || options.PlayniteGameIds.Count == 0))
            {
                noAchievementsGameIds = _cacheService.GetNoAchievementsGameIds();
            }

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
                else if (!options.IncludeUnplayedGames)
                {
                    candidates = allGames.Where(g => g != null && g.Playtime > 0);
                }
                else
                {
                    candidates = allGames.Where(g => g != null);
                }
            }

            var targets = new List<RefreshGameTarget>();
            var seenGameIds = new HashSet<Guid>();
            var quickLimit = Math.Max(1, options.QuickRefreshRecentGamesCount);
            var skippedNoProvider = 0;
            var skippedNoAchievements = 0;

            foreach (var game in candidates)
            {
                if (game == null || !seenGameIds.Add(game.Id))
                {
                    continue;
                }

                // Skip games already marked as having no achievements
                if (noAchievementsGameIds != null &&
                    noAchievementsGameIds.Contains(game.Id.ToString()))
                {
                    skippedNoAchievements++;
                    continue;
                }

                var provider = ResolveProviderForGame(game, providers);
                if (provider == null)
                {
                    skippedNoProvider++;
                    continue;
                }

                targets.Add(new RefreshGameTarget { Game = game, Provider = provider });

                if (options.QuickRefreshMode && targets.Count >= quickLimit)
                {
                    break;
                }
            }

            if (skippedNoProvider > 0)
            {
                _logger?.Debug($"Skipped {skippedNoProvider} games without a capable provider.");
            }

            if (skippedNoAchievements > 0)
            {
                _logger?.Debug($"Skipped {skippedNoAchievements} games with NoAchievements flag.");
            }

            return targets;
        }

        private async Task<RebuildPayload> RefreshAsync(
            CacheRefreshOptions options,
            Action<RebuildUpdate> progressCallback,
            CancellationToken cancel)
        {
            options ??= new CacheRefreshOptions();

            var authenticatedProviders = GetAuthenticatedProviders();
            if (authenticatedProviders.Count == 0)
            {
                _logger?.Warn(ResourceProvider.GetString("LOCPlayAch_Log_RefreshNoAuthenticatedProviders"));
                return new RebuildPayload();
            }

            var refreshTargets = GetrefreshTargets(options, authenticatedProviders);
            var gamesToRefresh = refreshTargets.Select(x => x.Game).ToList();
            var gamesByProvider = refreshTargets
                .GroupBy(x => x.Provider)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Game).ToList());

            // log games by providers to check, list all games and all providers
            _logger.Debug(string.Format(
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshSummary"),
                gamesToRefresh.Count,
                _providers.Count,
                gamesByProvider.Count));
            // _logger.Debug($"[Refresh] Games with providers: {string.Join(", ", refreshTargets.Select(x => x.Game.Name + " => " + x.Provider.ProviderName))}");

            if (gamesByProvider.Count == 0)
            {
                _logger?.Warn(ResourceProvider.GetString("LOCPlayAch_Log_RefreshNoMatchingProviders"));
                return new RebuildPayload();
            }

            var totalGames = gamesToRefresh.Count;
            var summary = new RebuildSummary();
            var refreshedSoFar = 0;

            foreach (var kvp in gamesByProvider)
            {
                var provider = kvp.Key;
                var games = kvp.Value;

                // Create a localized callback for this provider
                Action<ProviderRefreshUpdate> wrappedCallback = (u) =>
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
                    var globalIndex = refreshedSoFar + localIndex;

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
                    .RefreshAsync(games, wrappedCallback, data => OnGameRefreshed(provider, data, cancel), cancel)
                    .ConfigureAwait(false);

                refreshedSoFar += games.Count;

                if (payload?.Summary == null)
                    continue;

                summary.GamesRefreshed += payload.Summary.GamesRefreshed;
                summary.GamesWithAchievements += payload.Summary.GamesWithAchievements;
                summary.GamesWithoutAchievements += payload.Summary.GamesWithoutAchievements;
            }

            return new RebuildPayload { Summary = summary };
        }

        private async Task OnGameRefreshed(IDataProvider provider, GameAchievementData data, CancellationToken cancel = default)
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

            TryAutoEnablePointsColumnForEpic(provider, data);

            await PopulateAchievementIconCacheAsync(data, cancel).ConfigureAwait(false);

            var key = data.PlayniteGameId.Value.ToString();

            if (!string.IsNullOrWhiteSpace(key))
            {
                var writeResult = _cacheService.SaveGameData(key, data);
                if (writeResult == null || !writeResult.Success)
                {
                    var errorCode = writeResult?.ErrorCode ?? "unknown";
                    var errorMessage = writeResult?.ErrorMessage ?? "Unknown cache persistence failure.";

                    throw new CachePersistenceException(
                        key,
                        provider?.ProviderName ?? data.ProviderName,
                        errorCode,
                        $"Persisting refreshed game data failed. key={key}, provider={provider?.ProviderName ?? data.ProviderName}, code={errorCode}, message={errorMessage}",
                        writeResult?.Exception);
                }

                Interlocked.Increment(ref _savedGamesInCurrentRun);
                NotifyCacheInvalidatedThrottled(force: false);
            }
        }

        private void InitializePointsColumnVisibilityDefaults()
        {
            if (_settings?.Persisted == null)
            {
                return;
            }

            var hasEpicAchievements = HasCachedEpicAchievements();
            var changed = false;
            lock (_pointsColumnVisibilityLock)
            {
                var map = _settings.Persisted.DataGridColumnVisibility;
                if (map == null)
                {
                    map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    _settings.Persisted.DataGridColumnVisibility = map;
                }

                if (!map.TryGetValue(PointsColumnKey, out var pointsVisible))
                {
                    map[PointsColumnKey] = hasEpicAchievements;
                    changed = true;
                }
                else if (pointsVisible && !_settings.Persisted.PointsColumnAutoEnabled)
                {
                    _settings.Persisted.PointsColumnAutoEnabled = true;
                    changed = true;
                }

                if (hasEpicAchievements && !_settings.Persisted.PointsColumnAutoEnabled)
                {
                    _settings.Persisted.PointsColumnAutoEnabled = true;
                    changed = true;
                }
            }

            if (changed)
            {
                TryPersistSettings(notifySettingsSaved: false);
            }
        }

        private void TryAutoEnablePointsColumnForEpic(IDataProvider provider, GameAchievementData data)
        {
            if (!IsEpicProvider(provider, data) || data?.Achievements == null || data.Achievements.Count == 0)
            {
                return;
            }

            var changed = false;
            lock (_pointsColumnVisibilityLock)
            {
                if (_settings?.Persisted == null || _settings.Persisted.PointsColumnAutoEnabled)
                {
                    return;
                }

                var map = _settings.Persisted.DataGridColumnVisibility;
                if (map == null)
                {
                    map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    _settings.Persisted.DataGridColumnVisibility = map;
                }

                _settings.Persisted.PointsColumnAutoEnabled = true;
                changed = true;

                if (!map.TryGetValue(PointsColumnKey, out var pointsVisible) || !pointsVisible)
                {
                    map[PointsColumnKey] = true;
                    changed = true;
                }
            }

            if (changed)
            {
                TryPersistSettings(notifySettingsSaved: true);
            }
        }

        private bool HasCachedEpicAchievements()
        {
            try
            {
                var allGameData = GetAllGameAchievementData();
                if (allGameData == null || allGameData.Count == 0)
                {
                    return false;
                }

                return allGameData.Any(data =>
                    data?.Achievements != null &&
                    data.Achievements.Count > 0 &&
                    IsEpicProvider(provider: null, data));
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed while checking cached Epic achievement data for Points-column defaults.");
                return false;
            }
        }

        private static bool IsEpicProvider(IDataProvider provider, GameAchievementData data)
        {
            if (provider != null &&
                string.Equals(provider.ProviderKey, EpicProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsEpicToken(data?.ProviderName) || ContainsEpicToken(data?.LibrarySourceName);
        }

        private static bool ContainsEpicToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("epic", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryPersistSettings(bool notifySettingsSaved)
        {
            try
            {
                _plugin?.SavePluginSettings(_settings);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to persist Points-column visibility defaults.");
            }

            if (notifySettingsSaved)
            {
                PostToUi(() => PlayniteAchievementsPlugin.NotifySettingsSaved());
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
                if (achievement == null || !IsHttpIconPath(achievement.UnlockedIconPath))
                {
                    continue;
                }

                if (!groupedByIcon.TryGetValue(achievement.UnlockedIconPath, out var grouped))
                {
                    grouped = new List<AchievementDetail>();
                    groupedByIcon[achievement.UnlockedIconPath] = grouped;
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
                    achievement.UnlockedIconPath = resolved.LocalPath;
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
                _logger?.Debug(ex, string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Log_RefreshResolveIconPathFailed"),
                    originalPath));
                return default;
            }
        }

        private string ResolveFinalSuccessMessage(RebuildPayload payload, Func<RebuildPayload, string> finalMessage)
        {
            if (finalMessage != null)
            {
                try
                {
                    var message = finalMessage(payload);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return message;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed"));
                }
            }

            return ResourceProvider.GetString("LOCPlayAch_Status_RefreshComplete");
        }

        // -----------------------------
        // Public refresh methods
        // -----------------------------

        private Task StartManagedRefreshCoreAsync(
            CacheRefreshOptions options,
            Func<RebuildPayload, string> finalMessage,
            string errorLogMessage)
        {
            return RunManagedAsync(
                cancel => RefreshAsync(options, HandleUpdate, cancel),
                finalMessage,
                errorLogMessage
            );
        }

        private Task StartManagedGameIdRefreshAsync(
            RefreshModeType mode,
            List<Guid> gameIds,
            Func<RebuildPayload, string> finalMessage,
            string errorLogMessage,
            string emptySelectionLogMessage = null)
        {
            if (gameIds == null || gameIds.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(emptySelectionLogMessage))
                {
                    _logger.Info(emptySelectionLogMessage);
                }

                Report(FormatRefreshCompletionWithModeAndCount(mode, 0), 1, 1);
                return Task.CompletedTask;
            }

            return StartManagedRefreshCoreAsync(
                new CacheRefreshOptions { PlayniteGameIds = gameIds, IncludeUnplayedGames = true },
                finalMessage,
                errorLogMessage
            );
        }

        private static string GetRefreshModeShortName(RefreshModeType mode)
        {
            var resourceKey = mode == RefreshModeType.LibrarySelected
                ? "LOCPlayAch_RefreshModeShort_Selected"
                : mode.GetShortResourceKey();

            return ResourceProvider.GetString(resourceKey);
        }

        private static string FormatRefreshCompletionWithModeAndCount(RefreshModeType mode, int GamesRefreshed)
        {
            return string.Format(
                ResourceProvider.GetString("LOCPlayAch_Status_RefreshCompleteWithModeAndCount"),
                GetRefreshModeShortName(mode),
                Math.Max(0, GamesRefreshed));
        }

        private List<Guid> GetInstalledGameIds()
        {
            return _api.Database.Games
                .Where(g => g != null && g.IsInstalled)
                .Select(g => g.Id)
                .ToList();
        }

        private List<Guid> GetFavoriteGameIds()
        {
            return _api.Database.Games
                .Where(g => g != null && g.Favorite)
                .Select(g => g.Id)
                .ToList();
        }

        private List<Guid> GetLibrarySelectedGameIds()
        {
            return _api.MainView.SelectedGames?
                .Where(g => g != null)
                .Select(g => g.Id)
                .ToList();
        }

        private List<Guid> GetMissingGameIds()
        {
            var authenticatedProviders = GetAuthenticatedProviders();
            if (authenticatedProviders.Count == 0)
            {
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshMissingNoAuthenticatedProviders"));
                return new List<Guid>();
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
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshMissingNoGames"));
                return missingGameIds;
            }

            _logger.Info(string.Format(
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshMissingFoundGames"),
                missingGameIds.Count));
            return missingGameIds;
        }

        private async Task StartManagedMissingRefreshAsync()
        {
            var missingGameIds = await Task.Run(GetMissingGameIds).ConfigureAwait(false);
            await StartManagedGameIdRefreshAsync(
                RefreshModeType.Missing,
                missingGameIds,
                payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.Missing, payload?.Summary?.GamesRefreshed ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshMissingFailed"))
                .ConfigureAwait(false);
        }

        private Task StartManagedRebuildAsync()
        {
            return StartManagedRefreshCoreAsync(
                FullRefreshOptions(),
                payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.Full, payload?.Summary?.GamesRefreshed ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshFullFailed")
            );
        }

        private Task StartManagedSingleGameRefreshAsync(Guid playniteGameId)
        {
            return StartManagedRefreshCoreAsync(
                SingleGameOptions(playniteGameId),
                payload => ResourceProvider.GetString("LOCPlayAch_Status_RefreshComplete"),
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshSingleFailed")
            );
        }

        private Task StartManagedQuickRefreshAsync()
        {
            return StartManagedRefreshCoreAsync(
                QuickRefreshOptions(),
                payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.Quick, payload?.Summary?.GamesRefreshed ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshQuickFailed")
            );
        }

        /// <summary>
        /// Executes a refresh based on the specified refresh mode key.
        /// </summary>
        public Task ExecuteRefreshAsync(string modeKey, Guid? singleGameId = null)
        {
            // Parse string to enum, default to Quick if invalid
            if (!Enum.TryParse<RefreshModeType>(modeKey, out var mode))
            {
                _logger.Warn(string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Log_RefreshUnknownModeKey"),
                    modeKey));
                mode = RefreshModeType.Quick;
            }

            return ExecuteRefreshAsync(mode, singleGameId);
        }

        public Task ExecuteRefreshForGamesAsync(IEnumerable<Guid> gameIds)
        {
            var ids = gameIds?
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList() ?? new List<Guid>();

            return StartManagedGameIdRefreshAsync(
                RefreshModeType.LibrarySelected,
                ids,
                payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.LibrarySelected, payload?.Summary?.GamesRefreshed ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshSelectedFailed"),
                ResourceProvider.GetString("LOCPlayAch_Log_RefreshNoSelectedGames"));
        }

        /// <summary>
        /// Executes a refresh based on the specified refresh mode type.
        /// </summary>
        public Task ExecuteRefreshAsync(RefreshModeType mode, Guid? singleGameId = null)
        {
            switch (mode)
            {
                case RefreshModeType.Quick:
                    return StartManagedQuickRefreshAsync();

                case RefreshModeType.Full:
                    return StartManagedRebuildAsync();

                case RefreshModeType.Installed:
                    return StartManagedGameIdRefreshAsync(
                        RefreshModeType.Installed,
                        GetInstalledGameIds(),
                        payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.Installed, payload?.Summary?.GamesRefreshed ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshInstalledFailed"),
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshNoInstalledGames"));

                case RefreshModeType.Favorites:
                    return StartManagedGameIdRefreshAsync(
                        RefreshModeType.Favorites,
                        GetFavoriteGameIds(),
                        payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.Favorites, payload?.Summary?.GamesRefreshed ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshFavoritesFailed"),
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshNoFavoriteGames"));

                case RefreshModeType.Single:
                    if (singleGameId.HasValue)
                        return StartManagedSingleGameRefreshAsync(singleGameId.Value);
                    _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshSingleNoGameId"));
                    return Task.CompletedTask;

                case RefreshModeType.LibrarySelected:
                    return StartManagedGameIdRefreshAsync(
                        RefreshModeType.LibrarySelected,
                        GetLibrarySelectedGameIds(),
                        payload => FormatRefreshCompletionWithModeAndCount(RefreshModeType.LibrarySelected, payload?.Summary?.GamesRefreshed ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshSelectedFailed"),
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshNoSelectedGames"));

                case RefreshModeType.Missing:
                    return StartManagedMissingRefreshAsync();

                default:
                    _logger.Warn(string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Log_RefreshUnknownModeEnum"),
                        mode));
                    return StartManagedQuickRefreshAsync();
            }
        }

        public void CancelCurrentRebuild()
        {
            _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_RefreshCancelRequested"));
            lock (_runLock)
            {
                _activeRunCts?.Cancel();
            }
        }

        public void RemoveGameCache(Guid playniteGameId)
        {
            if (playniteGameId == Guid.Empty)
            {
                return;
            }

            try
            {
                _cacheService.RemoveGameData(playniteGameId);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to remove achievement cache for game '{playniteGameId}'.");
            }

            try
            {
                _diskImageService.ClearGameCache(playniteGameId.ToString());
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to remove icon cache for game '{playniteGameId}'.");
            }

            NotifyCacheInvalidatedThrottled(force: true);
        }

        public CacheWriteResult SetCapstone(Guid playniteGameId, string capstoneApiName)
        {
            if (playniteGameId == Guid.Empty)
            {
                return CacheWriteResult.CreateFailure(
                    string.Empty,
                    "invalid_game_id",
                    ResourceProvider.GetString("LOCPlayAch_Capstone_Error_InvalidGame"));
            }

            try
            {
                var result = _cacheService.SetCapstone(playniteGameId, capstoneApiName);
                if (result.Success)
                {
                    NotifyCacheInvalidatedThrottled(force: true);
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed setting capstone for gameId={playniteGameId}.");
                return CacheWriteResult.CreateFailure(
                    playniteGameId.ToString(),
                    "sql_write_failed",
                    ex.Message,
                    ex);
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
                _logger?.Error(ex, string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Log_RefreshGetGameDataFailed"),
                    playniteGameId));
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
                if (_cacheService is CacheManager optimizedCacheManager)
                {
                    return optimizedCacheManager.LoadAllGameDataFast() ?? new List<GameAchievementData>();
                }

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
                _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Log_RefreshGetAllGameDataFailed"));
                return new();
            }
        }

    }
}

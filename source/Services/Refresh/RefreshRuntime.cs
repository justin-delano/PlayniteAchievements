using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers.Steam.Models;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.ProgressReporting;
using PlayniteAchievements.Services.Friends;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;

namespace PlayniteAchievements.Services
{
    /// <summary>
    /// Manages user achievement refreshing and caching operations.
    /// </summary>
    public partial class RefreshRuntime : IDisposable
    {
        public event EventHandler<ProgressReport> RebuildProgress;

        /// <summary>
        /// Gets the list of game IDs that were refreshed in the most recent refresh operation.
        /// </summary>
        public List<Guid> LastRefreshedGameIds { get; private set; } = new List<Guid>();

        /// <summary>
        /// Gets the provider keys that failed authentication in the most recent refresh.
        /// </summary>
        public List<string> GetLastFailedAuthProviderKeys() => new List<string>(_lastFailedAuthProviderKeys);

        /// <summary>
        /// Raised after each individual game is refreshed and cached.
        /// Argument is the game ID that was refreshed.
        /// </summary>
        public event Action<Guid> GameRefreshed;

        public ProgressReport GetLastRebuildProgress() => _progressReportingService.GetLastProgress();
        public string GetLastRebuildStatus() => _progressReportingService.GetLastStatus();

        public bool IsRebuilding
        {
            get { return _refreshStateManager.IsRebuilding; }
        }

        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly ICacheManager _cacheService;
        private readonly DiskImageService _diskImageService;
        private readonly AchievementIconService _achievementIconService;
        private readonly ProgressReportingService _progressReportingService;
        private readonly RefreshStateManager _refreshStateManager;
        private readonly TargetSelectionResolver _targetSelectionResolver;
        private readonly RefreshRequestPlanner _refreshRequestPlanner;
        private readonly RefreshProgressReporter _refreshProgressReporter;
        private readonly PlayniteAchievements.Providers.ProviderRegistry _providerRegistry;
        private readonly Action<RebuildPayload> _onRefreshCompleted;
        private int _savedGamesInCurrentRun;
        private volatile List<string> _lastFailedAuthProviderKeys = new List<string>();

        // Dependencies that need disposal
        private readonly IReadOnlyList<IDataProvider> _providers;

        public ICacheManager Cache => _cacheService;

        /// <summary>
        /// Gets the provider registry for checking/modifying provider enabled state.
        /// </summary>
        public PlayniteAchievements.Providers.ProviderRegistry ProviderRegistry => _providerRegistry;

        internal virtual async Task<IReadOnlyList<IDataProvider>> GetAuthenticatedProvidersOrShowDialogAsync(CancellationToken ct = default)
        {
            var authenticatedProviders = await GetAuthenticatedProvidersAsync(ct).ConfigureAwait(false);
            if (authenticatedProviders.Count > 0)
            {
                return authenticatedProviders;
            }

            _logger.Info("Refresh attempted but no platforms are authenticated.");
            PostToUi(() => _api.Dialogs.ShowMessage(
                ResourceProvider.GetString("LOCPlayAch_Error_NoAuthenticatedProviders"),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning));
            return Array.Empty<IDataProvider>();
        }

        /// <summary>
        /// Gets the list of available data providers.
        /// </summary>
        public IReadOnlyList<IDataProvider> Providers => _providers;

        /// <summary>
        /// Gets the list of available refresh modes with localized display names.
        /// </summary>
        public IReadOnlyList<RefreshMode> GetRefreshModes()
        {
            return ((RefreshModeType[])Enum.GetValues(typeof(RefreshModeType)))
                .Where(modeType => !modeType.IsFriendRefreshMode())
                .Select(modeType =>
            {
                var mode = new RefreshMode(modeType, modeType.GetResourceKey(), modeType.GetShortResourceKey())
                {
                    DisplayName = ResourceProvider.GetString(modeType.GetResourceKey()) ?? modeType.GetKey(),
                    ShortDisplayName = ResourceProvider.GetString(modeType.GetShortResourceKey()) ?? modeType.GetKey()
                };

                return mode;
            })
            .ToList();
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

        public RefreshRuntime(
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            PlayniteAchievementsPlugin plugin,
            IEnumerable<IDataProvider> providers,
            DiskImageService diskImageService,
            ManagedCustomIconService managedCustomIconService,
            PlayniteAchievements.Providers.ProviderRegistry providerRegistry,
            IEnumerable<string> refreshOrder,
            Action<RebuildPayload> onRefreshCompleted = null)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            if (providers == null) throw new ArgumentNullException(nameof(providers));
            _diskImageService = diskImageService ?? throw new ArgumentNullException(nameof(diskImageService));
            _cacheService = new CacheManager(api, logger, plugin, _diskImageService);
            _achievementIconService = new AchievementIconService(
                _diskImageService,
                managedCustomIconService ?? throw new ArgumentNullException(nameof(managedCustomIconService)),
                settings?.Persisted,
                _logger);
            _progressReportingService = new ProgressReportingService(_logger, PostToUi);
            _refreshStateManager = new RefreshStateManager();
            _targetSelectionResolver = new TargetSelectionResolver(_api, _settings, _cacheService, _logger, refreshOrder);
            _refreshRequestPlanner = new RefreshRequestPlanner(
                _api,
                _settings,
                _logger,
                _targetSelectionResolver);
            _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
            _refreshProgressReporter = new RefreshProgressReporter((report, prioritizePending) => Report(report, prioritizePending));
            _onRefreshCompleted = onRefreshCompleted;

            _providers = providers.ToList();
        }

        public void Dispose()
        {
            _progressReportingService?.Dispose();

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

        private (Guid? OperationId, RefreshModeType? Mode, Guid? SingleGameId) GetActiveRunContext()
        {
            var context = _refreshStateManager.GetActiveRunContext();
            return (context.OperationId, context.Mode, context.SingleGameId);
        }

        private void Report(
            string message,
            int current = 0,
            int total = 0,
            bool canceled = false,
            Guid? operationId = null,
            RefreshModeType? mode = null,
            Guid? currentGameId = null)
        {
            var report = new ProgressReport
            {
                Message = message,
                CurrentStep = current,
                TotalSteps = total,
                IsCanceled = canceled,
                OperationId = operationId,
                Mode = mode,
                CurrentGameId = currentGameId
            };

            Report(report);
        }

        private void Report(ProgressReport report)
        {
            Report(report, prioritizePending: false);
        }

        private void Report(ProgressReport report, bool prioritizePending)
        {
            if (report == null)
            {
                return;
            }

            var context = GetActiveRunContext();
            if (!report.OperationId.HasValue)
            {
                report.OperationId = context.OperationId;
            }

            if (!report.Mode.HasValue)
            {
                report.Mode = context.Mode;
            }

            if (!report.CurrentGameId.HasValue && report.Mode == RefreshModeType.Single)
            {
                report.CurrentGameId = context.SingleGameId;
            }

            _progressReportingService.Report(this, RebuildProgress, report, prioritizePending);
        }

        /// <summary>
        /// Calculates percentage for a refresh progress report.
        /// </summary>
        public double CalculateProgressPercent(ProgressReport report)
        {
            return _progressReportingService.CalculateProgressPercent(report);
        }

        /// <summary>
        /// Determines if the provided report represents a final refresh state.
        /// </summary>
        public bool IsFinalProgressReport(ProgressReport report)
        {
            return _progressReportingService.IsFinalProgressReport(report);
        }

        /// <summary>
        /// Resolves the user-facing refresh status message from report + manager state.
        /// </summary>
        public string ResolveProgressMessage(ProgressReport report = null)
        {
            return _progressReportingService.ResolveProgressMessage(report);
        }

        /// <summary>
        /// Gets a centralized refresh status snapshot for UI consumers.
        /// </summary>
        public RefreshStatusSnapshot GetRefreshStatusSnapshot(ProgressReport report = null)
        {
            return _progressReportingService.GetRefreshStatusSnapshot(IsRebuilding, report);
        }

        /// <summary>
        /// Gets a transient "starting refresh" snapshot for immediate UI updates.
        /// </summary>
        public RefreshStatusSnapshot GetStartingRefreshStatusSnapshot()
        {
            return _progressReportingService.GetStartingRefreshStatusSnapshot(true);
        }

        // -----------------------------
        // Managed refresh runner
        // -----------------------------

        private bool TryBeginRun(
            Guid operationId,
            RefreshModeType mode,
            Guid? singleGameId,
            CancellationToken externalToken,
            out CancellationTokenSource cts)
        {
            if (!_refreshStateManager.TryBeginRun(
                operationId,
                mode,
                singleGameId,
                externalToken,
                out cts,
                out var activeContext))
            {
                _logger.Info("Refresh already in progress.");
                Report(
                    _progressReportingService.GetLastStatus() ?? ResourceProvider.GetString("LOCPlayAch_Status_UpdatingCache"),
                    0,
                    1,
                    operationId: activeContext.OperationId,
                    mode: activeContext.Mode,
                    currentGameId: activeContext.SingleGameId);
                return false;
            }

            _logger.Info("Starting new refresh.");
            return true;
        }

        private void EndRun()
        {
            _logger.Info("Refresh ended.");
            _refreshStateManager.EndRun();
        }

        private static List<IDataProvider> MaterializeProviderScope(IEnumerable<IDataProvider> providers)
        {
            return providers?
                .Where(provider => provider != null)
                .ToList() ?? new List<IDataProvider>();
        }

        private async Task RunManagedAsync(
            RefreshModeType mode,
            Guid? singleGameId,
            CancellationToken externalToken,
            Func<Guid, CancellationToken, Task<RebuildPayload>> runner,
            Func<RebuildPayload, string> finalMessage,
            string errorLogMessage,
            IReadOnlyList<IDataProvider> providerScope = null)
        {
            var operationId = Guid.NewGuid();

            var effectiveProviderScope = MaterializeProviderScope(providerScope);
            if (effectiveProviderScope.Count == 0 && providerScope == null)
            {
                effectiveProviderScope = MaterializeProviderScope(
                    await GetAuthenticatedProvidersAsync(externalToken).ConfigureAwait(false));
            }

            if (effectiveProviderScope.Count == 0)
            {
                _logger.Info("Refresh requested but no platforms are authenticated.");
                Report(
                    ResourceProvider.GetString("LOCPlayAch_Error_NoAuthenticatedProviders"),
                    0,
                    1,
                    operationId: operationId,
                    mode: mode,
                    currentGameId: singleGameId);
                return;
            }

            if (!TryBeginRun(operationId, mode, singleGameId, externalToken, out var cts))
                return;

            _refreshProgressReporter.Reset();
            Interlocked.Exchange(ref _savedGamesInCurrentRun, 0);

            // Report immediately so UI updates buttons before any async work
            var startMsg = ResourceProvider.GetString("LOCPlayAch_Status_Starting");
            Report(
                startMsg,
                0,
                1,
                operationId: operationId,
                mode: mode,
                currentGameId: singleGameId);

            RebuildPayload payload = null;
            try
            {
                // Run refresh setup/execution on background thread so UI commands are never blocked
                // by synchronous pre-refresh work (game filtering, capability checks, etc.).
                payload = await Task.Run(
                    async () => await runner(operationId, cts.Token).ConfigureAwait(false),
                    cts.Token).ConfigureAwait(false);

                // Store refreshed game IDs for subscribers (e.g., tag syncing)
                if (payload?.Summary?.RefreshedGameIds != null)
                {
                    LastRefreshedGameIds = new List<Guid>(payload.Summary.RefreshedGameIds);
                }
                else
                {
                    LastRefreshedGameIds = new List<Guid>();
                }

                // Store failed provider keys for notification consumers.
                _lastFailedAuthProviderKeys = payload?.FailedProviderKeys?.Count > 0
                    ? new List<string>(payload.FailedProviderKeys)
                    : new List<string>();
            }
            catch (OperationCanceledException)
            {
                _logger.Info("User achievement refresh was canceled.");
                Report(
                    ResourceProvider.GetString("LOCPlayAch_Status_Canceled"),
                    0,
                    1,
                    canceled: true,
                    operationId: operationId,
                    mode: mode,
                    currentGameId: singleGameId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, errorLogMessage);
                Report(
                    ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed"),
                    0,
                    1,
                    operationId: operationId,
                    mode: mode,
                    currentGameId: singleGameId);
            }
            finally
            {
                var hasSavedGames = Interlocked.Exchange(ref _savedGamesInCurrentRun, 0) > 0;
                var wasCanceled = cts.IsCancellationRequested;
                var finalTotalSteps = _refreshProgressReporter.CompletionTotalSteps;
                EndRun();

                // Send final completion report AFTER EndRun so IsRebuilding is false when UI processes it
                if (!wasCanceled && payload != null)
                {
                    var msg = ResolveFinalSuccessMessage(payload, finalMessage);
                    Report(
                        msg,
                        finalTotalSteps,
                        finalTotalSteps,
                        operationId: operationId,
                        mode: mode,
                        currentGameId: singleGameId);
                }

                if (hasSavedGames)
                {
                    _cacheService.NotifyCacheInvalidated();
                }

                // Notify refresh completion subscribers (e.g., auth failure notifications).
                if (!wasCanceled && payload != null)
                {
                    try { _onRefreshCompleted?.Invoke(payload); } catch { }
                }

                _refreshProgressReporter.Reset();
            }
        }

        private sealed class RefreshGameTarget
        {
            public Game Game { get; set; }
            public IDataProvider Provider { get; set; }
        }

        private sealed class CurrentRefreshPlanBuildResult
        {
            public IReadOnlyList<IDataProvider> Providers { get; set; } = Array.Empty<IDataProvider>();
            public List<RefreshGameTarget> Targets { get; set; } = new List<RefreshGameTarget>();
            public List<ProviderRefreshExecutor.ProviderExecutionPlan> ProviderPlans { get; set; } =
                new List<ProviderRefreshExecutor.ProviderExecutionPlan>();
        }

        public async Task<IReadOnlyList<IDataProvider>> GetAuthenticatedProvidersAsync(CancellationToken ct = default)
        {
            var authenticatedProviders = new List<IDataProvider>();

            foreach (var provider in _providers)
            {
                if (provider == null || !_providerRegistry.IsProviderEnabled(provider.ProviderKey))
                {
                    continue;
                }

                if (await IsProviderAuthenticatedAsync(provider, ct).ConfigureAwait(false))
                {
                    authenticatedProviders.Add(provider);
                }
            }

            return authenticatedProviders;
        }

        private List<RefreshGameTarget> GetRefreshTargets(CacheRefreshOptions options, IReadOnlyList<IDataProvider> providers)
        {
            return _targetSelectionResolver.GetRefreshTargets(options, providers)
                .Select(target => new RefreshGameTarget { Game = target.Game, Provider = target.Provider })
                .ToList();
        }

        private CurrentRefreshPlanBuildResult BuildCurrentRefreshPlans(
            CacheRefreshOptions options,
            IReadOnlyList<IDataProvider> providers)
        {
            options ??= new CacheRefreshOptions();
            var scopedProviders = MaterializeProviderScope(providers);
            var result = new CurrentRefreshPlanBuildResult
            {
                Providers = scopedProviders
            };

            if (scopedProviders.Count == 0)
            {
                return result;
            }

            var refreshTargets = GetRefreshTargets(options, scopedProviders);
            var orderedProviders = _targetSelectionResolver.OrderProvidersForRefresh(scopedProviders);
            var providerOrder = orderedProviders
                .Select((provider, index) => new { provider, index })
                .ToDictionary(x => x.provider, x => x.index);

            result.Targets = refreshTargets;
            result.ProviderPlans = refreshTargets
                .GroupBy(x => x.Provider)
                .OrderBy(group => providerOrder.TryGetValue(group.Key, out var index) ? index : int.MaxValue)
                .Select(group => new ProviderRefreshExecutor.ProviderExecutionPlan
                {
                    Provider = group.Key,
                    Games = group.Select(x => x.Game).ToList()
                })
                .ToList();

            _logger.Debug(string.Format(
                "Games to refresh: {0}, Platforms: {1}, Grouped platforms: {2}",
                refreshTargets.Count,
                _providers.Count,
                result.ProviderPlans.Count));

            return result;
        }

        private async Task<RebuildPayload> RefreshAsync(
            CacheRefreshOptions options,
            CancellationToken cancel,
            Guid operationId,
            RefreshModeType mode,
            Guid? singleGameId = null,
            bool forceIconRefresh = false,
            IReadOnlyList<IDataProvider> providerScope = null,
            bool? runProvidersInParallelOverride = null)
        {
            var providers = providerScope == null
                ? MaterializeProviderScope(await GetAuthenticatedProvidersAsync(cancel).ConfigureAwait(false))
                : MaterializeProviderScope(providerScope);
            if (providers.Count == 0)
            {
                _logger?.Warn("No authenticated platforms available for refresh.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var planBuild = BuildCurrentRefreshPlans(options, providers);
            if (planBuild.ProviderPlans.Count == 0)
            {
                _logger?.Warn("No matching platforms available for refresh options.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            return await ExecuteCurrentProviderPlansAsync(
                planBuild.ProviderPlans,
                planBuild.Targets.Count,
                cancel,
                operationId,
                mode,
                singleGameId,
                forceIconRefresh,
                runProvidersInParallelOverride,
                initializeProgress: true).ConfigureAwait(false);
        }

        private async Task<RebuildPayload> ExecuteCurrentProviderPlansAsync(
            IReadOnlyList<ProviderRefreshExecutor.ProviderExecutionPlan> providerPlans,
            int totalGames,
            CancellationToken cancel,
            Guid operationId,
            RefreshModeType mode,
            Guid? singleGameId = null,
            bool forceIconRefresh = false,
            bool? runProvidersInParallelOverride = null,
            bool initializeProgress = true)
        {
            var plans = providerPlans?
                .Where(plan => plan?.Provider != null && plan.Games?.Count > 0)
                .ToList() ?? new List<ProviderRefreshExecutor.ProviderExecutionPlan>();
            if (plans.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            if (initializeProgress)
            {
                // Run the owned-game refresh in weighted units [0, TotalUnits] so per-game icon downloads
                // can advance the bar fractionally within a game. The plain game-count scale is too coarse
                // (a single-game refresh would sit at 0 until completion, then jump to 100%).
                _refreshProgressReporter.ConfigureWeightedProgress(
                    FriendRefreshProgressSession.TotalUnits, 0, FriendRefreshProgressSession.TotalUnits);
                _refreshProgressReporter.Initialize(totalGames);
            }

            var progressScope = new RefreshProgressScope(operationId, mode, singleGameId);

            var runProvidersInParallel = runProvidersInParallelOverride ?? (_settings?.Persisted?.EnableParallelProviderRefresh ?? true);
            var timer = Stopwatch.StartNew();
            _logger?.Debug(
                $"[RefreshPerf] phase=current.start mode={mode} providers={plans.Count} games={totalGames} parallel={runProvidersInParallel}");
            var providerResults = await ProviderRefreshExecutor.ExecuteProvidersAsync(
                plans,
                runProvidersInParallel,
                plan => ExecuteCurrentProviderPlanAsync(plan, progressScope, forceIconRefresh, cancel),
                cancel).ConfigureAwait(false);

            var payload = CreateCurrentRefreshPayload(providerResults);
            timer.Stop();
            _logger?.Debug(
                $"[RefreshPerf] phase=current.total mode={mode} ms={timer.ElapsedMilliseconds} providers={plans.Count} games={totalGames} refreshed={payload.Summary.GamesRefreshed} withAchievements={payload.Summary.GamesWithAchievements} withoutAchievements={payload.Summary.GamesWithoutAchievements}");
            return payload;
        }

        private async Task<RebuildPayload> ExecuteCurrentProviderPlanAsync(
            ProviderRefreshExecutor.ProviderExecutionPlan plan,
            RefreshProgressScope progressScope,
            bool forceIconRefresh,
            CancellationToken cancel)
        {
            if (plan?.Provider == null || plan.Games == null || plan.Games.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var timer = Stopwatch.StartNew();
            try
            {
                return await plan.Provider.RefreshAsync(
                    plan.Games,
                    game => _refreshProgressReporter.ReportGameStarting(game, progressScope),
                    (game, data) => _refreshProgressReporter.OnProviderGameCompletedAsync(
                        plan.Provider,
                        game,
                        data,
                        progressScope,
                        cancel,
                        (provider, refreshedGame, refreshedData, scope, token) =>
                            OnGameRefreshed(provider, refreshedGame, refreshedData, scope, forceIconRefresh, token)),
                    cancel).ConfigureAwait(false);
            }
            finally
            {
                timer.Stop();
                _logger?.Debug(
                    $"[RefreshPerf] phase=current.provider provider={plan.Provider.ProviderKey} ms={timer.ElapsedMilliseconds} games={plan.Games.Count}");
            }
        }

        private static RebuildPayload CreateCurrentRefreshPayload(
            IReadOnlyList<ProviderRefreshExecutor.ProviderExecutionResult> providerResults)
        {
            var mergedSummary = new RebuildSummary();
            var authRequired = false;
            var failedProviderKeys = new List<string>();

            foreach (var result in providerResults)
            {
                if (result?.Payload == null)
                {
                    continue;
                }

                if (result.Payload.AuthRequired)
                {
                    authRequired = true;
                    var key = result.Provider?.ProviderKey;
                    if (!string.IsNullOrWhiteSpace(key) &&
                        !failedProviderKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        failedProviderKeys.Add(key);
                    }
                }

                if (result.Payload.Summary == null)
                {
                    continue;
                }

                mergedSummary.GamesRefreshed += result.Payload.Summary.GamesRefreshed;
                mergedSummary.GamesWithAchievements += result.Payload.Summary.GamesWithAchievements;
                mergedSummary.GamesWithoutAchievements += result.Payload.Summary.GamesWithoutAchievements;

                if (result.Payload.Summary.RefreshedGameIds != null)
                {
                    mergedSummary.RefreshedGameIds.AddRange(result.Payload.Summary.RefreshedGameIds);
                }
            }

            return new RebuildPayload
            {
                Summary = mergedSummary,
                AuthRequired = authRequired,
                FailedProviderKeys = failedProviderKeys
            };
        }

        /// <summary>
        /// Refreshes only the friend roster (friend list + avatars) for authenticated friend-capable
        /// providers, without ownership/definition/achievement work. Backs the in-settings
        /// "refresh friends list" action. Returns the number of active friends saved.
        /// </summary>
        public async Task<int> RefreshFriendRosterAsync(CancellationToken cancel = default)
        {
            var autoProviderKeys = _settings?.Persisted?.AutoDiscoverFriendProviderKeys;
            return await RefreshFriendRosterAsync(autoProviderKeys, cancel).ConfigureAwait(false);
        }

        public async Task<int> RefreshFriendRosterAsync(
            IReadOnlyCollection<string> providerKeys,
            CancellationToken cancel = default)
        {
            if ((_cacheService as IFriendCacheManager) == null)
            {
                return 0;
            }

            var providerKeySet = providerKeys == null
                ? null
                : new HashSet<string>(
                    providerKeys
                        .Where(key => !string.IsNullOrWhiteSpace(key))
                        .Select(key => key.Trim()),
                    StringComparer.OrdinalIgnoreCase);

            var providers = MaterializeProviderScope(await GetAuthenticatedProvidersAsync(cancel).ConfigureAwait(false))
                .Where(provider => provider?.Friends != null)
                .ToList();
            if (providerKeySet?.Count > 0)
            {
                providers = providers
                    .Where(provider => providerKeySet.Contains(provider.ProviderKey))
                    .ToList();
            }

            if (providers.Count == 0)
            {
                _logger?.Warn("No authenticated friend-capable platforms available for friend roster refresh.");
                return 0;
            }

            return await RefreshFriendRosterAsync(providers, cancel)
                .ConfigureAwait(false);
        }

        private async Task<RebuildPayload> RefreshFriendsAsync(
            FriendRefreshOptions options,
            CancellationToken cancel,
            Guid operationId,
            RefreshModeType mode,
            IReadOnlyList<IDataProvider> providerScope = null)
        {
            var providers = providerScope == null
                ? MaterializeProviderScope(await GetAuthenticatedProvidersAsync(cancel).ConfigureAwait(false))
                : MaterializeProviderScope(providerScope);

            providers = providers
                .Where(provider => provider?.Friends != null)
                .ToList();

            if (providers.Count == 0 || (_cacheService as IFriendCacheManager) == null)
            {
                _logger?.Warn("No authenticated friend-capable platforms available for friends refresh.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            void ReportFriendProgress(string message, int current, int total)
            {
                _refreshProgressReporter.ReportFriendProgress(
                    message,
                    Math.Max(0, Math.Min(current, Math.Max(1, total) - 1)),
                    Math.Max(1, total),
                    new RefreshProgressScope(operationId, mode, null));
            }

            return await RefreshAsync(providers, options, ReportFriendProgress, cancel)
                .ConfigureAwait(false);
        }

        private async Task<RebuildPayload> ExecuteRefreshPlanAsync(
            RefreshRequestPlanner.ResolvedRequest resolved,
            CancellationToken cancel,
            Guid operationId,
            RefreshModeType mode,
            Guid? singleGameId)
        {
            var payload = new RebuildPayload();
            if (resolved == null)
            {
                return payload;
            }

            var currentOptions = resolved.CurrentUserOptions;
            var friendOptions = resolved.FriendOptions;
            var forceIconRefresh = resolved.Options?.ForceIconRefresh == true;

            if (currentOptions == null)
            {
                return await RefreshFriendsAsync(
                    friendOptions,
                    cancel,
                    operationId,
                    mode,
                    resolved.FriendProviderScope).ConfigureAwait(false);
            }

            if (friendOptions == null)
            {
                return await RefreshAsync(
                    currentOptions,
                    cancel,
                    operationId,
                    mode,
                    singleGameId,
                    forceIconRefresh,
                    resolved.CurrentProviderScope,
                    resolved.RunProvidersInParallelOverride).ConfigureAwait(false);
            }

            var currentProviders = MaterializeProviderScope(resolved.CurrentProviderScope);
            var friendProviders = MaterializeProviderScope(resolved.FriendProviderScope)
                .Where(provider => provider?.Friends != null)
                .ToList();

            var currentPlanBuild = BuildCurrentRefreshPlans(currentOptions, currentProviders);
            var currentPlansByProvider = currentPlanBuild.ProviderPlans
                .Where(plan => plan?.Provider != null && plan.Games?.Count > 0)
                .ToDictionary(plan => plan.Provider);

            var hasCurrentWork = currentPlansByProvider.Count > 0;
            var hasFriendWork = friendProviders.Count > 0 && (_cacheService as IFriendCacheManager) != null;
            if (!hasCurrentWork && !hasFriendWork)
            {
                _logger?.Warn("No matching platforms available for refresh options.");
                return payload;
            }

            if (friendOptions != null && friendProviders.Count == 0)
            {
                _logger?.Warn("No authenticated friend-capable platforms available for friends refresh.");
            }

            if (!hasFriendWork)
            {
                return await RefreshAsync(
                    currentOptions,
                    cancel,
                    operationId,
                    mode,
                    singleGameId,
                    forceIconRefresh,
                    resolved.CurrentProviderScope,
                    resolved.RunProvidersInParallelOverride).ConfigureAwait(false);
            }

            if (!hasCurrentWork)
            {
                return await RefreshFriendsAsync(
                    friendOptions,
                    cancel,
                    operationId,
                    mode,
                    resolved.FriendProviderScope).ConfigureAwait(false);
            }

            var friendProviderSet = new HashSet<IDataProvider>(friendProviders);
            var providerOrder = ResolveCombinedProviderOrder(currentPlansByProvider.Keys.Concat(friendProviders));
            if (providerOrder.Count == 0)
            {
                return payload;
            }

            const int totalUnits = FriendRefreshProgressSession.TotalUnits;
            const int maxReportUnits = totalUnits - 1;
            var preparationUnits = hasFriendWork
                ? Math.Min(3500, Math.Max(1500, maxReportUnits / 3))
                : maxReportUnits;

            void ReportFriendProgress(string message, int current, int total)
            {
                _refreshProgressReporter.ReportFriendProgress(
                    message,
                    Math.Max(0, Math.Min(current, Math.Max(1, total) - 1)),
                    Math.Max(1, total),
                    new RefreshProgressScope(operationId, mode, singleGameId));
            }

            var progressScope = new RefreshProgressScope(operationId, mode, singleGameId);
            var friendProgress = new FriendRefreshProgressSession(ReportFriendProgress);
            var orderedFriendProviders = providerOrder
                .Where(provider => friendProviderSet.Contains(provider))
                .ToList();
            var friendProviderIndexByProvider = orderedFriendProviders
                .Select((provider, index) => new { provider, index })
                .ToDictionary(item => item.provider, item => item.index);
            friendProgress.InitializeProviderTotal(orderedFriendProviders.Count);

            var providerWorkPlans = providerOrder
                .Where(provider => currentPlansByProvider.ContainsKey(provider) || friendProviderSet.Contains(provider))
                .Select(provider => new ProviderRefreshExecutor.ProviderExecutionPlan
                {
                    Provider = provider,
                    Games = currentPlansByProvider.TryGetValue(provider, out var currentPlan)
                        ? currentPlan.Games
                        : Array.Empty<Game>()
                })
                .ToList();

            var runProvidersInParallel = resolved.RunProvidersInParallelOverride ??
                                         (_settings?.Persisted?.EnableParallelProviderRefresh ?? true);
            var contexts = new List<FriendProviderRefreshContext>();
            var contextsLock = new object();
            var payloadLock = new object();
            var friendPerf = new FriendRefreshPerfSession(_logger, friendOptions, friendProviders.Count, "combined");
            var friendPrepareTimer = Stopwatch.StartNew();

            try
            {
                // Combined preparation runs current-game refreshes and friend-roster loads concurrently.
                // Drive one shared aggregate over [0, preparationUnits] so the two workstreams cannot
                // collide or freeze each other. Initialize still tracks the current-game count so the
                // per-game status text ("Refreshing X (n/total)") stays correct.
                _refreshProgressReporter.Initialize(currentPlanBuild.Targets.Count);
                _refreshProgressReporter.InitializePreparation(
                    currentPlanBuild.Targets.Count + orderedFriendProviders.Count,
                    preparationUnits,
                    totalUnits);
                var providerResults = await ProviderRefreshExecutor.ExecuteProvidersAsync(
                    providerWorkPlans,
                    runProvidersInParallel,
                    async plan =>
                    {
                        cancel.ThrowIfCancellationRequested();
                        var providerPayload = new RebuildPayload();
                        var provider = plan.Provider;

                        if (currentPlansByProvider.TryGetValue(provider, out var currentPlan))
                        {
                            var currentPayload = await ExecuteCurrentProviderPlanAsync(
                                currentPlan,
                                progressScope,
                                forceIconRefresh,
                                cancel).ConfigureAwait(false);
                            Merge(providerPayload, currentPayload);
                            MarkAuthFailure(providerPayload, provider.ProviderKey, currentPayload?.AuthRequired == true);
                        }

                        if (friendProviderIndexByProvider.TryGetValue(provider, out var friendProviderIndex))
                        {
                            // Suppress the friend session's roster emission during phase 1 (pass null
                            // progress); roster loads are reflected through the shared preparation
                            // aggregate instead, so they no longer collide with current-game progress.
                            var context = await PrepareProviderRefreshAsync(
                                provider.Friends,
                                friendOptions,
                                providerPayload,
                                progress: null,
                                friendProviderIndex,
                                orderedFriendProviders.Count,
                                cancel).ConfigureAwait(false);

                            var rosterFormat = ResourceProvider.GetString("LOCPlayAch_FriendsRefresh_Progress_LoadingFriends");
                            if (string.IsNullOrWhiteSpace(rosterFormat))
                            {
                                rosterFormat = "Loading friends {1}/{2}: {0}";
                            }

                            _refreshProgressReporter.ReportPreparationUnitCompleted(
                                string.Format(
                                    rosterFormat,
                                    string.IsNullOrWhiteSpace(provider.ProviderKey) ? "provider" : provider.ProviderKey.Trim(),
                                    friendProviderIndex + 1,
                                    orderedFriendProviders.Count),
                                progressScope);

                            if (context != null)
                            {
                                lock (contextsLock)
                                {
                                    contexts.Add(context);
                                }
                            }
                        }

                        return providerPayload;
                    },
                    cancel).ConfigureAwait(false);

                foreach (var result in providerResults)
                {
                    Merge(payload, result?.Payload);
                }

                if (hasFriendWork)
                {
                    friendPerf.LogPrepare(friendPrepareTimer, contexts, "combinedProviderWork=true");
                    _refreshProgressReporter.ConfigureWeightedProgress(totalUnits, preparationUnits, maxReportUnits);
                    await RefreshPreparedFriendContextsAsync(
                        contexts,
                        friendOptions,
                        payload,
                        payloadLock,
                        friendProgress,
                        friendPerf,
                        cancel).ConfigureAwait(false);
                    friendPerf.LogTotal(payload, contexts);
                }
            }
            finally
            {
                EndFriendRefreshContexts(contexts);
            }

            return payload;
        }

        private IReadOnlyList<IDataProvider> ResolveCombinedProviderOrder(IEnumerable<IDataProvider> providers)
        {
            var unique = new List<IDataProvider>();
            foreach (var provider in providers ?? Enumerable.Empty<IDataProvider>())
            {
                if (provider != null && !unique.Contains(provider))
                {
                    unique.Add(provider);
                }
            }

            return _targetSelectionResolver.OrderProvidersForRefresh(unique);
        }

        private async Task OnGameRefreshed(
            IDataProvider provider,
            Game game,
            GameAchievementData data,
            RefreshProgressScope progressScope,
            bool forceIconRefresh,
            CancellationToken cancel = default)
        {
            if (data?.PlayniteGameId == null) return;

            // Ensure provider metadata is persisted for diagnostics and future multi-provider caching.
            try
            {
                if (string.IsNullOrWhiteSpace(data.ProviderKey))
                {
                    data.ProviderKey = provider?.ProviderKey;
                }
            }
            catch
            {
            }

            var unlockedIconOverrides = GameCustomDataLookup.GetAchievementUnlockedIconOverrides(data.PlayniteGameId.Value);
            var lockedIconOverrides = GameCustomDataLookup.GetAchievementLockedIconOverrides(data.PlayniteGameId.Value);

            await _achievementIconService.PopulateAchievementIconCacheAsync(
                data,
                unlockedIconOverrides,
                lockedIconOverrides,
                cancel,
                (downloaded, total) => _refreshProgressReporter.ReportIconProgress(game, data, downloaded, total, progressScope),
                forceRefreshExistingTargets: forceIconRefresh)
                .ConfigureAwait(false);

            var key = data.PlayniteGameId.Value.ToString();

            if (!string.IsNullOrWhiteSpace(key))
            {
                // Persist provider payload as-is. Runtime overlays (capstone/category/order/game reference)
                // are applied on read and are not written back to cache.

                var writeResult = _cacheService.SaveGameData(key, data);
                if (writeResult == null || !writeResult.Success)
                {
                    var errorCode = writeResult?.ErrorCode ?? "unknown";
                    var errorMessage = writeResult?.ErrorMessage ?? "Unknown cache persistence failure.";

                    throw new CachePersistenceException(
                        key,
                        provider?.ProviderKey ?? data.ProviderKey,
                        errorCode,
                        $"Persisting refreshed game data failed. key={key}, provider={provider?.ProviderKey ?? data.ProviderKey}, code={errorCode}, message={errorMessage}",
                        writeResult?.Exception);
                }

                Interlocked.Increment(ref _savedGamesInCurrentRun);

                PromoteMatchingProviderOnlyFriendGame(provider, data);

                // Fire per-game refresh event for amortized tag syncing
                try { GameRefreshed?.Invoke(game.Id); } catch { }
            }
        }

        private void PromoteMatchingProviderOnlyFriendGame(IDataProvider provider, GameAchievementData data)
        {
            if (_friendCache == null ||
                data?.PlayniteGameId == null ||
                data.PlayniteGameId.Value == Guid.Empty)
            {
                return;
            }

            var providerKey = string.IsNullOrWhiteSpace(data.ProviderKey)
                ? provider?.ProviderKey
                : data.ProviderKey;
            if (string.IsNullOrWhiteSpace(providerKey) ||
                !HasProviderGameIdentity(data.AppId, data.ProviderGameKey))
            {
                return;
            }

            var promotion = _friendCache.PromoteProviderOnlyGameToPlayniteBacked(
                providerKey,
                data.AppId,
                data.ProviderGameKey,
                data.PlayniteGameId.Value);
            if (promotion == null)
            {
                return;
            }

            if (!promotion.Success)
            {
                _logger?.Warn(
                    $"Failed to promote provider-only friend game for {providerKey}/{GetProviderGameCacheKey(data.AppId, data.ProviderGameKey)}: {promotion.ErrorMessage}");
                return;
            }

            // The owned game already downloaded its own fresh icons and the promoted friend rows
            // now reference the owned definitions, so the provider-only friend icons are orphaned.
            if (promotion.WrittenCount > 0)
            {
                var friendCacheKey = GetProviderGameCacheKey(data.AppId, data.ProviderGameKey);
                _achievementIconService.DeleteFriendGameIconCache(providerKey, friendCacheKey);
            }
        }

        private string ResolveFinalSuccessMessage(RebuildPayload payload, Func<RebuildPayload, string> finalMessage)
        {
            var defaultMessage = ResourceProvider.GetString("LOCPlayAch_Status_RefreshComplete");
            string resolvedMessage = null;

            if (finalMessage != null)
            {
                try
                {
                    var message = finalMessage(payload);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        resolvedMessage = message;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed"));
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedMessage))
            {
                resolvedMessage = defaultMessage;
            }

            if (payload?.AuthRequired == true)
            {
                var suffix = ResourceProvider.GetString("LOCPlayAch_Status_RefreshCompleteAuthRequiredSuffix");
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    suffix = "Some providers require authentication.";
                }

                return string.Concat(resolvedMessage, " ", suffix);
            }

            return resolvedMessage;
        }

        // -----------------------------
        // Public refresh methods
        // -----------------------------

        private Task StartManagedResolvedRequestAsync(
            RefreshRequestPlanner.ResolvedRequest resolved,
            CancellationToken externalToken = default)
        {
            return RunManagedAsync(
                resolved.Mode,
                resolved.SingleGameId,
                externalToken,
                (operationId, cancel) => RefreshResolvedAsync(resolved, operationId, cancel),
                payload => FormatRefreshCompletionForResolvedRequest(resolved, payload),
                resolved.ErrorLogMessage ?? "Refresh failed.",
                resolved.ProviderScope);
        }

        private async Task<RebuildPayload> RefreshResolvedAsync(
            RefreshRequestPlanner.ResolvedRequest resolved,
            Guid operationId,
            CancellationToken cancel)
        {
            var payload = new RebuildPayload();
            if (resolved == null)
            {
                return payload;
            }

            if (resolved.CurrentUserOptions != null || resolved.FriendOptions != null)
            {
                return await ExecuteRefreshPlanAsync(
                    resolved,
                    cancel,
                    operationId,
                    resolved.Mode,
                    resolved.SingleGameId).ConfigureAwait(false);
            }

            return payload;
        }

        private static string FormatFriendRefreshCompletionForResolvedRequest(
            RefreshRequestPlanner.ResolvedRequest resolved,
            RebuildPayload payload)
        {
            var format = ResourceProvider.GetString("LOCPlayAch_Status_FriendsRefreshCompleteWithModeAndCount");
            if (string.IsNullOrWhiteSpace(format))
            {
                format = "{0} complete. {1} friend game checks refreshed.";
            }

            return string.Format(
                format,
                GetRefreshModeShortName(resolved.Mode),
                Math.Max(0, payload?.FriendSummary?.CandidatesRefreshed ?? 0));
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

        private void ShowCustomRefreshMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _api?.Dialogs?.ShowMessage(
                message,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        public Task ExecuteRefreshAsync(CustomRefreshOptions options, CancellationToken externalToken = default)
        {
            return ExecuteRefreshAsync(new RefreshRequest
            {
                Mode = RefreshModeType.Custom,
                Options = RefreshOptions.FromCustom(options)
            }, externalToken);
        }

        /// <summary>
        /// Executes a refresh based on the specified refresh mode key.
        /// </summary>
        public Task ExecuteRefreshAsync(string modeKey, Guid? singleGameId = null, CancellationToken externalToken = default)
        {
            return ExecuteRefreshAsync(new RefreshRequest
            {
                ModeKey = modeKey,
                SingleGameId = singleGameId
            }, externalToken);
        }

        public virtual Task ExecuteRefreshAsync(RefreshRequest request, CancellationToken externalToken = default)
        {
            return ExecuteRefreshAsync(request, authenticatedProviders: null, externalToken);
        }

        internal virtual async Task ExecuteRefreshAsync(
            RefreshRequest request,
            IReadOnlyList<IDataProvider> authenticatedProviders,
            CancellationToken externalToken = default)
        {
            var effectiveAuthenticatedProviders = MaterializeProviderScope(authenticatedProviders);
            if (effectiveAuthenticatedProviders.Count == 0 && authenticatedProviders == null)
            {
                effectiveAuthenticatedProviders = MaterializeProviderScope(
                    await GetAuthenticatedProvidersAsync(externalToken).ConfigureAwait(false));
            }

            var resolved = _refreshRequestPlanner.Resolve(request, effectiveAuthenticatedProviders);
            if (!resolved.ShouldExecute)
            {
                if (!string.IsNullOrWhiteSpace(resolved.EmptySelectionLogMessage))
                {
                    _logger.Info(resolved.EmptySelectionLogMessage);
                    Report(FormatRefreshCompletionWithModeAndCount(resolved.Mode, 0), 1, 1, mode: resolved.Mode);
                }

                if (!string.IsNullOrWhiteSpace(resolved.UserMessage))
                {
                    ShowCustomRefreshMessage(resolved.UserMessage);
                }

                return;
            }

            resolved.ProviderScope ??= effectiveAuthenticatedProviders;
            await StartManagedResolvedRequestAsync(resolved, externalToken).ConfigureAwait(false);
        }

        public Task ExecuteRefreshForGamesAsync(IEnumerable<Guid> gameIds, CancellationToken externalToken = default)
        {
            return ExecuteRefreshAsync(new RefreshRequest
            {
                GameIds = gameIds?.ToList()
            }, externalToken);
        }

        /// <summary>
        /// Executes a refresh based on the specified refresh mode type.
        /// </summary>
        public Task ExecuteRefreshAsync(RefreshModeType mode, Guid? singleGameId = null, CancellationToken externalToken = default)
        {
            return ExecuteRefreshAsync(new RefreshRequest
            {
                Mode = mode,
                SingleGameId = singleGameId
            }, externalToken);
        }

        private static string FormatRefreshCompletionForResolvedRequest(
            RefreshRequestPlanner.ResolvedRequest resolved,
            RebuildPayload payload)
        {
            if (resolved?.CurrentUserOptions != null && resolved.FriendOptions != null)
            {
                var format = ResourceProvider.GetString("LOCPlayAch_Status_CombinedRefreshCompleteWithModeAndCounts");
                if (string.IsNullOrWhiteSpace(format))
                {
                    format = "{0} complete: {1} games refreshed; {2} friend game checks refreshed.";
                }

                return string.Format(
                    format,
                    GetRefreshModeShortName(resolved.Mode),
                    Math.Max(0, payload?.Summary?.GamesRefreshed ?? 0),
                    Math.Max(0, payload?.FriendSummary?.CandidatesRefreshed ?? 0));
            }

            if (resolved?.FriendOptions != null)
            {
                return FormatFriendRefreshCompletionForResolvedRequest(resolved, payload);
            }

            if (resolved.Mode == RefreshModeType.Single)
            {
                return ResourceProvider.GetString("LOCPlayAch_Status_RefreshComplete");
            }

            return FormatRefreshCompletionWithModeAndCount(resolved.Mode, payload?.Summary?.GamesRefreshed ?? 0);
        }

        public void CancelCurrentRebuild()
        {
            _logger.Info("Cancel refresh requested.");
            _refreshStateManager.CancelCurrentRebuild();
        }

        public async Task<bool> IsProviderAuthenticatedAsync(IDataProvider provider, CancellationToken ct = default)
        {
            var result = await ProbeProviderAuthStateAsync(provider, ct).ConfigureAwait(false);
            return result.IsSuccess;
        }

        public async Task<AuthProbeResult> ProbeProviderAuthStateAsync(IDataProvider provider, CancellationToken ct = default)
        {
            if (provider == null)
            {
                return AuthProbeResult.NotAuthenticated();
            }

            try
            {
                if (provider.AuthSession != null)
                {
                    return await provider.AuthSession.ProbeAuthStateAsync(ct).ConfigureAwait(false);
                }

                return provider.IsAuthenticated
                    ? AuthProbeResult.AlreadyAuthenticated()
                    : AuthProbeResult.NotAuthenticated();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Auth probe failed for provider '{provider.ProviderKey}'.");
                return AuthProbeResult.ProbeFailed();
            }
        }


        // -----------------------------
        // Friends refresh helpers
        // -----------------------------

        private const int FriendRefreshParallelism = 4;
        private static readonly TimeSpan DefaultDefinitionTtl = TimeSpan.FromDays(7);

        private IFriendCacheManager _friendCache => _cacheService as IFriendCacheManager;

        public async Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<IDataProvider> providerScope,
            FriendRefreshOptions options,
            Action<string, int, int> reportProgress,
            CancellationToken cancel = default)
        {
            var payload = new RebuildPayload();
            if (_friendCache == null)
            {
                return payload;
            }

            options = NormalizeOptions(options);
            var providers = (providerScope ?? Array.Empty<IDataProvider>())
                .Where(provider => provider?.Friends != null)
                .ToList();

            var progress = new FriendRefreshProgressSession(reportProgress);
            progress.InitializeProviderTotal(providers.Count);
            var contexts = new List<FriendProviderRefreshContext>();
            var payloadLock = new object();
            var perf = new FriendRefreshPerfSession(_logger, options, providers.Count);
            try
            {
                var prepareTimer = Stopwatch.StartNew();
                for (var i = 0; i < providers.Count; i++)
                {
                    var provider = providers[i];
                    cancel.ThrowIfCancellationRequested();
                    var context = await PrepareProviderRefreshAsync(
                        provider.Friends,
                        options,
                        payload,
                        progress,
                        i,
                        providers.Count,
                        cancel).ConfigureAwait(false);
                    if (context != null)
                    {
                        contexts.Add(context);
                    }
                }
                perf.LogPrepare(prepareTimer, contexts);

                await RefreshPreparedFriendContextsAsync(
                    contexts,
                    options,
                    payload,
                    payloadLock,
                    progress,
                    perf,
                    cancel).ConfigureAwait(false);

                perf.LogTotal(payload, contexts);
            }
            finally
            {
                EndFriendRefreshContexts(contexts);
            }

            return payload;
        }

        private async Task RefreshPreparedFriendContextsAsync(
            IReadOnlyList<FriendProviderRefreshContext> contexts,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            FriendRefreshPerfSession perf,
            CancellationToken cancel)
        {
            var activeContexts = contexts
                .Where(context => context?.CanContinue == true)
                .ToList();

            perf.LogPhase(
                Stopwatch.StartNew(),
                "friend.rosterMetadata",
                "skipped=true reason=settings-friends-list-only");

            var ownershipBefore = payload.FriendSummary.OwnershipPagesRefreshed;
            var ownershipRowsBefore = payload.FriendSummary.OwnershipRowsWritten;
            var ownershipTimer = Stopwatch.StartNew();
            activeContexts = activeContexts
                .Where(context => context.CanContinue)
                .ToList();
            var requiresOwnershipRefresh = RequiresAnyOwnershipRefresh(activeContexts, options);
            var logicalFriends = requiresOwnershipRefresh
                ? BuildLogicalFriendGroups(activeContexts)
                : new List<LogicalFriendRefreshGroup>();
            progress.InitializeFriendLibraryTotal(logicalFriends.Count);
            if (requiresOwnershipRefresh)
            {
                await RefreshOwnershipByLogicalFriendAsync(
                    logicalFriends,
                    options,
                    payload,
                    payloadLock,
                    progress,
                    cancel).ConfigureAwait(false);
            }
            perf.LogPhase(
                ownershipTimer,
                "friend.ownership",
                $"logicalFriends={logicalFriends.Count} pages={payload.FriendSummary.OwnershipPagesRefreshed - ownershipBefore} rows={payload.FriendSummary.OwnershipRowsWritten - ownershipRowsBefore} required={requiresOwnershipRefresh}");

            // Pre-pass: compute every context's definition plan (a read-only cache read) and set the
            // definitions sub-band total once, before any definition/probe completion is reported. This
            // is what keeps the monotonic clamp from freezing the bar during the definitions phase.
            var planTimer = Stopwatch.StartNew();
            var definitionChecksTotal = 0;
            foreach (var context in activeContexts)
            {
                if (context.DiscoverUnowned && context.Preparation.CanRefreshAchievements)
                {
                    context.DefinitionPlan = ComputeUnownedDefinitionPlan(
                        context.ProviderKey,
                        context.OwnershipSnapshots,
                        options);
                    definitionChecksTotal += context.DefinitionPlan.TotalDefinitionChecks;
                }
            }
            perf.LogDefinitionPlan(planTimer, activeContexts, definitionChecksTotal);

            progress.InitializeDefinitionChecksTotal(definitionChecksTotal);

            var definitionsTimer = Stopwatch.StartNew();
            foreach (var context in activeContexts)
            {
                if (context.DiscoverUnowned && context.Preparation.CanRefreshAchievements)
                {
                    await RefreshUnownedDefinitionsAndOwnershipAsync(
                        context.Provider,
                        context.ProviderKey,
                        context.OwnershipSnapshots,
                        context.ProbedProviderOnlyAchievementKeys,
                        context.DefinitionPlan,
                        payload,
                        payloadLock,
                        progress,
                        cancel).ConfigureAwait(false);
                }
            }
            perf.LogPhase(definitionsTimer, "friend.definitions", $"checks={definitionChecksTotal}");

            // Mapped-game scrape work for the discovery scopes (Full/Shared/Installed) is built from the
            // fresh ownership snapshot (game-centric, live hints); provider-only games were already
            // scraped by the definition/probe phase above. Recent draws from the whole cached friend
            // library filtered by the ownership-derived recency gate, and SelectedGame targets a specific
            // library game across friends — both source from the cache-backed candidate loader. The
            // scrape total is only knowable now, so its sub-band is initialized here, still ahead of its
            // first completion.
            var loadCandidatesTimer = Stopwatch.StartNew();
            var achievementWorkItems = new List<FriendAchievementWorkItem>();
            foreach (var context in activeContexts)
            {
                if (context.OwnershipSnapshots != null && UsesSnapshotCandidateBuilder(options))
                {
                    if (!context.Preparation.CanRefreshAchievements)
                    {
                        _logger?.Debug($"Skipping {context.ProviderKey} friend achievement scrapes: provider did not prepare achievement auth.");
                        MarkAuthFailure(payload, context.ProviderKey, true);
                        continue;
                    }

                    achievementWorkItems.AddRange(
                        BuildMappedAchievementWorkItemsFromSnapshots(context, options, payload));
                }
                else
                {
                    achievementWorkItems.AddRange(
                        LoadAchievementWorkItems(new[] { context }, options, payload));
                }
            }
            perf.LogCandidateLoad(loadCandidatesTimer, activeContexts, achievementWorkItems.Count);
            progress.InitializeAchievementScrapeTotal(achievementWorkItems.Count);
            var achievementsBefore = payload.FriendSummary.CandidatesRefreshed;
            var achievementsSavedBefore = payload.FriendSummary.AchievementsSaved;
            var achievementTimer = Stopwatch.StartNew();
            await RefreshAchievementWorkItemsAsync(
                achievementWorkItems,
                payload,
                payloadLock,
                progress,
                cancel).ConfigureAwait(false);
            perf.LogPhase(
                achievementTimer,
                "friend.achievements",
                $"workItems={achievementWorkItems.Count} refreshed={payload.FriendSummary.CandidatesRefreshed - achievementsBefore} saved={payload.FriendSummary.AchievementsSaved - achievementsSavedBefore}");
        }

        private void EndFriendRefreshContexts(IEnumerable<FriendProviderRefreshContext> contexts)
        {
            foreach (var context in contexts ?? Enumerable.Empty<FriendProviderRefreshContext>())
            {
                try
                {
                    context?.Provider?.EndRefresh();
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Failed to end friend refresh for {context?.ProviderKey}.");
                }
            }
        }

        /// <summary>
        /// Refreshes only the friend roster (the list of friends and their avatars) for each
        /// friend-capable provider, without fetching ownership, game definitions, or achievements.
        /// Used by the in-settings "refresh friends list" action. Returns the number of active friends
        /// saved across providers.
        /// </summary>
        public async Task<int> RefreshFriendRosterAsync(
            IReadOnlyList<IDataProvider> providerScope,
            CancellationToken cancel = default)
        {
            if (_friendCache == null)
            {
                return 0;
            }

            var providers = (providerScope ?? Array.Empty<IDataProvider>())
                .Where(provider => provider?.Friends != null)
                .ToList();

            var saved = 0;
            foreach (var provider in providers)
            {
                cancel.ThrowIfCancellationRequested();
                saved += await RefreshProviderRosterAsync(provider.Friends, cancel).ConfigureAwait(false);
            }

            return saved;
        }

        private async Task<int> RefreshProviderRosterAsync(
            IFriendsProvider friendsProvider,
            CancellationToken cancel)
        {
            if (friendsProvider == null)
            {
                return 0;
            }

            try
            {
                var payload = new RebuildPayload();
                var context = await PrepareProviderRosterRefreshAsync(
                    friendsProvider,
                    payload,
                    cancel).ConfigureAwait(false);
                if (context?.CanContinue != true)
                {
                    return 0;
                }

                await DownloadFriendAvatarsAsync(context.ProviderKey, context.Friends, cancel).ConfigureAwait(false);
                var saved = SaveFriendList(context, payload: null);
                return Math.Max(0, saved);
            }
            finally
            {
                friendsProvider.EndRefresh();
            }
        }

        private async Task<FriendProviderRefreshContext> PrepareProviderRosterRefreshAsync(
            IFriendsProvider friendsProvider,
            RebuildPayload payload,
            CancellationToken cancel)
        {
            if (friendsProvider == null)
            {
                return null;
            }

            var providerKey = friendsProvider.ProviderKey;
            var context = new FriendProviderRefreshContext
            {
                Provider = friendsProvider,
                ProviderKey = providerKey,
                RosterSource = "provider"
            };

            var preparationResult = await friendsProvider.BeginRefreshAsync(cancel).ConfigureAwait(false);
            if (preparationResult?.Success != true)
            {
                _logger?.Debug($"Friend roster refresh skipped for {providerKey}: {preparationResult?.ErrorMessage ?? "provider unavailable"}");
                MarkAuthFailure(payload, providerKey, preparationResult?.AuthRequired == true);
                return context;
            }

            context.Preparation = preparationResult.Data ?? new FriendsRefreshPreparation();
            context.CanContinue = true;
            payload.FriendSummary.ProvidersProcessed++;

            var friendsResult = await friendsProvider.GetFriendsAsync(cancel).ConfigureAwait(false);
            if (friendsResult?.Success != true)
            {
                _logger?.Debug($"Friend roster refresh skipped for {providerKey}: {friendsResult?.ErrorMessage ?? "friend list unavailable"}");
                MarkAuthFailure(payload, providerKey, friendsResult?.AuthRequired == true);
                context.CanContinue = false;
                return context;
            }

            var discoveredFriends = NormalizeProviderFriendIdentities(providerKey, friendsResult.Data);
            context.Friends = FilterIgnoredFriends(providerKey, discoveredFriends).ToList();
            context.ScopedFriends = context.Friends;
            payload.FriendSummary.FriendsFetched += context.Friends.Count;
            return context;
        }

        private int SaveFriendList(FriendProviderRefreshContext context, RebuildPayload payload)
        {
            if (context == null)
            {
                return -1;
            }

            var writeFriends = _friendCache.SaveFriendList(context.ProviderKey, context.Friends);
            if (writeFriends?.Success != true)
            {
                _logger?.Warn($"Failed to save {context.ProviderKey} friend list: {writeFriends?.ErrorMessage}");
                return -1;
            }

            if (payload != null)
            {
                payload.FriendSummary.FriendsSaved += writeFriends.WrittenCount;
            }

            _logger?.Debug(
                $"Saved {context.ProviderKey} friend list: fetched={context.Friends.Count}, active={writeFriends.WrittenCount}, skipped={writeFriends.SkippedCount}.");
            return writeFriends.WrittenCount;
        }

        private async Task<FriendProviderRefreshContext> PrepareProviderRefreshAsync(
            IFriendsProvider friendsProvider,
            FriendRefreshOptions options,
            RebuildPayload payload,
            FriendRefreshProgressSession progress,
            int providerIndex,
            int providerTotal,
            CancellationToken cancel)
        {
            if (friendsProvider == null)
            {
                return null;
            }

            var providerKey = friendsProvider.ProviderKey;
            var context = new FriendProviderRefreshContext
            {
                Provider = friendsProvider,
                ProviderKey = providerKey,
                DiscoverUnowned = ShouldDiscoverUnowned(providerKey, options),
                MaxDegreeOfParallelism = ResolveFriendRefreshParallelism()
            };
            // The game-centric candidate builder reads the fresh, hint-bearing ownership snapshot for
            // every scope that fetches ownership (Full/Shared/Installed/Recent and ownership-mapping
            // Custom). SelectedGame (and other non-ownership scopes) leave this null and fall back to the
            // cache-sourced candidate path. Retaining the snapshot adds no network cost: the ownership
            // scrape already runs for these scopes (see ShouldRefreshOwnership).
            context.OwnershipSnapshots = ShouldRefreshOwnership(providerKey, options)
                ? new List<FriendOwnershipSnapshot>()
                : null;

            progress?.ReportLoadingFriends(providerKey, providerIndex, providerTotal);
            var preparationResult = await friendsProvider.BeginRefreshAsync(cancel).ConfigureAwait(false);
            if (preparationResult?.Success != true)
            {
                _logger?.Debug($"Friends refresh skipped for {providerKey}: {preparationResult?.ErrorMessage ?? "provider unavailable"}");
                MarkAuthFailure(payload, providerKey, preparationResult?.AuthRequired == true);
                progress?.ReportProviderRosterLoaded(providerKey);
                return context;
            }

            context.Preparation = preparationResult.Data ?? new FriendsRefreshPreparation();
            context.CanContinue = true;
            payload.FriendSummary.ProvidersProcessed++;

            if (friendsProvider is ICurrentUserGameLabelReceiver labelReceiver)
            {
                var currentUserLabels = _friendCache.LoadCurrentUserGameLabels() ??
                                        new List<CurrentUserGameLabel>();
                labelReceiver.SetCurrentUserGameLabels(currentUserLabels);
                _logger?.Debug(
                    $"Supplied {currentUserLabels.Count} current-user game label(s) to {providerKey} friend merge.");
            }

            if (!TryPrepareFriendRosterFromSettingsOrCache(providerKey, options, context, payload))
            {
                context.CanContinue = false;
            }

            progress?.ReportProviderRosterLoaded(providerKey);
            return context;
        }

        private async Task RefreshOwnershipByLogicalFriendAsync(
            IReadOnlyList<LogicalFriendRefreshGroup> logicalFriends,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            if (logicalFriends == null || logicalFriends.Count == 0)
            {
                return;
            }

            var maxDegreeOfParallelism = Math.Max(1, logicalFriends.Max(group =>
                group?.Accounts?.Max(account => account?.Context?.MaxDegreeOfParallelism ?? 1) ?? 1));
            if (maxDegreeOfParallelism <= 1 || logicalFriends.Count == 1)
            {
                for (var i = 0; i < logicalFriends.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var shouldContinue = await RefreshLogicalFriendOwnershipAsync(
                        logicalFriends[i],
                        options,
                        payload,
                        payloadLock,
                        cancel).ConfigureAwait(false);
                    progress?.ReportFriendLibraryCompleted(logicalFriends[i]?.DisplayName);
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                try
                {
                    await RunBoundedAsync(
                        logicalFriends,
                        maxDegreeOfParallelism,
                        async (group, _, token) =>
                        {
                            var shouldContinue = await RefreshLogicalFriendOwnershipAsync(
                                group,
                                options,
                                payload,
                                payloadLock,
                                token).ConfigureAwait(false);
                            progress?.ReportFriendLibraryCompleted(group?.DisplayName);
                            if (!shouldContinue)
                            {
                                authCts.Cancel();
                            }
                        },
                        authCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancel.ThrowIfCancellationRequested();
                }
            }
        }

        private async Task<bool> RefreshLogicalFriendOwnershipAsync(
            LogicalFriendRefreshGroup logicalFriend,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            CancellationToken cancel)
        {
            foreach (var account in logicalFriend?.Accounts ?? Enumerable.Empty<FriendAccountRefreshItem>())
            {
                var context = account?.Context;
                var friend = account?.Friend;
                if (context == null || friend == null || !ShouldRefreshOwnership(context.ProviderKey, options))
                {
                    continue;
                }

                var shouldContinue = await RefreshOwnershipItemAsync(
                    context.Provider,
                    context.ProviderKey,
                    friend,
                    options,
                    payload,
                    payloadLock,
                    context.OwnershipSnapshots,
                    context.RecencyFreshKeys,
                    cancel).ConfigureAwait(false);
                if (!shouldContinue)
                {
                    return false;
                }
            }

            return true;
        }

        // Game-centric mapped-unlock candidate builder. Sources scrape work directly from the fresh,
        // hint-bearing ownership snapshot instead of round-tripping hint-less rows through the cache, so
        // the mode-aware selection (installed / recency / no-achievement) is decided from live data.
        // Only games shared with the current user's library are produced here; provider-only games are
        // scraped by the Full-scope definition/probe phase (which prunes zero-unlock games pre-network).
        private List<FriendAchievementWorkItem> BuildMappedAchievementWorkItemsFromSnapshots(
            FriendProviderRefreshContext context,
            FriendRefreshOptions options,
            RebuildPayload payload)
        {
            var workItems = new List<FriendAchievementWorkItem>();
            var snapshots = context?.OwnershipSnapshots;
            if (snapshots == null)
            {
                return workItems;
            }

            var mappedIds = BuildFriendGameMappingLookup(context.ProviderKey);
            var installedIds = ResolveInstalledFriendGameIds(options);
            var ignoredIds = GetIgnoredFriendIds(context.ProviderKey);
            var scope = options?.Scope ?? FriendRefreshScope.Recent;
            var isRecent = scope == FriendRefreshScope.Recent;
            var hasExplicitTargets = HasExplicitProviderGameTargets(options);
            var raw = 0;

            foreach (var snapshot in snapshots)
            {
                var friend = snapshot?.Friend;
                if (friend == null ||
                    string.IsNullOrWhiteSpace(friend.ExternalUserId) ||
                    ignoredIds.Contains(friend.ExternalUserId))
                {
                    continue;
                }

                foreach (var item in snapshot.Ownership ?? Enumerable.Empty<FriendGameOwnership>())
                {
                    if (!HasProviderGameIdentity(item))
                    {
                        continue;
                    }

                    raw++;

                    // Explicit provider-game targets narrow the set to the requested games.
                    if (hasExplicitTargets &&
                        !IsExplicitProviderGameTarget(options, item.AppId, item.ProviderGameKey))
                    {
                        continue;
                    }

                    // Games the provider reports as having no achievements never qualify.
                    if (item.AchievementTotalHint.HasValue && item.AchievementTotalHint.Value <= 0)
                    {
                        continue;
                    }

                    var key = GetProviderGameCacheKey(item);
                    var mapped = ResolveMappedFriendGame(context.ProviderKey, item, key, mappedIds, out var playniteId);

                    // Provider-only games are handled by the definition/probe phase; skip them here.
                    if (!mapped)
                    {
                        continue;
                    }

                    // Installed scope: keep only games whose mapped Playnite game is installed.
                    if (scope == FriendRefreshScope.Installed &&
                        !(playniteId.HasValue && installedIds != null && installedIds.Contains(playniteId.Value)))
                    {
                        continue;
                    }

                    // Recent scope: skip games the ownership step positively confirmed unchanged since
                    // the last successful scrape.
                    if (isRecent &&
                        context.RecencyFreshKeys.Contains(BuildRecencyGameKey(friend.ExternalUserId, key)))
                    {
                        continue;
                    }

                    workItems.Add(new FriendAchievementWorkItem
                    {
                        Context = context,
                        Candidate = new FriendRefreshCandidate
                        {
                            Friend = friend,
                            AppId = item.AppId,
                            ProviderGameKey = item.ProviderGameKey,
                            PlayniteGameId = playniteId,
                            GameName = item.GameName,
                            PlaytimeForeverMinutes = item.PlaytimeForeverMinutes,
                            LastPlayedUtc = item.LastPlayedUtc
                        }
                    });
                }
            }

            context.RawCandidatesLoaded += raw;
            context.CandidatesQueued += workItems.Count;
            if (payload != null)
            {
                payload.FriendSummary.CandidatesLoaded += workItems.Count;
            }

            _logger?.Debug(
                $"Built {context.ProviderKey} mapped friend scrape candidates from snapshot: raw={raw}, queued={workItems.Count}, scope={scope}.");
            return workItems;
        }

        private Dictionary<string, Guid> BuildFriendGameMappingLookup(string providerKey)
        {
            var dict = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var mappings = _friendCache?.LoadFriendGameMappings(providerKey);
            if (mappings != null)
            {
                foreach (var mapping in mappings)
                {
                    if (mapping == null || mapping.PlayniteGameId == Guid.Empty)
                    {
                        continue;
                    }

                    var key = GetProviderGameCacheKey(mapping.AppId, mapping.ProviderGameKey);
                    if (!string.IsNullOrEmpty(key))
                    {
                        dict[key] = mapping.PlayniteGameId;
                    }
                }
            }

            return dict;
        }

        private static HashSet<Guid> ResolveInstalledFriendGameIds(FriendRefreshOptions options)
        {
            if (options?.Scope != FriendRefreshScope.Installed)
            {
                return null;
            }

            var set = new HashSet<Guid>();
            foreach (var id in options.PlayniteGameIds ?? Enumerable.Empty<Guid>())
            {
                if (id != Guid.Empty)
                {
                    set.Add(id);
                }
            }

            return set;
        }

        // Resolves whether a freshly-scraped friend game maps to the current user's library, preferring
        // the materialized per-provider mapping (also yields the Playnite game id for the Installed
        // intersection). Falls back to the per-game cache check only when no mapping is materialized
        // (e.g. a test double that does not populate LoadFriendGameMappings).
        private bool ResolveMappedFriendGame(
            string providerKey,
            FriendGameOwnership item,
            string key,
            IReadOnlyDictionary<string, Guid> mappedIds,
            out Guid? playniteGameId)
        {
            // An inline mapping resolved by the provider during the scrape (e.g. Exophase maps by
            // name/slug against the current-user labels) is authoritative, even before the mapping is
            // persisted to the Games table. Steam/RA leave it null and are resolved from the cache below.
            if (item?.PlayniteGameId.HasValue == true && item.PlayniteGameId.Value != Guid.Empty)
            {
                playniteGameId = item.PlayniteGameId;
                return true;
            }

            if (mappedIds != null && mappedIds.Count > 0)
            {
                if (!string.IsNullOrEmpty(key) && mappedIds.TryGetValue(key, out var id))
                {
                    playniteGameId = id;
                    return true;
                }

                playniteGameId = null;
                return false;
            }

            playniteGameId = null;
            return IsPlayniteLibraryFriendGame(providerKey, item);
        }

        private List<FriendAchievementWorkItem> LoadAchievementWorkItems(
            IReadOnlyList<FriendProviderRefreshContext> contexts,
            FriendRefreshOptions options,
            RebuildPayload payload)
        {
            var workItems = new List<FriendAchievementWorkItem>();
            foreach (var context in contexts ?? Array.Empty<FriendProviderRefreshContext>())
            {
                var rawCandidates = _friendCache.LoadFriendRefreshCandidates(context.ProviderKey, options) ??
                                    new List<FriendRefreshCandidate>();
                context.RawCandidatesLoaded += rawCandidates.Count;
                var candidates = FilterProviderOnlyDetailCandidates(
                    context.ProviderKey,
                    FilterIgnoredCandidates(
                        context.ProviderKey,
                        rawCandidates),
                    options);

                if (context.ProbedProviderOnlyAchievementKeys.Count > 0)
                {
                    var beforeProbedFilter = candidates.Count;
                    candidates = candidates
                        .Where(candidate => !context.ProbedProviderOnlyAchievementKeys.Contains(
                            BuildFriendProviderGameKey(candidate.Friend?.ExternalUserId, GetProviderGameCacheKey(candidate))))
                        .ToList();
                    context.CandidatesSkippedAlreadyProbed += Math.Max(0, beforeProbedFilter - candidates.Count);
                }

                // Recent scope: drop only games the ownership step positively confirmed unchanged since the
                // last successful scrape (provider-driven recency). Anything not confirmed fresh is still
                // scraped. Other scopes scrape every candidate.
                if (options.Scope == FriendRefreshScope.Recent)
                {
                    var beforeRecencyFilter = candidates.Count;
                    candidates = candidates
                        .Where(candidate => !context.RecencyFreshKeys.Contains(
                            BuildRecencyGameKey(candidate.Friend?.ExternalUserId, GetProviderGameCacheKey(candidate))))
                        .ToList();
                    context.CandidatesSkippedRecencyFresh += Math.Max(0, beforeRecencyFilter - candidates.Count);
                }

                context.CandidatesQueued += candidates.Count;
                payload.FriendSummary.CandidatesLoaded += candidates.Count;
                _logger?.Debug(
                    $"Loaded {context.ProviderKey} friend achievement scrape candidates: raw={rawCandidates.Count}, queued={candidates.Count}, skippedAlreadyProbed={context.CandidatesSkippedAlreadyProbed}, skippedRecencyFresh={context.CandidatesSkippedRecencyFresh}, scope={options.Scope}.");

                if (!context.Preparation.CanRefreshAchievements)
                {
                    _logger?.Debug($"Skipping {context.ProviderKey} friend achievement scrapes: provider did not prepare achievement auth.");
                    MarkAuthFailure(payload, context.ProviderKey, true);
                    continue;
                }

                workItems.AddRange(candidates.Select(candidate => new FriendAchievementWorkItem
                {
                    Context = context,
                    Candidate = candidate
                }));
            }

            return workItems;
        }

        private async Task RefreshAchievementWorkItemsAsync(
            IReadOnlyList<FriendAchievementWorkItem> workItems,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            if (workItems == null || workItems.Count == 0)
            {
                return;
            }

            var maxDegreeOfParallelism = Math.Max(1, workItems.Max(item => item?.Context?.MaxDegreeOfParallelism ?? 1));
            if (maxDegreeOfParallelism <= 1 || workItems.Count == 1)
            {
                var limiter = CreateScanRateLimiter();
                foreach (var item in workItems)
                {
                    cancel.ThrowIfCancellationRequested();
                    var shouldContinue = await RefreshAchievementCandidateAsync(
                        item.Context.Provider,
                        item.Context.ProviderKey,
                        item.Candidate,
                        payload,
                        payloadLock,
                        delayBeforeRequest: true,
                        limiter,
                        cancel).ConfigureAwait(false);
                    progress?.ReportAchievementScrapeCompleted(FormatFriendGameDetail(item.Candidate));
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                try
                {
                    await RunBoundedAsync(
                        workItems,
                        maxDegreeOfParallelism,
                        async (item, _, token) =>
                        {
                            var limiter = CreateScanRateLimiter();
                            var shouldContinue = await RefreshAchievementCandidateAsync(
                                item.Context.Provider,
                                item.Context.ProviderKey,
                                item.Candidate,
                                payload,
                                payloadLock,
                                delayBeforeRequest: false,
                                limiter,
                                token).ConfigureAwait(false);
                            progress?.ReportAchievementScrapeCompleted(FormatFriendGameDetail(item.Candidate));
                            if (!shouldContinue)
                            {
                                authCts.Cancel();
                            }
                        },
                        authCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancel.ThrowIfCancellationRequested();
                }
            }
        }

        private async Task<bool> RefreshOwnershipItemAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            FriendIdentity friend,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            List<FriendOwnershipSnapshot> ownershipSnapshots,
            HashSet<string> recencyFreshKeys,
            CancellationToken cancel)
        {
            if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
            {
                return true;
            }

            var limiter = CreateScanRateLimiter();
            var ownershipResult = await limiter.ExecuteWithRetryAsync(
                () => friendsProvider.GetOwnedGamesAsync(friend, cancel),
                IsTransientError,
                cancel).ConfigureAwait(false);
            if (ownershipResult?.Success != true)
            {
                _logger?.Debug($"Friend ownership unavailable for {providerKey}/{friend.ExternalUserId}: {ownershipResult?.ErrorMessage}");
                if (ownershipResult?.AuthRequired == true)
                {
                    lock (payloadLock)
                    {
                        MarkAuthFailure(payload, providerKey, true);
                    }

                    return false;
                }

                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.OwnershipPagesRefreshed++;
            }

            var ownedGames = ScopeOwnedGamesForRefresh(ownershipResult.Data, options);
            // Retain the fresh, hint-bearing ownership snapshot for the game-centric candidate builder.
            // The list is non-null for every scope that fetches ownership (see PrepareProviderRefreshAsync).
            if (ownershipSnapshots != null)
            {
                lock (ownershipSnapshots)
                {
                    ownershipSnapshots.Add(new FriendOwnershipSnapshot
                    {
                        Friend = friend,
                        Ownership = ownedGames
                            .Where(item => HasProviderGameIdentity(item))
                            .ToList()
                    });
                }
            }

            // Recent scope: decide recency here, while the freshly-fetched playtime / last-played is still
            // in hand and the cached row has not yet been overwritten by the save below. Steam compares
            // playtime; RA/Exophase compare the last-played/last-unlock timestamp against the last scrape.
            // We record only the games positively confirmed unchanged; everything else is (re)scraped.
            if (options?.Scope == FriendRefreshScope.Recent && recencyFreshKeys != null)
            {
                var previous = _friendCache.LoadFriendOwnershipRecency(providerKey, friend.ExternalUserId) ??
                               new Dictionary<string, FriendOwnershipRecency>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in ownedGames)
                {
                    if (!HasProviderGameIdentity(item))
                    {
                        continue;
                    }

                    var cacheKey = GetProviderGameCacheKey(item);
                    if (string.IsNullOrEmpty(cacheKey))
                    {
                        continue;
                    }

                    previous.TryGetValue(cacheKey, out var prev);
                    if (!IsRecencyStale(providerKey, item, prev))
                    {
                        lock (recencyFreshKeys)
                        {
                            recencyFreshKeys.Add(BuildRecencyGameKey(friend.ExternalUserId, cacheKey));
                        }
                    }
                }
            }

            // The per-friend ownership save only syncs mapped/shared games (and prunes stale rows).
            // Provider-only ownership is persisted solely by ProbeAndPersistProviderOnlyFriendGameAsync
            // once a friend's unlocks are confirmed, so it is never written blindly here.
            var writeOwnership = _friendCache.SaveFriendOwnership(
                providerKey,
                friend.ExternalUserId,
                ownedGames,
                new FriendOwnershipSaveOptions
                {
                    IncludeProviderOnlyGames = false
                });
            if (writeOwnership?.Success != true)
            {
                _logger?.Warn($"Failed to save friend ownership for {providerKey}/{friend.ExternalUserId}: {writeOwnership?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.OwnershipRowsWritten += writeOwnership.WrittenCount;
            }

            _logger?.Debug(
                $"Saved friend ownership for {providerKey}/{friend.ExternalUserId}: " +
                $"fetched={ownedGames.Count}, shared={writeOwnership.WrittenCount}, skippedUnshared={writeOwnership.SkippedCount}.");
            return true;
        }

        // Computes the unowned-definition plan without performing any fetch (the only cache access is the
        // read-only LoadFriendGameDefinitionStates). Run up front so the definitions progress sub-band
        // knows its full total before it emits a completion.
        private UnownedDefinitionPlan ComputeUnownedDefinitionPlan(
            string providerKey,
            IReadOnlyList<FriendOwnershipSnapshot> ownershipSnapshots,
            FriendRefreshOptions options)
        {
            var plan = new UnownedDefinitionPlan();
            var snapshots = ownershipSnapshots?
                .Where(snapshot => snapshot?.Friend != null && snapshot.Ownership?.Count > 0)
                .ToList();
            if (snapshots == null || snapshots.Count == 0)
            {
                return plan;
            }

            var requestedProviderGameKeys = options?.ProviderGameKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var requestedAppIds = options?.ProviderAppIds?
                .Where(id => id > 0)
                .Select(id => id.ToString())
                .Distinct()
                .ToList();
            var requestedKeys = new List<string>();
            if (requestedProviderGameKeys?.Count > 0)
            {
                requestedKeys.AddRange(requestedProviderGameKeys);
            }

            if (requestedAppIds?.Count > 0)
            {
                requestedKeys.AddRange(requestedAppIds);
            }

            var requestedKeySet = requestedKeys.Count > 0
                ? new HashSet<string>(requestedKeys, StringComparer.OrdinalIgnoreCase)
                : null;

            var ownershipByKey = snapshots
                .SelectMany(snapshot => snapshot.Ownership)
                .Where(item => HasProviderGameIdentity(item) &&
                               ShouldRefreshFriendGameDefinition(providerKey, item, options) &&
                               (requestedKeySet == null || requestedKeySet.Contains(GetProviderGameCacheKey(item))))
                .GroupBy(GetProviderGameCacheKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList());
            if (ownershipByKey.Count == 0)
            {
                return plan;
            }

            var providerGameKeys = ownershipByKey.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
            var states = _friendCache.LoadFriendGameDefinitionStates(providerKey, providerGameKeys) ??
                         new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase);
            var definitionTtl = options.DefinitionTtl.GetValueOrDefault(DefaultDefinitionTtl);
            if (definitionTtl <= TimeSpan.Zero)
            {
                definitionTtl = DefaultDefinitionTtl;
            }

            var cutoffUtc = DateTime.UtcNow - definitionTtl;
            var dueProviderGameKeys = options?.ForceDefinitionRefresh == true
                ? providerGameKeys
                : providerGameKeys
                    .Where(key => IsDefinitionCheckDue(states.TryGetValue(key, out var state) ? state : null, cutoffUtc))
                    .ToList();

            plan.OwnershipByKey = ownershipByKey;
            plan.ProviderGameKeys = providerGameKeys;
            plan.DueProviderGameKeys = dueProviderGameKeys;

            // Provider-only probe scrapes only happen for providers that guard zero-unlock games; count
            // exactly the items the probe loop below will visit so the definitions total stays exact.
            if (ShouldGuardProviderOnlyZeroUnlocks(providerKey))
            {
                var discovered = new HashSet<string>(providerGameKeys, StringComparer.OrdinalIgnoreCase);
                plan.ProbeItemCount = snapshots.Sum(snapshot => snapshot.Ownership.Count(item =>
                    HasProviderGameIdentity(item) &&
                    discovered.Contains(GetProviderGameCacheKey(item)) &&
                    !IsPlayniteLibraryFriendGame(providerKey, item)));
            }

            return plan;
        }

        private async Task RefreshUnownedDefinitionsAndOwnershipAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            IReadOnlyList<FriendOwnershipSnapshot> ownershipSnapshots,
            HashSet<string> probedProviderOnlyAchievementKeys,
            UnownedDefinitionPlan plan,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            var snapshots = ownershipSnapshots?
                .Where(snapshot => snapshot?.Friend != null && snapshot.Ownership?.Count > 0)
                .ToList();
            if (snapshots == null || snapshots.Count == 0 || plan == null || plan.OwnershipByKey.Count == 0)
            {
                return;
            }

            // The definitions total was set once from this plan before the phase began (see the pre-pass
            // in RefreshPreparedFriendContextsAsync); do not grow it here.
            var ownershipByKey = plan.OwnershipByKey;
            var providerGameKeys = plan.ProviderGameKeys;
            var dueProviderGameKeys = plan.DueProviderGameKeys;
            var noAchievementDefinitionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var noAchievementProbeSkips = 0;

            if (dueProviderGameKeys.Count > 0)
            {
                var limiter = CreateScanRateLimiter();
                for (var i = 0; i < dueProviderGameKeys.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var providerGameKey = dueProviderGameKeys[i];
                    var ownershipRows = ownershipByKey[providerGameKey];
                    var sample = ownershipRows.FirstOrDefault(item => item != null);
                    var appId = Math.Max(0, sample?.AppId ?? 0);
                    var gameName = ResolveOwnershipGameName(ownershipRows, providerKey, providerGameKey);
                    progress?.ReportDefinitionCheckActive(gameName);

                    await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
                    var definitionResult = await limiter.ExecuteWithRetryAsync(
                        () => friendsProvider.GetFriendGameDefinitionAsync(providerGameKey, appId, gameName, cancel),
                        IsTransientError,
                        cancel).ConfigureAwait(false);

                    if (definitionResult?.AuthRequired == true)
                    {
                        lock (payloadLock)
                        {
                            MarkAuthFailure(payload, providerKey, true);
                        }

                        return;
                    }

                    var definition = definitionResult?.Data ?? new FriendGameDefinition
                    {
                        ProviderKey = providerKey,
                        AppId = appId,
                        ProviderGameKey = providerGameKey,
                        GameName = gameName,
                        Status = definitionResult?.TransientFailure == true
                            ? FriendGameDefinitionStatus.Transient
                            : FriendGameDefinitionStatus.Unavailable,
                        LastCheckedUtc = DateTime.UtcNow
                    };

                    definition.ProviderKey = providerKey;
                    definition.AppId = appId;
                    definition.ProviderGameKey = providerGameKey;
                    if (string.IsNullOrWhiteSpace(definition.GameName))
                    {
                        definition.GameName = gameName;
                    }

                    await DownloadDefinitionAchievementIconsAsync(definition, cancel, progress).ConfigureAwait(false);

                    var writeDefinition = _friendCache.SaveFriendGameDefinition(providerKey, definition);
                    if (writeDefinition?.Success != true)
                    {
                        _logger?.Warn($"Failed to save friend game definition for {providerKey}/{providerGameKey}: {writeDefinition?.ErrorMessage}");
                    }

                    if (definition.Status == FriendGameDefinitionStatus.NoAchievements)
                    {
                        noAchievementDefinitionKeys.Add(providerGameKey);
                    }

                    // Download the achievements-page header banner and store it as the game's local
                    // icon+cover paths, mirroring the Steam owned-game image flow. The URL is never
                    // persisted.
                    await DownloadDefinitionGameImageAsync(providerKey, providerGameKey, appId, definition.IconUrl, definition.GameName, cancel, progress)
                        .ConfigureAwait(false);
                    progress?.ReportDefinitionCheckCompleted(gameName);
                }
            }

            var discoveredProviderGameKeys = new HashSet<string>(providerGameKeys, StringComparer.OrdinalIgnoreCase);
            var providerOnlyProbeLimiter = CreateScanRateLimiter();
            foreach (var snapshot in snapshots)
            {
                foreach (var item in snapshot.Ownership
                    .Where(item => HasProviderGameIdentity(item) && discoveredProviderGameKeys.Contains(GetProviderGameCacheKey(item))))
                {
                    var providerGameKey = GetProviderGameCacheKey(item);
                    // Mapped (Playnite-library) games are already persisted by the per-friend ownership
                    // save; only provider-only games need the probe to confirm unlocks before persisting.
                    if (IsPlayniteLibraryFriendGame(providerKey, item))
                    {
                        continue;
                    }

                    if (noAchievementDefinitionKeys.Contains(providerGameKey))
                    {
                        noAchievementProbeSkips++;
                        if (ShouldGuardProviderOnlyZeroUnlocks(providerKey))
                        {
                            progress?.ReportDefinitionCheckCompleted(
                                ResolveOwnershipGameName(new[] { item }, providerKey, providerGameKey));
                        }

                        continue;
                    }

                    var shouldContinue = await ProbeAndPersistProviderOnlyFriendGameAsync(
                        friendsProvider,
                        providerKey,
                        snapshot.Friend,
                        item,
                        probedProviderOnlyAchievementKeys,
                        providerOnlyProbeLimiter,
                        payload,
                        payloadLock,
                        progress,
                        cancel).ConfigureAwait(false);
                    // Provider-only probes are network scrapes counted in the definitions total (see
                    // ComputeUnownedDefinitionPlan); report one completion each so the bar advances
                    // through them. Gated on the same guard used to count them so total and completions
                    // stay in lockstep.
                    if (ShouldGuardProviderOnlyZeroUnlocks(providerKey))
                    {
                        progress?.ReportDefinitionCheckCompleted(
                            ResolveOwnershipGameName(new[] { item }, providerKey, GetProviderGameCacheKey(item)));
                    }

                    if (!shouldContinue)
                    {
                        return;
                    }
                }
            }

            await DownloadUnownedGameImagesAsync(providerKey, discoveredProviderGameKeys, ownershipByKey, cancel, progress).ConfigureAwait(false);
            _logger?.Debug(
                $"[RefreshPerf] phase=friend.definitions.provider provider={providerKey} providerKeys={providerGameKeys.Count} dueDefinitions={dueProviderGameKeys.Count} probeItems={plan.ProbeItemCount} noAchievementDefinitionKeys={noAchievementDefinitionKeys.Count} noAchievementProbeSkips={noAchievementProbeSkips}");
        }

        private async Task<bool> ProbeAndPersistProviderOnlyFriendGameAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            FriendIdentity friend,
            FriendGameOwnership ownership,
            HashSet<string> probedProviderOnlyAchievementKeys,
            RateLimiter limiter,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            if (friendsProvider == null ||
                friend == null ||
                ownership == null ||
                !ShouldGuardProviderOnlyZeroUnlocks(providerKey))
            {
                return true;
            }

            await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
            var scrapeResult = await limiter.ExecuteWithRetryAsync(
                () => friendsProvider.GetFriendGameAchievementsAsync(
                    friend,
                    ownership.ProviderGameKey,
                    ownership.AppId,
                    ownership.GameName,
                    cancel),
                IsTransientError,
                cancel).ConfigureAwait(false);

            if (scrapeResult?.AuthRequired == true)
            {
                lock (payloadLock)
                {
                    MarkAuthFailure(payload, providerKey, true);
                }

                return false;
            }

            if (scrapeResult?.Success != true)
            {
                return true;
            }

            var probedKey = BuildFriendProviderGameKey(friend.ExternalUserId, GetProviderGameCacheKey(ownership));
            if (!string.IsNullOrEmpty(probedKey) && probedProviderOnlyAchievementKeys != null)
            {
                lock (probedProviderOnlyAchievementKeys)
                {
                    probedProviderOnlyAchievementKeys.Add(probedKey);
                }
            }

            var achievements = scrapeResult.Data ?? new FriendGameAchievements();
            achievements.Friend = achievements.Friend ?? friend;
            achievements.AppId = achievements.AppId > 0 ? achievements.AppId : ownership.AppId;
            achievements.ProviderGameKey = string.IsNullOrWhiteSpace(achievements.ProviderGameKey)
                ? ownership.ProviderGameKey
                : achievements.ProviderGameKey;

            if (!HasAnyUnlockedFriendAchievements(achievements))
            {
                return true;
            }

            var writeOwnership = _friendCache.SaveFriendOwnership(
                providerKey,
                friend.ExternalUserId,
                new[] { ownership },
                new FriendOwnershipSaveOptions { IncludeProviderOnlyGames = true });
            if (writeOwnership?.Success != true)
            {
                _logger?.Warn($"Failed to save provider-only friend ownership for {providerKey}/{friend.ExternalUserId}: {writeOwnership?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.OwnershipRowsWritten += writeOwnership.WrittenCount;
            }

            var writeAchievements = _friendCache.SaveFriendGameAchievements(
                providerKey,
                friend.ExternalUserId,
                ownership.ProviderGameKey,
                ownership.AppId,
                achievements);
            if (writeAchievements?.Success != true)
            {
                _logger?.Warn($"Failed to save probed provider-only friend achievements for {providerKey}/{friend.ExternalUserId}/{GetProviderGameCacheKey(ownership)}: {writeAchievements?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.CandidatesRefreshed++;
                payload.FriendSummary.AchievementsSaved++;
            }

            return true;
        }

        private async Task DownloadFriendAvatarsAsync(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends,
            CancellationToken cancel)
        {
            if (friends == null || friends.Count == 0)
            {
                return;
            }

            var friendsWithAvatars = friends
                .Where(friend => friend != null && DiskImageService.IsCacheableImageSource(friend.AvatarUrl))
                .ToList();
            if (friendsWithAvatars.Count == 0)
            {
                return;
            }

            // Because the avatar filename no longer changes when the source URL changes, compare the
            // incoming URL against the persisted one (single load per provider) so a friend's new
            // avatar is re-downloaded while unchanged avatars reuse the cached file.
            var persistedAvatarUrls = LoadPersistedAvatarUrls(providerKey);

            await Task.WhenAll(friendsWithAvatars
                    .Select(async friend =>
                    {
                        try
                        {
                            persistedAvatarUrls.TryGetValue(friend.ExternalUserId ?? string.Empty, out var previousUrl);
                            await _achievementIconService
                                .PopulateFriendAvatarIconCacheAsync(providerKey, friend, previousUrl, cancel)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, $"Failed to cache friend avatar for {providerKey}/{friend.ExternalUserId}.");
                        }
                    }))
                .ConfigureAwait(false);
        }

        private Dictionary<string, string> LoadPersistedAvatarUrls(string providerKey)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var persisted = _friendCache?.LoadFriendIdentities(providerKey);
                if (persisted != null)
                {
                    foreach (var identity in persisted)
                    {
                        if (identity != null && !string.IsNullOrWhiteSpace(identity.ExternalUserId))
                        {
                            map[identity.ExternalUserId] = identity.AvatarUrl;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to load persisted friend avatar URLs for {providerKey}.");
            }

            return map;
        }

        // Progress denominators for the friend image download loops. These mirror what the loops
        // actually attempt (one unit per distinct cacheable game image), using the same
        // DiskImageService.IsCacheableImageSource predicate the downloads use, so the total can't drift
        // from the reported completions.
        private static int CountFriendGameImageSources(
            IEnumerable<string> providerGameKeys,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey)
        {
            if (providerGameKeys == null || ownershipByKey == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var providerGameKey in providerGameKeys)
            {
                if (string.IsNullOrWhiteSpace(providerGameKey) ||
                    !ownershipByKey.TryGetValue(providerGameKey, out var owners))
                {
                    continue;
                }

                var source = owners?.FirstOrDefault(item => item != null);
                if (source == null)
                {
                    continue;
                }

                total += CountDistinctCacheableSources(source.IconUrl, source.CoverUrl);
            }

            return total;
        }

        private static int CountDistinctCacheableSources(params string[] sources)
        {
            if (sources == null || sources.Length == 0)
            {
                return 0;
            }

            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                if (DiskImageService.IsCacheableImageSource(source))
                {
                    distinct.Add(source);
                }
            }

            return distinct.Count;
        }

        private async Task DownloadDefinitionAchievementIconsAsync(
            FriendGameDefinition definition,
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            if (_achievementIconService == null)
            {
                return;
            }

            // The icon pipeline no-ops on empty input and reports its own (completed, total), so no
            // pre-count guard is needed here.
            await _achievementIconService
                .PopulateFriendAchievementIconCacheAsync(
                    definition,
                    cancel,
                    (completed, total) => progress?.ReportAchievementImages(completed, total, definition.GameName))
                .ConfigureAwait(false);
        }

        private async Task DownloadUnownedGameImagesAsync(
            string providerKey,
            HashSet<string> providerGameKeys,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey,
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            if (providerGameKeys == null || providerGameKeys.Count == 0)
            {
                return;
            }

            var imageAttempts = CountFriendGameImageSources(providerGameKeys, ownershipByKey);
            if (imageAttempts <= 0)
            {
                return;
            }

            var completed = 0;
            progress?.ReportFriendGameImages(0, imageAttempts);
            void ReportImageCompleted(string gameName)
            {
                var count = Interlocked.Increment(ref completed);
                progress?.ReportFriendGameImages(count, imageAttempts, gameName);
            }

            await Task.WhenAll(providerGameKeys.Select(providerGameKey =>
                    DownloadUnownedGameImageAsync(providerKey, providerGameKey, ownershipByKey, ReportImageCompleted, cancel)))
                .ConfigureAwait(false);
        }

        private async Task DownloadDefinitionGameImageAsync(
            string providerKey,
            string providerGameKey,
            int appId,
            string bannerUrl,
            string gameName,
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            var imageAttempts = DiskImageService.IsCacheableImageSource(bannerUrl) ? 1 : 0;
            if (imageAttempts <= 0)
            {
                return;
            }

            var cacheKey = GetProviderGameCacheKey(appId, providerGameKey);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            var detail = !string.IsNullOrWhiteSpace(gameName) ? gameName : cacheKey;
            progress?.ReportFriendGameImages(0, imageAttempts, detail);
            var completed = 0;
            try
            {
                var localPath = await _achievementIconService
                    .PopulateFriendGameIconCacheAsync(
                        providerKey,
                        cacheKey,
                        bannerUrl,
                        cancel,
                        () => progress?.ReportFriendGameImages(Interlocked.Increment(ref completed), imageAttempts, detail))
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    return;
                }

                var writeImages = _friendCache.SaveProviderGameImagePaths(providerKey, cacheKey, appId, localPath, localPath);
                if (writeImages?.Success != true)
                {
                    _logger?.Warn($"Failed to save friend game header image paths for {providerKey}/{cacheKey}: {writeImages?.ErrorMessage}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to cache friend game header image for {providerKey}/{cacheKey}.");
            }
        }

        private async Task DownloadUnownedGameImageAsync(
            string providerKey,
            string providerGameKey,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey,
            Action<string> reportImageCompleted,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(providerGameKey) ||
                ownershipByKey == null ||
                !ownershipByKey.TryGetValue(providerGameKey, out var owners))
            {
                return;
            }

            var source = owners?.FirstOrDefault(item => item != null);
            if (source == null)
            {
                return;
            }

            var gameKey = GetProviderGameCacheKey(source.AppId, source.ProviderGameKey) ??
                          (string.IsNullOrWhiteSpace(providerGameKey) ? null : providerGameKey.Trim());
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                return;
            }

            var gameName = !string.IsNullOrWhiteSpace(source.GameName) ? source.GameName : gameKey;
            try
            {
                var result = await _achievementIconService
                    .PopulateFriendGameImageCacheAsync(
                        providerKey,
                        gameKey,
                        source.IconUrl,
                        source.CoverUrl,
                        cancel,
                        () => reportImageCompleted?.Invoke(gameName))
                    .ConfigureAwait(false);
                if (result?.HasAnyPath != true)
                {
                    return;
                }

                var writeImages = _friendCache.SaveProviderGameImagePaths(providerKey, gameKey, source.AppId, result.IconPath, result.CoverPath);
                if (writeImages?.Success != true)
                {
                    _logger?.Warn($"Failed to save unowned game image paths for {providerKey}/{gameKey}: {writeImages?.ErrorMessage}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to cache unowned game images for {providerKey}/{providerGameKey}.");
            }
        }

        private async Task<bool> RefreshAchievementCandidateAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            FriendRefreshCandidate candidate,
            RebuildPayload payload,
            object payloadLock,
            bool delayBeforeRequest,
            RateLimiter limiter,
            CancellationToken cancel)
        {
            if (candidate?.Friend == null || !HasProviderGameIdentity(candidate.AppId, candidate.ProviderGameKey))
            {
                return true;
            }

            if (delayBeforeRequest)
            {
                await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
            }

            var scrapeResult = await limiter.ExecuteWithRetryAsync(
                () => friendsProvider.GetFriendGameAchievementsAsync(
                    candidate.Friend,
                    candidate.ProviderGameKey,
                    candidate.AppId,
                    candidate.GameName,
                    cancel),
                IsTransientError,
                cancel).ConfigureAwait(false);

            if (scrapeResult?.AuthRequired == true)
            {
                lock (payloadLock)
                {
                    MarkAuthFailure(payload, providerKey, true);
                }

                return false;
            }

            var achievements = scrapeResult?.Data ?? CreateFailureResult(candidate, scrapeResult);
            if (ShouldSkipProviderOnlyZeroUnlocks(providerKey, candidate, scrapeResult, achievements))
            {
                lock (payloadLock)
                {
                    payload.FriendSummary.CandidatesRefreshed++;
                }

                return true;
            }

            var writeAchievements = _friendCache.SaveFriendGameAchievements(
                providerKey,
                candidate.Friend.ExternalUserId,
                candidate.ProviderGameKey,
                candidate.AppId,
                achievements);
            if (writeAchievements?.Success != true)
            {
                _logger?.Warn($"Failed to save friend achievements for {providerKey}/{candidate.Friend.ExternalUserId}/{GetProviderGameCacheKey(candidate)}: {writeAchievements?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.CandidatesRefreshed++;
                payload.FriendSummary.AchievementsSaved++;
            }

            return true;
        }

        private int ResolveFriendRefreshParallelism()
        {
            return (_settings?.Persisted?.EnableParallelProviderRefresh ?? true)
                ? FriendRefreshParallelism
                : 1;
        }

        private bool TryPrepareFriendRosterFromSettingsOrCache(
            string providerKey,
            FriendRefreshOptions options,
            FriendProviderRefreshContext context,
            RebuildPayload payload)
        {
            if (context == null)
            {
                return false;
            }

            var selectedIds = NormalizeFriendSelectionIds(options?.FriendExternalUserIds);
            var ignoredIds = GetIgnoredFriendIds(providerKey);
            var focused = IsFocusedFriendGameRefresh(options);
            var friends = LoadConfiguredFriendIdentities(providerKey, ignoredIds, out var source);
            var lookup = friends
                .Where(friend => friend != null && !string.IsNullOrWhiteSpace(friend.ExternalUserId))
                .GroupBy(friend => friend.ExternalUserId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var scoped = new List<FriendIdentity>();
            var synthesized = 0;
            if (selectedIds.Count > 0)
            {
                foreach (var id in selectedIds)
                {
                    if (ignoredIds.Contains(id))
                    {
                        continue;
                    }

                    if (lookup.TryGetValue(id, out var friend))
                    {
                        scoped.Add(friend);
                        continue;
                    }

                    if (!focused)
                    {
                        continue;
                    }

                    synthesized++;
                    scoped.Add(new FriendIdentity
                    {
                        ProviderKey = providerKey,
                        ExternalUserId = id,
                        DisplayName = id
                    });
                }
            }
            else
            {
                scoped.AddRange(lookup.Values);
            }

            if (scoped.Count == 0)
            {
                _logger?.Debug(
                    $"Friends refresh skipped for {providerKey}: no configured or cached active friends available for refresh.");
                context.RosterSource = source;
                return false;
            }

            var allFriends = lookup.Values.ToList();
            foreach (var friend in scoped)
            {
                if (!string.IsNullOrWhiteSpace(friend.ExternalUserId) &&
                    !lookup.ContainsKey(friend.ExternalUserId.Trim()))
                {
                    allFriends.Add(friend);
                }
            }

            context.Friends = allFriends;
            context.ScopedFriends = scoped;
            context.RosterSource = synthesized > 0
                ? (lookup.Count > 0 ? source + "+request" : "request")
                : source;
            payload.FriendSummary.FriendsFetched += context.Friends.Count;

            _logger?.Debug(
                $"[RefreshPerf] phase=friend.roster provider={providerKey} source={context.RosterSource} friends={context.Friends.Count} scopedFriends={context.ScopedFriends.Count} selectedIds={selectedIds.Count} synthesized={synthesized}");
            return true;
        }

        private List<FriendIdentity> LoadConfiguredFriendIdentities(
            string providerKey,
            HashSet<string> ignoredIds,
            out string source)
        {
            var settingsFriends = NormalizeProviderFriendIdentities(
                    providerKey,
                    _settings?.Persisted?.GetActiveFriendIdentities(providerKey))
                .Where(friend => friend != null &&
                                 !string.IsNullOrWhiteSpace(friend.ExternalUserId) &&
                                 !(ignoredIds?.Contains(friend.ExternalUserId.Trim()) == true))
                .ToList();
            if (settingsFriends.Count > 0)
            {
                source = "settings";
                return settingsFriends;
            }

            try
            {
                var cachedFriends = NormalizeProviderFriendIdentities(
                        providerKey,
                        _friendCache?.LoadFriendIdentities(providerKey))
                    .Where(friend => friend != null &&
                                     !string.IsNullOrWhiteSpace(friend.ExternalUserId) &&
                                     !(ignoredIds?.Contains(friend.ExternalUserId.Trim()) == true))
                    .ToList();
                source = cachedFriends.Count > 0 ? "cache" : "none";
                return cachedFriends;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Friend roster cache lookup failed for {providerKey}.");
                source = "cache-error";
                return new List<FriendIdentity>();
            }
        }

        private IReadOnlyList<FriendIdentity> FilterIgnoredFriends(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends)
        {
            var ignoredIds = GetIgnoredFriendIds(providerKey);
            if (ignoredIds.Count == 0)
            {
                return friends ?? Array.Empty<FriendIdentity>();
            }

            return (friends ?? Array.Empty<FriendIdentity>())
                .Where(friend => !ignoredIds.Contains(friend?.ExternalUserId ?? string.Empty))
                .ToList();
        }

        private List<LogicalFriendRefreshGroup> BuildLogicalFriendGroups(
            IReadOnlyList<FriendProviderRefreshContext> contexts)
        {
            var groups = new Dictionary<string, LogicalFriendRefreshGroup>(StringComparer.OrdinalIgnoreCase);
            var accountLookup = new Dictionary<string, FriendIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var context in contexts ?? Array.Empty<FriendProviderRefreshContext>())
            {
                foreach (var friend in (IEnumerable<FriendIdentity>)context?.ScopedFriends ?? Array.Empty<FriendIdentity>())
                {
                    var accountKey = FriendAccountRef.BuildKey(context.ProviderKey, friend?.ExternalUserId);
                    if (!string.IsNullOrWhiteSpace(accountKey) && !accountLookup.ContainsKey(accountKey))
                    {
                        accountLookup[accountKey] = friend;
                    }
                }
            }

            foreach (var context in contexts ?? Array.Empty<FriendProviderRefreshContext>())
            {
                foreach (var friend in (IEnumerable<FriendIdentity>)context?.ScopedFriends ?? Array.Empty<FriendIdentity>())
                {
                    if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
                    {
                        continue;
                    }

                    var mergeGroup = _settings?.Persisted?.GetFriendMergeGroupForAccount(context.ProviderKey, friend.ExternalUserId);
                    var groupKey = !string.IsNullOrWhiteSpace(mergeGroup?.Id)
                        ? "merged|" + mergeGroup.Id
                        : "account|" + FriendAccountRef.BuildKey(context.ProviderKey, friend.ExternalUserId);
                    if (string.IsNullOrWhiteSpace(groupKey))
                    {
                        continue;
                    }

                    if (!groups.TryGetValue(groupKey, out var group))
                    {
                        group = new LogicalFriendRefreshGroup
                        {
                            Key = groupKey,
                            DisplayName = ResolveLogicalFriendDisplayName(mergeGroup, friend, accountLookup)
                        };
                        groups[groupKey] = group;
                    }

                    group.Accounts.Add(new FriendAccountRefreshItem
                    {
                        Context = context,
                        Friend = friend
                    });
                }
            }

            return groups.Values
                .OrderBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string ResolveLogicalFriendDisplayName(
            FriendMergeGroup mergeGroup,
            FriendIdentity fallbackFriend,
            IReadOnlyDictionary<string, FriendIdentity> accountLookup)
        {
            if (!string.IsNullOrWhiteSpace(mergeGroup?.Nickname))
            {
                return mergeGroup.Nickname.Trim();
            }

            foreach (var member in mergeGroup?.Members ?? Enumerable.Empty<FriendAccountRef>())
            {
                if (!string.IsNullOrWhiteSpace(member?.Key) &&
                    accountLookup != null &&
                    accountLookup.TryGetValue(member.Key, out var friend) &&
                    !string.IsNullOrWhiteSpace(friend?.DisplayName))
                {
                    return friend.DisplayName.Trim();
                }
            }

            return GetFriendDisplayName(fallbackFriend);
        }

        private static IReadOnlyList<FriendIdentity> NormalizeProviderFriendIdentities(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends)
        {
            return (friends ?? Array.Empty<FriendIdentity>())
                .Where(friend => !string.IsNullOrWhiteSpace(friend?.ExternalUserId))
                .Select(friend => new FriendIdentity
                {
                    ProviderKey = string.IsNullOrWhiteSpace(friend.ProviderKey)
                        ? providerKey
                        : friend.ProviderKey.Trim(),
                    ExternalUserId = friend.ExternalUserId.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(friend.DisplayName)
                        ? friend.ExternalUserId.Trim()
                        : friend.DisplayName.Trim(),
                    AvatarUrl = string.IsNullOrWhiteSpace(friend.AvatarUrl) ? null : friend.AvatarUrl.Trim(),
                    AvatarPath = string.IsNullOrWhiteSpace(friend.AvatarPath) ? null : friend.AvatarPath.Trim(),
                    LastRefreshedUtc = friend.LastRefreshedUtc
                })
                .GroupBy(friend => friend.ExternalUserId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        // Restricts per-friend work to the requested subset when the options carry a friend
        // selection (e.g. a custom refresh of specific friends); otherwise returns all friends.
        private static IReadOnlyList<FriendIdentity> ScopeFriendsToSelection(
            IReadOnlyList<FriendIdentity> friends,
            FriendRefreshOptions options)
        {
            var all = friends ?? Array.Empty<FriendIdentity>();
            var selectedIds = NormalizeFriendSelectionIds(options?.FriendExternalUserIds);
            if (selectedIds.Count == 0)
            {
                return all;
            }

            return all
                .Where(friend => friend != null && selectedIds.Contains(friend.ExternalUserId?.Trim() ?? string.Empty))
                .ToList();
        }

        private static HashSet<string> NormalizeFriendSelectionIds(IReadOnlyCollection<string> friendExternalUserIds)
        {
            return new HashSet<string>(
                friendExternalUserIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()) ??
                Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        private List<FriendRefreshCandidate> FilterIgnoredCandidates(
            string providerKey,
            List<FriendRefreshCandidate> candidates)
        {
            var ignoredIds = GetIgnoredFriendIds(providerKey);
            if (ignoredIds.Count == 0)
            {
                return candidates ?? new List<FriendRefreshCandidate>();
            }

            return (candidates ?? new List<FriendRefreshCandidate>())
                .Where(candidate => !ignoredIds.Contains(candidate?.Friend?.ExternalUserId ?? string.Empty))
                .ToList();
        }

        private List<FriendRefreshCandidate> FilterProviderOnlyDetailCandidates(
            string providerKey,
            List<FriendRefreshCandidate> candidates,
            FriendRefreshOptions options)
        {
            // This cache-sourced loader now serves only the scopes that do not build candidates from the
            // fresh ownership snapshot (Recent, SelectedGame, Custom). Recent and SelectedGame resolve to
            // library-mapped games; Custom provider-only targets are explicit and, when discovered this
            // run, are de-duplicated against the probe via ProbedProviderOnlyAchievementKeys upstream.
            return (candidates ?? new List<FriendRefreshCandidate>())
                .Where(candidate => ShouldRefreshFriendGameAchievements(providerKey, candidate, options))
                .ToList();
        }

        private HashSet<string> GetIgnoredFriendIds(string providerKey)
        {
            return _settings?.Persisted?.GetIgnoredFriendIds(providerKey) ??
                   new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private bool IsPlayniteLibraryFriendGame(string providerKey, FriendGameOwnership ownership)
        {
            if (ownership?.PlayniteGameId.HasValue == true &&
                ownership.PlayniteGameId.Value != Guid.Empty)
            {
                return true;
            }

            return _friendCache?.IsProviderGameMappedToPlayniteLibrary(
                providerKey,
                ownership?.AppId ?? 0,
                ownership?.ProviderGameKey) == true;
        }

        private bool IsPlayniteLibraryFriendGame(string providerKey, FriendRefreshCandidate candidate)
        {
            if (candidate?.PlayniteGameId.HasValue == true &&
                candidate.PlayniteGameId.Value != Guid.Empty)
            {
                return true;
            }

            return _friendCache?.IsProviderGameMappedToPlayniteLibrary(
                providerKey,
                candidate?.AppId ?? 0,
                candidate?.ProviderGameKey) == true;
        }

        private bool ShouldRefreshFriendGameDefinition(
            string providerKey,
            FriendGameOwnership ownership,
            FriendRefreshOptions options)
        {
            if (ownership == null)
            {
                return false;
            }

            // Games the provider reports as having no achievements are never candidates (symmetric with
            // the mapped-unlock builder). Excludes them before the schema fetch rather than discovering
            // it only after fetching a NoAchievements definition.
            if (ownership.AchievementTotalHint.HasValue && ownership.AchievementTotalHint.Value <= 0)
            {
                return false;
            }

            if (IsPlayniteLibraryFriendGame(providerKey, ownership))
            {
                return true;
            }

            if (IsExplicitProviderGameTarget(options, ownership.AppId, ownership.ProviderGameKey))
            {
                return true;
            }

            if (HasZeroUnlockHint(ownership))
            {
                return false;
            }

            if (HasPositiveUnlockHint(ownership))
            {
                return true;
            }

            // Hint unknown (provider did not supply an earned count): in a scope that discovers
            // provider-only games (Full) scrape it anyway rather than silently dropping the game.
            // The post-scrape zero-unlock guard prunes it if it turns out empty.
            return options?.DiscoversProviderOnlyGames() == true;
        }

        private bool ShouldRefreshFriendGameAchievements(
            string providerKey,
            FriendRefreshCandidate candidate,
            FriendRefreshOptions options)
        {
            if (candidate?.Friend == null || !HasProviderGameIdentity(candidate.AppId, candidate.ProviderGameKey))
            {
                return false;
            }

            if (IsPlayniteLibraryFriendGame(providerKey, candidate))
            {
                return true;
            }

            if (IsExplicitProviderGameTarget(options, candidate.AppId, candidate.ProviderGameKey))
            {
                return true;
            }

            // Cache-sourced candidates carry no unlock hint (the hint is a live-scrape-only signal), so
            // candidacy for the remaining games is decided by scope: a discovery scope keeps them, others
            // (the mapped checks above already returned) drop them.
            return options?.DiscoversProviderOnlyGames() == true;
        }

        private static IReadOnlyList<FriendGameOwnership> ScopeOwnedGamesForRefresh(
            IReadOnlyList<FriendGameOwnership> ownedGames,
            FriendRefreshOptions options)
        {
            var source = ownedGames ?? Array.Empty<FriendGameOwnership>();
            if (!HasExplicitProviderGameTargets(options))
            {
                return source;
            }

            return source
                .Where(item => IsExplicitProviderGameTarget(options, item?.AppId ?? 0, item?.ProviderGameKey))
                .ToList();
        }

        private static bool HasPositiveUnlockHint(FriendGameOwnership ownership)
        {
            return ownership?.AchievementUnlocksHint > 0;
        }

        private static bool HasZeroUnlockHint(FriendGameOwnership ownership)
        {
            return ownership?.AchievementUnlocksHint.HasValue == true &&
                   ownership.AchievementUnlocksHint.GetValueOrDefault() <= 0;
        }

        private static bool IsExplicitProviderGameTarget(
            FriendRefreshOptions options,
            int appId,
            string providerGameKey)
        {
            if (options == null)
            {
                return false;
            }

            if (appId > 0 && options.ProviderAppIds?.Any(id => id == appId) == true)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(providerGameKey) &&
                options.ProviderGameKeys?.Any(key =>
                    string.Equals(key?.Trim(), providerGameKey.Trim(), StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }

            return false;
        }

        private static bool HasExplicitProviderGameTargets(FriendRefreshOptions options)
        {
            return options?.ProviderAppIds?.Any(id => id > 0) == true ||
                   options?.ProviderGameKeys?.Any(key => !string.IsNullOrWhiteSpace(key)) == true;
        }

        private static bool IsFocusedFriendGameRefresh(FriendRefreshOptions options)
        {
            return options?.Scope == FriendRefreshScope.SelectedGame ||
                   HasExplicitProviderGameTargets(options) ||
                   (options?.Scope == FriendRefreshScope.Custom &&
                    options.PlayniteGameIds?.Any(id => id != Guid.Empty) == true);
        }

        private bool ShouldSkipProviderOnlyZeroUnlocks(
            string providerKey,
            FriendRefreshCandidate candidate,
            FriendsProviderResult<FriendGameAchievements> scrapeResult,
            FriendGameAchievements achievements)
        {
            return ShouldGuardProviderOnlyZeroUnlocks(providerKey) &&
                   scrapeResult?.Success == true &&
                   candidate?.Friend != null &&
                   HasProviderGameIdentity(candidate.AppId, candidate.ProviderGameKey) &&
                   !IsPlayniteLibraryFriendGame(providerKey, candidate) &&
                   !HasAnyUnlockedFriendAchievements(achievements);
        }

        private RateLimiter CreateScanRateLimiter()
        {
            return new RateLimiter(
                Math.Max(0, _settings?.Persisted?.ScanDelayMs ?? 200),
                Math.Max(0, _settings?.Persisted?.MaxRetryAttempts ?? 3));
        }

        private static async Task RunBoundedAsync<T>(
            IReadOnlyList<T> items,
            int maxDegreeOfParallelism,
            Func<T, int, CancellationToken, Task> body,
            CancellationToken cancel)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (items == null || items.Count == 0)
            {
                return;
            }

            var degree = Math.Max(1, Math.Min(maxDegreeOfParallelism, items.Count));
            if (degree == 1)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    await body(items[i], i, cancel).ConfigureAwait(false);
                }

                return;
            }

            var nextIndex = -1;
            var workers = Enumerable.Range(0, degree)
                .Select(_ => WorkerAsync())
                .ToArray();

            await Task.WhenAll(workers).ConfigureAwait(false);

            async Task WorkerAsync()
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    var index = Interlocked.Increment(ref nextIndex);
                    if (index >= items.Count)
                    {
                        return;
                    }

                    await body(items[index], index, cancel).ConfigureAwait(false);
                }
            }
        }

        private static bool IsTransientError(Exception ex)
        {
            if (ex == null || ex is OperationCanceledException)
            {
                return false;
            }

            var message = ex.Message ?? string.Empty;
            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("temporarily", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("502", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("503", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("504", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 message.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            return ex.InnerException != null &&
                   !ReferenceEquals(ex.InnerException, ex) &&
                   IsTransientError(ex.InnerException);
        }

        private static bool IsDefinitionCheckDue(FriendGameDefinitionState state, DateTime cutoffUtc)
        {
            if (state == null || !state.LastCheckedUtc.HasValue)
            {
                return true;
            }

            if (state.Status != FriendGameDefinitionStatus.Ok)
            {
                return true;
            }

            return state.LastCheckedUtc.Value < cutoffUtc;
        }

        private static string ResolveOwnershipGameName(
            IReadOnlyList<FriendGameOwnership> ownership,
            string providerKey,
            string providerGameKey)
        {
            var name = ownership?
                .Select(item => item?.GameName)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : $"{providerKey} Game {providerGameKey}";
        }

        private static bool HasProviderGameIdentity(FriendGameOwnership ownership)
        {
            return ownership != null && HasProviderGameIdentity(ownership.AppId, ownership.ProviderGameKey);
        }

        private static bool HasProviderGameIdentity(int appId, string providerGameKey)
        {
            return appId > 0 || !string.IsNullOrWhiteSpace(providerGameKey);
        }

        private static string GetProviderGameCacheKey(FriendGameOwnership ownership)
        {
            return ownership == null ? null : GetProviderGameCacheKey(ownership.AppId, ownership.ProviderGameKey);
        }

        private static string GetProviderGameCacheKey(FriendRefreshCandidate candidate)
        {
            return candidate == null ? null : GetProviderGameCacheKey(candidate.AppId, candidate.ProviderGameKey);
        }

        private static string GetProviderGameCacheKey(int appId, string providerGameKey)
        {
            return !string.IsNullOrWhiteSpace(providerGameKey)
                ? providerGameKey.Trim()
                : (appId > 0 ? appId.ToString() : null);
        }

        private static string BuildRecencyGameKey(string externalUserId, string providerGameCacheKey)
        {
            return BuildFriendProviderGameKey(externalUserId, providerGameCacheKey);
        }

        private static string BuildFriendProviderGameKey(string externalUserId, string providerGameCacheKey)
        {
            return (externalUserId?.Trim() ?? string.Empty) + (char)31 + (providerGameCacheKey ?? string.Empty);
        }


        // Recent-scope recency: does the freshly-fetched ownership show new activity since the last
        // successful scrape? Steam uses playtime (the reliably-scraped signal); RA/Exophase use the
        // last-played / last-unlock timestamp. Never-seen, never-scraped, and previously-failed games
        // are always considered stale so they get (re)scraped.
        private static bool IsRecencyStale(string providerKey, FriendGameOwnership fresh, FriendOwnershipRecency prev)
        {
            if (fresh == null)
            {
                return false;
            }

            if (prev == null || !prev.LastScrapedUtc.HasValue ||
                !string.Equals(prev.LastScrapeStatus, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                return fresh.PlaytimeForeverMinutes > prev.PlaytimeForeverMinutes;
            }

            return fresh.LastPlayedUtc.HasValue &&
                   (!prev.LastPlayedUtc.HasValue || fresh.LastPlayedUtc.Value > prev.LastPlayedUtc.Value);
        }

        private FriendRefreshOptions NormalizeOptions(FriendRefreshOptions options)
        {
            var normalized = options?.Clone() ?? new FriendRefreshOptions();
            if (!normalized.DefinitionTtl.HasValue || normalized.DefinitionTtl.Value <= TimeSpan.Zero)
            {
                normalized.DefinitionTtl = DefaultDefinitionTtl;
            }

            return normalized;
        }

        private static bool ShouldRefreshOwnership(string providerKey, FriendRefreshOptions options)
        {
            if (options == null)
            {
                return false;
            }

            // Full, Shared, Installed and Recent all resolve their candidates from the friend's cached
            // ownership, so each needs the ownership fetched. SelectedGame targets a specific
            // current-user library game (cross-joined with friends) and needs no ownership fetch.
            if (options.Scope == FriendRefreshScope.Full ||
                options.Scope == FriendRefreshScope.Shared ||
                options.Scope == FriendRefreshScope.Installed ||
                options.Scope == FriendRefreshScope.Recent)
            {
                return true;
            }

            if (HasExplicitProviderGameTargets(options))
            {
                return true;
            }

            if (options.Scope == FriendRefreshScope.SelectedGame ||
                options.PlayniteGameIds?.Any(id => id != Guid.Empty) == true)
            {
                return false;
            }

            return options.Scope == FriendRefreshScope.Custom && RequiresOwnershipMapping(providerKey);
        }

        private static bool RequiresAnyOwnershipRefresh(
            IEnumerable<FriendProviderRefreshContext> contexts,
            FriendRefreshOptions options)
        {
            return (contexts ?? Enumerable.Empty<FriendProviderRefreshContext>())
                .Any(context => context != null && ShouldRefreshOwnership(context.ProviderKey, options));
        }

        private static bool RequiresOwnershipMapping(string providerKey)
        {
            return string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase);
        }

        // The discovery scopes (Full/Shared/Installed) resolve their scrape candidates from the fresh,
        // hint-bearing ownership snapshot (game-centric). Recent draws from the whole cached friend
        // library filtered by the recency gate, and SelectedGame/Custom target specific games across
        // friends; those keep the cache-backed candidate loader.
        private static bool UsesSnapshotCandidateBuilder(FriendRefreshOptions options)
        {
            switch (options?.Scope)
            {
                case FriendRefreshScope.Full:
                case FriendRefreshScope.Shared:
                case FriendRefreshScope.Installed:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldDiscoverUnowned(string providerKey, FriendRefreshOptions options)
        {
            return options?.DiscoversProviderOnlyGames() == true &&
                   SupportsProviderOnlyFriendDetails(providerKey);
        }

        private static bool ShouldGuardProviderOnlyZeroUnlocks(string providerKey)
        {
            return SupportsProviderOnlyFriendDetails(providerKey);
        }

        private static bool SupportsProviderOnlyFriendDetails(string providerKey)
        {
            return string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(providerKey, "RetroAchievements", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyUnlockedFriendAchievements(FriendGameAchievements achievements)
        {
            return achievements?.Rows?.Any(row => row?.Unlocked == true) == true;
        }

        private static string GetFriendDisplayName(FriendIdentity friend)
        {
            if (!string.IsNullOrWhiteSpace(friend?.DisplayName))
            {
                return friend.DisplayName.Trim();
            }

            return friend?.ExternalUserId?.Trim();
        }

        private static string FormatFriendGameDetail(FriendRefreshCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            var friendName = GetFriendDisplayName(candidate.Friend);
            var gameName = !string.IsNullOrWhiteSpace(candidate.GameName)
                ? candidate.GameName.Trim()
                : GetProviderGameCacheKey(candidate);

            if (string.IsNullOrWhiteSpace(friendName))
            {
                return gameName;
            }

            if (string.IsNullOrWhiteSpace(gameName))
            {
                return friendName;
            }

            return $"{friendName} - {gameName}";
        }

        private static void MarkAuthFailure(RebuildPayload payload, string providerKey, bool authRequired)
        {
            if (!authRequired || payload == null)
            {
                return;
            }

            payload.AuthRequired = true;
            if (!string.IsNullOrWhiteSpace(providerKey) &&
                !payload.FailedProviderKeys.Contains(providerKey, StringComparer.OrdinalIgnoreCase))
            {
                payload.FailedProviderKeys.Add(providerKey);
            }
        }

        private static void Merge(RebuildPayload target, RebuildPayload source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.AuthRequired |= source.AuthRequired;
            foreach (var key in source.FailedProviderKeys ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(key) &&
                    !target.FailedProviderKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    target.FailedProviderKeys.Add(key);
                }
            }

            target.Summary.GamesRefreshed += source.Summary?.GamesRefreshed ?? 0;
            target.Summary.GamesWithAchievements += source.Summary?.GamesWithAchievements ?? 0;
            target.Summary.GamesWithoutAchievements += source.Summary?.GamesWithoutAchievements ?? 0;
            foreach (var gameId in source.Summary?.RefreshedGameIds ?? Enumerable.Empty<Guid>())
            {
                if (gameId != Guid.Empty && !target.Summary.RefreshedGameIds.Contains(gameId))
                {
                    target.Summary.RefreshedGameIds.Add(gameId);
                }
            }

            target.FriendSummary.ProvidersProcessed += source.FriendSummary?.ProvidersProcessed ?? 0;
            target.FriendSummary.FriendsFetched += source.FriendSummary?.FriendsFetched ?? 0;
            target.FriendSummary.FriendsSaved += source.FriendSummary?.FriendsSaved ?? 0;
            target.FriendSummary.OwnershipPagesRefreshed += source.FriendSummary?.OwnershipPagesRefreshed ?? 0;
            target.FriendSummary.OwnershipRowsWritten += source.FriendSummary?.OwnershipRowsWritten ?? 0;
            target.FriendSummary.CandidatesLoaded += source.FriendSummary?.CandidatesLoaded ?? 0;
            target.FriendSummary.CandidatesRefreshed += source.FriendSummary?.CandidatesRefreshed ?? 0;
            target.FriendSummary.AchievementsSaved += source.FriendSummary?.AchievementsSaved ?? 0;
        }

        private static FriendGameAchievements CreateFailureResult(
            FriendRefreshCandidate candidate,
            FriendsProviderResult<FriendGameAchievements> scrapeResult)
        {
            return new FriendGameAchievements
            {
                Friend = candidate.Friend,
                AppId = candidate.AppId,
                ProviderGameKey = candidate.ProviderGameKey,
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = true,
                TransientFailure = scrapeResult?.TransientFailure == true,
                DetailCode = SteamScrapeDetail.None
            };
        }

        private sealed class FriendOwnershipSnapshot
        {
            public FriendIdentity Friend { get; set; }
            public List<FriendGameOwnership> Ownership { get; set; } = new List<FriendGameOwnership>();
        }

        private sealed class FriendProviderRefreshContext
        {
            public IFriendsProvider Provider { get; set; }
            public string ProviderKey { get; set; }
            public FriendsRefreshPreparation Preparation { get; set; } = new FriendsRefreshPreparation();
            public List<FriendIdentity> Friends { get; set; } = new List<FriendIdentity>();
            public List<FriendIdentity> ScopedFriends { get; set; } = new List<FriendIdentity>();
            public string RosterSource { get; set; } = "unknown";
            public bool DiscoverUnowned { get; set; }
            public bool CanContinue { get; set; }
            public int MaxDegreeOfParallelism { get; set; } = 1;
            public List<FriendOwnershipSnapshot> OwnershipSnapshots { get; set; }
            public HashSet<string> ProbedProviderOnlyAchievementKeys { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int RawCandidatesLoaded { get; set; }
            public int CandidatesSkippedAlreadyProbed { get; set; }
            public int CandidatesSkippedRecencyFresh { get; set; }
            public int CandidatesQueued { get; set; }

            // Full-scope only: the unowned-definition plan (which provider games are due for a
            // definition fetch, plus the provider-only probe count), computed up front so the friend
            // definitions progress sub-band knows its full total before it emits any completion.
            public UnownedDefinitionPlan DefinitionPlan { get; set; }

            // Recent-scope only: keys for friend games the freshly-fetched ownership positively confirms
            // unchanged since the last successful scrape (Steam: playtime equal; RA/Exophase: no newer
            // last-played/unlock). Populated at the ownership step and used by LoadAchievementWorkItems to SKIP.
            public HashSet<string> RecencyFreshKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class LogicalFriendRefreshGroup
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public List<FriendAccountRefreshItem> Accounts { get; } = new List<FriendAccountRefreshItem>();
        }

        private sealed class FriendAccountRefreshItem
        {
            public FriendProviderRefreshContext Context { get; set; }
            public FriendIdentity Friend { get; set; }
        }

        private sealed class FriendAchievementWorkItem
        {
            public FriendProviderRefreshContext Context { get; set; }
            public FriendRefreshCandidate Candidate { get; set; }
        }

        // Precomputed plan for the unowned-definition discovery phase. Computed once (a read-only cache
        // read) so the definitions progress sub-band has its full total before it reports any completion,
        // avoiding the monotonic-clamp freeze that piecemeal totals caused.
        private sealed class UnownedDefinitionPlan
        {
            public Dictionary<string, List<FriendGameOwnership>> OwnershipByKey { get; set; } =
                new Dictionary<string, List<FriendGameOwnership>>(StringComparer.OrdinalIgnoreCase);
            public List<string> ProviderGameKeys { get; set; } = new List<string>();
            public List<string> DueProviderGameKeys { get; set; } = new List<string>();
            public int ProbeItemCount { get; set; }

            // Total number of network-backed game checks the definitions phase will perform: one per due
            // definition fetch plus one per provider-only probe scrape.
            public int TotalDefinitionChecks => DueProviderGameKeys.Count + ProbeItemCount;
        }

        private sealed class FriendRefreshPerfSession
        {
            private readonly ILogger _logger;
            private readonly FriendRefreshOptions _options;
            private readonly int _providerCount;
            private readonly string _kind;
            private readonly Stopwatch _total = Stopwatch.StartNew();

            public FriendRefreshPerfSession(
                ILogger logger,
                FriendRefreshOptions options,
                int providerCount,
                string kind = "friends")
            {
                _logger = logger;
                _options = options;
                _providerCount = Math.Max(0, providerCount);
                _kind = string.IsNullOrWhiteSpace(kind) ? "friends" : kind.Trim();
                Log(
                    "friend.start",
                    $"providers={_providerCount} scope={_options?.Scope} focused={IsFocusedFriendGameRefresh(_options)} playniteGames={Count(_options?.PlayniteGameIds)} providerApps={Count(_options?.ProviderAppIds)} providerKeys={Count(_options?.ProviderGameKeys)} friends={Count(_options?.FriendExternalUserIds)} forceDefinitions={_options?.ForceDefinitionRefresh == true}");
            }

            public void LogPrepare(
                Stopwatch timer,
                IReadOnlyList<FriendProviderRefreshContext> contexts,
                string extra = null)
            {
                var active = contexts?.Count(context => context?.CanContinue == true) ?? 0;
                var friends = contexts?.Sum(context => context?.Friends?.Count ?? 0) ?? 0;
                var scoped = contexts?.Sum(context => context?.ScopedFriends?.Count ?? 0) ?? 0;
                var sources = string.Join(
                    ",",
                    (contexts ?? Array.Empty<FriendProviderRefreshContext>())
                    .Where(context => context != null)
                    .GroupBy(context => context.RosterSource ?? "unknown", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Key + ":" + group.Count()));
                Log(
                    "friend.prepare",
                    $"ms={Elapsed(timer)} contexts={contexts?.Count ?? 0} active={active} friends={friends} scopedFriends={scoped} rosterSources={sources}{FormatExtra(extra)}");
            }

            public void LogDefinitionPlan(
                Stopwatch timer,
                IReadOnlyList<FriendProviderRefreshContext> contexts,
                int totalChecks)
            {
                var providerKeys = contexts?
                    .Where(context => context?.DefinitionPlan != null)
                    .Sum(context => context.DefinitionPlan.ProviderGameKeys.Count) ?? 0;
                var dueDefinitions = contexts?
                    .Where(context => context?.DefinitionPlan != null)
                    .Sum(context => context.DefinitionPlan.DueProviderGameKeys.Count) ?? 0;
                var probes = contexts?
                    .Where(context => context?.DefinitionPlan != null)
                    .Sum(context => context.DefinitionPlan.ProbeItemCount) ?? 0;
                Log(
                    "friend.definitionPlan",
                    $"ms={Elapsed(timer)} providerKeys={providerKeys} dueDefinitions={dueDefinitions} probes={probes} totalChecks={totalChecks}");
            }

            public void LogCandidateLoad(
                Stopwatch timer,
                IReadOnlyList<FriendProviderRefreshContext> contexts,
                int workItems)
            {
                var raw = contexts?.Sum(context => context?.RawCandidatesLoaded ?? 0) ?? 0;
                var queued = contexts?.Sum(context => context?.CandidatesQueued ?? 0) ?? 0;
                var probed = contexts?.Sum(context => context?.CandidatesSkippedAlreadyProbed ?? 0) ?? 0;
                var recency = contexts?.Sum(context => context?.CandidatesSkippedRecencyFresh ?? 0) ?? 0;
                Log(
                    "friend.loadCandidates",
                    $"ms={Elapsed(timer)} raw={raw} queued={queued} workItems={workItems} skippedAlreadyProbed={probed} skippedRecencyFresh={recency}");
            }

            public void LogPhase(Stopwatch timer, string phase, string detail)
            {
                Log(phase, $"ms={Elapsed(timer)} {detail}".TrimEnd());
            }

            public void LogTotal(
                RebuildPayload payload,
                IReadOnlyList<FriendProviderRefreshContext> contexts)
            {
                _total.Stop();
                var summary = payload?.FriendSummary ?? new FriendRefreshSummary();
                var scoped = contexts?.Sum(context => context?.ScopedFriends?.Count ?? 0) ?? 0;
                Log(
                    "friend.total",
                    $"ms={_total.ElapsedMilliseconds} providersProcessed={summary.ProvidersProcessed} scopedFriends={scoped} friendsFetched={summary.FriendsFetched} friendsSaved={summary.FriendsSaved} ownershipPages={summary.OwnershipPagesRefreshed} ownershipRows={summary.OwnershipRowsWritten} candidatesLoaded={summary.CandidatesLoaded} candidatesRefreshed={summary.CandidatesRefreshed} achievementsSaved={summary.AchievementsSaved}");
            }

            private void Log(string phase, string detail)
            {
                _logger?.Debug($"[RefreshPerf] kind={_kind} phase={phase} {detail}");
            }

            private static long Elapsed(Stopwatch timer)
            {
                timer?.Stop();
                return timer?.ElapsedMilliseconds ?? 0;
            }

            private static int Count<T>(IReadOnlyCollection<T> values)
            {
                return values?.Count ?? 0;
            }

            private static string FormatExtra(string extra)
            {
                return string.IsNullOrWhiteSpace(extra) ? string.Empty : " " + extra.Trim();
            }
        }

    internal sealed class FriendRefreshProgressSession
    {
        internal const int TotalUnits = 10000;
        private const int MaxReportUnits = TotalUnits - 1;
        private const int RosterStart = 0;
        private const int RosterEnd = 1000;
        private const int LibraryStart = 1000;
        private const int LibraryEnd = 4200;
        private const int GameStart = 4200;
        // The game-check band is split into an ordered definitions sub-band [GameStart, GameChecksSplit]
        // and an achievement-scrape sub-band [GameChecksSplit, GameEnd]. Each sub-band's total is known
        // before it emits any completion, so the monotonic clamp never freezes mid-phase. The split is a
        // fixed midpoint (a fully proportional split is impossible because the scrape total is not known
        // until after the definitions phase runs); when there are no due definitions the scrape phase
        // collapses to use the full [GameStart, GameEnd] range so no dead gap appears.
        private const int GameChecksSplit = 7050;
        private const int GameEnd = 9900;

        private readonly Action<string, int, int> _reportProgress;
        private readonly object _sync = new object();
        private int _lastReportedUnits;
        private int _providerTotal = 1;
        private int _providersCompleted;
        private int _libraryTotal;
        private int _librariesCompleted;
        private int _definitionTotal;
        private int _definitionsCompleted;
        private int _scrapeTotal;
        private int _scrapeCompleted;
        private bool _definitionsEmittedAny;

        public FriendRefreshProgressSession(Action<string, int, int> reportProgress)
        {
            _reportProgress = reportProgress;
        }

        public FriendRefreshProgressSession(Action<string, int, int> reportProgress, int providerCount)
            : this(reportProgress)
        {
            InitializeProviderTotal(providerCount);
        }

        public void InitializeProviderTotal(int total)
        {
            lock (_sync)
            {
                _providerTotal = Math.Max(1, total);
                _providersCompleted = 0;
            }
        }

        public void ReportLoadingFriends()
        {
            ReportLoadingFriends(null, 0, _providerTotal);
        }

        public void ReportLoadingFriends(string providerKey, int providerIndex, int providerTotal)
        {
            var total = Math.Max(1, providerTotal);
            var current = Math.Max(0, Math.Min(providerIndex, total - 1));
            var message = Format(
                "LOCPlayAch_FriendsRefresh_Progress_LoadingFriends",
                "Loading friends {1}/{2}: {0}",
                string.IsNullOrWhiteSpace(providerKey) ? "provider" : providerKey.Trim(),
                current + 1,
                total);
            ReportCountAt(RosterStart, RosterEnd, current, total, message);
        }

        public void ReportProviderRosterLoaded(string providerKey)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _providerTotal);
                _providersCompleted = Math.Max(0, Math.Min(total, _providersCompleted + 1));
                current = _providersCompleted;
            }

            var message = Format(
                "LOCPlayAch_FriendsRefresh_Progress_LoadingFriends",
                "Loading friends {1}/{2}: {0}",
                string.IsNullOrWhiteSpace(providerKey) ? "provider" : providerKey.Trim(),
                current,
                total);
            ReportCountAt(RosterStart, RosterEnd, current, total, message);
        }

        public void InitializeFriendLibraryTotal(int total)
        {
            lock (_sync)
            {
                _libraryTotal = Math.Max(0, total);
                _librariesCompleted = 0;
            }
        }

        public void ReportFriendLibraryCompleted(string friendName = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _libraryTotal);
                _librariesCompleted = Math.Max(0, Math.Min(total, _librariesCompleted + 1));
                current = _librariesCompleted;
            }

            ReportCount(
                LibraryStart,
                LibraryEnd,
                current,
                total,
                friendName,
                "LOCPlayAch_FriendsRefresh_Progress_Libraries",
                "Refreshing friend libraries {0}/{1}",
                "LOCPlayAch_FriendsRefresh_Progress_LibrariesNamed",
                "Refreshing friend libraries {0}/{1}: {2}");
        }

        public void InitializeDefinitionChecksTotal(int total)
        {
            lock (_sync)
            {
                _definitionTotal = Math.Max(0, total);
                _definitionsCompleted = 0;
            }
        }

        public void InitializeAchievementScrapeTotal(int total)
        {
            lock (_sync)
            {
                _scrapeTotal = Math.Max(0, total);
                _scrapeCompleted = 0;
            }
        }

        public void ReportDefinitionCheckActive(string detail = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                current = Math.Max(0, _definitionsCompleted);
                total = Math.Max(1, _definitionTotal);
            }

            ReportDefinitionChecks(current, total, detail);
        }

        public void ReportDefinitionCheckCompleted(string detail = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _definitionTotal);
                _definitionsCompleted = Math.Max(0, Math.Min(total, _definitionsCompleted + 1));
                current = _definitionsCompleted;
            }

            ReportDefinitionChecks(current, total, detail);
        }

        public void ReportAchievementScrapeCompleted(string detail = null)
        {
            int current;
            int total;
            int scrapeStart;
            lock (_sync)
            {
                total = Math.Max(1, _scrapeTotal);
                _scrapeCompleted = Math.Max(0, Math.Min(total, _scrapeCompleted + 1));
                current = _scrapeCompleted;
                scrapeStart = _definitionsEmittedAny ? GameChecksSplit : GameStart;
            }

            ReportCount(
                scrapeStart,
                GameEnd,
                current,
                total,
                detail,
                "LOCPlayAch_FriendsRefresh_Progress_GameChecks",
                "Refreshing friend games {0}/{1}",
                "LOCPlayAch_FriendsRefresh_Progress_GameChecksNamed",
                "Refreshing friend games {0}/{1}: {2}");
        }

        // Achievement-icon and game-image downloads are sub-steps of the game-definition check for a given
        // friend game. They advance the bar fractionally within that game's check unit but keep the SAME
        // "Checking friend games" status text, so the message never bounces between "checking games" and
        // "downloading images".
        public void ReportAchievementImages(int completed, int total, string gameName = null)
        {
            ReportImageProgress(completed, total, gameName);
        }

        public void ReportFriendGameImages(int completed, int total, string gameName = null)
        {
            ReportImageProgress(completed, total, gameName);
        }

        private void ReportDefinitionChecks(int completed, int total, string detail)
        {
            lock (_sync)
            {
                _definitionsEmittedAny = true;
            }

            ReportCount(
                GameStart,
                GameChecksSplit,
                completed,
                total,
                detail,
                "LOCPlayAch_FriendsRefresh_Progress_GameChecks",
                "Refreshing friend games {0}/{1}",
                "LOCPlayAch_FriendsRefresh_Progress_GameChecksNamed",
                "Refreshing friend games {0}/{1}: {2}");
        }

        private void ReportImageProgress(int completed, int total, string detail)
        {
            total = Math.Max(1, total);
            completed = Math.Max(0, Math.Min(completed, total));
            // Image downloads happen during the definitions phase (per due game), so their fractional
            // sub-progress maps into the definitions sub-band over the definitions total. This keeps
            // image progress from leaking into (and freezing) the achievement-scrape sub-band. The image
            // completed/total drive only the fractional bar position; the visible text reuses the shared
            // "Checking friend games" message (with the game-check counts) so the phrasing never changes.
            int definitionsCompleted;
            int definitionTotal;
            lock (_sync)
            {
                _definitionsEmittedAny = true;
                definitionsCompleted = Math.Max(0, _definitionsCompleted);
                definitionTotal = Math.Max(1, _definitionTotal);
            }

            var fractional = completed <= 0
                ? 0d
                : Math.Min(0.85d, completed / (double)total * 0.85d);
            var effectiveCompleted = Math.Min(definitionTotal, definitionsCompleted + fractional);
            var units = GameStart + (int)((GameChecksSplit - GameStart) * effectiveCompleted / definitionTotal);
            var message = FormatCount(
                "LOCPlayAch_FriendsRefresh_Progress_GameChecks",
                "Refreshing friend games {0}/{1}",
                "LOCPlayAch_FriendsRefresh_Progress_GameChecksNamed",
                "Checking friend games {0}/{1}: {2}",
                definitionsCompleted,
                definitionTotal,
                detail);
            ReportAt(units, message);
        }

        private void ReportCount(
            int start,
            int end,
            int completed,
            int total,
            string detail,
            string resourceKey,
            string fallback,
            string namedResourceKey,
            string namedFallback)
        {
            total = Math.Max(1, total);
            completed = Math.Max(0, Math.Min(completed, total));
            var localUnits = start + (int)((long)(end - start) * completed / total);
            var message = FormatCount(resourceKey, fallback, namedResourceKey, namedFallback, completed, total, detail);
            ReportAt(localUnits, message);
        }

        private void ReportCountAt(int start, int end, int completed, int total, string message)
        {
            total = Math.Max(1, total);
            completed = Math.Max(0, Math.Min(completed, total));
            var localUnits = start + (int)((long)(end - start) * completed / total);
            ReportAt(localUnits, message);
        }

        private void ReportAt(int localUnits, string message)
        {
            if (_reportProgress == null)
            {
                return;
            }

            var globalUnits = Math.Max(0, Math.Min(MaxReportUnits, localUnits));
            lock (_sync)
            {
                if (globalUnits < _lastReportedUnits)
                {
                    globalUnits = _lastReportedUnits;
                }
                else
                {
                    _lastReportedUnits = globalUnits;
                }
            }

            _reportProgress(message, globalUnits, TotalUnits);
        }

        private static string FormatCount(
            string resourceKey,
            string fallback,
            string namedResourceKey,
            string namedFallback,
            int current,
            int total,
            string detail)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return Format(namedResourceKey, namedFallback, current, total, detail.Trim());
            }

            return Format(resourceKey, fallback, current, total);
        }

        private static string Format(string resourceKey, string fallback, params object[] args)
        {
            var format = ResourceProvider.GetString(resourceKey);
            if (string.IsNullOrWhiteSpace(format) ||
                (format.StartsWith("<!", StringComparison.Ordinal) && format.EndsWith("!>", StringComparison.Ordinal)))
            {
                format = fallback;
            }

            return string.Format(format, args ?? Array.Empty<object>());
        }
    }
}
}

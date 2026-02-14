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

            _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanAttemptedNoProviders"));
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
        /// Calculates percentage for a scan progress report.
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
        /// Determines if the provided report represents a final scan state.
        /// </summary>
        public bool IsFinalProgressReport(ProgressReport report)
        {
            return IsFinalProgressReport(report, CalculateProgressPercent(report));
        }

        /// <summary>
        /// Resolves the user-facing scan status message from report + manager state.
        /// </summary>
        public string ResolveProgressMessage(ProgressReport report = null)
        {
            var effectiveReport = report ?? _lastProgress;
            var isFinal = IsFinalProgressReport(effectiveReport);
            return ResolveProgressMessage(effectiveReport, isFinal);
        }

        /// <summary>
        /// Gets a centralized scan status snapshot for UI consumers.
        /// </summary>
        public ScanStatusSnapshot GetScanStatusSnapshot(ProgressReport report = null)
        {
            var effectiveReport = report ?? _lastProgress;
            var progressPercent = CalculateProgressPercent(effectiveReport);
            var isFinal = IsFinalProgressReport(effectiveReport, progressPercent);

            return new ScanStatusSnapshot
            {
                IsScanning = IsRebuilding,
                IsFinal = isFinal,
                IsCanceled = effectiveReport?.IsCanceled == true,
                ProgressPercent = progressPercent,
                Message = ResolveProgressMessage(effectiveReport, isFinal)
            };
        }

        /// <summary>
        /// Gets a transient "starting scan" snapshot for immediate UI updates.
        /// </summary>
        public ScanStatusSnapshot GetStartingScanStatusSnapshot()
        {
            return new ScanStatusSnapshot
            {
                IsScanning = IsRebuilding,
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
                return ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete");
            }

            if (!string.IsNullOrWhiteSpace(_lastStatus))
            {
                return _lastStatus;
            }

            return ResourceProvider.GetString("LOCPlayAch_Status_Starting");
        }

        // -----------------------------
        // Scan option builders
        // -----------------------------

        private CacheScanOptions FullRefreshOptions()
        {
            return new CacheScanOptions
            {
                QuickRefreshMode = false,
                IncludeUnplayedGames = _settings.Persisted.IncludeUnplayedGames
            };
        }

        private CacheScanOptions SingleGameOptions(Guid playniteGameId)
        {
            return new CacheScanOptions
            {
                PlayniteGameIds = new[] { playniteGameId },
                IncludeUnplayedGames = true
            };
        }

        private CacheScanOptions QuickRefreshOptions()
        {
            return new CacheScanOptions
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
        // Managed scan runner
        // -----------------------------

        private bool TryBeginRun(out CancellationTokenSource cts)
        {
            lock (_runLock)
            {
                if (_activeRunCts != null)
                {
                    _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanAlreadyInProgress"));
                    cts = null;
                    Report(_lastStatus ?? ResourceProvider.GetString("LOCPlayAch_Status_UpdatingCache"), 0, 1);
                    return false;
                }

                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanStarting"));
                _activeRunCts = new CancellationTokenSource();
                cts = _activeRunCts;
                return true;
            }
        }

        private void EndRun()
        {
            _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanEndRun"));
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
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanRequestedNoProviders"));
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
                payload = await runner(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanCanceled"));
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
                    _logger?.Debug(ex, string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanProviderCapabilityCheckFailed"),
                        game?.Name));
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
                else if (!options.IncludeUnplayedGames)
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
                _logger?.Warn(ResourceProvider.GetString("LOCPlayAch_Log_ScanNoAuthenticatedProviders"));
                return new RebuildPayload();
            }

            var scanTargets = GetScanTargets(options, authenticatedProviders);
            var gamesToScan = scanTargets.Select(x => x.Game).ToList();
            var gamesByProvider = scanTargets
                .GroupBy(x => x.Provider)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Game).ToList());

            // log games by providers to check, list all games and all providers
            _logger.Debug(string.Format(
                ResourceProvider.GetString("LOCPlayAch_Log_ScanSummary"),
                gamesToScan.Count,
                _providers.Count,
                gamesByProvider.Count));
            // _logger.Debug($"[Scan] Games with providers: {string.Join(", ", scanTargets.Select(x => x.Game.Name + " => " + x.Provider.ProviderName))}");

            if (gamesByProvider.Count == 0)
            {
                _logger?.Warn(ResourceProvider.GetString("LOCPlayAch_Log_ScanNoMatchingProviders"));
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
                _logger?.Debug(ex, string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Log_ScanResolveIconPathFailed"),
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

            return ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete");
        }

        // -----------------------------
        // Public scan methods
        // -----------------------------

        private Task StartManagedScanCoreAsync(
            CacheScanOptions options,
            Func<RebuildPayload, string> finalMessage,
            string errorLogMessage)
        {
            return RunManagedAsync(
                cancel => ScanAsync(options, HandleUpdate, cancel),
                finalMessage,
                errorLogMessage
            );
        }

        private Task StartManagedGameIdScanAsync(
            ScanModeType mode,
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

                Report(FormatScanCompletionWithModeAndCount(mode, 0), 1, 1);
                return Task.CompletedTask;
            }

            return StartManagedScanCoreAsync(
                new CacheScanOptions { PlayniteGameIds = gameIds, IncludeUnplayedGames = true },
                finalMessage,
                errorLogMessage
            );
        }

        private static string GetScanModeShortName(ScanModeType mode)
        {
            var resourceKey = mode == ScanModeType.LibrarySelected
                ? "LOCPlayAch_ScanModeShort_Selected"
                : mode.GetShortResourceKey();

            return ResourceProvider.GetString(resourceKey);
        }

        private static string FormatScanCompletionWithModeAndCount(ScanModeType mode, int gamesScanned)
        {
            return string.Format(
                ResourceProvider.GetString("LOCPlayAch_Status_ScanCompleteWithModeAndCount"),
                GetScanModeShortName(mode),
                Math.Max(0, gamesScanned));
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
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanMissingNoAuthenticatedProviders"));
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
                _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanMissingNoGames"));
                return missingGameIds;
            }

            _logger.Info(string.Format(
                ResourceProvider.GetString("LOCPlayAch_Log_ScanMissingFoundGames"),
                missingGameIds.Count));
            return missingGameIds;
        }

        private Task StartManagedRebuildAsync()
        {
            return StartManagedScanCoreAsync(
                FullRefreshOptions(),
                payload => FormatScanCompletionWithModeAndCount(ScanModeType.Full, payload?.Summary?.GamesScanned ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_ScanFullFailed")
            );
        }

        private Task StartManagedSingleGameScanAsync(Guid playniteGameId)
        {
            return StartManagedScanCoreAsync(
                SingleGameOptions(playniteGameId),
                payload => ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete"),
                ResourceProvider.GetString("LOCPlayAch_Log_ScanSingleFailed")
            );
        }

        private Task StartManagedQuickRefreshAsync()
        {
            return StartManagedScanCoreAsync(
                QuickRefreshOptions(),
                payload => FormatScanCompletionWithModeAndCount(ScanModeType.Quick, payload?.Summary?.GamesScanned ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_ScanQuickFailed")
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
                _logger.Warn(string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Log_ScanUnknownModeKey"),
                    modeKey));
                mode = ScanModeType.Quick;
            }

            return ExecuteScanAsync(mode, singleGameId);
        }

        public Task ExecuteScanForGamesAsync(IEnumerable<Guid> gameIds)
        {
            var ids = gameIds?
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList() ?? new List<Guid>();

            return StartManagedGameIdScanAsync(
                ScanModeType.LibrarySelected,
                ids,
                payload => FormatScanCompletionWithModeAndCount(ScanModeType.LibrarySelected, payload?.Summary?.GamesScanned ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Log_ScanSelectedFailed"),
                ResourceProvider.GetString("LOCPlayAch_Log_ScanNoSelectedGames"));
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
                    return StartManagedGameIdScanAsync(
                        ScanModeType.Installed,
                        GetInstalledGameIds(),
                        payload => FormatScanCompletionWithModeAndCount(ScanModeType.Installed, payload?.Summary?.GamesScanned ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanInstalledFailed"),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanNoInstalledGames"));

                case ScanModeType.Favorites:
                    return StartManagedGameIdScanAsync(
                        ScanModeType.Favorites,
                        GetFavoriteGameIds(),
                        payload => FormatScanCompletionWithModeAndCount(ScanModeType.Favorites, payload?.Summary?.GamesScanned ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanFavoritesFailed"),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanNoFavoriteGames"));

                case ScanModeType.Single:
                    if (singleGameId.HasValue)
                        return StartManagedSingleGameScanAsync(singleGameId.Value);
                    _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanSingleNoGameId"));
                    return Task.CompletedTask;

                case ScanModeType.LibrarySelected:
                    return StartManagedGameIdScanAsync(
                        ScanModeType.LibrarySelected,
                        GetLibrarySelectedGameIds(),
                        payload => FormatScanCompletionWithModeAndCount(ScanModeType.LibrarySelected, payload?.Summary?.GamesScanned ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanSelectedFailed"),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanNoSelectedGames"));

                case ScanModeType.Missing:
                    return StartManagedGameIdScanAsync(
                        ScanModeType.Missing,
                        GetMissingGameIds(),
                        payload => FormatScanCompletionWithModeAndCount(ScanModeType.Missing, payload?.Summary?.GamesScanned ?? 0),
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanMissingFailed"));

                default:
                    _logger.Warn(string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Log_ScanUnknownModeEnum"),
                        mode));
                    return StartManagedQuickRefreshAsync();
            }
        }

        public void CancelCurrentRebuild()
        {
            _logger.Info(ResourceProvider.GetString("LOCPlayAch_Log_ScanCancelRequested"));
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

            var key = playniteGameId.ToString();

            try
            {
                _cacheService.RemoveGameData(key);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to remove achievement cache for game '{playniteGameId}'.");
            }

            try
            {
                _diskImageService.ClearGameCache(key);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to remove icon cache for game '{playniteGameId}'.");
            }

            NotifyCacheInvalidatedThrottled(force: true);
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
                    ResourceProvider.GetString("LOCPlayAch_Log_ScanGetGameDataFailed"),
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
                _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Log_ScanGetAllGameDataFailed"));
                return new();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Cache;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.ThemeIntegration;

namespace PlayniteAchievements.Services.Library
{
    internal sealed class LibraryProjectionService : IDisposable
    {
        private const int WarmDebounceMs = 1500;

        private readonly object _sync = new object();
        private readonly AchievementDataService _achievementDataService;
        private readonly IReadOnlyList<IDataProvider> _providers;
        private readonly IPlayniteAPI _api;
        private readonly ICacheManager _cacheManager;
        private readonly GameCustomDataStore _customDataStore;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly Dictionary<string, LibraryProjectionSnapshot> _cache =
            new Dictionary<string, LibraryProjectionSnapshot>(StringComparer.Ordinal);
        private int _cacheGeneration;
        private int _warmGeneration;
        private bool _warmSuppressed;
        private bool _disposed;

        public LibraryProjectionService(
            AchievementDataService achievementDataService,
            IReadOnlyList<IDataProvider> providers,
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            ICacheManager cacheManager,
            GameCustomDataStore customDataStore,
            ILogger logger)
        {
            _achievementDataService = achievementDataService ?? throw new ArgumentNullException(nameof(achievementDataService));
            _providers = providers ?? new List<IDataProvider>();
            _api = api;
            _cacheManager = cacheManager;
            _customDataStore = customDataStore;
            _settings = settings;
            _logger = logger;

            if (_cacheManager != null)
            {
                _cacheManager.CacheInvalidated += OnProjectionSourceChanged;
                _cacheManager.CacheDeltaUpdated += OnProjectionSourceChanged;
            }

            if (_customDataStore != null)
            {
                _customDataStore.CustomDataChanged += OnProjectionSourceChanged;
            }

            if (_settings?.Persisted != null)
            {
                _settings.Persisted.PropertyChanged += OnPersistedSettingsChanged;
            }
        }

        public OverviewDataSnapshot GetOverviewSnapshot(
            PlayniteAchievementsSettings settings,
            ISet<string> revealedKeys,
            CancellationToken token)
        {
            var useCache = revealedKeys == null || revealedKeys.Count == 0;
            var snapshot = GetOrBuild(
                "overview",
                useCache,
                () => BuildOverview(settings ?? _settings, revealedKeys, token));
            return snapshot?.OverviewSnapshot ?? new OverviewDataSnapshot();
        }

        public LibraryRuntimeState GetThemeLightState(
            int recentUnlockLimit,
            CancellationToken token,
            out bool usedCachedSummary,
            out int? hydratedGameCount)
        {
            var normalizedLimit = Math.Max(0, recentUnlockLimit);
            var snapshot = GetOrBuild(
                "theme-light:" + normalizedLimit,
                useCache: true,
                build: () => BuildThemeLight(normalizedLimit, token));

            usedCachedSummary = snapshot?.UsedCachedSummary == true;
            hydratedGameCount = snapshot?.HydratedGameCount;
            return snapshot?.LibraryState ?? new LibraryRuntimeState();
        }

        public LibraryRuntimeState GetThemeFullState(
            CancellationToken token,
            out int? hydratedGameCount)
        {
            var snapshot = GetOrBuild(
                "theme-full",
                useCache: true,
                build: () => BuildThemeFull(token));

            hydratedGameCount = snapshot?.HydratedGameCount;
            return snapshot?.LibraryState ?? new LibraryRuntimeState();
        }

        public void Invalidate()
        {
            lock (_sync)
            {
                _cacheGeneration++;
                _cache.Clear();
            }

            ScheduleWarm();
        }

        // Triggers the first background warm. Called once Playnite has finished starting so the
        // warmed snapshot resolves game presentation (cover, icon, playtime, last played) against
        // a populated game database rather than baking in blank values during early startup.
        public void Warm()
        {
            ScheduleWarm();
        }

        // While a game session is active the background warm is skipped: the in-game poller's
        // periodic saves would otherwise rebuild the whole-library projection every tick, and that
        // rebuild holds the store lock long enough to stall UI-thread cache reads. Invalidate()
        // still clears the snapshot cache on every delta, so on-demand consumers always rebuild
        // with fresh data; only the precompute is dropped. The post-session warm comes from the
        // stopped-game refresh's own cache delta (or an explicit Warm() when that refresh is
        // skipped), so deactivation itself schedules nothing.
        public void SetGameSessionActive(bool active)
        {
            lock (_sync)
            {
                _warmSuppressed = active;
            }
        }

        public void Dispose()
        {
            _disposed = true;

            if (_cacheManager != null)
            {
                _cacheManager.CacheInvalidated -= OnProjectionSourceChanged;
                _cacheManager.CacheDeltaUpdated -= OnProjectionSourceChanged;
            }

            if (_customDataStore != null)
            {
                _customDataStore.CustomDataChanged -= OnProjectionSourceChanged;
            }

            if (_settings?.Persisted != null)
            {
                _settings.Persisted.PropertyChanged -= OnPersistedSettingsChanged;
            }
        }

        private LibraryProjectionSnapshot GetOrBuild(
            string key,
            bool useCache,
            Func<LibraryProjectionSnapshot> build)
        {
            int generation = 0;
            if (useCache)
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (_cache.TryGetValue(key, out var cached))
                    {
                        return cached;
                    }

                    generation = _cacheGeneration;
                }
            }
            else
            {
                ThrowIfDisposed();
            }

            var snapshot = build() ?? new LibraryProjectionSnapshot();

            if (useCache)
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (generation == _cacheGeneration)
                    {
                        _cache[key] = snapshot;
                    }
                }
            }

            return snapshot;
        }

        private LibraryProjectionSnapshot BuildOverview(
            PlayniteAchievementsSettings settings,
            ISet<string> revealedKeys,
            CancellationToken token)
        {
            var builder = new OverviewDataBuilder(
                _achievementDataService,
                _providers,
                _api,
                _logger);

            return new LibraryProjectionSnapshot
            {
                OverviewSnapshot = builder.Build(settings, revealedKeys, token)
            };
        }

        private LibraryProjectionSnapshot BuildThemeLight(int recentUnlockLimit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var summaryData = _achievementDataService.GetCachedSummaryDataForTheme(recentUnlockLimit);
            if (summaryData != null)
            {
                return new LibraryProjectionSnapshot
                {
                    UsedCachedSummary = true,
                    LibraryState = LibraryRuntimeStateBuilder.BuildFromCachedSummary(summaryData, _api, token)
                };
            }

            var allData = _achievementDataService.GetAllVisibleGameAchievementDataForTheme() ??
                          new List<GameAchievementData>();
            token.ThrowIfCancellationRequested();

            return new LibraryProjectionSnapshot
            {
                UsedCachedSummary = false,
                HydratedGameCount = allData.Count,
                LibraryState = LibraryRuntimeStateBuilder.Build(
                    allData,
                    _api,
                    token,
                    includeHeavyAchievementLists: false)
            };
        }

        private LibraryProjectionSnapshot BuildThemeFull(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var allData = _achievementDataService.GetAllVisibleGameAchievementDataForTheme() ??
                          new List<GameAchievementData>();
            token.ThrowIfCancellationRequested();

            return new LibraryProjectionSnapshot
            {
                UsedCachedSummary = false,
                HydratedGameCount = allData.Count,
                LibraryState = LibraryRuntimeStateBuilder.Build(
                    allData,
                    _api,
                    token,
                    includeHeavyAchievementLists: true)
            };
        }

        private void OnProjectionSourceChanged(object sender, EventArgs e)
        {
            Invalidate();
        }

        private void OnPersistedSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            Invalidate();
        }

        private void ScheduleWarm()
        {
            lock (_sync)
            {
                if (_warmSuppressed)
                {
                    return;
                }
            }

            var generation = Interlocked.Increment(ref _warmGeneration);
            _ = WarmAfterDelayAsync(generation);
        }

        private async Task WarmAfterDelayAsync(int generation)
        {
            try
            {
                await Task.Delay(WarmDebounceMs).ConfigureAwait(false);
                if (_disposed || generation != Volatile.Read(ref _warmGeneration))
                {
                    return;
                }

                using (PerfScope.StartStartup(_logger, "Warm.OverviewProjection", thresholdMs: 250))
                {
                    GetOverviewSnapshot(
                        _settings,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to warm library projection cache.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LibraryProjectionService));
            }
        }
    }
}

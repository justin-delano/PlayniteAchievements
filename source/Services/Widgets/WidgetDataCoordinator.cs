using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
#if !TEST
using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Library;
#endif
using PlayniteAchievements.Services.Overview;

namespace PlayniteAchievements.Services.Widgets
{
    /// <summary>
    /// Owns the cached aggregate achievement snapshot shared by widget hosts.
    /// Layout, profile, and pin changes do not invalidate this cache.
    /// </summary>
    public class WidgetDataCoordinator : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly Func<OverviewDataSnapshot> _snapshotFactory;
        private readonly ILogger _logger;
        private OverviewDataSnapshot _snapshot;
        private Task<OverviewDataSnapshot> _buildTask;
        private int _generation;
        private int _buildGeneration;
        private bool _invalidated = true;
        private bool _disposed;

#if !TEST
        internal WidgetDataCoordinator(
            AchievementDataService achievementDataService,
            LibraryProjectionService libraryProjectionService,
            IReadOnlyList<IDataProvider> providers,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings,
            Friends.IFriendCacheManager friendCache = null)
            : this(
                () =>
                {
                    if (libraryProjectionService != null)
                    {
                        return libraryProjectionService.GetOverviewSnapshot(
                            settings,
                            CancellationToken.None);
                    }

                    var builder = new OverviewDataBuilder(
                        achievementDataService,
                        providers,
                        playniteApi,
                        logger,
                        friendCache);
                    return builder.Build(settings, CancellationToken.None);
                },
                logger)
        {
        }
#endif

        public WidgetDataCoordinator(Func<OverviewDataSnapshot> snapshotFactory, ILogger logger = null)
        {
            _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
            _logger = logger;
        }

        public event EventHandler SnapshotInvalidated;

        public void Invalidate()
        {
            lock (_syncRoot)
            {
                _invalidated = true;
                _generation++;
            }

            SnapshotInvalidated?.Invoke(this, EventArgs.Empty);
        }

        public Task<OverviewDataSnapshot> GetSnapshotAsync(CancellationToken cancel)
        {
            return GetSnapshotAsync(forceRefresh: false, cancel);
        }

        public async Task<OverviewDataSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancel)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                Task<OverviewDataSnapshot> task;
                int taskGeneration;
                lock (_syncRoot)
                {
                    ThrowIfDisposed();

                    if (!forceRefresh && !_invalidated && _snapshot != null)
                    {
                        return _snapshot;
                    }

                    if (_buildTask != null)
                    {
                        task = _buildTask;
                        taskGeneration = _buildGeneration;
                    }
                    else
                    {
                        taskGeneration = _generation;
                        task = Task.Run(BuildSnapshot);
                        _buildTask = task;
                        _buildGeneration = taskGeneration;
                    }
                }

                var snapshot = await task.ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();

                lock (_syncRoot)
                {
                    ThrowIfDisposed();
                    var isCurrentGeneration = taskGeneration == _generation;
                    if (ReferenceEquals(_buildTask, task))
                    {
                        _buildTask = null;
                        if (isCurrentGeneration)
                        {
                            _snapshot = snapshot ?? new OverviewDataSnapshot();
                            _invalidated = false;
                        }
                    }

                    if (isCurrentGeneration)
                    {
                        return _snapshot ?? snapshot ?? new OverviewDataSnapshot();
                    }
                }

                // A source invalidated while this build was running. Do not publish the stale
                // result or strand listeners on it; loop once more and join/build the new
                // generation instead.
                forceRefresh = false;
            }
        }

        public virtual void Dispose()
        {
            lock (_syncRoot)
            {
                _disposed = true;
                _snapshot = null;
                _buildTask = null;
            }
        }

        private OverviewDataSnapshot BuildSnapshot()
        {
            try
            {
                return _snapshotFactory() ?? new OverviewDataSnapshot();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to build shared achievement widget snapshot.");
                return new OverviewDataSnapshot();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WidgetDataCoordinator));
            }
        }
    }
}

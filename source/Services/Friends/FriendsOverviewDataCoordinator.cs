using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Friends
{
    internal sealed class FriendsOverviewSnapshot
    {
        public FriendsOverviewData Data { get; set; } = new FriendsOverviewData();

        public FriendOverviewProjection Projection { get; set; } = new FriendOverviewProjection(null);

        public List<FriendSummaryItem> Friends { get; set; } = new List<FriendSummaryItem>();

        public List<FriendGameSummaryItem> Games { get; set; } = new List<FriendGameSummaryItem>();

        public List<FriendAchievementDisplayItem> RecentUnlocks { get; set; } = new List<FriendAchievementDisplayItem>();

        public List<FriendAchievementDisplayItem> AllAchievements { get; set; } = new List<FriendAchievementDisplayItem>();

        public List<FriendAchievementDisplayItem> AllUnlockedAchievements { get; set; } = new List<FriendAchievementDisplayItem>();
    }

    internal sealed class FriendsOverviewDataCoordinator : IDisposable
    {
        private static readonly TimeSpan DefaultWarmDebounceInterval = TimeSpan.FromMilliseconds(1500);

        private readonly object _syncRoot = new object();
        private readonly IFriendCacheManager _friendCache;
        private readonly Func<PersistedSettings> _persistedSettingsFactory;
        private readonly ILogger _logger;
        private readonly TimeSpan _warmDebounceInterval;
        private FriendsOverviewSnapshot _snapshot;
        private Task<FriendsOverviewSnapshot> _buildTask;
        private int _invalidationVersion = 1;
        private int _snapshotVersion;
        private int _buildTaskVersion;
        private int _warmGeneration;
        private bool _disposed;

        public FriendsOverviewDataCoordinator(
            IFriendCacheManager friendCache,
            Func<PersistedSettings> persistedSettingsFactory,
            ILogger logger = null,
            TimeSpan? warmDebounceInterval = null)
        {
            _friendCache = friendCache;
            _persistedSettingsFactory = persistedSettingsFactory ?? (() => null);
            _logger = logger;
            _warmDebounceInterval = warmDebounceInterval ?? DefaultWarmDebounceInterval;
        }

        public event EventHandler SnapshotInvalidated;

        public bool TryGetCurrentSnapshot(out FriendsOverviewSnapshot snapshot)
        {
            lock (_syncRoot)
            {
                snapshot = !_disposed &&
                           _snapshot != null &&
                           _snapshotVersion == _invalidationVersion
                    ? _snapshot
                    : null;
                return snapshot != null;
            }
        }

        public void Invalidate()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _invalidationVersion++;
            }

            SnapshotInvalidated?.Invoke(this, EventArgs.Empty);
        }

        // Matches the overview/start-page projection warm: wait until Playnite's game database
        // is populated, then rebuild the shared friend snapshot so library covers/icons resolve
        // without relying on another UI surface to trigger a second load.
        public void Warm()
        {
            var generation = Interlocked.Increment(ref _warmGeneration);
            _ = WarmAfterDelayAsync(generation);
        }

        public Task<FriendsOverviewSnapshot> GetSnapshotAsync(CancellationToken cancel)
        {
            return GetSnapshotAsync(forceRefresh: false, cancel);
        }

        public async Task<FriendsOverviewSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancel)
        {
            var forceNextBuild = forceRefresh;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                Task<FriendsOverviewSnapshot> task;
                lock (_syncRoot)
                {
                    ThrowIfDisposed();

                    if (!forceNextBuild &&
                        _snapshot != null &&
                        _snapshotVersion == _invalidationVersion)
                    {
                        return _snapshot;
                    }

                    if (!forceNextBuild && _buildTask != null)
                    {
                        task = _buildTask;
                    }
                    else
                    {
                        _buildTaskVersion = _invalidationVersion;
                        task = Task.Run(BuildSnapshot);
                        _buildTask = task;
                        forceNextBuild = false;
                    }
                }

                var snapshot = await task.ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();

                lock (_syncRoot)
                {
                    if (ReferenceEquals(_buildTask, task))
                    {
                        _snapshot = snapshot ?? new FriendsOverviewSnapshot();
                        _snapshotVersion = _buildTaskVersion;
                        _buildTask = null;
                    }

                    if (_snapshot != null &&
                        _snapshotVersion == _invalidationVersion)
                    {
                        return _snapshot;
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _disposed = true;
                _snapshot = null;
                _buildTask = null;
            }
        }

        private async Task WarmAfterDelayAsync(int generation)
        {
            try
            {
                if (_warmDebounceInterval > TimeSpan.Zero)
                {
                    await Task.Delay(_warmDebounceInterval).ConfigureAwait(false);
                }

                if (generation != Volatile.Read(ref _warmGeneration) || IsDisposed())
                {
                    return;
                }

                Invalidate();
                await GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to warm friends overview snapshot.");
            }
        }

        private FriendsOverviewSnapshot BuildSnapshot()
        {
            try
            {
                var persisted = _persistedSettingsFactory();
                FriendsOverviewData data;
                using (PerfScope.Start(_logger, "FriendsOverview.LoadCache", thresholdMs: 25))
                {
                    data = _friendCache?.LoadFriendsOverviewData(0) ?? new FriendsOverviewData();
                }

                return CreateSnapshot(data, persisted);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to build friends overview snapshot.");
                return new FriendsOverviewSnapshot();
            }
        }

        internal static FriendsOverviewSnapshot CreateSnapshot(FriendsOverviewData data, PersistedSettings persisted)
        {
            var projection = new FriendOverviewProjection(data, persisted);
            return new FriendsOverviewSnapshot
            {
                Data = data ?? new FriendsOverviewData(),
                Projection = projection,
                Friends = projection.Friends.ToList(),
                Games = projection.AggregateGames.ToList(),
                RecentUnlocks = projection.RecentUnlocks.ToList(),
                AllAchievements = projection.AllAchievements.ToList(),
                AllUnlockedAchievements = projection.AllUnlockedAchievements.ToList()
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FriendsOverviewDataCoordinator));
            }
        }

        private bool IsDisposed()
        {
            lock (_syncRoot)
            {
                return _disposed;
            }
        }
    }
}

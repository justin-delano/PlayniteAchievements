using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        // Invalidation bursts (a friend scan flushes cache invalidations every ~2s) previously
        // fanned out one SnapshotInvalidated event each, and every consumer rebuild is a full
        // multi-second cache load materializing the whole friend display graph (~50k+ display
        // items). Coalescing to a leading fire plus one trailing fire per window caps rebuild
        // cadence while keeping isolated invalidations (settings toggles) instant.
        private static readonly TimeSpan DefaultInvalidationEventThrottleInterval = TimeSpan.FromSeconds(15);

        private readonly object _syncRoot = new object();
        private readonly IFriendCacheManager _friendCache;
        private readonly Func<PersistedSettings> _persistedSettingsFactory;
        private readonly ILogger _logger;
        private readonly TimeSpan _warmDebounceInterval;
        private readonly TimeSpan _invalidationEventThrottleInterval;
        private readonly Stopwatch _eventClock = Stopwatch.StartNew();
        private FriendsOverviewSnapshot _snapshot;
        private Task<FriendsOverviewSnapshot> _buildTask;
        private int _invalidationVersion = 1;
        private int _snapshotVersion;
        private int _buildTaskVersion;
        private int _warmGeneration;
        private TimeSpan? _lastInvalidationEventAt;
        private bool _trailingInvalidationScheduled;
        private bool _disposed;

        public FriendsOverviewDataCoordinator(
            IFriendCacheManager friendCache,
            Func<PersistedSettings> persistedSettingsFactory,
            ILogger logger = null,
            TimeSpan? warmDebounceInterval = null,
            TimeSpan? invalidationEventThrottleInterval = null)
        {
            _friendCache = friendCache;
            _persistedSettingsFactory = persistedSettingsFactory ?? (() => null);
            _logger = logger;
            _warmDebounceInterval = warmDebounceInterval ?? DefaultWarmDebounceInterval;
            _invalidationEventThrottleInterval =
                invalidationEventThrottleInterval ?? DefaultInvalidationEventThrottleInterval;
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
            bool fireNow;
            var trailingDelay = TimeSpan.Zero;
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _invalidationVersion++;
                // A version-mismatched snapshot can never be returned to a reader, so drop the
                // reference immediately instead of pinning the full friend row set until the
                // next successful build replaces it.
                _snapshot = null;

                var now = _eventClock.Elapsed;
                if (_lastInvalidationEventAt == null ||
                    now - _lastInvalidationEventAt.Value >= _invalidationEventThrottleInterval)
                {
                    _lastInvalidationEventAt = now;
                    fireNow = true;
                }
                else if (!_trailingInvalidationScheduled)
                {
                    _trailingInvalidationScheduled = true;
                    trailingDelay = _lastInvalidationEventAt.Value + _invalidationEventThrottleInterval - now;
                    fireNow = false;
                }
                else
                {
                    // A trailing fire is already scheduled; this invalidation folds into it. The
                    // version bump above still marks the snapshot stale for direct requests.
                    return;
                }
            }

            if (fireNow)
            {
                SnapshotInvalidated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _ = FireTrailingInvalidationAsync(trailingDelay);
            }
        }

        private async Task FireTrailingInvalidationAsync(TimeSpan delay)
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }

                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _trailingInvalidationScheduled = false;
                    _lastInvalidationEventAt = _eventClock.Elapsed;
                }

                SnapshotInvalidated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Trailing snapshot invalidation fire failed.");
            }
        }

        // Matches the overview/start-page projection warm: called once Playnite has finished
        // starting so the shared friend snapshot reflects a populated game database. A build may
        // already be in flight (e.g. a friends surface or friend-consuming theme requested one),
        // so this coalesces instead of always rebuilding; see WarmAfterDelayAsync for the
        // per-state behavior.
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
            cancel.ThrowIfCancellationRequested();

            Task<FriendsOverviewSnapshot> task;
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (!forceRefresh &&
                    _snapshot != null &&
                    _snapshotVersion == _invalidationVersion)
                {
                    return _snapshot;
                }

                if (!forceRefresh && _buildTask != null)
                {
                    task = _buildTask;
                }
                else
                {
                    _buildTaskVersion = _invalidationVersion;
                    task = Task.Run(BuildSnapshot);
                    _buildTask = task;
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
            }

            // Bounded staleness: the result reflects the cache as of build start. When an
            // invalidation lands mid-build, consumers converge through the next (throttled)
            // SnapshotInvalidated fire instead of looping here — during scans that loop rebuilt
            // the full friend display graph every few seconds.
            return snapshot ?? new FriendsOverviewSnapshot();
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

                // A snapshot build may already have completed or be in flight (a friends
                // surface or a friend-consuming theme requests one on demand). Rebuilding here
                // would repeat the multi-second friend cache load and hold the cache lock while
                // the main view is coming up, so coalesce instead:
                // - build in flight: leave it untouched. Invalidating now would make the
                //   awaiting consumer rebuild immediately, recreating the duplicate load.
                // - completed build: mark it stale (it may have resolved game presentation
                //   before Playnite's database was populated) and let the next consumer rebuild
                //   on demand.
                // - no snapshot and no build: eager warm, as before, so friend surfaces do not
                //   depend on another UI surface to trigger the first load.
                bool hasCurrentSnapshot;
                bool hasBuildInFlight;
                lock (_syncRoot)
                {
                    hasCurrentSnapshot = _snapshot != null && _snapshotVersion == _invalidationVersion;
                    hasBuildInFlight = _buildTask != null;
                }

                if (hasBuildInFlight)
                {
                    return;
                }

                Invalidate();
                if (hasCurrentSnapshot)
                {
                    return;
                }

                using (PerfScope.StartStartup(_logger, "Warm.FriendsOverviewSnapshot", thresholdMs: 250))
                {
                    await GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
                }
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
                var memBaseline = MemoryDiagnostics.Capture();
                var persisted = _persistedSettingsFactory();
                FriendsOverviewData data;
                using (PerfScope.Start(_logger, "FriendsOverview.LoadCache", thresholdMs: 25))
                {
                    data = _friendCache?.LoadFriendsOverviewData(0) ?? new FriendsOverviewData();
                }

                var snapshot = CreateSnapshot(data, persisted);
                MemoryDiagnostics.Log(
                    _logger,
                    "friendsOverview.build",
                    memBaseline,
                    $"friends={snapshot.Friends.Count} games={snapshot.Games.Count} recentUnlocks={snapshot.RecentUnlocks.Count} allAchievements={snapshot.AllAchievements.Count} allUnlocked={snapshot.AllUnlockedAchievements.Count}");
                return snapshot;
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

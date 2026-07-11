using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Cache;
using PlayniteAchievements.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Friends
{
    internal sealed class FriendGameAchievementsDataCoordinator : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly IFriendCacheManager _friendCache;
        private readonly FriendsOverviewDataCoordinator _overviewCoordinator;
        private readonly Func<PersistedSettings> _persistedSettingsFactory;
        private readonly ILogger _logger;
        private readonly Dictionary<Guid, CacheEntry> _snapshots = new Dictionary<Guid, CacheEntry>();
        private readonly Dictionary<Guid, BuildEntry> _buildTasks = new Dictionary<Guid, BuildEntry>();
        private int _invalidationVersion = 1;
        private bool _disposed;

        public FriendGameAchievementsDataCoordinator(
            IFriendCacheManager friendCache,
            FriendsOverviewDataCoordinator overviewCoordinator,
            Func<PersistedSettings> persistedSettingsFactory,
            ILogger logger = null)
        {
            _friendCache = friendCache;
            _overviewCoordinator = overviewCoordinator;
            _persistedSettingsFactory = persistedSettingsFactory ?? (() => null);
            _logger = logger;
        }

        public event EventHandler SnapshotInvalidated;

        public void Invalidate()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _invalidationVersion++;
                _snapshots.Clear();
            }

            SnapshotInvalidated?.Invoke(this, EventArgs.Empty);
        }

        public async Task<FriendsOverviewSnapshot> GetSnapshotAsync(Guid playniteGameId, CancellationToken cancel)
        {
            if (playniteGameId == Guid.Empty)
            {
                return new FriendsOverviewSnapshot();
            }

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                Task<FriendsOverviewSnapshot> task;
                lock (_syncRoot)
                {
                    ThrowIfDisposed();
                    if (_snapshots.TryGetValue(playniteGameId, out var cached) &&
                        cached.Version == _invalidationVersion)
                    {
                        return cached.Snapshot ?? new FriendsOverviewSnapshot();
                    }

                    if (_buildTasks.TryGetValue(playniteGameId, out var running))
                    {
                        task = running.Task;
                    }
                    else
                    {
                        var version = _invalidationVersion;
                        task = Task.Run(() => BuildSnapshot(playniteGameId));
                        _buildTasks[playniteGameId] = new BuildEntry
                        {
                            Version = version,
                            Task = task
                        };
                    }
                }

                var snapshot = await task.ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();

                lock (_syncRoot)
                {
                    if (_buildTasks.TryGetValue(playniteGameId, out var running) &&
                        ReferenceEquals(running.Task, task))
                    {
                        if (running.Version == _invalidationVersion)
                        {
                            _snapshots[playniteGameId] = new CacheEntry
                            {
                                Version = running.Version,
                                Snapshot = snapshot ?? new FriendsOverviewSnapshot()
                            };
                        }

                        _buildTasks.Remove(playniteGameId);
                    }

                    if (_snapshots.TryGetValue(playniteGameId, out var cached) &&
                        cached.Version == _invalidationVersion)
                    {
                        return cached.Snapshot ?? new FriendsOverviewSnapshot();
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _disposed = true;
                _snapshots.Clear();
                _buildTasks.Clear();
            }
        }

        private FriendsOverviewSnapshot BuildSnapshot(Guid playniteGameId)
        {
            try
            {
                if (_overviewCoordinator?.TryGetCurrentSnapshot(out var overviewSnapshot) == true)
                {
                    using (PerfScope.Start(_logger, "FriendsGameAchievements.DeriveWarmSnapshot", thresholdMs: 10))
                    {
                        return SliceWarmSnapshot(overviewSnapshot, playniteGameId);
                    }
                }

                var persisted = _persistedSettingsFactory();
                var hideSpoilers = persisted?.FriendsOverviewHideSpoilers ?? true;
                FriendsOverviewData data;
                using (PerfScope.Start(_logger, "FriendsGameAchievements.LoadCache", thresholdMs: 25))
                {
                    data = _friendCache?.LoadFriendGameAchievementData(playniteGameId, hideSpoilers) ??
                           new FriendsOverviewData();
                }

                using (PerfScope.Start(_logger, "FriendsGameAchievements.Project", thresholdMs: 10))
                {
                    return FriendsOverviewDataCoordinator.CreateSnapshot(data, persisted);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to build friend game achievements snapshot for {playniteGameId}.");
                return new FriendsOverviewSnapshot();
            }
        }

        private static FriendsOverviewSnapshot SliceWarmSnapshot(
            FriendsOverviewSnapshot source,
            Guid playniteGameId)
        {
            var allAchievements = (source?.AllAchievements ?? new List<FriendAchievementDisplayItem>())
                .Where(achievement => achievement?.PlayniteGameId == playniteGameId)
                .ToList();
            var allUnlocked = allAchievements
                .Where(achievement => achievement.Unlocked)
                .ToList();
            var recent = allUnlocked
                .Where(achievement => achievement.UnlockTimeUtc.HasValue)
                .OrderByDescending(achievement => achievement.UnlockTimeUtc ?? DateTime.MinValue)
                .ToList();
            var games = (source?.Games ?? new List<FriendGameSummaryItem>())
                .Where(game => game?.PlayniteGameId == playniteGameId)
                .ToList();
            var links = (source?.Data?.FriendGameLinks ?? new List<FriendGameLinkItem>())
                .Where(link => link?.PlayniteGameId == playniteGameId)
                .ToList();

            var friendKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var achievement in allAchievements)
            {
                var key = FriendOverviewProjection.GetFriendScopeKey(achievement);
                if (!FriendOverviewProjection.IsAllScope(key))
                {
                    friendKeys.Add(key);
                }
            }

            foreach (var link in links)
            {
                var key = FriendOverviewProjection.GetFriendScopeKey(link);
                if (!FriendOverviewProjection.IsAllScope(key))
                {
                    friendKeys.Add(key);
                }
            }

            var friends = (source?.Friends ?? new List<FriendSummaryItem>())
                .Where(friend => friendKeys.Contains(FriendOverviewProjection.GetFriendScopeKey(friend)))
                .ToList();
            var data = new FriendsOverviewData
            {
                Friends = friends,
                Games = games,
                FriendGameLinks = links,
                RecentUnlocks = recent,
                AllAchievements = allAchievements,
                AllUnlockedAchievements = allUnlocked
            };

            return new FriendsOverviewSnapshot
            {
                Data = data,
                Projection = source?.Projection ?? new FriendOverviewProjection(null),
                Friends = friends,
                Games = games,
                RecentUnlocks = recent,
                AllAchievements = allAchievements,
                AllUnlockedAchievements = allUnlocked
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FriendGameAchievementsDataCoordinator));
            }
        }

        private sealed class CacheEntry
        {
            public int Version { get; set; }
            public FriendsOverviewSnapshot Snapshot { get; set; }
        }

        private sealed class BuildEntry
        {
            public int Version { get; set; }
            public Task<FriendsOverviewSnapshot> Task { get; set; }
        }
    }
}

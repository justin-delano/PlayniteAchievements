using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using Playnite.SDK;

namespace PlayniteAchievements.Services
{

    public sealed class CacheManager : ICacheManager
    {
        private readonly ILogger _logger;
        private readonly CacheStorage _storage;

        private readonly object _sync = new object();

        // In-memory state (user achievements only)
        // key = "{playniteGameId}" OR "app:{appId}"
        private Dictionary<string, GameAchievementData> _userAchievements =
            new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<GameCacheUpdatedEventArgs> GameCacheUpdated;
        public event EventHandler CacheInvalidated;

        public CacheManager(IPlayniteAPI api, ILogger logger, PlayniteAchievementsPlugin plugin)
        {
            _logger = logger;
            _storage = new CacheStorage(plugin, logger);
        }

        private void RaiseGameCacheUpdatedEvent(string gameId)
        {
            try { GameCacheUpdated?.Invoke(this, new GameCacheUpdatedEventArgs(gameId)); }
            catch (Exception e)
            {
                _logger?.Error(e, ResourceProvider.GetString("LOCPlayAch_Error_NotifySubscribers"));
            }
        }
        
        private void RaiseCacheInvalidatedEvent()
        {
            try { CacheInvalidated?.Invoke(this, EventArgs.Empty); }
            catch (Exception e)
            {
                _logger?.Error(e, ResourceProvider.GetString("LOCPlayAch_Error_NotifySubscribers"));
            }
        }

        private void ClearMemoryState_Locked()
        {
            _userAchievements = new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase);
        }

        // ---------------------------
        // Disk presence policy
        // ---------------------------

        private bool CoreArtifactsPresent()
        {
            try
            {
                return Directory.Exists(_storage.UserCacheRootDir);
            }
            catch
            {
                return false;
            }
        }

        private bool InitializeCacheState_Locked()
        {
            if (!CoreArtifactsPresent())
            {
                ClearMemoryState_Locked();
                return true;
            }

            if (!Directory.Exists(_storage.UserCacheRootDir))
            {
                _storage.EnsureDir(_storage.UserCacheRootDir);
                _userAchievements = new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }

        public void EnsureDiskCacheOrClearMemory()
        {
            bool clearedAll;
            lock (_sync)
            {
                clearedAll = InitializeCacheState_Locked();
            }
            if (clearedAll) RaiseCacheInvalidatedEvent();
        }

        public bool CacheFileExists() => CoreArtifactsPresent();

        public bool IsCacheValid()
        {
            // Check if any cached games exist
            var cachedGames = GetCachedGameIds();
            return cachedGames.Any();
        }

        /// <summary>
        /// Gets a list of all cached game IDs (PlayniteGameId or app_{AppId}).
        /// </summary>
        public List<string> GetCachedGameIds()
        {
            lock (_sync)
            {
                InitializeCacheState_Locked();

                var files = _storage.EnumerateUserCacheFiles()?.ToList();
                if (files == null || files.Count == 0)
                    return new List<string>();

                var ids = new List<string>();
                foreach (var f in files)
                {
                    var key = Path.GetFileNameWithoutExtension(f);
                    if (!string.IsNullOrWhiteSpace(key))
                        ids.Add(key);
                }

                return ids;
            }
        }

        public void ClearCache()
        {
            lock (_sync)
            {
                ClearMemoryState_Locked();

                // Delete user achievement cache
                _storage.DeleteDirectoryIfExists(_storage.UserCacheRootDir);

                // Legacy cleanup: older dev builds created a provider-scoped cache folder.
                // Keep only a single per-game cache directory going forward.
                try
                {
                    var legacy = Path.Combine(_storage.BaseDir, "achievement_cache_by_provider");
                    _storage.DeleteDirectoryIfExists(legacy);
                }
                catch
                {
                }
            }

            RaiseCacheInvalidatedEvent();
        }
        // ---------------------------
        // User achievements
        // ---------------------------

        private string UserKey(string key) => key?.Trim() ?? string.Empty;

        public GameAchievementData LoadGameData(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) return null;

                lock (_sync)
                {
                    InitializeCacheState_Locked();

                    var k = UserKey(key);
                    if (_userAchievements.TryGetValue(k, out var cached) && cached != null)
                        return cached;

                    var disk = _storage.ReadUserAchievement(k);
                    if (disk != null)
                    {
                        disk.LastUpdatedUtc = DateTimeUtilities.AsUtcKind(disk.LastUpdatedUtc);

                        // Back-compat: older cache entries may not have persisted PlayniteGameId.
                        // Most fullscreen aggregation relies on this being present.
                        if (disk.PlayniteGameId == null && Guid.TryParse(k, out var parsedId))
                        {
                            disk.PlayniteGameId = parsedId;
                        }
                        _userAchievements[k] = disk;
                    }

                    return disk;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Error_FileOperationFailed"));
                return null;
            }
        }

        public void SaveGameData(string key, GameAchievementData data)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) return;

                var k = UserKey(key);
                lock (_sync)
                {
                    var toWrite = data ?? new GameAchievementData();
                    toWrite.LastUpdatedUtc = DateTimeUtilities.AsUtcKind(toWrite.LastUpdatedUtc);

                    // Ensure PlayniteGameId is stored for guid-keyed entries (fullscreen aggregation depends on it).
                    if (toWrite.PlayniteGameId == null && Guid.TryParse(k, out var parsedId))
                    {
                        toWrite.PlayniteGameId = parsedId;
                    }
                    
                    _storage.WriteUserAchievement(k, toWrite);

                    _userAchievements[k] = toWrite;
                }

                RaiseGameCacheUpdatedEvent(k);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Error_FileOperationFailed"));
            }
        }

        public void RemoveGameData(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                var k = UserKey(key);
                lock (_sync)
                {
                    InitializeCacheState_Locked();
                    _userAchievements.Remove(k);
                    _storage.DeleteUserAchievement(k);
                }

                RaiseGameCacheUpdatedEvent(k);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, ResourceProvider.GetString("LOCPlayAch_Error_FileOperationFailed"));
            }
        }

        public void NotifyCacheInvalidated()
        {
            RaiseCacheInvalidatedEvent();
        }
    }

    public class GameCacheUpdatedEventArgs : EventArgs
    {
        public string GameId { get; }
        public GameCacheUpdatedEventArgs(string gameId)
        {
            GameId = gameId;
        }
    }
}

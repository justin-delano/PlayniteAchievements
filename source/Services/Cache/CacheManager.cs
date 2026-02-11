using System;
using System.Collections.Generic;
using System.IO;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Database;
using Playnite.SDK;

namespace PlayniteAchievements.Services
{

    public sealed class CacheManager : ICacheManager
    {
        private readonly ILogger _logger;
        private readonly CacheStorage _storage;
        private readonly SqlNadoCacheStore _store;
        private readonly LegacyJsonCacheImporter _importer;

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
            _store = new SqlNadoCacheStore(plugin, logger, _storage.BaseDir);
            _importer = new LegacyJsonCacheImporter(_storage, _store, logger);

            try
            {
                _store.EnsureInitialized();
                _importer.ImportIfNeeded();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to initialize SQLNado achievement cache.");
            }
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
                return _store.HasAnyCurrentUserCacheRows();
            }
            catch
            {
                return false;
            }
        }

        private bool InitializeCacheState_Locked()
        {
            try
            {
                var hadDbFile = File.Exists(_store.DatabasePath);
                _store.EnsureInitialized();
                if (!hadDbFile)
                {
                    ClearMemoryState_Locked();
                    return true;
                }
            }
            catch
            {
                ClearMemoryState_Locked();
                return true;
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
            return CoreArtifactsPresent();
        }

        /// <summary>
        /// Gets a list of all cached game IDs (PlayniteGameId or app_{AppId}).
        /// </summary>
        public List<string> GetCachedGameIds()
        {
            lock (_sync)
            {
                InitializeCacheState_Locked();
                return _store.GetCachedGameIdsForCurrentUsers() ?? new List<string>();
            }
        }

        public void ClearCache()
        {
            lock (_sync)
            {
                ClearMemoryState_Locked();
                _store.ClearCacheData();

                // Legacy cleanup retained for old dev builds that created provider-scoped cache folders.
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
                    {
                        return cached;
                    }

                    var dbData = _store.LoadCurrentUserGameData(k);
                    if (dbData != null)
                    {
                        dbData.LastUpdatedUtc = DateTimeUtilities.AsUtcKind(dbData.LastUpdatedUtc);

                        // Back-compat: older cache entries may not have persisted PlayniteGameId.
                        if (dbData.PlayniteGameId == null && Guid.TryParse(k, out var parsedId))
                        {
                            dbData.PlayniteGameId = parsedId;
                        }
                        _userAchievements[k] = dbData;
                    }

                    return dbData;
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
                    if (toWrite.LastUpdatedUtc == default(DateTime))
                    {
                        toWrite.LastUpdatedUtc = DateTime.UtcNow;
                    }
                    toWrite.LastUpdatedUtc = DateTimeUtilities.AsUtcKind(toWrite.LastUpdatedUtc);

                    // Ensure PlayniteGameId is stored for guid-keyed entries (fullscreen aggregation depends on it).
                    if (toWrite.PlayniteGameId == null && Guid.TryParse(k, out var parsedId))
                    {
                        toWrite.PlayniteGameId = parsedId;
                    }

                    _store.SaveCurrentUserGameData(k, toWrite);

                    _userAchievements[k] = toWrite;
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

using System;
using System.Collections.Generic;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Services
{
    public interface ICacheManager
    {
        event EventHandler<GameCacheUpdatedEventArgs> GameCacheUpdated;
        event EventHandler<CacheDeltaEventArgs> CacheDeltaUpdated;
        event EventHandler CacheInvalidated;

        void EnsureDiskCacheOrClearMemory();
        bool CacheFileExists();
        bool IsCacheValid();

        // Per-game achievement cache
        List<string> GetCachedGameIds();
        GameAchievementData LoadGameData(string key);
        CacheWriteResult SaveGameData(string key, GameAchievementData data);
        CacheWriteResult SetCompletedMarker(Guid playniteGameId, string markerApiName);
        void RemoveGameData(Guid playniteGameId);
        void NotifyCacheInvalidated();

        void ClearCache();

        // Debug export
        string ExportDatabaseToCsv(string exportDirectory);
    }
}

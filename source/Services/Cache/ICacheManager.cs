using System;
using System.Collections.Generic;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Services
{
    public interface ICacheManager
    {
        event EventHandler<GameCacheUpdatedEventArgs> GameCacheUpdated;
        event EventHandler CacheInvalidated;

        void EnsureDiskCacheOrClearMemory();
        bool CacheFileExists();
        bool IsCacheValid();

        // Per-game achievement cache
        List<string> GetCachedGameIds();
        GameAchievementData LoadGameData(string key);
        void SaveGameData(string key, GameAchievementData data);
        void RemoveGameData(Guid playniteGameId);
        void NotifyCacheInvalidated();

        void ClearCache();
    }
}

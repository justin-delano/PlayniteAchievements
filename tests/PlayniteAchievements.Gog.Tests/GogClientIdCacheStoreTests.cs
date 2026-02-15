using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.GOG;
using System;
using System.IO;
using System.Threading;

namespace PlayniteAchievements.Gog.Tests
{
    [TestClass]
    public class GogClientIdCacheStoreTests
    {
        [TestMethod]
        public void CacheStore_PersistsAndLoadsEntries()
        {
            var tempRoot = CreateTempRoot();
            try
            {
                var store = new GogClientIdCacheStore(tempRoot, logger: null, ttl: TimeSpan.FromDays(30));
                store.SetClientId("1207664643", "client-abc");
                store.Save();

                var reloaded = new GogClientIdCacheStore(tempRoot, logger: null, ttl: TimeSpan.FromDays(30));
                var found = reloaded.TryGetClientId("1207664643", out var clientId);

                Assert.IsTrue(found);
                Assert.AreEqual("client-abc", clientId);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [TestMethod]
        public void CacheStore_PrunesExpiredEntriesOnLoad()
        {
            var tempRoot = CreateTempRoot();
            try
            {
                var ttl = TimeSpan.FromMilliseconds(10);
                var store = new GogClientIdCacheStore(tempRoot, logger: null, ttl: ttl);
                store.SetClientId("1207664644", "client-expiring");
                store.Save();

                Thread.Sleep(25);

                var reloaded = new GogClientIdCacheStore(tempRoot, logger: null, ttl: ttl);
                var found = reloaded.TryGetClientId("1207664644", out _);

                Assert.IsFalse(found);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [TestMethod]
        public void CacheStore_IgnoresCorruptCacheFile()
        {
            var tempRoot = CreateTempRoot();
            try
            {
                var cacheDir = Path.Combine(tempRoot, "gog");
                Directory.CreateDirectory(cacheDir);
                var cachePath = Path.Combine(cacheDir, "client_id_cache.json.gz");
                File.WriteAllBytes(cachePath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

                var store = new GogClientIdCacheStore(tempRoot, logger: null, ttl: TimeSpan.FromDays(30));
                var found = store.TryGetClientId("1207664645", out _);

                Assert.IsFalse(found);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static string CreateTempRoot()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAchievements.Gog.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup for test temp files.
            }
        }
    }
}

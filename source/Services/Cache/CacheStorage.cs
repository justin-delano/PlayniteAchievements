using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PlayniteAchievements.Models;
using Playnite.SDK;

namespace PlayniteAchievements.Services
{
    // Centralizes disk paths and atomic JSON read/write for cache artifacts.
    public sealed class CacheStorage
    {
        private readonly ILogger _logger;

        public string BaseDir { get; }
        public string UserCacheRootDir { get; }  // Per-game achievement cache directory

        public CacheStorage(PlayniteAchievementsPlugin plugin, ILogger logger)
        {
            _logger = logger;
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));

            BaseDir = plugin.GetPluginUserDataPath();
            UserCacheRootDir = Path.Combine(BaseDir, "achievement_cache");  // Per-game achievement cache

            EnsureDir(BaseDir);
            EnsureDir(UserCacheRootDir);
        }

        public void EnsureDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }



        public IEnumerable<string> EnumerateUserCacheFiles()
        {
            return Directory.Exists(UserCacheRootDir)
                ? Directory.EnumerateFiles(UserCacheRootDir, "*.json")
                : Enumerable.Empty<string>();
        }

        public string UserPath(string key) => Path.Combine(UserCacheRootDir, key + ".json");


        public GameAchievementData ReadUserAchievement(string key) => AtomicJson.Read<GameAchievementData>(UserPath(key));
        public void WriteUserAchievement(string key, GameAchievementData data)
        {
            EnsureDir(UserCacheRootDir);
            AtomicJson.WriteAtomic(UserPath(key), data);
        }

        public void DeleteFileIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public void DeleteDirectoryIfExists(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private static class AtomicJson
        {
            private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            public static T Read<T>(string path) where T : class
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                    var json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<T>(json, _settings);
                }
                catch
                {
                    return null;
                }
            }

            public static void WriteAtomic<T>(string path, T data)
            {
                if (string.IsNullOrWhiteSpace(path)) return;

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var tmp = path + ".tmp";
                try
                {
                    var json = JsonConvert.SerializeObject(data, _settings);
                    File.WriteAllText(tmp, json);

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tmp, path, destinationBackupFileName: null);
                            return;
                        }
                        catch
                        {
                            File.Delete(path);
                        }
                    }

                    File.Move(tmp, path);
                }
                finally
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
            }
        }
    }
}

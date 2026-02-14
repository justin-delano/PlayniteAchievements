using Playnite.SDK;
using System;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Database
{
    internal sealed class LegacyJsonCacheImporter
    {
        private readonly CacheStorage _storage;
        private readonly SqlNadoCacheStore _store;
        private readonly ILogger _logger;

        public LegacyJsonCacheImporter(CacheStorage storage, SqlNadoCacheStore store, ILogger logger)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger;
        }

        public void ImportIfNeeded()
        {
            _store.EnsureInitialized();
            if (string.Equals(_store.GetMetadata("legacy_import_done"), "1", StringComparison.Ordinal))
            {
                return;
            }

            int imported = 0;
            int failed = 0;
            int deleted = 0;
            int deleteFailed = 0;

            var files = _storage.EnumerateUserCacheFiles()?.ToList();
            if (files != null)
            {
                foreach (var file in files)
                {
                    try
                    {
                        var key = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        var data = _storage.ReadUserAchievement(key);
                        if (data == null)
                        {
                            continue;
                        }

                        _store.SaveCurrentUserGameData(key, data);
                        imported++;

                        try
                        {
                            _storage.DeleteFileIfExists(file);
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            deleteFailed++;
                            _logger?.Warn(ex, $"Imported legacy cache but failed to delete source file: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger?.Warn(ex, $"Failed importing legacy cache file: {file}");
                    }
                }
            }

            _store.SetMetadata("legacy_import_last_attempt_utc", DateTime.UtcNow.ToString("O"));
            _store.SetMetadata("legacy_import_imported_count", imported.ToString());
            _store.SetMetadata("legacy_import_failed_count", failed.ToString());
            _store.SetMetadata("legacy_import_deleted_count", deleted.ToString());
            _store.SetMetadata("legacy_import_delete_failed_count", deleteFailed.ToString());

            try
            {
                if (!_storage.EnumerateUserCacheFiles().Any())
                {
                    _storage.DeleteDirectoryIfExists(_storage.UserCacheRootDir);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to delete legacy achievement_cache directory after migration.");
            }

            if (SqlNadoCacheBehavior.ShouldMarkLegacyImportDone(failed))
            {
                _store.SetMetadata("legacy_import_done", "1");
                _store.SetMetadata("legacy_import_utc", DateTime.UtcNow.ToString("O"));
            }
            else
            {
                _store.SetMetadata("legacy_import_done", "0");
            }

            _logger?.Info($"Legacy cache import finished. Imported={imported}, Failed={failed}, Deleted={deleted}, DeleteFailed={deleteFailed}");
        }
    }
}

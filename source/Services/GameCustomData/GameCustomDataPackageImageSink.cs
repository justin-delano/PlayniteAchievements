using PlayniteAchievements.Services.Images;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace PlayniteAchievements.Services.GameCustomData
{
    public sealed partial class GameCustomDataStore
    {
        /// <summary>
        /// Where the package rewrites put a bundled image once its entry has been validated. The
        /// rewrite bodies own all validation (entry path, presence, decodability, URL passthrough),
        /// so an import and a preview reject the same packages and differ only in this target.
        /// </summary>
        private interface IPackageImageSink
        {
            /// <summary>Called where an import resolves the managed icon service; throws when icons cannot be written.</summary>
            void EnsureIconTargetAvailable();

            /// <summary>Called where an import checks the notification image store; throws when notification images cannot be written.</summary>
            void EnsureNotificationTargetAvailable();

            /// <summary>Writes an achievement icon override entry and returns the path the override should hold.</summary>
            string WriteAchievementIcon(ZipArchiveEntry entry, string fileStem, AchievementIconVariant variant);

            /// <summary>Writes a custom achievement icon entry and returns the path the definition should hold.</summary>
            string WriteCustomAchievementIcon(ZipArchiveEntry entry, string fileStem, AchievementIconVariant variant);

            /// <summary>Writes a category image entry and returns the path the category override should hold.</summary>
            string WriteCategoryImage(ZipArchiveEntry entry, string fileStem);

            /// <summary>Writes a notification image entry and returns the path the style slot should hold.</summary>
            string WriteNotificationImage(ZipArchiveEntry entry, NotificationImageSlot slot, string extension);
        }

        /// <summary>
        /// The import target: the game's managed custom icon folder and the notification image
        /// store, written exactly as the import path always has.
        /// </summary>
        private sealed class ManagedPackageImageSink : IPackageImageSink
        {
            private readonly GameCustomDataStore _store;
            private readonly Guid _playniteGameId;
            private readonly string _gameIdText;

            public ManagedPackageImageSink(GameCustomDataStore store, Guid playniteGameId)
            {
                _store = store;
                _playniteGameId = playniteGameId;
                _gameIdText = playniteGameId.ToString("D");
            }

            public void EnsureIconTargetAvailable()
            {
                _store.GetManagedCustomIconServiceOrThrow();
            }

            public void EnsureNotificationTargetAvailable()
            {
                if (_store._notificationImageStore == null)
                {
                    throw new InvalidOperationException("Notification image store is not available.");
                }
            }

            public string WriteAchievementIcon(ZipArchiveEntry entry, string fileStem, AchievementIconVariant variant)
            {
                var targetPath = _store.GetManagedCustomIconServiceOrThrow()
                    .GetAchievementCustomIconPath(_gameIdText, fileStem, variant);
                CopyEntryTo(entry, targetPath);
                return targetPath;
            }

            public string WriteCustomAchievementIcon(ZipArchiveEntry entry, string fileStem, AchievementIconVariant variant)
            {
                return _store.ImportPackageImageToManagedPath(_playniteGameId, entry, fileStem, variant);
            }

            public string WriteCategoryImage(ZipArchiveEntry entry, string fileStem)
            {
                var targetPath = _store.GetManagedCustomIconServiceOrThrow()
                    .GetCategoryCustomImagePath(_gameIdText, fileStem);
                CopyEntryTo(entry, targetPath);
                return targetPath;
            }

            public string WriteNotificationImage(ZipArchiveEntry entry, NotificationImageSlot slot, string extension)
            {
                var tempDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "PlayniteAchievements",
                    "PortableNotificationImports");
                Directory.CreateDirectory(tempDirectory);
                var tempPath = Path.Combine(
                    tempDirectory,
                    Guid.NewGuid().ToString("N") + extension);
                try
                {
                    using (var source = entry.Open())
                    using (var destination = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        source.CopyTo(destination);
                    }

                    var managedPath = _store._notificationImageStore
                        .MaterializeAsync(
                            tempPath,
                            NotificationImageOwner.ForGame(_playniteGameId),
                            slot,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    if (string.IsNullOrWhiteSpace(managedPath) || !File.Exists(managedPath))
                    {
                        throw new InvalidOperationException(
                            $"Failed to import packaged notification image '{entry.FullName}'.");
                    }

                    return managedPath;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            private static void CopyEntryTo(ZipArchiveEntry entry, string targetPath)
            {
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                using (var source = entry.Open())
                using (var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    source.CopyTo(destination);
                }
            }
        }

        /// <summary>
        /// The preview target: extracts each entry once into a caller-supplied directory under a
        /// unique file name and returns its absolute path. Touches no managed store.
        /// </summary>
        private sealed class ScratchPackageImageSink : IPackageImageSink
        {
            private readonly string _directory;
            private readonly Dictionary<string, string> _extracted =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public ScratchPackageImageSink(string directory)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new ArgumentException("Scratch directory is required.", nameof(directory));
                }

                _directory = Path.GetFullPath(directory);
                Directory.CreateDirectory(_directory);
            }

            /// <summary>Extracted files, keyed by normalized package entry name.</summary>
            public IReadOnlyDictionary<string, string> Extracted => _extracted;

            public void EnsureIconTargetAvailable()
            {
            }

            public void EnsureNotificationTargetAvailable()
            {
            }

            public string WriteAchievementIcon(ZipArchiveEntry entry, string fileStem, AchievementIconVariant variant)
            {
                return Extract(entry, null);
            }

            public string WriteCustomAchievementIcon(ZipArchiveEntry entry, string fileStem, AchievementIconVariant variant)
            {
                return Extract(entry, null);
            }

            public string WriteCategoryImage(ZipArchiveEntry entry, string fileStem)
            {
                return Extract(entry, null);
            }

            public string WriteNotificationImage(ZipArchiveEntry entry, NotificationImageSlot slot, string extension)
            {
                return Extract(entry, extension);
            }

            /// <summary>
            /// Extracts an entry, reusing the earlier file when the same entry was already
            /// extracted. The extension defaults to the entry's own, or .png when it has none.
            /// </summary>
            public string Extract(ZipArchiveEntry entry, string extension)
            {
                if (entry == null)
                {
                    throw new InvalidOperationException("Package image entry is missing.");
                }

                var key = NormalizeArchiveEntryName(entry.FullName) ?? entry.FullName;
                if (_extracted.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(entry.Name);
                }

                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".png";
                }

                var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + extension);
                using (var source = entry.Open())
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    source.CopyTo(destination);
                }

                _extracted[key] = path;
                return path;
            }
        }
    }
}

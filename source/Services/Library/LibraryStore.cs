using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.Workshop;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The library index: one entry per preset file in the preset folders, plus Workshop items
    /// that have no stored package. The preset folders stay the file layer; the index at
    /// <c>UserData\library\library.json</c> adds identity, origin and version. The first read
    /// reconciles the index with the folders: unindexed files become local items, entries whose
    /// file is gone are dropped (their ids collect in <see cref="TakeDroppedIds"/> so links to
    /// them can be removed), and changed files get their new hash. Thread-safe.
    /// </summary>
    public sealed class LibraryStore
    {
        public const string DirectoryName = "library";
        public const string IndexFileName = "library.json";
        public const string ShowcaseFolderName = "showcase_presets";
        public const string GameDataFolderName = "gamedata_presets";

        private const int SchemaVersion = 1;

        private static readonly IReadOnlyDictionary<LibraryItemKind, KindLayout> Layouts =
            new Dictionary<LibraryItemKind, KindLayout>
            {
                [LibraryItemKind.Colors] = new KindLayout("color_presets", ColorPackPortableStore.PackageFileExtension),
                [LibraryItemKind.Sounds] = new KindLayout("unlock_sound_presets", UnlockSoundPortableStore.PackageFileExtension),
                [LibraryItemKind.Toast] = new KindLayout(
                    Path.Combine("notification_style_presets", "toast"),
                    NotificationStylePortableStore.ToastPackageFileExtension),
                [LibraryItemKind.Frame] = new KindLayout(
                    Path.Combine("notification_style_presets", "frame"),
                    NotificationStylePortableStore.FramePackageFileExtension),
                [LibraryItemKind.ShowcasePage] = new KindLayout(ShowcaseFolderName, ShowcasePagePortableStore.PackageFileExtension),
                [LibraryItemKind.GameData] = new KindLayout(GameDataFolderName, GameCustomDataStore.PortableFileExtension)
            };

        private readonly string _root;
        private readonly string _indexPath;
        private readonly Action<Exception, string> _warn;
        private readonly Func<DateTime> _utcNow;
        private readonly object _sync = new object();
        private readonly List<string> _pendingDropped = new List<string>();
        private List<LibraryItem> _items;

        private sealed class KindLayout
        {
            public KindLayout(string folder, string extension)
            {
                Folder = folder;
                Extension = extension;
            }

            public string Folder { get; }

            public string Extension { get; }
        }

        private sealed class IndexFile
        {
            public int SchemaVersion { get; set; }

            public List<LibraryItem> Items { get; set; } = new List<LibraryItem>();
        }

        /// <param name="pluginUserDataPath">The plugin's user data folder; preset folders and the index live under it.</param>
        /// <param name="warn">Optional sink for read and write failures.</param>
        /// <param name="utcNow">Optional clock, for tests.</param>
        public LibraryStore(string pluginUserDataPath, Action<Exception, string> warn = null, Func<DateTime> utcNow = null)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            _root = pluginUserDataPath;
            _indexPath = Path.Combine(_root, DirectoryName, IndexFileName);
            _warn = warn;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Raised after the index changed, on the thread that changed it.</summary>
        public event EventHandler Changed;

        /// <summary>The plugin's user data folder that relative paths resolve against.</summary>
        public string RootPath => _root;

        /// <summary>The library folder (<c>UserData\library</c>), which also holds links and baselines.</summary>
        public string LibraryDirectory => Path.Combine(_root, DirectoryName);

        public string IndexPath => _indexPath;

        /// <summary>Whether the index file exists; false before the first save, which is what keys the migration.</summary>
        public bool IndexExists => File.Exists(_indexPath);

        /// <summary>The folder, relative to the user data folder, that holds presets of a kind.</summary>
        public static string FolderOf(LibraryItemKind kind) => Layouts[kind].Folder;

        /// <summary>The preset file extension of a kind, including the dot.</summary>
        public static string ExtensionOf(LibraryItemKind kind) => Layouts[kind].Extension;

        /// <summary>Every item, as copies.</summary>
        public IReadOnlyList<LibraryItem> Items
        {
            get
            {
                lock (_sync)
                {
                    EnsureLoaded();
                    return _items.Select(item => item.Clone()).ToList();
                }
            }
        }

        public LibraryItem Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            lock (_sync)
            {
                EnsureLoaded();
                return FindLocked(id)?.Clone();
            }
        }

        /// <summary>The item whose preset file is <paramref name="path"/> (absolute, or relative to the user data folder).</summary>
        public LibraryItem FindByPath(string path)
        {
            var relative = ToRelativePath(path);
            if (relative == null)
            {
                return null;
            }

            lock (_sync)
            {
                EnsureLoaded();
                return _items.FirstOrDefault(item => SamePath(item.RelativePath, relative))?.Clone();
            }
        }

        /// <summary>The absolute path of an item's preset file, or null when it has none.</summary>
        public string FullPath(LibraryItem item)
        {
            return string.IsNullOrEmpty(item?.RelativePath) ? null : Path.Combine(_root, item.RelativePath);
        }

        /// <summary>A path relative to the user data folder; null when it lies outside it.</summary>
        public string ToRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!Path.IsPathRooted(path))
            {
                return path;
            }

            var root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : null;
        }

        /// <summary>
        /// Adds an item, or replaces the item with the same id or the same preset file, and saves.
        /// A missing hash is computed from the file; a local item's version is its hash. The
        /// added time of a replaced item is kept. Returns the stored copy.
        /// </summary>
        public LibraryItem Upsert(LibraryItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (string.IsNullOrWhiteSpace(item.Id))
            {
                throw new ArgumentException("A library item needs an id.", nameof(item));
            }

            LibraryItem stored;
            lock (_sync)
            {
                EnsureLoaded();
                stored = UpsertLocked(item.Clone());
                Save();
            }

            RaiseChanged();
            return stored.Clone();
        }

        /// <summary>
        /// Replaces the item <paramref name="oldId"/> with <paramref name="item"/> in one save, for
        /// a local item that turns out to be a Workshop item. Returns the stored copy.
        /// </summary>
        public LibraryItem Replace(string oldId, LibraryItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            LibraryItem stored;
            lock (_sync)
            {
                EnsureLoaded();
                var previous = FindLocked(oldId);
                if (previous != null && !string.Equals(previous.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _items.Remove(previous);
                }

                var copy = item.Clone();
                if (previous != null && copy.AddedUtc == default(DateTime))
                {
                    copy.AddedUtc = previous.AddedUtc;
                }

                stored = UpsertLocked(copy);
                Save();
            }

            RaiseChanged();
            return stored.Clone();
        }

        public bool Remove(string id)
        {
            bool removed;
            lock (_sync)
            {
                EnsureLoaded();
                removed = _items.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
                if (removed)
                {
                    Save();
                }
            }

            if (removed)
            {
                RaiseChanged();
            }

            return removed;
        }

        /// <summary>
        /// Brings the index in line with the preset folders and saves when anything changed.
        /// Dropped ids are also queued for <see cref="TakeDroppedIds"/>.
        /// </summary>
        public LibraryReconcileResult Reconcile()
        {
            LibraryReconcileResult result;
            lock (_sync)
            {
                if (_items == null)
                {
                    // The load reconciles; report what it did.
                    result = Load();
                }
                else
                {
                    result = ReconcileLocked();
                    if (result.HasChanges)
                    {
                        Save();
                    }
                }
            }

            if (result.HasChanges)
            {
                RaiseChanged();
            }

            return result;
        }

        /// <summary>The ids of items dropped by reconciles since the last call, so their links can be removed.</summary>
        public IReadOnlyList<string> TakeDroppedIds()
        {
            lock (_sync)
            {
                EnsureLoaded();
                var dropped = _pendingDropped.ToList();
                _pendingDropped.Clear();
                return dropped;
            }
        }

        /// <summary>Lowercase hex SHA-256 of a file, the format Workshop installs record.</summary>
        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        // ---- internals ------------------------------------------------------------------------

        /// <summary>Loads and reconciles on first use. A load raises no change notice: nothing read the index before it.</summary>
        private void EnsureLoaded()
        {
            if (_items == null)
            {
                Load();
            }
        }

        private LibraryReconcileResult Load()
        {
            _items = new List<LibraryItem>();
            var hadIndex = File.Exists(_indexPath);
            if (hadIndex)
            {
                try
                {
                    var index = JsonConvert.DeserializeObject<IndexFile>(File.ReadAllText(_indexPath));
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in index?.Items ?? new List<LibraryItem>())
                    {
                        if (item != null && !string.IsNullOrWhiteSpace(item.Id) && Layouts.ContainsKey(item.Kind) && seen.Add(item.Id))
                        {
                            _items.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _warn?.Invoke(ex, "Failed reading the library index; it is rebuilt from the preset folders.");
                    TryBackUpUnreadableIndex();
                }
            }

            var result = ReconcileLocked();
            if (result.HasChanges || !hadIndex)
            {
                Save();
            }

            return result;
        }

        private void TryBackUpUnreadableIndex()
        {
            try
            {
                File.Copy(_indexPath, _indexPath + ".unreadable", overwrite: true);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, "Failed keeping a copy of the unreadable library index.");
            }
        }

        private LibraryReconcileResult ReconcileLocked()
        {
            var result = new LibraryReconcileResult();
            var now = _utcNow();

            // Drop duplicate claims on one file; the first entry keeps it.
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _items.ToList())
            {
                if (!string.IsNullOrEmpty(item.RelativePath) && !claimed.Add(NormalizePath(item.RelativePath)))
                {
                    _items.Remove(item);
                    result.DroppedIds.Add(item.Id);
                }
            }

            // Every preset file on disk, by kind.
            var unindexed = new List<(LibraryItemKind Kind, string RelativePath)>();
            foreach (var pair in Layouts)
            {
                foreach (var relative in ListPresetFiles(pair.Key))
                {
                    if (!claimed.Contains(NormalizePath(relative)))
                    {
                        unindexed.Add((pair.Key, relative));
                    }
                }
            }

            // Indexed files: gone (perhaps renamed) or changed.
            var missing = new List<LibraryItem>();
            foreach (var item in _items)
            {
                if (string.IsNullOrEmpty(item.RelativePath))
                {
                    continue;
                }

                var full = Path.Combine(_root, item.RelativePath);
                if (!File.Exists(full))
                {
                    missing.Add(item);
                    continue;
                }

                var hash = HashCached(item, full);
                if (hash == null || string.Equals(hash, item.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.ContentHash = hash;
                item.UpdatedUtc = now;
                if (item.IsWorkshop)
                {
                    result.ModifiedWorkshopIds.Add(item.Id);
                }
                else
                {
                    item.Version = hash;
                    result.NewVersionIds.Add(item.Id);
                }
            }

            // Unindexed files: a renamed indexed file (same kind and content), or a new local item.
            foreach (var file in unindexed)
            {
                var full = Path.Combine(_root, file.RelativePath);
                var info = TryGetFileInfo(full);
                var hash = TryHash(full);
                if (info == null || hash == null)
                {
                    continue;
                }

                var moved = missing.FirstOrDefault(item =>
                    item.Kind == file.Kind && string.Equals(item.ContentHash, hash, StringComparison.OrdinalIgnoreCase));
                if (moved != null)
                {
                    missing.Remove(moved);
                    moved.RelativePath = file.RelativePath;
                    moved.FileLength = info.Length;
                    moved.FileWriteUtc = info.LastWriteTimeUtc;
                    if (!moved.IsWorkshop)
                    {
                        moved.Name = Path.GetFileNameWithoutExtension(file.RelativePath);
                    }

                    moved.UpdatedUtc = now;
                    result.MovedIds.Add(moved.Id);
                    continue;
                }

                var added = new LibraryItem
                {
                    Id = LibraryItem.NewLocalId(),
                    Kind = file.Kind,
                    Name = Path.GetFileNameWithoutExtension(file.RelativePath),
                    RelativePath = file.RelativePath,
                    Origin = LibraryItemOrigin.Local,
                    ContentHash = hash,
                    Version = hash,
                    FileLength = info.Length,
                    FileWriteUtc = info.LastWriteTimeUtc,
                    AddedUtc = now,
                    UpdatedUtc = now
                };
                _items.Add(added);
                result.AddedIds.Add(added.Id);
            }

            foreach (var item in missing)
            {
                _items.Remove(item);
                result.DroppedIds.Add(item.Id);
            }

            _pendingDropped.AddRange(result.DroppedIds);
            return result;
        }

        private IEnumerable<string> ListPresetFiles(LibraryItemKind kind)
        {
            var layout = Layouts[kind];
            var directory = Path.Combine(_root, layout.Folder);
            if (!Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                return Directory.EnumerateFiles(directory)
                    .Where(path => path.EndsWith(layout.Extension, StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.Combine(layout.Folder, Path.GetFileName(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, $"Failed listing the presets in {directory}.");
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>The file's hash, re-read only when its length or write time changed since the last hash.</summary>
        private string HashCached(LibraryItem item, string full)
        {
            var info = TryGetFileInfo(full);
            if (info == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(item.ContentHash)
                && item.FileLength == info.Length
                && item.FileWriteUtc == info.LastWriteTimeUtc)
            {
                return item.ContentHash;
            }

            var hash = TryHash(full);
            if (hash != null)
            {
                item.FileLength = info.Length;
                item.FileWriteUtc = info.LastWriteTimeUtc;
            }

            return hash;
        }

        private LibraryItem UpsertLocked(LibraryItem item)
        {
            var now = _utcNow();
            if (!string.IsNullOrEmpty(item.RelativePath))
            {
                item.RelativePath = ToRelativePath(item.RelativePath) ?? item.RelativePath;
                var full = Path.Combine(_root, item.RelativePath);
                var info = TryGetFileInfo(full);
                if (string.IsNullOrEmpty(item.ContentHash) || info == null
                    || item.FileLength != info.Length || item.FileWriteUtc != info.LastWriteTimeUtc)
                {
                    var hash = info == null ? null : TryHash(full);
                    if (hash != null)
                    {
                        item.ContentHash = hash;
                        item.FileLength = info.Length;
                        item.FileWriteUtc = info.LastWriteTimeUtc;
                    }
                }
            }

            if (!item.IsWorkshop)
            {
                item.Version = item.ContentHash;
            }

            var replaced = _items
                .Where(existing =>
                    string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(item.RelativePath) && SamePath(existing.RelativePath, item.RelativePath)))
                .ToList();
            foreach (var existing in replaced)
            {
                _items.Remove(existing);
            }

            if (item.AddedUtc == default(DateTime))
            {
                item.AddedUtc = replaced.Count > 0 ? replaced.Min(existing => existing.AddedUtc) : now;
            }

            item.UpdatedUtc = now;
            _items.Add(item);
            return item;
        }

        private LibraryItem FindLocked(string id)
        {
            return _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_indexPath));
                var temp = _indexPath + ".tmp";
                File.WriteAllText(
                    temp,
                    JsonConvert.SerializeObject(new IndexFile { SchemaVersion = SchemaVersion, Items = _items }, Formatting.Indented));
                if (File.Exists(_indexPath))
                {
                    File.Delete(_indexPath);
                }

                File.Move(temp, _indexPath);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, "Failed writing the library index.");
            }
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, "A library change handler failed.");
            }
        }

        private FileInfo TryGetFileInfo(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info : null;
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, $"Failed reading {path}.");
                return null;
            }
        }

        private string TryHash(string path)
        {
            try
            {
                return HashFile(path);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, $"Failed hashing {path}.");
                return null;
            }
        }

        private static bool SamePath(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
                   && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string relative)
        {
            return relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        }
    }
}

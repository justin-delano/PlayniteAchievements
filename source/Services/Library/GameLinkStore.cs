using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The links of per-game targets (<c>toast:game:&lt;id&gt;</c>, <c>frame:game:&lt;id&gt;</c>,
    /// <c>gamedata:&lt;id&gt;</c>) in <c>UserData\library\links.json</c>. They live in a file
    /// rather than the settings because per-game data is saved outside the settings edit session.
    /// Thread-safe; reads return copies.
    /// </summary>
    public sealed class GameLinkStore
    {
        public const string FileName = "links.json";

        private const int SchemaVersion = 1;

        private readonly string _path;
        private readonly Action<Exception, string> _warn;
        private readonly object _sync = new object();
        private Dictionary<string, LibraryLink> _links;

        private sealed class LinksFile
        {
            public int SchemaVersion { get; set; }

            public Dictionary<string, LibraryLink> Links { get; set; }
        }

        /// <param name="libraryDirectory">The library folder (<c>UserData\library</c>).</param>
        /// <param name="warn">Optional sink for read and write failures.</param>
        public GameLinkStore(string libraryDirectory, Action<Exception, string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(libraryDirectory))
            {
                throw new ArgumentException("The library folder is required.", nameof(libraryDirectory));
            }

            _path = Path.Combine(libraryDirectory, FileName);
            _warn = warn;
        }

        /// <summary>Raised after a link changed, on the thread that changed it.</summary>
        public event EventHandler Changed;

        public string FilePath => _path;

        /// <summary>Every link, as copies.</summary>
        public IReadOnlyDictionary<string, LibraryLink> All
        {
            get
            {
                lock (_sync)
                {
                    EnsureLoaded();
                    return LibraryLink.CloneAll(_links);
                }
            }
        }

        public LibraryLink Get(string targetKey)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                return null;
            }

            lock (_sync)
            {
                EnsureLoaded();
                return _links.TryGetValue(targetKey, out var link) ? link.Clone() : null;
            }
        }

        /// <summary>Stores a copy of the link of a per-game target, or removes it when <paramref name="link"/> is null.</summary>
        public void Set(string targetKey, LibraryLink link)
        {
            if (!LibraryTargetKeys.IsPerGame(targetKey))
            {
                throw new ArgumentException($"'{targetKey}' is not a per-game target; its link belongs in the settings.", nameof(targetKey));
            }

            bool changed;
            lock (_sync)
            {
                EnsureLoaded();
                if (link == null)
                {
                    changed = _links.Remove(targetKey);
                }
                else
                {
                    _links[targetKey] = link.Clone();
                    changed = true;
                }

                if (changed)
                {
                    Save();
                }
            }

            if (changed)
            {
                RaiseChanged();
            }
        }

        public void Remove(string targetKey) => Set(targetKey, null);

        /// <summary>The target keys whose links point at <paramref name="libraryItemId"/>.</summary>
        public IReadOnlyList<string> TargetsOf(string libraryItemId)
        {
            lock (_sync)
            {
                EnsureLoaded();
                return _links
                    .Where(pair => string.Equals(pair.Value.LibraryItemId, libraryItemId, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToList();
            }
        }

        /// <summary>Removes every link to the given items (items dropped from the library). Returns the target keys unlinked.</summary>
        public IReadOnlyList<string> UnlinkItems(IEnumerable<string> libraryItemIds)
        {
            var ids = new HashSet<string>(libraryItemIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            List<string> removed;
            lock (_sync)
            {
                EnsureLoaded();
                removed = _links.Where(pair => ids.Contains(pair.Value.LibraryItemId ?? string.Empty)).Select(pair => pair.Key).ToList();
                foreach (var key in removed)
                {
                    _links.Remove(key);
                }

                if (removed.Count > 0)
                {
                    Save();
                }
            }

            if (removed.Count > 0)
            {
                RaiseChanged();
            }

            return removed;
        }

        /// <summary>Points the links of <paramref name="oldId"/> at <paramref name="newId"/>, for a local item that became a Workshop item.</summary>
        public int RenameItem(string oldId, string newId)
        {
            if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
            {
                return 0;
            }

            var count = 0;
            lock (_sync)
            {
                EnsureLoaded();
                foreach (var link in _links.Values)
                {
                    if (string.Equals(link.LibraryItemId, oldId, StringComparison.OrdinalIgnoreCase))
                    {
                        link.LibraryItemId = newId;
                        count++;
                    }
                }

                if (count > 0)
                {
                    Save();
                }
            }

            if (count > 0)
            {
                RaiseChanged();
            }

            return count;
        }

        private void EnsureLoaded()
        {
            if (_links != null)
            {
                return;
            }

            _links = new Dictionary<string, LibraryLink>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(_path))
            {
                return;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<LinksFile>(File.ReadAllText(_path));
                _links = LibraryLink.CloneAll(file?.Links);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, "Failed reading the per-game library links; starting empty.");
                try
                {
                    File.Copy(_path, _path + ".unreadable", overwrite: true);
                }
                catch (Exception copyEx)
                {
                    _warn?.Invoke(copyEx, "Failed keeping a copy of the unreadable per-game library links.");
                }
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var temp = _path + ".tmp";
                File.WriteAllText(
                    temp,
                    JsonConvert.SerializeObject(new LinksFile { SchemaVersion = SchemaVersion, Links = _links }, Formatting.Indented));
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                File.Move(temp, _path);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, "Failed writing the per-game library links.");
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
                _warn?.Invoke(ex, "A library link change handler failed.");
            }
        }
    }
}

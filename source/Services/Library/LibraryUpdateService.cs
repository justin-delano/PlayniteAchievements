using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>How a new package meets the targets that follow its item.</summary>
    public enum LibraryApplyMode
    {
        /// <summary>The user's edits since the last apply stay; everything else follows the package.</summary>
        Merge,

        /// <summary>The package is applied as published.</summary>
        Replace
    }

    /// <summary>What writing one Workshop part into the library did.</summary>
    public sealed class LibraryPartWrite
    {
        public LibraryPartWrite(LibraryItem item, string writtenName, string keptLocalCopyName)
        {
            Item = item;
            WrittenName = writtenName;
            KeptLocalCopyName = keptLocalCopyName;
        }

        /// <summary>The stored Workshop item.</summary>
        public LibraryItem Item { get; }

        /// <summary>The preset name the package was written under.</summary>
        public string WrittenName { get; }

        /// <summary>The name of the edited file kept as a local item, or null when nothing was edited.</summary>
        public string KeptLocalCopyName { get; }
    }

    /// <summary>What merging an item's current package into its followers did.</summary>
    public sealed class LibraryMergeReport
    {
        /// <summary>Targets that now hold the item's current version.</summary>
        public List<string> UpdatedTargets { get; } = new List<string>();

        /// <summary>Values kept from the user's edits that differ from the package.</summary>
        public int KeptEdits { get; set; }

        /// <summary>Followers of a kind without an adapter yet: they take the new version when it is applied again.</summary>
        public List<string> PendingTargets { get; } = new List<string>();
    }

    /// <summary>One target that follows a library item, as the Library page lists it.</summary>
    public sealed class LibraryTargetUse
    {
        public LibraryTargetUse(string targetKey, LibraryLink link, bool isEdited, bool isUpdateAvailable, bool canReset)
        {
            TargetKey = targetKey;
            Link = link;
            IsEdited = isEdited;
            IsUpdateAvailable = isUpdateAvailable;
            CanReset = canReset;
        }

        public string TargetKey { get; }

        public LibraryLink Link { get; }

        /// <summary>True when the target changed since the item was applied; only known for kinds with an adapter.</summary>
        public bool IsEdited { get; }

        /// <summary>True when the item has a version the target has not taken yet.</summary>
        public bool IsUpdateAvailable { get; }

        /// <summary>True when the item can be applied again as published from here.</summary>
        public bool CanReset { get; }
    }

    /// <summary>
    /// Keeps library items and the targets that follow them in step: writes a Workshop part's new
    /// package into the library (an edited file stays as a local item), merges an item's current
    /// version into its followers through the settings adapters, and ends links to items that
    /// are gone. Settings-backed followers changed because of work on disk (a new package, a
    /// deleted file) are written to the live settings and the settings edit snapshot together,
    /// so a Cancel cannot bring back a link to something that changed underneath it.
    /// </summary>
    public sealed class LibraryUpdateService
    {
        private readonly LibraryApplyService _apply;
        private readonly GameLinkStore _gameLinks;
        private readonly IReadOnlyList<ISettingsLibraryAdapter> _adapters;
        private readonly Func<PersistedSettings> _live;
        private readonly Action<Action<PersistedSettings>> _updateIncludingSnapshot;
        private readonly Action<Action<PersistedSettings>> _updateLive;

        /// <param name="apply">The apply service over the library.</param>
        /// <param name="gameLinks">The per-game links.</param>
        /// <param name="adapters">The settings-backed adapters, one per kind that has one.</param>
        /// <param name="live">The live settings.</param>
        /// <param name="updateIncludingSnapshot">Applies a change to the live settings and any open edit snapshot, and saves outside an edit session.</param>
        /// <param name="updateLive">Applies a change to the live settings only, and saves outside an edit session.</param>
        public LibraryUpdateService(
            LibraryApplyService apply,
            GameLinkStore gameLinks,
            IEnumerable<ISettingsLibraryAdapter> adapters,
            Func<PersistedSettings> live,
            Action<Action<PersistedSettings>> updateIncludingSnapshot,
            Action<Action<PersistedSettings>> updateLive)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _gameLinks = gameLinks ?? throw new ArgumentNullException(nameof(gameLinks));
            _adapters = (adapters ?? Enumerable.Empty<ISettingsLibraryAdapter>()).Where(adapter => adapter != null).ToList();
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _updateIncludingSnapshot = updateIncludingSnapshot ?? throw new ArgumentNullException(nameof(updateIncludingSnapshot));
            _updateLive = updateLive ?? throw new ArgumentNullException(nameof(updateLive));
        }

        public LibraryStore Library => _apply.Library;

        public GameLinkStore GameLinks => _gameLinks;

        /// <summary>The adapter of a kind, or null for kinds without one yet.</summary>
        public ISettingsLibraryAdapter AdapterFor(LibraryItemKind kind)
        {
            return _adapters.FirstOrDefault(adapter => adapter.Kind == kind);
        }

        // ---- packages ---------------------------------------------------------------------------

        /// <summary>
        /// Writes a Workshop part's package as the library item <paramref name="incoming"/>
        /// describes. The item's earlier file is overwritten in place, unless it was edited since
        /// the Workshop wrote it: that file then stays as a local item and the package lands under
        /// a free name. A first install never overwrites a preset of the same name.
        /// </summary>
        /// <param name="fallbackPublishedHash">The written hash an older install recorded, for items stored before the library kept it.</param>
        public LibraryPartWrite WriteWorkshopPart(
            LibraryItem incoming,
            string packagePath,
            ILibraryPackageFolder folder,
            string fallbackPublishedHash = null)
        {
            if (incoming == null || string.IsNullOrWhiteSpace(incoming.Id) || !incoming.IsWorkshop)
            {
                throw new ArgumentException("A Workshop library item is required.", nameof(incoming));
            }

            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            var library = _apply.Library;
            var existing = library.Find(incoming.Id);
            var existingPath = existing == null ? null : library.FullPath(existing);
            string keptLocalCopy = null;
            string name;
            if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
            {
                var published = existing.PublishedHash ?? fallbackPublishedHash;
                if (!string.IsNullOrEmpty(published)
                    && !string.Equals(LibraryStore.HashFile(existingPath), published, StringComparison.OrdinalIgnoreCase))
                {
                    var local = library.Upsert(new LibraryItem
                    {
                        Id = LibraryItem.NewLocalId(),
                        Kind = existing.Kind,
                        Name = folder.NameOf(existingPath),
                        RelativePath = existing.RelativePath,
                        Origin = LibraryItemOrigin.Local
                    });
                    keptLocalCopy = local.Name;
                    name = folder.UniqueName(existing.Name ?? incoming.Name);
                }
                else
                {
                    name = folder.NameOf(existingPath);
                }
            }
            else
            {
                name = folder.Find(incoming.Name) == null ? incoming.Name : folder.UniqueName(incoming.Name);
            }

            var written = folder.Save(name, packagePath);
            var item = incoming.Clone();
            item.Name = existing?.Name ?? incoming.Name;
            item.RelativePath = written;
            item.ContentHash = null;
            item.FileLength = null;
            item.FileWriteUtc = null;
            item.PublishedHash = LibraryStore.HashFile(written);
            item.AddedUtc = existing?.AddedUtc ?? default(DateTime);
            var stored = library.Upsert(item);
            return new LibraryPartWrite(stored, folder.NameOf(written), keptLocalCopy);
        }

        /// <summary>Records a Workshop item that has no stored package (game data), keeping its name and added time.</summary>
        public LibraryItem RecordWorkshopItem(LibraryItem incoming)
        {
            if (incoming == null || string.IsNullOrWhiteSpace(incoming.Id) || !incoming.IsWorkshop)
            {
                throw new ArgumentException("A Workshop library item is required.", nameof(incoming));
            }

            var existing = _apply.Library.Find(incoming.Id);
            var item = incoming.Clone();
            item.Name = existing?.Name ?? incoming.Name;
            item.RelativePath = existing?.RelativePath;
            item.AddedUtc = existing?.AddedUtc ?? default(DateTime);
            return _apply.Library.Upsert(item);
        }

        // ---- followers --------------------------------------------------------------------------

        /// <summary>
        /// Brings every follower of the item to its current version: settings targets of a kind
        /// with an adapter are merged (or replaced) in the live settings and the edit snapshot;
        /// followers of other kinds are reported as pending.
        /// </summary>
        public LibraryMergeReport MergeIntoTargets(string itemId, LibraryApplyMode mode)
        {
            var report = new LibraryMergeReport();
            var item = _apply.Library.Find(itemId);
            if (item == null)
            {
                return report;
            }

            var adapter = AdapterFor(item.Kind);
            if (adapter == null)
            {
                report.PendingTargets.AddRange(TargetsOf(item.Id));
                return report;
            }

            var updated = false;
            int? kept = null;
            _updateIncludingSnapshot(settings =>
            {
                var link = settings?.GetLibraryLink(adapter.TargetKey);
                if (link == null || !SameId(link.LibraryItemId, item.Id))
                {
                    return;
                }

                var keptHere = 0;
                if (mode == LibraryApplyMode.Replace)
                {
                    _apply.ApplyToSettings(adapter, item, settings);
                }
                else
                {
                    _apply.UpdateSettings(adapter, settings, out keptHere);
                }

                updated = true;
                kept = kept ?? keptHere;
            });

            if (updated)
            {
                report.UpdatedTargets.Add(adapter.TargetKey);
                report.KeptEdits = kept ?? 0;
            }

            return report;
        }

        /// <summary>The keys of every target that follows the item: live settings links and per-game links.</summary>
        public IReadOnlyList<string> TargetsOf(string itemId)
        {
            var keys = new List<string>();
            var live = _live();
            if (live != null)
            {
                keys.AddRange(live.LibraryLinks
                    .Where(pair => SameId(pair.Value?.LibraryItemId, itemId))
                    .Select(pair => pair.Key));
            }

            keys.AddRange(_gameLinks.TargetsOf(itemId));
            return keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Every follower of the item with its state, for the Library page.</summary>
        public IReadOnlyList<LibraryTargetUse> UsesOf(LibraryItem item)
        {
            var uses = new List<LibraryTargetUse>();
            if (item == null)
            {
                return uses;
            }

            var live = _live();
            foreach (var key in TargetsOf(item.Id))
            {
                var link = LibraryTargetKeys.IsPerGame(key) ? _gameLinks.Get(key) : live?.GetLibraryLink(key);
                if (link == null)
                {
                    continue;
                }

                var adapter = LibraryTargetKeys.IsPerGame(key) ? null : _adapters.FirstOrDefault(candidate =>
                    candidate.Kind == item.Kind && string.Equals(candidate.TargetKey, key, StringComparison.OrdinalIgnoreCase));
                if (adapter != null && live != null)
                {
                    LibraryLinkState state;
                    try
                    {
                        state = _apply.GetState(adapter, live, link);
                    }
                    catch (Exception)
                    {
                        state = new LibraryLinkState(link, item, false, false);
                    }

                    uses.Add(new LibraryTargetUse(key, link, state.IsEdited, state.IsUpdateAvailable, canReset: true));
                }
                else
                {
                    var updateAvailable = !string.IsNullOrEmpty(item.Version)
                                          && !string.Equals(item.Version, link.AppliedVersion, StringComparison.OrdinalIgnoreCase);
                    uses.Add(new LibraryTargetUse(key, link, isEdited: false, updateAvailable, canReset: false));
                }
            }

            return uses;
        }

        /// <summary>Applies the item a settings target follows again as published. False when the target has no adapter.</summary>
        public bool Reset(string targetKey)
        {
            var adapter = _adapters.FirstOrDefault(candidate => string.Equals(candidate.TargetKey, targetKey, StringComparison.OrdinalIgnoreCase));
            if (adapter == null)
            {
                return false;
            }

            var done = false;
            _updateLive(settings => done = _apply.ResetSettings(adapter, settings));
            return done;
        }

        /// <summary>Ends a target's link; the target keeps its current values.</summary>
        public void StopFollowing(string targetKey)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                return;
            }

            if (LibraryTargetKeys.IsPerGame(targetKey))
            {
                _gameLinks.Remove(targetKey);
            }
            else
            {
                _updateLive(settings => settings?.SetLibraryLink(targetKey, null));
            }
        }

        /// <summary>Ends every link to an item, in the settings, the edit snapshot and the per-game links.</summary>
        public void UnlinkItem(string itemId)
        {
            UnlinkItems(new[] { itemId });
        }

        /// <summary>Ends the links to items a reconcile dropped (their files are gone). Returns how many items were dropped.</summary>
        public int UnlinkDropped()
        {
            var dropped = _apply.Library.TakeDroppedIds();
            if (dropped.Count > 0)
            {
                UnlinkItems(dropped);
            }

            return dropped.Count;
        }

        private void UnlinkItems(IReadOnlyCollection<string> itemIds)
        {
            var ids = itemIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            if (ids.Count == 0)
            {
                return;
            }

            _updateIncludingSnapshot(settings =>
            {
                foreach (var id in ids)
                {
                    LibraryApplyService.UnlinkItem(settings, id);
                }
            });

            _gameLinks.UnlinkItems(ids);
        }

        private static bool SameId(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}

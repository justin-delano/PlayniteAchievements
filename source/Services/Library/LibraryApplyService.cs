using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// Applies library items to targets and keeps their links: apply (replace, then a new
    /// baseline and link), state (untouched, edited, update available), reset, stop following
    /// and update (a three-way merge of baseline, current and incoming, then a new baseline).
    /// The generic methods return the link and leave storing it to the caller; the settings
    /// methods store it in the given <see cref="PersistedSettings"/>. Inside a settings edit
    /// session the card passes the live settings, so Cancel undoes the value and the link
    /// together; a change made outside the session goes through
    /// <c>SettingsViewModel.UpdatePersistedIncludingEditSnapshot</c> so it survives a Cancel.
    /// </summary>
    public sealed class LibraryApplyService
    {
        private readonly LibraryStore _library;
        private readonly LibraryBaselineStore _baselines;
        private readonly Func<DateTime> _utcNow;

        public LibraryApplyService(LibraryStore library, LibraryBaselineStore baselines, Func<DateTime> utcNow = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public LibraryStore Library => _library;

        public LibraryBaselineStore Baselines => _baselines;

        // ---- generic targets ------------------------------------------------------------------

        /// <summary>Writes the item onto the target as published and returns the new link.</summary>
        public LibraryLink Apply<TTarget>(ILibraryAdapter<TTarget> adapter, LibraryItem item, TTarget target)
        {
            var path = RequirePackage(adapter, item);
            adapter.ApplyReplace(path, target);
            return RecordLink(adapter, item, target, path);
        }

        /// <summary>
        /// Links the target to the item without writing it, for a target the item was just saved
        /// from: the target already holds the item's values.
        /// </summary>
        public LibraryLink Link<TTarget>(ILibraryAdapter<TTarget> adapter, LibraryItem item, TTarget target)
        {
            var path = RequirePackage(adapter, item);
            return RecordLink(adapter, item, target, path);
        }

        /// <summary>
        /// Merges the item's current version into the target, keeping the user's edits since
        /// <paramref name="link"/>'s baseline, and returns the new link. Without a readable
        /// baseline the item is applied as published.
        /// </summary>
        public LibraryLink Update<TTarget>(
            ILibraryAdapter<TTarget> adapter,
            LibraryItem item,
            TTarget target,
            LibraryLink link,
            out int keptEdits)
        {
            var path = RequirePackage(adapter, item);
            var baseline = link == null ? null : _baselines.Read(link.BaselineFile);
            if (baseline == null)
            {
                keptEdits = 0;
                adapter.ApplyReplace(path, target);
            }
            else
            {
                adapter.ApplyMerged(path, target, baseline, out keptEdits);
            }

            return RecordLink(adapter, item, target, path);
        }

        /// <summary>How the target stands against the item <paramref name="link"/> names.</summary>
        public LibraryLinkState GetState<TTarget>(ILibraryAdapter<TTarget> adapter, TTarget target, LibraryLink link)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            if (link == null)
            {
                return LibraryLinkState.Unlinked;
            }

            var item = _library.Find(link.LibraryItemId);
            if (item != null && item.Kind != adapter.Kind)
            {
                item = null;
            }

            var baseline = _baselines.Read(link.BaselineFile) as JObject;
            var owned = baseline?.Properties().Select(property => property.Name).ToList();
            var current = adapter.Project(target, owned);
            var edited = !string.Equals(
                LibraryBaselineStore.HashProjection(current),
                link.BaselineHash,
                StringComparison.OrdinalIgnoreCase);
            var updateAvailable = item != null
                                  && !string.IsNullOrEmpty(item.Version)
                                  && !string.Equals(item.Version, link.AppliedVersion, StringComparison.OrdinalIgnoreCase);
            return new LibraryLinkState(link, item, edited, updateAvailable);
        }

        // ---- settings-backed targets ----------------------------------------------------------

        /// <summary>Applies the item to the settings target and stores the link in <paramref name="settings"/>.</summary>
        public void ApplyToSettings(ISettingsLibraryAdapter adapter, LibraryItem item, PersistedSettings settings)
        {
            RequireSettings(settings);
            settings.SetLibraryLink(adapter.TargetKey, Apply(adapter, item, settings));
        }

        /// <summary>Links the settings target to an item saved from it, without writing values.</summary>
        public void LinkSettings(ISettingsLibraryAdapter adapter, LibraryItem item, PersistedSettings settings)
        {
            RequireSettings(settings);
            settings.SetLibraryLink(adapter.TargetKey, Link(adapter, item, settings));
        }

        public LibraryLinkState GetSettingsState(ISettingsLibraryAdapter adapter, PersistedSettings settings)
        {
            RequireSettings(settings);
            return GetState(adapter, settings, settings.GetLibraryLink(adapter.TargetKey));
        }

        /// <summary>Applies the followed item again as published. False when the target follows nothing in the library.</summary>
        public bool ResetSettings(ISettingsLibraryAdapter adapter, PersistedSettings settings)
        {
            var item = FollowedItem(adapter, settings);
            if (item == null)
            {
                return false;
            }

            ApplyToSettings(adapter, item, settings);
            return true;
        }

        /// <summary>
        /// Merges the followed item's current version into the settings target. False when the
        /// target follows nothing in the library.
        /// </summary>
        public bool UpdateSettings(ISettingsLibraryAdapter adapter, PersistedSettings settings, out int keptEdits)
        {
            keptEdits = 0;
            var item = FollowedItem(adapter, settings);
            if (item == null)
            {
                return false;
            }

            var link = Update(adapter, item, settings, settings.GetLibraryLink(adapter.TargetKey), out keptEdits);
            settings.SetLibraryLink(adapter.TargetKey, link);
            return true;
        }

        /// <summary>Ends tracking; the target keeps its current values.</summary>
        public static void StopFollowing(ISettingsLibraryAdapter adapter, PersistedSettings settings)
        {
            RequireSettings(settings);
            settings.SetLibraryLink(adapter.TargetKey, null);
        }

        /// <summary>Removes every settings link to the item. Returns how many were removed.</summary>
        public static int UnlinkItem(PersistedSettings settings, string itemId)
        {
            if (settings == null || string.IsNullOrWhiteSpace(itemId))
            {
                return 0;
            }

            var keys = settings.LibraryLinks
                .Where(pair => string.Equals(pair.Value?.LibraryItemId, itemId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in keys)
            {
                settings.SetLibraryLink(key, null);
            }

            return keys.Count;
        }

        /// <summary>The baseline files the settings' links reference, for <see cref="LibraryBaselineStore.Sweep"/>.</summary>
        public static IEnumerable<string> ReferencedBaselines(PersistedSettings settings)
        {
            return settings?.LibraryLinks.Values
                       .Where(link => link != null && !string.IsNullOrWhiteSpace(link.BaselineFile))
                       .Select(link => link.BaselineFile)
                       .ToList()
                   ?? new List<string>();
        }

        // ---- internals ------------------------------------------------------------------------

        private LibraryItem FollowedItem(ISettingsLibraryAdapter adapter, PersistedSettings settings)
        {
            RequireSettings(settings);
            var link = settings.GetLibraryLink(adapter.TargetKey);
            var item = link == null ? null : _library.Find(link.LibraryItemId);
            return item != null && item.Kind == adapter.Kind ? item : null;
        }

        private LibraryLink RecordLink<TTarget>(ILibraryAdapter<TTarget> adapter, LibraryItem item, TTarget target, string packagePath)
        {
            var projection = adapter.Project(target, adapter.OwnedKeys(packagePath));
            var baseline = _baselines.Write(projection);
            return new LibraryLink
            {
                LibraryItemId = item.Id,
                AppliedVersion = item.Version,
                BaselineFile = baseline.File,
                BaselineHash = baseline.Hash,
                AppliedUtc = _utcNow()
            };
        }

        private string RequirePackage<TTarget>(ILibraryAdapter<TTarget> adapter, LibraryItem item)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (item.Kind != adapter.Kind)
            {
                throw new InvalidOperationException($"A {item.Kind} item cannot be applied as {adapter.Kind}.");
            }

            var path = _library.FullPath(item);
            if (path == null || !File.Exists(path))
            {
                throw new FileNotFoundException("The library item has no package file.", path);
            }

            return path;
        }

        private static void RequireSettings(PersistedSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
        }
    }
}

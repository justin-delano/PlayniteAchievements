using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// Reads and writes one kind of library item on its target. The projection is the part of
    /// the target the item owns, as JSON with stable property order, with managed files as
    /// <c>{"file":"sha256:..."}</c> so a re-import to a new path is the same value. A link's
    /// baseline is always the projection read back after an apply.
    /// </summary>
    public interface ILibraryAdapter<in TTarget>
    {
        LibraryItemKind Kind { get; }

        /// <summary>
        /// The target's projection. <paramref name="ownedKeys"/> limits it to the top-level
        /// properties an item owns (the tiers a sound pack carries); null projects everything.
        /// </summary>
        JObject Project(TTarget target, IEnumerable<string> ownedKeys = null);

        /// <summary>
        /// The top-level projection properties a package owns on its target, or null when a
        /// package owns the whole projection.
        /// </summary>
        IReadOnlyCollection<string> OwnedKeys(string packagePath);

        /// <summary>Writes the package onto the target as published.</summary>
        void ApplyReplace(string packagePath, TTarget target);

        /// <summary>
        /// Merges the package into the target against <paramref name="baseline"/> with
        /// <see cref="JsonThreeWayMerge"/>: what the user left as the baseline had it follows the
        /// package, what the user changed stays. <paramref name="keptEdits"/> counts the kept
        /// values that differ from the package.
        /// </summary>
        void ApplyMerged(string packagePath, TTarget target, JToken baseline, out int keptEdits);
    }

    /// <summary>
    /// An adapter whose target gives the parts of a package ids of its own (a showcase page's
    /// widgets). The link keeps the map from package ids to target ids, so an update pairs each
    /// part with the one it became. Both applies take the map the link holds (null when there is
    /// none) and return the map after the apply.
    /// </summary>
    public interface ILibraryIdMapAdapter<in TTarget> : ILibraryAdapter<TTarget>
    {
        IReadOnlyDictionary<string, string> ApplyReplace(string packagePath, TTarget target, IReadOnlyDictionary<string, string> idMap);

        IReadOnlyDictionary<string, string> ApplyMerged(
            string packagePath,
            TTarget target,
            JToken baseline,
            IReadOnlyDictionary<string, string> idMap,
            out int keptEdits);

        /// <summary>The map for a target the package was just made from, whose parts keep their ids.</summary>
        IReadOnlyDictionary<string, string> IdentityMap(string packagePath);
    }

    /// <summary>An adapter whose target lives in the settings, with the key its link is stored under.</summary>
    public interface ISettingsLibraryAdapter : ILibraryAdapter<PersistedSettings>
    {
        /// <summary>The <see cref="LibraryTargetKeys"/> key of the target.</summary>
        string TargetKey { get; }
    }

    /// <summary>How a target stands against the library item it follows.</summary>
    public sealed class LibraryLinkState
    {
        public static readonly LibraryLinkState Unlinked = new LibraryLinkState(null, null, false, false);

        public LibraryLinkState(LibraryLink link, LibraryItem item, bool isEdited, bool isUpdateAvailable)
        {
            Link = link;
            Item = item;
            IsEdited = isEdited;
            IsUpdateAvailable = isUpdateAvailable;
        }

        /// <summary>The link, or null when the target follows nothing.</summary>
        public LibraryLink Link { get; }

        /// <summary>The followed item, or null when the target follows nothing or the item is gone.</summary>
        public LibraryItem Item { get; }

        /// <summary>True when the target follows an item that is still in the library.</summary>
        public bool IsFollowing => Link != null && Item != null;

        /// <summary>True when the target's projection no longer hashes to its baseline.</summary>
        public bool IsEdited { get; }

        /// <summary>True when the item's version differs from the version that was applied.</summary>
        public bool IsUpdateAvailable { get; }
    }
}

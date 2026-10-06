using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>What a library item customizes. Bundles are not a kind: each bundle part is an item of its own kind.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LibraryItemKind
    {
        Colors,
        Sounds,
        Toast,
        Frame,
        ShowcasePage,
        GameData
    }

    /// <summary>Where a library item came from.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LibraryItemOrigin
    {
        /// <summary>A preset the user saved or imported from a file.</summary>
        Local,

        /// <summary>An item installed from the Workshop.</summary>
        Workshop
    }

    /// <summary>
    /// One entry of the library index (<c>UserData\library\library.json</c>). The preset file is
    /// the content; the entry adds identity, origin and version so that applied copies can be
    /// tracked and updated.
    /// </summary>
    public sealed class LibraryItem
    {
        /// <summary>The prefix of the ids of Workshop items.</summary>
        public const string WorkshopIdPrefix = "ws:";

        /// <summary>
        /// A GUID ("N" format) for a local item; <c>ws:&lt;itemId&gt;</c> for a Workshop item,
        /// or <c>ws:&lt;itemId&gt;#&lt;part&gt;</c> for one part of a Workshop item that carries
        /// several kinds.
        /// </summary>
        public string Id { get; set; }

        public LibraryItemKind Kind { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The preset file, relative to the plugin's user data folder, or null for a Workshop item
        /// that has no stored package (a showcase page or game data installed before the library).
        /// </summary>
        public string RelativePath { get; set; }

        public LibraryItemOrigin Origin { get; set; }

        /// <summary>For a Workshop item, the Workshop item id.</summary>
        public string WorkshopItemId { get; set; }

        /// <summary>For a Workshop item, the part of the package this item is (colors, sounds, toast, frame), or null.</summary>
        public string Part { get; set; }

        /// <summary>
        /// The version applied copies compare against: the Workshop version for a Workshop item,
        /// the content hash for a local item, so re-saving a local preset is a new version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>Lowercase hex SHA-256 of the preset file, or null when there is no file.</summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// For a Workshop item, the hash of the preset file as the Workshop wrote it. A file that
        /// no longer has it was edited in place, and an update keeps that file as a local item.
        /// </summary>
        public string PublishedHash { get; set; }

        /// <summary>For a Workshop item, the author the Workshop index names.</summary>
        public string Author { get; set; }

        /// <summary>Length of the preset file when <see cref="ContentHash"/> was computed.</summary>
        public long? FileLength { get; set; }

        /// <summary>Last write time of the preset file when <see cref="ContentHash"/> was computed.</summary>
        public DateTime? FileWriteUtc { get; set; }

        public DateTime AddedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }

        [JsonIgnore]
        public bool IsWorkshop => Origin == LibraryItemOrigin.Workshop;

        /// <summary>The library id of a Workshop item, or of one part of it.</summary>
        public static string WorkshopId(string workshopItemId, string part = null)
        {
            if (string.IsNullOrWhiteSpace(workshopItemId))
            {
                throw new ArgumentException("A Workshop item id is required.", nameof(workshopItemId));
            }

            return string.IsNullOrWhiteSpace(part)
                ? WorkshopIdPrefix + workshopItemId.Trim()
                : WorkshopIdPrefix + workshopItemId.Trim() + "#" + part.Trim().ToLowerInvariant();
        }

        /// <summary>A new id for a local item.</summary>
        public static string NewLocalId() => Guid.NewGuid().ToString("N");

        public LibraryItem Clone()
        {
            return (LibraryItem)MemberwiseClone();
        }
    }

    /// <summary>What a reconcile of the index against the preset folders changed.</summary>
    public sealed class LibraryReconcileResult
    {
        /// <summary>Items whose file is gone; links to them should be removed.</summary>
        public List<string> DroppedIds { get; } = new List<string>();

        /// <summary>Unindexed preset files that became local items.</summary>
        public List<string> AddedIds { get; } = new List<string>();

        /// <summary>Local items whose file content changed, which is a new version.</summary>
        public List<string> NewVersionIds { get; } = new List<string>();

        /// <summary>Workshop items whose file no longer has the hash the library recorded.</summary>
        public List<string> ModifiedWorkshopIds { get; } = new List<string>();

        /// <summary>Items whose file was renamed or moved within its folder, found by content hash.</summary>
        public List<string> MovedIds { get; } = new List<string>();

        public bool HasChanges =>
            DroppedIds.Count > 0 || AddedIds.Count > 0 || NewVersionIds.Count > 0 ||
            ModifiedWorkshopIds.Count > 0 || MovedIds.Count > 0;
    }
}

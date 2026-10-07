using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Records that a target (the colors, the sounds, a notification scope, a showcase page)
    /// follows a library item: which item, which version of it was applied, and the baseline (the
    /// target's projection right after that apply) that later updates merge against. A game's
    /// Workshop data uses the same record without a library item behind it: its link names the
    /// Workshop item (<c>ws:...</c>) and also carries the item's name and a copy of the package.
    /// Settings-backed targets keep their links in <see cref="PersistedSettings.LibraryLinks"/>,
    /// so Cancel undoes a value and its link together; per-game targets keep them in the
    /// library's links file.
    /// </summary>
    public sealed class LibraryLink
    {
        /// <summary>The library item id (a GUID for a local item, <c>ws:...</c> for a Workshop item).</summary>
        public string LibraryItemId { get; set; }

        /// <summary>The item version that was applied: the Workshop version, or the content hash of a local item.</summary>
        public string AppliedVersion { get; set; }

        /// <summary>The baseline file written by that apply; a file name resolves inside the library's baselines folder.</summary>
        public string BaselineFile { get; set; }

        /// <summary>Hash of the baseline projection; the target is untouched while its projection still hashes to it.</summary>
        public string BaselineHash { get; set; }

        public DateTime AppliedUtc { get; set; }

        /// <summary>
        /// For a target that gives the parts of the item ids of its own (a showcase page's
        /// widgets), the id each part has in the item's package mapped to the id it has on the
        /// target, so the next update pairs them up; null for other targets.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public Dictionary<string, string> IdMap { get; set; }

        /// <summary>
        /// For a game's Workshop data, the item's display name. Game data is not a library item,
        /// so its link carries the name itself; null for other targets.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>
        /// For a game's Workshop data, the file name of the copy of the package as applied, in the
        /// library's game data folder, so Reset works offline; null for other targets and for
        /// links made before copies were kept.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string PackageFile { get; set; }

        public LibraryLink Clone()
        {
            var clone = (LibraryLink)MemberwiseClone();
            clone.IdMap = IdMap == null ? null : new Dictionary<string, string>(IdMap, StringComparer.OrdinalIgnoreCase);
            return clone;
        }

        /// <summary>
        /// A case-insensitive copy of <paramref name="links"/> with every link cloned. Entries
        /// with a blank key or no link are left out; every other key is kept, known or not.
        /// </summary>
        public static Dictionary<string, LibraryLink> CloneAll(IEnumerable<KeyValuePair<string, LibraryLink>> links)
        {
            var result = new Dictionary<string, LibraryLink>(StringComparer.OrdinalIgnoreCase);
            if (links == null)
            {
                return result;
            }

            foreach (var pair in links)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                {
                    result[pair.Key] = pair.Value.Clone();
                }
            }

            return result;
        }
    }
}

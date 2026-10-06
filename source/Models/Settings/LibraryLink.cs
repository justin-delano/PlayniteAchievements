using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Records that a target (the colors, the sounds, a notification scope, a showcase page, a
    /// game's data) follows a library item: which item, which version of it was applied, and the
    /// baseline (the target's projection right after that apply) that later updates merge against.
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

        public LibraryLink Clone()
        {
            return (LibraryLink)MemberwiseClone();
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

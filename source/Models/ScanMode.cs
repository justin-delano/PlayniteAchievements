using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Represents a scan mode for achievement scanning operations.
    /// </summary>
    public class ScanMode
    {
        /// <summary>
        /// Unique identifier for this scan mode.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Localized display name for this scan mode (long version for menus).
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Localized short display name for this scan mode (for sidebar dropdown).
        /// </summary>
        public string ShortDisplayName { get; set; }

        /// <summary>
        /// Description of what this scan mode does.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Resource key for localized display name (long version).
        /// </summary>
        public string DisplayNameResourceKey { get; set; }

        /// <summary>
        /// Resource key for localized short display name (for sidebar).
        /// </summary>
        public string ShortDisplayNameResourceKey { get; set; }

        public ScanMode(string key, string displayNameResourceKey, string shortDisplayNameResourceKey = null, string description = null)
        {
            Key = key;
            DisplayNameResourceKey = displayNameResourceKey;
            ShortDisplayNameResourceKey = shortDisplayNameResourceKey ?? displayNameResourceKey;
            Description = description;
        }
    }

    /// <summary>
    /// Predefined scan mode keys.
    /// </summary>
    public static class ScanModeKeys
    {
        public const string Quick = "Quick";
        public const string Full = "Full";
        public const string Installed = "Installed";
        public const string Favorites = "Favorites";
        public const string Single = "Single";
        public const string LibrarySelected = "LibrarySelected";
    }
}

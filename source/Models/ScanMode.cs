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
        /// Localized display name for this scan mode.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Description of what this scan mode does.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Resource key for localized display name.
        /// </summary>
        public string DisplayNameResourceKey { get; set; }

        public ScanMode(string key, string displayNameResourceKey, string description = null)
        {
            Key = key;
            DisplayNameResourceKey = displayNameResourceKey;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Scan mode types for achievement scanning operations.
    /// </summary>
    public enum ScanModeType
    {
        Quick,
        Full,
        Installed,
        Favorites,
        Single,
        LibrarySelected,
        Missing
    }

    /// <summary>
    /// Extension methods for ScanModeType enum.
    /// </summary>
    public static class ScanModeExtensions
    {
        /// <summary>
        /// Gets the localization resource key for the full display name.
        /// </summary>
        public static string GetResourceKey(this ScanModeType mode) => $"LOCPlayAch_ScanMode_{mode}";

        /// <summary>
        /// Gets the localization resource key for the short display name.
        /// </summary>
        public static string GetShortResourceKey(this ScanModeType mode) => $"LOCPlayAch_ScanModeShort_{mode}";

        /// <summary>
        /// Gets the string key for this scan mode.
        /// </summary>
        public static string GetKey(this ScanModeType mode) => mode.ToString();
    }

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
        /// The scan mode type enum value.
        /// </summary>
        public ScanModeType Type { get; set; }

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

        public ScanMode(ScanModeType type, string displayNameResourceKey, string shortDisplayNameResourceKey)
        {
            Type = type;
            Key = type.GetKey();
            DisplayNameResourceKey = displayNameResourceKey;
            ShortDisplayNameResourceKey = shortDisplayNameResourceKey;
        }

        /// <summary>
        /// Resource key for localized display name (long version).
        /// </summary>
        public string DisplayNameResourceKey { get; set; }

        /// <summary>
        /// Resource key for localized short display name (for sidebar).
        /// </summary>
        public string ShortDisplayNameResourceKey { get; set; }
    }

    /// <summary>
    /// Predefined scan mode keys (obsolete - use ScanModeType enum instead).
    /// </summary>
    [Obsolete("Use ScanModeType enum instead.")]
    public static class ScanModeKeys
    {
        public const string Quick = "Quick";
        public const string Full = "Full";
        public const string Installed = "Installed";
        public const string Favorites = "Favorites";
        public const string Single = "Single";
        public const string LibrarySelected = "LibrarySelected";
        public const string Missing = "Missing";
    }
}

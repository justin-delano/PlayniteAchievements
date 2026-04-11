using System;

namespace PlayniteAchievements.Models.Achievements
{
    /// <summary>
    /// Icon resolution utilities for achievement display.
    /// </summary>
    public static class AchievementIconResolver
    {
        private const string DefaultIconPackUri = "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";
        private const string DefaultUnlockedIconPackUri = "pack://application:,,,/PlayniteAchievements;component/Resources/UnlockedAchIcon.png";
        private const string GrayPrefix = "gray:";

        /// <summary>
        /// Resolve the display icon for an achievement based on its unlock state.
        /// Theme-facing icon must be cheap to evaluate (WPF may call it frequently).
        /// Always uses the same icon URL; grayscale is applied by AsyncImage when needed.
        /// </summary>
        public static string GetDisplayIcon(bool unlocked, string iconPath)
        {
            var candidate = NormalizeIconPath(iconPath);
            if (!unlocked && !string.IsNullOrWhiteSpace(candidate))
            {
                candidate = ApplyGrayPrefix(candidate);
            }

            return string.IsNullOrWhiteSpace(candidate) ? DefaultIconPackUri : candidate;
        }

        public static string NormalizeIconPath(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return iconPath;
            }

            var normalized = iconPath.Trim();
            var isGray = normalized.StartsWith(GrayPrefix, StringComparison.OrdinalIgnoreCase);
            if (isGray)
            {
                normalized = normalized.Substring(GrayPrefix.Length).Trim();
            }

            if (string.Equals(normalized, "Resources/UnlockedAchIcon.png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "UnlockedAchIcon.png", StringComparison.OrdinalIgnoreCase))
            {
                normalized = DefaultUnlockedIconPackUri;
            }
            else if (string.Equals(normalized, "Resources/HiddenAchIcon.png", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(normalized, "HiddenAchIcon.png", StringComparison.OrdinalIgnoreCase))
            {
                normalized = DefaultIconPackUri;
            }

            return isGray ? ApplyGrayPrefix(normalized) : normalized;
        }

        /// <summary>
        /// True if two icon identifiers are the same (case-insensitive, trimmed).
        /// Kept for compatibility with existing code.
        /// </summary>
        public static bool AreSameIcon(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return string.Equals(NormalizeIcon(left), NormalizeIcon(right), StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Get the default hidden icon pack URI.
        /// </summary>
        public static string GetDefaultIcon() => DefaultIconPackUri;

        public static string GetDefaultUnlockedIcon() => DefaultUnlockedIconPackUri;

        /// <summary>
        /// Prefixes the icon identifier with "gray:" when not already prefixed.
        /// </summary>
        public static string ApplyGrayPrefix(string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return icon;
            }

            return icon.StartsWith(GrayPrefix, StringComparison.OrdinalIgnoreCase)
                ? icon
                : GrayPrefix + icon;
        }

        private static string NormalizeIcon(string value) => value?.Trim();
    }
}

using System;

namespace PlayniteAchievements.Models.Achievements
{
    /// <summary>
    /// Icon resolution utilities for achievement display.
    /// </summary>
    public static class AchievementIconResolver
    {
        private const string DefaultIconPackUri = "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";
        private const string GrayPrefix = "gray:";

        /// <summary>
        /// Resolve the display icon for an achievement based on its unlock state.
        /// Theme-facing icon must be cheap to evaluate (WPF may call it frequently).
        /// </summary>
        public static string GetDisplayIcon(
            bool unlocked,
            string unlockedIconUrl,
            string lockedIconUrl)
        {
            var candidate = unlocked ? unlockedIconUrl : lockedIconUrl;

            if (!unlocked && AreSameIcon(unlockedIconUrl, lockedIconUrl))
            {
                candidate = ApplyGrayPrefix(candidate);
            }

            return string.IsNullOrWhiteSpace(candidate) ? DefaultIconPackUri : candidate;
        }

        /// <summary>
        /// True if two icon identifiers are the same (case-insensitive, trimmed).
        /// </summary>
        public static bool AreSameIcon(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(NormalizeIcon(left), NormalizeIcon(right), StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Get the default hidden icon pack URI.
        /// </summary>
        public static string GetDefaultIcon() => DefaultIconPackUri;

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

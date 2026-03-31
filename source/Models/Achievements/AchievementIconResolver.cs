using System;
using System.IO;

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
        /// Get the default hidden icon pack URI.
        /// </summary>
        public static string GetDefaultIcon() => DefaultIconPackUri;

        public static string GetUnlockedDisplayIcon(string unlockedIconPath) =>
            string.IsNullOrWhiteSpace(unlockedIconPath)
                ? DefaultIconPackUri
                : unlockedIconPath;

        public static string GetLockedDisplayIcon(string unlockedIconPath, string lockedIconPath)
        {
            if (HasExplicitLockedIcon(lockedIconPath, unlockedIconPath))
            {
                return lockedIconPath;
            }

            var candidate = ApplyGrayPrefix(unlockedIconPath);
            return string.IsNullOrWhiteSpace(candidate) ? DefaultIconPackUri : candidate;
        }

        public static bool HasExplicitLockedIcon(string lockedIconPath, string unlockedIconPath)
        {
            if (string.IsNullOrWhiteSpace(lockedIconPath))
            {
                return false;
            }

            if (!IsUsableDisplayPath(lockedIconPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(unlockedIconPath))
            {
                return true;
            }

            return !string.Equals(
                NormalizeIcon(lockedIconPath),
                NormalizeIcon(unlockedIconPath),
                StringComparison.OrdinalIgnoreCase);
        }

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

        private static bool IsUsableDisplayPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.StartsWith(GrayPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return File.Exists(value);
        }
    }
}

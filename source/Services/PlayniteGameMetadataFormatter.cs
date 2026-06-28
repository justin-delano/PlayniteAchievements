using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Services
{
    public static class PlayniteGameMetadataFormatter
    {
        public static string GetPlatformText(Game game)
        {
            return JoinDisplayNames(game?.Platforms?.Select(platform => platform?.Name));
        }

        /// <summary>
        /// Returns the game's platform names, deduplicated and trimmed, preserving order.
        /// </summary>
        public static IReadOnlyList<string> GetPlatformNames(Game game)
        {
            return DistinctDisplayNames(game?.Platforms?.Select(platform => platform?.Name));
        }

        public static string GetRegionText(Game game)
        {
            return JoinDisplayNames(game?.Regions?.Select(region => region?.Name));
        }

        public static string JoinDisplayNames(IEnumerable<string> names)
        {
            var values = DistinctDisplayNames(names);
            return values.Count > 0 ? string.Join(", ", values) : string.Empty;
        }

        private static IReadOnlyList<string> DistinctDisplayNames(IEnumerable<string> names)
        {
            if (names == null)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var normalized = (name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                values.Add(normalized);
            }

            return values;
        }

        public static string FormatPlaytime(ulong playtimeSeconds)
        {
            var totalMinutes = playtimeSeconds / 60;
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;

            if (hours > 0)
            {
                return minutes > 0
                    ? string.Format(CultureInfo.CurrentCulture, L("LOCPlayAch_Playtime_HoursMinutes", "{0}h{1}m"), hours, minutes)
                    : string.Format(CultureInfo.CurrentCulture, L("LOCPlayAch_Playtime_Hours", "{0}h"), hours);
            }

            return string.Format(CultureInfo.CurrentCulture, L("LOCPlayAch_Playtime_Minutes", "{0}m"), totalMinutes);
        }

        private static string L(string key, string fallback)
        {
            string value;
            try
            {
                value = ResourceProvider.GetString(key);
            }
            catch (Exception)
            {
                return fallback;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Length > 4 &&
                value.StartsWith("<!", StringComparison.Ordinal) &&
                value.EndsWith("!>", StringComparison.Ordinal)
                ? fallback
                : value;
        }

        public static string BuildOverviewMetadataText(
            string platformText,
            string playtimeText,
            string regionText)
        {
            return BuildOverviewMetadataText(
                platformText,
                playtimeText,
                regionText,
                showPlatform: true,
                showPlaytime: true,
                showRegion: true);
        }

        public static string BuildOverviewMetadataText(
            string platformText,
            string playtimeText,
            string regionText,
            bool showPlatform,
            bool showPlaytime,
            bool showRegion)
        {
            var parts = new List<string>();
            if (showPlatform && !string.IsNullOrWhiteSpace(platformText))
            {
                parts.Add(platformText.Trim());
            }

            if (showPlaytime && !string.IsNullOrWhiteSpace(playtimeText))
            {
                parts.Add(playtimeText.Trim());
            }

            if (showRegion && !string.IsNullOrWhiteSpace(regionText))
            {
                parts.Add(regionText.Trim());
            }

            return parts.Count > 0 ? string.Join(" • ", parts) : string.Empty;
        }
    }
}

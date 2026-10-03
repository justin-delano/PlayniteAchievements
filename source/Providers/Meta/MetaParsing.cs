using PlayniteAchievements.Models.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PlayniteAchievements.Providers.Meta
{
    /// <summary>
    /// Pure parsing and mapping for Meta responses: the unlock feed's text fields and the join of
    /// per-app definitions against the feed's unlocks.
    /// </summary>
    internal static class MetaParsing
    {
        internal const string AchievementsModuleTypeName = "ProfileAchievementsModule";

        // The feed's date stays "MMM d, yyyy" in English; only the prefix ("Unlocked on") is localized,
        // and only when a locale parameter is sent.
        private static readonly Regex UnlockDateRegex = new Regex(
            @"(?<month>[A-Za-z]{3})[a-z]*\.?\s+(?<day>\d{1,2}),\s*(?<year>\d{4})",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex UnlockCountRegex = new Regex(
            @"(?<number>\d+(?:[.,]\d+)?)\s*(?<suffix>[KMB])?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parses "Unlocked on Dec 25, 2020" to the UTC instant of local midnight on that day, so the
        /// unlock displays on the same calendar day the profile shows. Returns null when no date is found.
        /// </summary>
        internal static DateTime? ParseUnlockDate(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var match = UnlockDateRegex.Match(description);
            if (!match.Success)
            {
                return null;
            }

            var text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2}",
                match.Groups["month"].Value,
                match.Groups["day"].Value,
                match.Groups["year"].Value);

            if (!DateTime.TryParseExact(
                    text,
                    "MMM d yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var day))
            {
                return null;
            }

            return DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();
        }

        /// <summary>
        /// Parses "2.3M players unlocked" / "86.4K players unlocked" / "512 players unlocked".
        /// </summary>
        internal static long? ParseUnlockCount(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var match = UnlockCountRegex.Match(description);
            if (!match.Success ||
                !double.TryParse(
                    match.Groups["number"].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var number))
            {
                return null;
            }

            switch (match.Groups["suffix"].Value.ToUpperInvariant())
            {
                case "K":
                    number *= 1_000d;
                    break;
                case "M":
                    number *= 1_000_000d;
                    break;
                case "B":
                    number *= 1_000_000_000d;
                    break;
            }

            return (long)Math.Round(number);
        }

        /// <summary>
        /// The achievements connection of the feed's achievements module, or null when the user is
        /// absent or the module is missing.
        /// </summary>
        internal static MetaFeedConnection GetAchievementsConnection(MetaFeedResponse response)
        {
            return response?.Data?.User?.ProfileInfo?.Modules?
                .FirstOrDefault(m => string.Equals(m?.TypeName, AchievementsModuleTypeName, StringComparison.Ordinal))?
                .Achievements;
        }

        /// <summary>
        /// Converts one feed page to unlocks. Entries without a definition id or not unlocked are skipped.
        /// </summary>
        internal static IEnumerable<MetaUnlock> ReadUnlocks(MetaFeedConnection connection)
        {
            if (connection?.Edges == null)
            {
                yield break;
            }

            foreach (var node in connection.Edges.Select(e => e?.Node))
            {
                var definitionId = node?.Definition?.Id;
                if (node == null || !node.IsUnlocked || string.IsNullOrWhiteSpace(definitionId))
                {
                    continue;
                }

                yield return new MetaUnlock
                {
                    DefinitionId = definitionId.Trim(),
                    UnlockDateUtc = ParseUnlockDate(node.UnlockDateDescription),
                    GlobalUnlockCount = ParseUnlockCount(node.Definition.UnlockCountDescription)
                };
            }
        }

        /// <summary>
        /// Indexes unlocks by definition id. The first entry wins, since the feed is newest first and
        /// carries one entry per unlocked definition.
        /// </summary>
        internal static Dictionary<string, MetaUnlock> IndexUnlocks(IEnumerable<MetaUnlock> unlocks)
        {
            var index = new Dictionary<string, MetaUnlock>(StringComparer.Ordinal);
            foreach (var unlock in unlocks ?? Enumerable.Empty<MetaUnlock>())
            {
                if (unlock?.DefinitionId != null && !index.ContainsKey(unlock.DefinitionId))
                {
                    index[unlock.DefinitionId] = unlock;
                }
            }

            return index;
        }

        /// <summary>
        /// Joins an app's definitions with the user's unlocks. A definition is unlocked when its id is in
        /// the unlock index; the unlock day is kept even when the date text could not be parsed.
        /// </summary>
        internal static List<AchievementDetail> MapAchievements(
            IReadOnlyList<MetaDefinition> definitions,
            IReadOnlyDictionary<string, MetaUnlock> unlocks)
        {
            var result = new List<AchievementDetail>();
            if (definitions == null)
            {
                return result;
            }

            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                {
                    continue;
                }

                MetaUnlock unlock = null;
                var isUnlocked = unlocks != null && unlocks.TryGetValue(definition.Id.Trim(), out unlock);

                result.Add(new AchievementDetail
                {
                    ApiName = string.IsNullOrWhiteSpace(definition.ApiName) ? definition.Id.Trim() : definition.ApiName.Trim(),
                    DisplayName = definition.Title?.Trim(),
                    Description = definition.Description?.Trim(),
                    UnlockedIconPath = NullIfBlank(definition.UnlockedImageUri),
                    LockedIconPath = NullIfBlank(definition.LockedImageUri),
                    Hidden = definition.IsSecret,
                    UnlockTimeUtc = isUnlocked ? unlock.UnlockDateUtc : null,
                    Unlocked = isUnlocked
                });
            }

            return result;
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}

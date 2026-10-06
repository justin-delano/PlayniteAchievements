using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Services
{
    /// <summary>
    /// One achievement as a CSV line: the editor's spreadsheet export and the CSV inside a
    /// custom-achievements .pa package share these columns. Every value is optional; a blank cell
    /// reads back as "leave unchanged".
    /// </summary>
    public sealed class CustomAchievementCsvRow
    {
        /// <summary>The line in the file, counting the header as 1. Zero for a row built in code.</summary>
        public int RowNumber { get; set; }

        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public int? Points { get; set; }

        /// <summary>Lowercase bronze, silver, gold or platinum.</summary>
        public string TrophyType { get; set; }

        public bool? Hidden { get; set; }

        /// <summary>A tier name (Common, Uncommon, Rare, UltraRare), or null when a percent is given.</summary>
        public string RarityTier { get; set; }

        /// <summary>The global unlock percent, 0 to 100, which takes precedence over the tier.</summary>
        public double? RarityPercent { get; set; }

        /// <summary>The stored category path, nested segments joined by the internal separator.</summary>
        public string Category { get; set; }

        public int? ProgressNum { get; set; }

        public int? ProgressDenom { get; set; }

        public bool? Unlocked { get; set; }

        public DateTime? UnlockTimeUtc { get; set; }

        public string UnlockedIconPath { get; set; }

        public string LockedIconPath { get; set; }

        /// <summary>The rarity cell's text: the percent with a % sign, or the tier name.</summary>
        public string RarityText => FormatRarity(RarityPercent, RarityTier);

        public static string FormatRarity(double? percent, string tier)
        {
            return percent.HasValue
                ? percent.Value.ToString(CultureInfo.InvariantCulture) + "%"
                : tier;
        }
    }

    /// <summary>
    /// Writes the achievement CSV. <see cref="CustomAchievementTextImportService"/> reads it back,
    /// mapping the readable column names to fields.
    /// </summary>
    public static class CustomAchievementCsvFormat
    {
        public const string Header =
            "ID,Title,Description,Points,Trophy Type,Hidden,Rarity,Category,Progress,Progress Total,Unlocked,Unlock Time";

        /// <summary>The icon columns a .pa package appends; a bare CSV cannot carry the files.</summary>
        public const string IconHeader = "Unlocked Icon,Locked Icon";

        public const string UnlockTimeFormat = "yyyy-MM-dd HH:mm:ss";

        public static List<string> BuildLines(IEnumerable<CustomAchievementCsvRow> rows, bool includeIcons)
        {
            var lines = new List<string> { includeIcons ? Header + "," + IconHeader : Header };
            lines.AddRange(
                (rows ?? Enumerable.Empty<CustomAchievementCsvRow>())
                    .Where(row => row != null)
                    .Select(row => FormatRow(row, includeIcons)));
            return lines;
        }

        public static string FormatRow(CustomAchievementCsvRow row, bool includeIcons)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            var fields = new List<string>
            {
                row.Id,
                row.DisplayName,
                row.Description,
                row.Points?.ToString(CultureInfo.InvariantCulture),
                row.TrophyType,
                FormatBool(row.Hidden),
                row.RarityText,
                string.IsNullOrWhiteSpace(row.Category) ? null : CategoryPathHelper.ToDisplayPath(row.Category),
                row.ProgressNum?.ToString(CultureInfo.InvariantCulture),
                row.ProgressDenom?.ToString(CultureInfo.InvariantCulture),
                FormatBool(row.Unlocked),
                row.UnlockTimeUtc.HasValue
                    ? DateTime.SpecifyKind(row.UnlockTimeUtc.Value, DateTimeKind.Utc)
                        .ToLocalTime()
                        .ToString(UnlockTimeFormat, CultureInfo.InvariantCulture)
                    : null
            };

            if (includeIcons)
            {
                fields.Add(row.UnlockedIconPath);
                fields.Add(row.LockedIconPath);
            }

            return string.Join(",", fields.Select(Escape));
        }

        public static CustomAchievementCsvRow FromDefinition(CustomAchievementDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return new CustomAchievementCsvRow
            {
                Id = definition.Id,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Points = definition.Points,
                TrophyType = definition.TrophyType,
                Hidden = definition.Hidden,
                RarityTier = definition.GlobalPercentUnlocked.HasValue ? null : definition.Rarity,
                RarityPercent = definition.GlobalPercentUnlocked,
                Category = definition.Category,
                ProgressNum = definition.ProgressNum,
                ProgressDenom = definition.ProgressDenom,
                // Blank rather than "false" when locked, so a shared file never re-locks anything.
                Unlocked = definition.Unlocked ? true : (bool?)null,
                UnlockTimeUtc = definition.UnlockTimeUtc,
                UnlockedIconPath = definition.UnlockedIconPath,
                LockedIconPath = definition.LockedIconPath
            };
        }

        /// <summary>
        /// Writes the lines as UTF-8 with a byte order mark, which is what makes Excel read
        /// non-ASCII titles correctly, and CRLF line endings.
        /// </summary>
        public static void WriteFile(string path, IEnumerable<string> lines)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.NewLine = "\r\n";
                foreach (var line in lines ?? Enumerable.Empty<string>())
                {
                    writer.WriteLine(line);
                }
            }
        }

        public static string Escape(string value)
        {
            var safe = value ?? string.Empty;
            if (safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return safe;
            }

            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "true" : "false") : null;
        }
    }
}

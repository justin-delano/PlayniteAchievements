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
    /// One achievement as a CSV line: the editor's spreadsheet export, and the CSV inside a
    /// custom-achievements .pa package. Every value is optional; a blank cell reads back as
    /// "leave unchanged".
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

        /// <summary>
        /// A web URL or a local file. A relative path read from a file is resolved against that
        /// file's folder.
        /// </summary>
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
            "ID,Title,Description,Points,Trophy Type,Hidden,Rarity,Category,Progress,Progress Total,Unlocked,Unlock Time,Unlocked Icon,Locked Icon";

        public const string UnlockTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// The separator the user's spreadsheet expects: Windows' list separator, which is what
        /// Excel splits a CSV on when opening it. A semicolon in locales whose decimal mark is a
        /// comma; a comma when the list separator is anything the reader does not take.
        /// </summary>
        public static char SpreadsheetDelimiter()
        {
            var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            return separator == ";" || separator == "\t" ? separator[0] : ',';
        }

        public static List<string> BuildLines(IEnumerable<CustomAchievementCsvRow> rows, char delimiter = ',')
        {
            var lines = new List<string> { Header.Replace(',', delimiter) };
            lines.AddRange(
                (rows ?? Enumerable.Empty<CustomAchievementCsvRow>())
                    .Where(row => row != null)
                    .Select(row => FormatRow(row, delimiter)));
            return lines;
        }

        public static string FormatRow(CustomAchievementCsvRow row, char delimiter = ',')
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
                    : null,
                row.UnlockedIconPath,
                row.LockedIconPath
            };

            return string.Join(delimiter.ToString(), fields.Select(Escape));
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

        /// <summary>
        /// Quotes a cell holding a quote, a line break or any separator the reader takes, so the
        /// cell reads back whole whichever separator the file uses.
        /// </summary>
        public static string Escape(string value)
        {
            var safe = value ?? string.Empty;
            if (safe.IndexOfAny(new[] { ',', ';', '\t', '"', '\r', '\n' }) < 0)
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

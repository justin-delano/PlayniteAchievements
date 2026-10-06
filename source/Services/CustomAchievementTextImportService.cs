using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Services
{
    public sealed class CustomAchievementTextImportResult
    {
        public List<CustomAchievementDefinition> Definitions { get; } =
            new List<CustomAchievementDefinition>();

        public List<string> Errors { get; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// The rows of an achievement CSV, each holding only the cells that were filled in.
    /// </summary>
    public sealed class CustomAchievementCsvParseResult
    {
        public List<CustomAchievementCsvRow> Rows { get; } = new List<CustomAchievementCsvRow>();

        public List<string> Errors { get; } = new List<string>();

        /// <summary>Header cells that name no known column, in file order.</summary>
        public List<string> IgnoredColumns { get; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;
    }

    public sealed class CustomAchievementTextImportService
    {
        private enum Field
        {
            Unknown,
            Id,
            DisplayName,
            Description,
            Points,
            TrophyType,
            Hidden,
            Rarity,
            Category,
            ProgressNum,
            ProgressDenom,
            Unlocked,
            UnlockTime,
            UnlockedIconPath,
            LockedIconPath
        }

        private static readonly Dictionary<string, Field> HeaderAliases =
            new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = Field.Id,
                ["customid"] = Field.Id,
                ["api"] = Field.Id,
                ["apiname"] = Field.Id,
                ["name"] = Field.DisplayName,
                ["title"] = Field.DisplayName,
                ["achievement"] = Field.DisplayName,
                ["achievementname"] = Field.DisplayName,
                ["displayname"] = Field.DisplayName,
                ["description"] = Field.Description,
                ["desc"] = Field.Description,
                ["details"] = Field.Description,
                ["points"] = Field.Points,
                ["score"] = Field.Points,
                ["gamerscore"] = Field.Points,
                ["trophy"] = Field.TrophyType,
                ["trophytype"] = Field.TrophyType,
                ["hidden"] = Field.Hidden,
                ["secret"] = Field.Hidden,
                ["rarity"] = Field.Rarity,
                ["category"] = Field.Category,
                ["categorylabel"] = Field.Category,
                ["progress"] = Field.ProgressNum,
                ["progressnum"] = Field.ProgressNum,
                ["current"] = Field.ProgressNum,
                ["progressdenom"] = Field.ProgressDenom,
                ["progresstotal"] = Field.ProgressDenom,
                ["total"] = Field.ProgressDenom,
                ["unlocked"] = Field.Unlocked,
                ["earned"] = Field.Unlocked,
                ["unlocktime"] = Field.UnlockTime,
                ["unlockedat"] = Field.UnlockTime,
                ["earnedat"] = Field.UnlockTime,
                ["dateunlocked"] = Field.UnlockTime,
                ["unlockedicon"] = Field.UnlockedIconPath,
                ["lockedicon"] = Field.LockedIconPath
            };

        private const string TrophyTypeMessage = "is not bronze, silver, gold or platinum.";

        private const string RarityMessage = "is not a percent from 0 to 100, or Common, Uncommon, Rare or Ultra Rare.";

        /// <summary>
        /// Reads every row, keeping only the cells that were filled in, and checks each value.
        /// A row may leave Title blank when it names an ID, since it can be updating an existing
        /// achievement; whether the ID is new is for the caller to decide.
        /// </summary>
        /// <param name="nowUtc">The clock an unlock time is checked against; the current time
        /// when null.</param>
        public CustomAchievementCsvParseResult Parse(string text, DateTime? nowUtc = null)
        {
            var result = new CustomAchievementCsvParseResult();
            var rows = ParseRows(text);
            var headerIndex = rows.FindIndex(row => !IsEmptyRow(row));
            if (headerIndex < 0)
            {
                result.Errors.Add("No rows were found.");
                return result;
            }

            var header = rows[headerIndex];
            var mapping = new List<Field>(header.Count);
            var seen = new HashSet<Field>();
            foreach (var cell in header)
            {
                var field = ResolveField(cell);
                if (field == Field.Unknown)
                {
                    var name = NormalizeText(cell);
                    if (name != null)
                    {
                        result.IgnoredColumns.Add(name);
                    }
                }
                else if (!seen.Add(field))
                {
                    result.Errors.Add($"Column \"{NormalizeText(cell)}\" appears more than once.");
                }

                mapping.Add(field);
            }

            if (!seen.Contains(Field.Id) && !seen.Contains(Field.DisplayName))
            {
                result.Errors.Add("An ID or Title column is required.");
            }

            if (result.HasErrors)
            {
                return result;
            }

            var latestUnlock = (nowUtc ?? DateTime.UtcNow).AddMinutes(1);
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var rowIndex = headerIndex + 1; rowIndex < rows.Count; rowIndex++)
            {
                var cells = rows[rowIndex];
                if (IsEmptyRow(cells))
                {
                    continue;
                }

                var rowNumber = rowIndex + 1;
                var row = new CustomAchievementCsvRow { RowNumber = rowNumber };
                var rowErrorCount = result.Errors.Count;
                for (var columnIndex = 0; columnIndex < cells.Count && columnIndex < mapping.Count; columnIndex++)
                {
                    ApplyField(result, row, mapping[columnIndex], NormalizeText(header[columnIndex]), cells[columnIndex], rowNumber);
                }

                if (row.Id == null && row.DisplayName == null)
                {
                    result.Errors.Add($"Row {rowNumber}: an ID or a Title is required.");
                }

                if (row.Id != null && !usedIds.Add(row.Id))
                {
                    result.Errors.Add($"Row {rowNumber}, ID: \"{row.Id}\" is used by an earlier row.");
                }

                if (row.Unlocked == false && row.UnlockTimeUtc.HasValue)
                {
                    result.Errors.Add($"Row {rowNumber}, Unlock Time: set while Unlocked is false.");
                }

                if (row.UnlockTimeUtc.HasValue && row.UnlockTimeUtc.Value > latestUnlock)
                {
                    result.Errors.Add($"Row {rowNumber}, Unlock Time: is in the future.");
                }

                if (row.ProgressNum.HasValue &&
                    row.ProgressDenom.HasValue &&
                    row.ProgressNum.Value > row.ProgressDenom.Value)
                {
                    result.Errors.Add($"Row {rowNumber}, Progress: is greater than Progress Total.");
                }

                if (result.Errors.Count == rowErrorCount)
                {
                    result.Rows.Add(row);
                }
            }

            return result;
        }

        /// <summary>
        /// Reads a CSV as complete definitions, for a package whose rows are all new: every row
        /// needs a Title, and a blank ID is generated from it.
        /// </summary>
        public CustomAchievementTextImportResult Import(string text)
        {
            var result = new CustomAchievementTextImportResult();
            var parsed = Parse(text);
            result.Errors.AddRange(parsed.Errors);
            if (parsed.HasErrors)
            {
                return result;
            }

            var usedIds = new HashSet<string>(
                parsed.Rows.Where(row => row.Id != null).Select(row => row.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in parsed.Rows)
            {
                if (row.DisplayName == null)
                {
                    result.Errors.Add($"Row {row.RowNumber}, Title: is required.");
                    continue;
                }

                var id = row.Id ?? CustomAchievementProjectionService.GenerateId(row.DisplayName, usedIds);
                usedIds.Add(id);
                result.Definitions.Add(ToDefinition(row, id));
            }

            if (result.Definitions.Count == 0 && !result.HasErrors)
            {
                result.Errors.Add("No importable achievements were found.");
            }

            return result;
        }

        /// <summary>A new authored achievement built from a row's cells.</summary>
        public static CustomAchievementDefinition ToDefinition(CustomAchievementCsvRow row, string id)
        {
            return new CustomAchievementDefinition
            {
                Id = id,
                DisplayName = row.DisplayName,
                Description = row.Description,
                Points = row.Points,
                TrophyType = row.TrophyType,
                Hidden = row.Hidden ?? false,
                Rarity = row.RarityPercent.HasValue
                    ? PercentRarityHelper.GetRarityTier(row.RarityPercent.Value).ToString()
                    : row.RarityTier,
                GlobalPercentUnlocked = row.RarityPercent,
                Category = row.Category,
                ProgressNum = row.ProgressNum,
                ProgressDenom = row.ProgressDenom,
                Unlocked = row.Unlocked ?? row.UnlockTimeUtc.HasValue,
                UnlockTimeUtc = row.Unlocked == false ? null : row.UnlockTimeUtc,
                UnlockedIconPath = row.UnlockedIconPath,
                LockedIconPath = row.LockedIconPath
            };
        }

        /// <summary>
        /// Reads a rarity cell: a percent with or without a % sign, or a tier name in any case,
        /// with spaces, dashes and underscores ignored.
        /// </summary>
        public static bool TryParseRarity(string value, out double? percent, out string tier)
        {
            percent = null;
            tier = null;
            var normalized = NormalizeText(value);
            if (normalized == null)
            {
                return false;
            }

            var percentText = normalized.TrimEnd('%').Trim();
            if (TryParseDouble(percentText, out var parsed))
            {
                if (parsed < 0 || parsed > 100)
                {
                    return false;
                }

                percent = parsed;
                return true;
            }

            var compact = new string(normalized
                .Where(c => c != ' ' && c != '-' && c != '_')
                .ToArray());
            // Matched by name, not Enum.TryParse, which also takes numbers and comma lists.
            tier = Enum.GetNames(typeof(RarityTier))
                .FirstOrDefault(name => string.Equals(name, compact, StringComparison.OrdinalIgnoreCase));
            return tier != null;
        }

        /// <summary>Lowercase bronze, silver, gold or platinum, or null for anything else.</summary>
        public static string NormalizeTrophyType(string value)
        {
            var normalized = NormalizeText(value)?.ToLowerInvariant();
            switch (normalized)
            {
                case "bronze":
                case "silver":
                case "gold":
                case "platinum":
                    return normalized;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Reads a category cell written as its display path ("Parent &gt; Child") into the stored
        /// path. Null for a blank cell.
        /// </summary>
        public static string ParseCategoryPath(string value)
        {
            var normalized = NormalizeText(value);
            if (normalized == null)
            {
                return null;
            }

            return CategoryPathHelper.JoinRaw(normalized.Split('>'));
        }

        private static Field ResolveField(string header)
        {
            var normalized = NormalizeHeader(header);
            return !string.IsNullOrWhiteSpace(normalized) &&
                   HeaderAliases.TryGetValue(normalized, out var field)
                ? field
                : Field.Unknown;
        }

        private static void ApplyField(
            CustomAchievementCsvParseResult result,
            CustomAchievementCsvRow row,
            Field field,
            string column,
            string rawValue,
            int rowNumber)
        {
            var value = NormalizeText(rawValue);
            if (field == Field.Unknown || value == null)
            {
                return;
            }

            void Error(string message) =>
                result.Errors.Add($"Row {rowNumber}, {column}: \"{value}\" {message}");

            switch (field)
            {
                case Field.Id:
                    row.Id = CustomAchievementProjectionService.NormalizeId(value);
                    break;
                case Field.DisplayName:
                    row.DisplayName = value;
                    break;
                case Field.Description:
                    row.Description = value;
                    break;
                case Field.Points:
                    if (TryParseInt(value, out var points) && points >= 0)
                    {
                        row.Points = points;
                    }
                    else
                    {
                        Error("is not a whole number of 0 or more.");
                    }
                    break;
                case Field.TrophyType:
                    row.TrophyType = NormalizeTrophyType(value);
                    if (row.TrophyType == null)
                    {
                        Error(TrophyTypeMessage);
                    }
                    break;
                case Field.Hidden:
                    if (TryParseBoolean(value, out var hidden))
                    {
                        row.Hidden = hidden;
                    }
                    else
                    {
                        Error("is not true or false.");
                    }
                    break;
                case Field.Rarity:
                    if (TryParseRarity(value, out var percent, out var tier))
                    {
                        row.RarityPercent = percent;
                        row.RarityTier = tier;
                    }
                    else
                    {
                        Error(RarityMessage);
                    }
                    break;
                case Field.Category:
                    row.Category = ParseCategoryPath(value);
                    break;
                case Field.ProgressNum:
                    if (TryParseInt(value, out var progress) && progress >= 0)
                    {
                        row.ProgressNum = progress;
                    }
                    else
                    {
                        Error("is not a whole number of 0 or more.");
                    }
                    break;
                case Field.ProgressDenom:
                    if (TryParseInt(value, out var total) && total > 0)
                    {
                        row.ProgressDenom = total;
                    }
                    else
                    {
                        Error("is not a whole number greater than 0.");
                    }
                    break;
                case Field.Unlocked:
                    if (TryParseBoolean(value, out var unlocked))
                    {
                        row.Unlocked = unlocked;
                    }
                    else
                    {
                        Error("is not true or false.");
                    }
                    break;
                case Field.UnlockTime:
                    if (TryParseUnlockTimeUtc(value, out var unlockTimeUtc))
                    {
                        row.UnlockTimeUtc = unlockTimeUtc;
                    }
                    else
                    {
                        Error("is not a date and time.");
                    }
                    break;
                case Field.UnlockedIconPath:
                    row.UnlockedIconPath = value;
                    break;
                case Field.LockedIconPath:
                    row.LockedIconPath = value;
                    break;
            }
        }

        private static bool TryParseInt(string value, out int parsed)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ||
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed);
        }

        private static bool TryParseDouble(string value, out double parsed)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                   double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
        }

        private static bool TryParseBoolean(string value, out bool parsed)
        {
            parsed = false;
            switch (NormalizeHeader(value))
            {
                case "true":
                case "yes":
                case "y":
                case "1":
                case "unlocked":
                case "earned":
                    parsed = true;
                    return true;
                case "false":
                case "no":
                case "n":
                case "0":
                case "locked":
                    parsed = false;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Reads an unlock time as local time unless it carries an offset or Z. The current
        /// culture is tried first because a spreadsheet saves dates in the user's own format.
        /// </summary>
        private static bool TryParseUnlockTimeUtc(string value, out DateTime utc)
        {
            const DateTimeStyles styles =
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces;
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, styles, out utc) ||
                DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out utc))
            {
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                return true;
            }

            return false;
        }

        private static bool IsEmptyRow(IEnumerable<string> row)
        {
            return row == null || row.All(value => string.IsNullOrWhiteSpace(value));
        }

        /// <summary>
        /// Splits the text into records. Blank records are kept so a record's index stays its
        /// spreadsheet row; only a trailing blank one is dropped.
        /// </summary>
        private static List<List<string>> ParseRows(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return rows;
            }

            if (text[0] == '﻿')
            {
                text = text.Substring(1);
            }

            var delimiter = DetectDelimiter(text);
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (c == delimiter)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    continue;
                }

                cell.Append(c);
            }

            row.Add(cell.ToString());
            if (!IsEmptyRow(row))
            {
                rows.Add(row);
            }

            return rows;
        }

        private static char DetectDelimiter(string text)
        {
            var header = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r' || c == '\n')
                {
                    break;
                }

                header.Append(c);
            }

            var headerText = header.ToString();
            return CountOutsideQuotes(headerText, '\t') > CountOutsideQuotes(headerText, ',')
                ? '\t'
                : ',';
        }

        private static int CountOutsideQuotes(string value, char delimiter)
        {
            var count = 0;
            var inQuotes = false;
            for (var i = 0; i < (value?.Length ?? 0); i++)
            {
                var c = value[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < value.Length && value[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (!inQuotes && c == delimiter)
                {
                    count++;
                }
            }

            return count;
        }

        private static string NormalizeHeader(string value)
        {
            var normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            var builder = new StringBuilder(normalized.Length);
            for (var i = 0; i < normalized.Length; i++)
            {
                var c = normalized[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static string NormalizeText(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }
}

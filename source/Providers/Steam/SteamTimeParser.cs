using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace PlayniteAchievements.Providers.Steam
{
    /// <summary>
    /// Utilities for parsing Steam achievement unlock times and handling Steam's Pacific timezone.
    /// </summary>
    internal static class SteamTimeParser
    {
        private static readonly TimeZoneInfo SteamBaseTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        private static readonly string[] TimeFormats = new[]
        {
            // With Year
            "MMM d, yyyy h:mmtt", "MMM dd, yyyy h:mmtt",
            "MMMM d, yyyy h:mmtt", "MMMM dd, yyyy h:mmtt",
            "MMM d, yyyy H:mm",   "MMM dd, yyyy H:mm",
            "MMMM d, yyyy H:mm",  "MMMM dd, yyyy H:mm",
            "MMM d yyyy h:mmtt",  "MMM dd yyyy h:mmtt",
            "MMMM d yyyy h:mmtt", "MMMM dd yyyy h:mmtt",
            "MMM d yyyy H:mm",    "MMM dd yyyy H:mm",
            "MMMM d yyyy H:mm",   "MMMM dd yyyy H:mm",
            "d MMM, yyyy h:mmtt", "dd MMM, yyyy h:mmtt",
            "d MMMM, yyyy h:mmtt","dd MMMM, yyyy h:mmtt",
            "d MMM, yyyy H:mm",   "dd MMM, yyyy H:mm",
            "d MMMM, yyyy H:mm",  "dd MMMM, yyyy H:mm",
            "d MMM yyyy h:mmtt",  "dd MMM yyyy h:mmtt",
            "d MMMM yyyy h:mmtt", "dd MMMM yyyy h:mmtt",
            "d MMM yyyy H:mm",    "dd MMM yyyy H:mm",
            "d MMMM yyyy H:mm",   "dd MMMM yyyy H:mm",
            "yyyy/MM/dd H:mm",    "yyyy-MM-dd H:mm",

            // Without Year (Implicit Current/Last Year)
            "MMM d H:mm",         "MMM dd H:mm",
            "MMMM d H:mm",        "MMMM dd H:mm",
            "d MMM H:mm",         "dd MMM H:mm",
            "d MMMM H:mm",        "dd MMMM H:mm",
            "MMM d h:mmtt",       "MMM dd h:mmtt",
            "MMMM d h:mmtt",      "MMMM dd h:mmtt",
            "d MMM h:mmtt",       "dd MMM h:mmtt",
            "d MMMM h:mmtt",      "dd MMMM h:mmtt"
        };

        /// <summary>
        /// Parse Steam's achievement unlock time format to UTC, handling multiple languages.
        /// </summary>
        public static DateTime? TryParseSteamUnlockTime(string text, string language)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Normalize non-breaking spaces
            var normalized = text
                .Replace('\u00A0', ' ')
                .Replace('\u2007', ' ')
                .Replace('\u202F', ' ');

            var clean = Regex.Replace(normalized, @"\s+", " ").Trim();
            if (clean.Length == 0) return null;

            try
            {
                var culture = GetCultureForSteamLanguage(language);
                var isEnglish = culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase);

                // 1. Extract Time
                // Matches 19:42, 7:42pm, 7:42 pm, 19h42 at the end of the string
                var timeMatch = Regex.Match(clean, @"(?<time>\d{1,2}[:h]\d{2}(?:\s*(?:am|pm|AM|PM))?)$");
                if (!timeMatch.Success) return null;

                var timeStr = timeMatch.Groups["time"].Value.Replace("h", ":");
                var datePart = clean.Substring(0, timeMatch.Index).Trim();

                // 2. Cleanup Date Part
                // Remove specific separator words (at, @, à, um) and separator chars (comma, dash) at the end.
                // Critical: Do NOT strip '.' as it may be part of an abbreviation (e.g. "janv.").
                // Regex matches:
                // 1. Whitespace followed by specific words (at, @, à, um)
                // 2. Trailing whitespace, commas, or dashes
                datePart = Regex.Replace(datePart, @"(?:\s+(?:at|@|à|um)|[\s,-])+$", "", RegexOptions.IgnoreCase);

                // English: Strip "Unlocked" prefix
                if (isEnglish)
                {
                    datePart = Regex.Replace(datePart, @"^Unlocked\s+", "", RegexOptions.IgnoreCase);
                }

                // Heuristic: For languages where date starts with Day (digit), strip non-digit prefixes
                // e.g. "Déverrouillé le 22 janv." -> "22 janv."
                if (StartsWithDay(language))
                {
                    var digitMatch = Regex.Match(datePart, @"\d");
                    if (digitMatch.Success)
                    {
                        datePart = datePart.Substring(digitMatch.Index);
                    }
                }
                
                // 3. Parse
                var toParse = $"{datePart} {timeStr}";

                // Strategy A: Try ParseExact with known formats (Robust)
                if (DateTime.TryParseExact(toParse, TimeFormats, culture, DateTimeStyles.AllowWhiteSpaces, out var dt))
                {
                    return HandleYearAndConvert(dt, datePart);
                }

                // Strategy B: Standard TryParse (Flexible)
                if (DateTime.TryParse(toParse, culture, DateTimeStyles.AllowWhiteSpaces, out dt))
                {
                    return HandleYearAndConvert(dt, datePart);
                }

                // Strategy C: Fallback for missing accents (e.g. "aout" vs "août")
                // Steam sometimes returns non-accented month names that fail strict parsing.
                if (TryFallbackParse(toParse, culture, out dt))
                {
                    return HandleYearAndConvert(dt, datePart);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryFallbackParse(string input, CultureInfo originalCulture, out DateTime result)
        {
            try
            {
                // Create a clone of the culture to modify
                var looseCulture = (CultureInfo)originalCulture.Clone();
                var dtf = looseCulture.DateTimeFormat;

                // Strip diacritics from month names in the culture
                dtf.MonthNames = dtf.MonthNames.Select(RemoveDiacritics).ToArray();
                dtf.AbbreviatedMonthNames = dtf.AbbreviatedMonthNames.Select(RemoveDiacritics).ToArray();
                
                // Strip diacritics from the input string
                var looseInput = RemoveDiacritics(input);

                return DateTime.TryParse(looseInput, looseCulture, DateTimeStyles.AllowWhiteSpaces, out result);
            }
            catch
            {
                result = default;
                return false;
            }
        }

        private static DateTime HandleYearAndConvert(DateTime dt, string originalDatePart)
        {
            // Handle year rollover (if year was missing from string)
            var steamNow = GetSteamNow();
            var hasYear = Regex.IsMatch(originalDatePart, @"\d{4}");

            if (!hasYear)
            {
                // If parsed successfully but without a year, .NET defaults to current year.
                // If that results in a future date (with 2 days buffer for timezone/clock diffs),
                // it likely belongs to the previous year.
                if (dt.Year == DateTime.Now.Year && dt > steamNow.AddDays(2))
                {
                    dt = dt.AddYears(-1);
                }
            }

            return ConvertToUtc(dt);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(normalizedString.Length);

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static DateTime ConvertToUtc(DateTime dt)
        {
            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), SteamBaseTimeZone);
            }
            catch
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }

        private static bool StartsWithDay(string language)
        {
            var l = language?.ToLowerInvariant();
            // Languages that typically start with Month or Year, or use characters where stripping is risky
            if (l == "english" || l == "japanese" || l == "koreana" || l == "schinese" || l == "tchinese" || l == "hungarian")
                return false;
            
            return true;
        }

private static CultureInfo GetCultureForSteamLanguage(string language)
        {
            switch (language?.ToLowerInvariant())
            {
                case "german": return new CultureInfo("de-DE");
                case "french": return new CultureInfo("fr-FR");
                case "spanish": return new CultureInfo("es-ES");
                case "italian": return new CultureInfo("it-IT");
                case "russian": return new CultureInfo("ru-RU");
                case "japanese": return new CultureInfo("ja-JP");
                case "portuguese": return new CultureInfo("pt-PT");
                case "brazilian": return new CultureInfo("pt-BR");
                case "polish": return new CultureInfo("pl-PL");
                case "dutch": return new CultureInfo("nl-NL");
                case "swedish": return new CultureInfo("sv-SE");
                case "finnish": return new CultureInfo("fi-FI");
                case "danish": return new CultureInfo("da-DK");
                case "norwegian": return new CultureInfo("no-NO");
                case "hungarian": return new CultureInfo("hu-HU");
                case "czech": return new CultureInfo("cs-CZ");
                case "romanian": return new CultureInfo("ro-RO");
                case "turkish": return new CultureInfo("tr-TR");
                case "greek": return new CultureInfo("el-GR");
                case "bulgarian": return new CultureInfo("bg-BG");
                case "ukrainian": return new CultureInfo("uk-UA");
                case "thai": return new CultureInfo("th-TH");
                case "vietnamese": return new CultureInfo("vi-VN");
                case "koreana": return new CultureInfo("ko-KR");
                case "schinese": return new CultureInfo("zh-CN");
                case "tchinese": return new CultureInfo("zh-TW");
                case "arabic": return new CultureInfo("ar-SA");
                default: return new CultureInfo("en-US");
            }
        }

        /// <summary>
        /// Get current time in Steam's Pacific timezone.
        /// </summary>
        public static DateTime GetSteamNow()
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SteamBaseTimeZone);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        /// <summary>
        /// Get the timezone offset for Steam's Pacific timezone for cookie usage.
        /// Returns -28800 seconds (UTC-8) as Steam's standard offset.
        /// </summary>
        public static string GetSteamTimezoneOffsetCookieValue()
        {
            return "-28800,0";
        }
    }
}

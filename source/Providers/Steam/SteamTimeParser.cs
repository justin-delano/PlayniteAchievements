using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayniteAchievements.Providers.Steam
{
    /// <summary>
    /// Utilities for parsing Steam achievement unlock times and handling Steam's Pacific timezone.
    /// Parsing is token-first and language-agnostic so it can tolerate noisy localized wrappers.
    /// </summary>
    internal static class SteamTimeParser
    {
        private static readonly TimeZoneInfo SteamBaseTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        private static readonly string[] SupportedCultureNames = new[]
        {
            "en-US", "de-DE", "fr-FR", "es-ES", "it-IT", "ru-RU",
            "ja-JP", "pt-PT", "pt-BR", "pl-PL", "nl-NL", "sv-SE",
            "fi-FI", "da-DK", "nb-NO", "hu-HU", "cs-CZ", "ro-RO",
            "tr-TR", "el-GR", "bg-BG", "uk-UA", "th-TH", "vi-VN",
            "ko-KR", "zh-CN", "zh-TW", "ar-SA"
        };

        private static readonly CultureInfo[] SupportedCultures = BuildSupportedCultures();

        private static readonly Regex CollapseSpacesRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex TimeAtEndRegex = new Regex(
            @"(?<prefix>(?:am|pm|a\.m\.|p\.m\.)\s*)?(?<time>\d{1,2}(?::|h)\d{2})(?<suffix>\s*(?:am|pm|a\.m\.|p\.m\.)?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExtractTokensRegex = new Regex(@"\d{1,4}\p{L}+|\p{L}+|\d{1,4}", RegexOptions.Compiled);
        private static readonly Regex HasYearRegex = new Regex(@"\b\d{4}\b", RegexOptions.Compiled);
        private static readonly Regex MonthAbbreviationDotRegex = new Regex(@"(?<=\p{L})\.(?=\s|$)", RegexOptions.Compiled);
        private static readonly Regex LeadingWordRegex = new Regex(@"^\s*(\p{L}+)\b", RegexOptions.Compiled);
        private static readonly Regex TrailingWordRegex = new Regex(@"\b(\p{L}+)\s*$", RegexOptions.Compiled);
        private static readonly Regex StartsWithDigitRegex = new Regex(@"^\s*\d", RegexOptions.Compiled);
        private static readonly Regex EndsWithDigitRegex = new Regex(@"\d\s*$", RegexOptions.Compiled);
        private static readonly Regex TrailingPunctuationRegex = new Regex(@"[\s,\-:;|]+$", RegexOptions.Compiled);

        private static readonly Lazy<Dictionary<string, int>> MonthTokenMap =
            new Lazy<Dictionary<string, int>>(BuildMonthTokenMap);

        /// <summary>
        /// Parse Steam's achievement unlock time format to UTC.
        /// </summary>
        public static DateTime? TryParseSteamUnlockTime(string text, string language)
        {
            return TryParseSteamUnlockTime(text, language, GetSteamNow());
        }

        internal static DateTime? TryParseSteamUnlockTime(string text, string language, DateTime steamNow)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var clean = NormalizeWhitespace(text);
            if (clean.Length == 0)
            {
                return null;
            }

            try
            {
                var culture = GetCultureForSteamLanguage(language);

                if (!TryExtractTime(clean, out var timeValue, out var datePart))
                {
                    return null;
                }

                var cleanedDatePart = CleanupDatePart(datePart);
                if (string.IsNullOrWhiteSpace(cleanedDatePart))
                {
                    return null;
                }

                if (TryParseTokenizedDateTime(cleanedDatePart, timeValue, steamNow.Year, out var tokenizedDt, out var tokenizedHasYear))
                {
                    return HandleYearAndConvert(tokenizedDt, tokenizedHasYear, steamNow);
                }

                var fallbackInput = $"{cleanedDatePart} {timeValue}".Trim();
                var hasYear = HasYearRegex.IsMatch(cleanedDatePart);

                if (TryParseWithCulture(fallbackInput, culture, out var dt) ||
                    TryParseAcrossCultures(fallbackInput, culture, out dt))
                {
                    return HandleYearAndConvert(dt, hasYear, steamNow);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryExtractTime(string input, out string normalizedTime, out string datePart)
        {
            normalizedTime = null;
            datePart = null;

            var match = TimeAtEndRegex.Match(input);
            if (!match.Success)
            {
                return false;
            }

            var prefix = NormalizeMeridiemToken(match.Groups["prefix"].Value);
            var suffix = NormalizeMeridiemToken(match.Groups["suffix"].Value);
            var meridiem = !string.IsNullOrWhiteSpace(suffix) ? suffix : prefix;
            var timeCore = NormalizeTime(match.Groups["time"].Value);

            normalizedTime = string.IsNullOrWhiteSpace(meridiem) ? timeCore : $"{timeCore} {meridiem}";
            datePart = input.Substring(0, match.Index).Trim();

            return !string.IsNullOrWhiteSpace(normalizedTime) && !string.IsNullOrWhiteSpace(datePart);
        }

        private static string NormalizeMeridiemToken(string token)
        {
            var cleaned = NormalizeWhitespace((token ?? string.Empty).Replace(".", ""));
            if (cleaned.Equals("am", StringComparison.OrdinalIgnoreCase))
            {
                return "AM";
            }

            if (cleaned.Equals("pm", StringComparison.OrdinalIgnoreCase))
            {
                return "PM";
            }

            return string.Empty;
        }

        private static string NormalizeWhitespace(string text)
        {
            var normalized = (text ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Replace('\u2007', ' ')
                .Replace('\u202F', ' ');
            return CollapseSpacesRegex.Replace(normalized, " ").Trim();
        }

        private static string CleanupDatePart(string datePart)
        {
            var cleaned = NormalizeWhitespace(datePart ?? string.Empty);
            cleaned = cleaned.Replace("@", " ");
            cleaned = MonthAbbreviationDotRegex.Replace(cleaned, "");
            cleaned = TrimLeadingNoiseWords(cleaned);
            cleaned = TrimTrailingNoiseWords(cleaned);
            cleaned = TrailingPunctuationRegex.Replace(cleaned, string.Empty);
            return NormalizeWhitespace(cleaned);
        }

        private static string TrimLeadingNoiseWords(string input)
        {
            var cleaned = input ?? string.Empty;

            for (var i = 0; i < 8 && cleaned.Length > 0; i++)
            {
                if (StartsWithDigitRegex.IsMatch(cleaned))
                {
                    break;
                }

                var match = LeadingWordRegex.Match(cleaned);
                if (!match.Success)
                {
                    break;
                }

                var token = NormalizeMonthToken(match.Groups[1].Value);
                if (MonthTokenMap.Value.ContainsKey(token))
                {
                    break;
                }

                cleaned = cleaned.Substring(match.Length).TrimStart();
            }

            return cleaned;
        }

        private static string TrimTrailingNoiseWords(string input)
        {
            var cleaned = input ?? string.Empty;

            for (var i = 0; i < 8 && cleaned.Length > 0; i++)
            {
                if (EndsWithDigitRegex.IsMatch(cleaned))
                {
                    break;
                }

                var match = TrailingWordRegex.Match(cleaned);
                if (!match.Success)
                {
                    break;
                }

                var token = NormalizeMonthToken(match.Groups[1].Value);
                if (MonthTokenMap.Value.ContainsKey(token))
                {
                    break;
                }

                cleaned = cleaned.Substring(0, match.Index).TrimEnd();
            }

            return cleaned;
        }

        private static bool TryParseTokenizedDateTime(string datePart, string timeRaw, int referenceYear, out DateTime dt, out bool hasYear)
        {
            dt = default;
            hasYear = false;

            if (!TryParseTimeOfDay(timeRaw, out var parsedTime))
            {
                return false;
            }

            var tokenized = NormalizeForTokenization(datePart);
            var tokens = ExtractTokensRegex.Matches(tokenized)
                .Cast<Match>()
                .Select(m => m.Value)
                .ToList();

            if (tokens.Count == 0)
            {
                return false;
            }

            var monthCandidates = new List<(int Index, int Month)>();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (MonthTokenMap.Value.TryGetValue(tokens[i], out var month))
                {
                    monthCandidates.Add((i, month));
                }
            }

            if (monthCandidates.Count == 0)
            {
                return false;
            }

            var chosen = monthCandidates.FirstOrDefault(c => HasAdjacentDayToken(tokens, c.Index));
            if (chosen.Month == 0)
            {
                chosen = monthCandidates[0];
            }

            if (!TryResolveDay(tokens, chosen.Index, out var day))
            {
                return false;
            }

            if (!TryResolveYear(tokens, chosen.Index, out var year))
            {
                year = referenceYear;
            }
            else
            {
                hasYear = true;
            }

            try
            {
                dt = new DateTime(year, chosen.Month, day, parsedTime.Hour, parsedTime.Minute, 0, DateTimeKind.Unspecified);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseTimeOfDay(string timeRaw, out DateTime parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(timeRaw))
            {
                return false;
            }

            var normalized = NormalizeTime(timeRaw);
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                normalized,
                Regex.Replace(normalized, @"(?i)(am|pm)$", " $1").Trim()
            };

            var formats = new[]
            {
                "H:mm", "HH:mm",
                "h:mmtt", "h:mm tt",
                "hh:mmtt", "hh:mm tt"
            };

            foreach (var candidate in candidates)
            {
                if (DateTime.TryParseExact(
                    candidate,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out parsed))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeTime(string timeRaw)
        {
            var normalized = (timeRaw ?? string.Empty).Trim();
            normalized = normalized.Replace('h', ':').Replace('H', ':');
            return NormalizeWhitespace(normalized);
        }

        private static bool TryParseWithCulture(string input, CultureInfo culture, out DateTime dt)
        {
            dt = default;
            if (culture == null)
            {
                return false;
            }

            if (DateTime.TryParse(input, culture, DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return true;
            }

            return TryFallbackParse(input, culture, out dt);
        }

        private static bool TryParseAcrossCultures(string input, CultureInfo firstCulture, out DateTime dt)
        {
            dt = default;

            if (firstCulture != null && TryParseWithCulture(input, firstCulture, out dt))
            {
                return true;
            }

            foreach (var culture in SupportedCultures)
            {
                if (firstCulture != null &&
                    culture.Name.Equals(firstCulture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseWithCulture(input, culture, out dt))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFallbackParse(string input, CultureInfo originalCulture, out DateTime result)
        {
            try
            {
                var looseCulture = (CultureInfo)originalCulture.Clone();
                var dtf = looseCulture.DateTimeFormat;

                dtf.MonthNames = dtf.MonthNames.Select(RemoveDiacritics).ToArray();
                dtf.AbbreviatedMonthNames = dtf.AbbreviatedMonthNames.Select(RemoveDiacritics).ToArray();
                dtf.MonthGenitiveNames = dtf.MonthGenitiveNames.Select(RemoveDiacritics).ToArray();
                dtf.AbbreviatedMonthGenitiveNames = dtf.AbbreviatedMonthGenitiveNames.Select(RemoveDiacritics).ToArray();

                var looseInput = RemoveDiacritics(input);
                return DateTime.TryParse(looseInput, looseCulture, DateTimeStyles.AllowWhiteSpaces, out result);
            }
            catch
            {
                result = default;
                return false;
            }
        }

        private static string NormalizeForTokenization(string input)
        {
            var source = RemoveDiacritics(input ?? string.Empty).ToLowerInvariant();
            var sb = new StringBuilder(source.Length);

            foreach (var ch in source)
            {
                if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(' ');
                }
            }

            return NormalizeWhitespace(sb.ToString());
        }

        private static Dictionary<string, int> BuildMonthTokenMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var culture in SupportedCultures)
            {
                var dtf = culture.DateTimeFormat;
                for (var i = 0; i < 12; i++)
                {
                    AddMonthToken(map, dtf.MonthNames[i], i + 1);
                    AddMonthToken(map, dtf.AbbreviatedMonthNames[i], i + 1);
                    AddMonthToken(map, dtf.MonthGenitiveNames[i], i + 1);
                    AddMonthToken(map, dtf.AbbreviatedMonthGenitiveNames[i], i + 1);
                }
            }

            // Common variant aliases seen in Steam output.
            map["sept"] = 9;
            map["set"] = 9;

            return map;
        }

        private static void AddMonthToken(Dictionary<string, int> map, string raw, int month)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var token = NormalizeMonthToken(raw);
            if (token.Length >= 2)
            {
                map[token] = month;
            }
        }

        private static string NormalizeMonthToken(string raw)
        {
            return RemoveDiacritics(raw ?? string.Empty)
                .ToLowerInvariant()
                .Trim()
                .Trim('.', ',', ';', ':');
        }

        private static bool HasAdjacentDayToken(List<string> tokens, int monthIndex)
        {
            return (monthIndex > 0 && IsDayToken(tokens[monthIndex - 1])) ||
                   (monthIndex + 1 < tokens.Count && IsDayToken(tokens[monthIndex + 1]));
        }

        private static bool IsDayToken(string token)
        {
            return TryParseLeadingNumber(token, out var value) && value >= 1 && value <= 31;
        }

        private static bool TryResolveDay(List<string> tokens, int monthIndex, out int day)
        {
            day = 0;

            if (monthIndex > 0 &&
                TryParseLeadingNumber(tokens[monthIndex - 1], out var prev) &&
                prev >= 1 && prev <= 31)
            {
                day = prev;
                return true;
            }

            if (monthIndex + 1 < tokens.Count &&
                TryParseLeadingNumber(tokens[monthIndex + 1], out var next) &&
                next >= 1 && next <= 31)
            {
                day = next;
                return true;
            }

            for (var distance = 2; distance < tokens.Count; distance++)
            {
                var left = monthIndex - distance;
                if (left >= 0 &&
                    TryParseLeadingNumber(tokens[left], out var leftValue) &&
                    leftValue >= 1 && leftValue <= 31)
                {
                    day = leftValue;
                    return true;
                }

                var right = monthIndex + distance;
                if (right < tokens.Count &&
                    TryParseLeadingNumber(tokens[right], out var rightValue) &&
                    rightValue >= 1 && rightValue <= 31)
                {
                    day = rightValue;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveYear(List<string> tokens, int monthIndex, out int year)
        {
            year = 0;

            // Prefer a year after month token first.
            for (var i = monthIndex + 1; i < tokens.Count; i++)
            {
                if (TryParseLeadingNumber(tokens[i], out var after) && after >= 1900 && after <= 3000)
                {
                    year = after;
                    return true;
                }
            }

            for (var i = monthIndex - 1; i >= 0; i--)
            {
                if (TryParseLeadingNumber(tokens[i], out var before) && before >= 1900 && before <= 3000)
                {
                    year = before;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseLeadingNumber(string token, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var digits = new string(token.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || digits.Length > 4)
            {
                return false;
            }

            return int.TryParse(digits, out value);
        }

        private static DateTime HandleYearAndConvert(DateTime dt, bool hasYear, DateTime steamNow)
        {
            if (!hasYear)
            {
                // If parsed yearless date lands too far in the future, treat it as previous year.
                if (dt > steamNow.AddDays(2))
                {
                    dt = dt.AddYears(-1);
                }
            }

            return ConvertToUtc(dt);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

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

        private static CultureInfo[] BuildSupportedCultures()
        {
            return SupportedCultureNames
                .Select(name =>
                {
                    try { return new CultureInfo(name); } catch { return null; }
                })
                .Where(c => c != null)
                .ToArray();
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
                case "norwegian": return new CultureInfo("nb-NO");
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayniteAchievements.Providers.Steam
{
    internal static class SteamTimeParser
    {
        private static readonly TimeZoneInfo SteamBaseTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        private static readonly string[] SupportedCultureNames =
        {
            "en-US", "de-DE", "fr-FR", "es-ES", "it-IT", "ru-RU",
            "ja-JP", "pt-PT", "pt-BR", "pl-PL", "nl-NL", "sv-SE",
            "fi-FI", "da-DK", "nb-NO", "hu-HU", "cs-CZ", "ro-RO",
            "tr-TR", "el-GR", "bg-BG", "uk-UA", "th-TH", "vi-VN",
            "ko-KR", "zh-CN", "zh-TW", "ar-SA"
        };

        private static readonly CultureInfo[] SupportedCultures = BuildSupportedCultures();
        private static readonly Lazy<Dictionary<string, int>> MonthTokenMap = new Lazy<Dictionary<string, int>>(BuildMonthTokenMap);
        private static readonly Dictionary<string, (Regex CurrentYear, Regex PastYears)> UnlockRegexByLanguage = BuildUnlockRegexByLanguage();

        private static readonly Regex CollapseSpacesRegex = new Regex(@"\s+", RegexOptions.Compiled);

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
                if (!TryMatchLanguageRegex(clean, language, out var match))
                {
                    return null;
                }

                if (!TryBuildLocalDateTime(match, steamNow.Year, out var localDateTime, out var hasExplicitYear))
                {
                    return null;
                }

                if (!hasExplicitYear && localDateTime > steamNow.AddDays(2))
                {
                    localDateTime = localDateTime.AddYears(-1);
                }

                return ConvertToUtc(localDateTime);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryMatchLanguageRegex(string input, string language, out Match match)
        {
            match = null;

            var key = (language ?? "english").Trim().ToLowerInvariant();
            if (!UnlockRegexByLanguage.TryGetValue(key, out var pair) &&
                !UnlockRegexByLanguage.TryGetValue("english", out pair))
            {
                return false;
            }

            var current = pair.CurrentYear.Match(input);
            if (current.Success)
            {
                match = current;
                return true;
            }

            var past = pair.PastYears.Match(input);
            if (past.Success)
            {
                match = past;
                return true;
            }

            return false;
        }

        private static bool TryBuildLocalDateTime(Match match, int referenceYear, out DateTime localDateTime, out bool hasYear)
        {
            localDateTime = default;
            hasYear = false;

            var dayGroup = match.Groups["day"];
            var monthGroup = match.Groups["month"];
            var yearGroup = match.Groups["year"];
            var hourGroup = match.Groups["hour"];
            var minuteGroup = match.Groups["minute"];
            var meridiemGroup = match.Groups["meridiem"];

            if (!dayGroup.Success || !monthGroup.Success || !hourGroup.Success || !minuteGroup.Success)
            {
                return false;
            }

            if (!int.TryParse(dayGroup.Value, out var day) || day < 1 || day > 31)
            {
                return false;
            }

            if (!TryResolveMonth(monthGroup.Value, out var month))
            {
                return false;
            }

            var year = referenceYear;
            if (yearGroup.Success && int.TryParse(yearGroup.Value, out var parsedYear) && parsedYear >= 1900 && parsedYear <= 3000)
            {
                year = parsedYear;
                hasYear = true;
            }

            if (!int.TryParse(hourGroup.Value, out var hour) || !int.TryParse(minuteGroup.Value, out var minute))
            {
                return false;
            }

            if (minute < 0 || minute > 59)
            {
                return false;
            }

            var meridiem = meridiemGroup.Success ? meridiemGroup.Value : string.Empty;
            if (!ApplyMeridiem(ref hour, meridiem))
            {
                return false;
            }

            if (hour < 0 || hour > 23)
            {
                return false;
            }

            try
            {
                localDateTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ApplyMeridiem(ref int hour, string meridiemRaw)
        {
            if (string.IsNullOrWhiteSpace(meridiemRaw))
            {
                return hour >= 0 && hour <= 23;
            }

            var normalized = NormalizeMeridiemToken(meridiemRaw);

            if (normalized == "am")
            {
                if (hour < 1 || hour > 12)
                {
                    return false;
                }

                hour = hour == 12 ? 0 : hour;
                return true;
            }

            if (normalized == "pm")
            {
                if (hour < 1 || hour > 12)
                {
                    return false;
                }

                hour = hour == 12 ? 12 : hour + 12;
                return true;
            }

            return false;
        }

        private static string NormalizeMeridiemToken(string token)
        {
            var cleaned = NormalizeWhitespace(token ?? string.Empty)
                .Replace(".", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();

            switch (cleaned)
            {
                case "am":
                case "aｍ":
                case "上午":
                case "오전":
                case "صباحًا":
                case "صباحا":
                    return "am";

                case "pm":
                case "pｍ":
                case "下午":
                case "오후":
                case "مساءً":
                case "مساء":
                    return "pm";

                default:
                    return string.Empty;
            }
        }

        private static bool TryResolveMonth(string monthRaw, out int month)
        {
            month = 0;
            if (string.IsNullOrWhiteSpace(monthRaw))
            {
                return false;
            }

            var compactNumeric = Regex.Replace(monthRaw, @"\D", string.Empty);
            if (compactNumeric.Length > 0 && int.TryParse(compactNumeric, out var numericMonth) && numericMonth >= 1 && numericMonth <= 12)
            {
                month = numericMonth;
                return true;
            }

            var token = NormalizeMonthToken(monthRaw);
            if (MonthTokenMap.Value.TryGetValue(token, out month))
            {
                return true;
            }

            var compact = token.Replace(" ", string.Empty);
            if (MonthTokenMap.Value.TryGetValue(compact, out month))
            {
                return true;
            }

            return false;
        }

        private static string NormalizeWhitespace(string text)
        {
            var normalized = (text ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Replace('\u2007', ' ')
                .Replace('\u202F', ' ');
            return CollapseSpacesRegex.Replace(normalized, " ").Trim();
        }

        private static DateTime ConvertToUtc(DateTime dt)
        {
            var local = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

            try
            {
                if (SteamBaseTimeZone.IsInvalidTime(local))
                {
                    local = local.AddHours(1);
                }

                if (SteamBaseTimeZone.IsAmbiguousTime(local))
                {
                    var offsets = SteamBaseTimeZone.GetAmbiguousTimeOffsets(local);
                    var preferredOffset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
                    return new DateTimeOffset(local, preferredOffset).UtcDateTime;
                }

                var offset = SteamBaseTimeZone.GetUtcOffset(local);
                return new DateTimeOffset(local, offset).UtcDateTime;
            }
            catch
            {
                return new DateTimeOffset(local, SteamBaseTimeZone.BaseUtcOffset).UtcDateTime;
            }
        }

        private static Dictionary<string, (Regex CurrentYear, Regex PastYears)> BuildUnlockRegexByLanguage()
        {
            (Regex CurrentYear, Regex PastYears) Pair(string currentYearPattern, string pastYearPattern)
            {
                return (
                    new Regex(currentYearPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
                    new Regex(pastYearPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }

            return new Dictionary<string, (Regex CurrentYear, Regex PastYears)>(StringComparer.OrdinalIgnoreCase)
            {
                ["english"] = Pair(
                    @"^Unlocked\s+(?:(?<month>[\p{L}]+)\s+(?<day>0?[1-9]|[12]\d|3[01]),\s*(?<year>\d{4})|(?<day>0?[1-9]|[12]\d|3[01])\s+(?<month>[\p{L}]+),\s*(?<year>\d{4}))\s*@\s*(?<hour>\d{1,2})\s*:\s*(?<minute>\d{2})\s*(?<meridiem>[ap]\.?m\.?)\s*$",
                    @"^Unlocked\s+(?:(?<month>[\p{L}]+)\s+(?<day>0?[1-9]|[12]\d|3[01])|(?<day>0?[1-9]|[12]\d|3[01])\s+(?<month>[\p{L}]+))\s*@\s*(?<hour>\d{1,2})\s*:\s*(?<minute>\d{2})\s*(?<meridiem>[ap]\.?m\.?)\s*$"),

                ["german"] = Pair(
                    @"^Am\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.?\s+(?<year>\d{4})\s+um\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s+freigeschaltet\s*$",
                    @"^Am\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.?\s+um\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s+freigeschaltet\s*$"),

                ["french"] = Pair(
                    @"^Débloqué\s+le\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.?\s+(?<year>\d{4})\s+à\s+(?<hour>\d{1,2})h(?<minute>\d{2})\s*$",
                    @"^Débloqué\s+le\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.?\s+à\s+(?<hour>\d{1,2})h(?<minute>\d{2})\s*$"),

                ["spanish"] = Pair(
                    @"^Se\s+desbloqueó\s+el\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+(?<year>\d{4})\s+a\s+las\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Se\s+desbloqueó\s+el\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+a\s+las\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["italian"] = Pair(
                    @"^Sbloccato\s+in\s+data\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+(?<year>\d{4}),\s+ore\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Sbloccato\s+in\s+data\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+),\s+ore\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["russian"] = Pair(
                    @"^Дата\s+получения:\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.\s+(?<year>\d{4})\s+г\.\s+в\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Дата\s+получения:\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+в\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["japanese"] = Pair(
                    @"^アンロックした日\s*(?<year>\d{4})年\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日\s*(?<hour>\d{1,2})時\s*(?<minute>\d{2})分\s*$",
                    @"^アンロックした日\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日\s*(?<hour>\d{1,2})時\s*(?<minute>\d{2})分\s*$"),

                ["portuguese"] = Pair(
                    @"^Desbloqueada\s+a\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.\s+(?<year>\d{4})\s+às\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Desbloqueada\s+a\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.\s+às\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["brazilian"] = Pair(
                    @"^Alcançada\s+em\s+(?<day>\d{1,2})/(?<month>[\p{L}]+)\./(?<year>\d{4})\s+às\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Alcançada\s+em\s+(?<day>\d{1,2})\s+de\s+(?<month>[\p{L}]+)\.\s+às\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["polish"] = Pair(
                    @"^Odblokowano:\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+(?<year>\d{4})\s+o\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Odblokowano:\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+o\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["dutch"] = Pair(
                    @"^Ontgrendeld\s+op\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+(?<year>\d{4})\s+om\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Ontgrendeld\s+op\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+om\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["swedish"] = Pair(
                    @"^Upplåst\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+),\s*(?<year>\d{4})\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Upplåst\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["finnish"] = Pair(
                    @"^Avattu\s+(?<day>\d{1,2})\.(?<month>\d{1,2})\.(?<year>\d{4})\s+klo\s+(?<hour>\d{1,2})\.(?<minute>\d{2})\s*$",
                    @"^Avattu\s+(?<day>\d{1,2})\.(?<month>\d{1,2})\.\s+klo\s+(?<hour>\d{1,2})\.(?<minute>\d{2})\s*$"),

                ["danish"] = Pair(
                    @"^Låst\s+op:\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.?\s+(?<year>\d{4})\s+kl\.\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Låst\s+op:\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.?\s+kl\.\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["norwegian"] = Pair(
                    @"^Låst\s+opp\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.?\s+(?<year>\d{4})\s+kl\.\s+(?<hour>\d{1,2})\.(?<minute>\d{2})\s*$",
                    @"^Låst\s+opp\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.?\s+kl\.\s+(?<hour>\d{1,2})\.(?<minute>\d{2})\s*$"),

                ["hungarian"] = Pair(
                    @"^Feloldva:\s*(?<year>\d{4})\.\s*(?<month>[\p{L}]+)\.\s*(?<day>\d{1,2})\.,\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Feloldva:\s*(?<month>[\p{L}]+)\.\s*(?<day>\d{1,2})\.,\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["czech"] = Pair(
                    @"^Odemčeno\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.\s+(?<year>\d{4})\s+v\s+(?<hour>\d{1,2})\.(?<minute>\d{2})\s*$",
                    @"^Odemčeno\s+(?<day>\d{1,2})\.\s+(?<month>[\p{L}]+)\.\s+v\s+(?<hour>\d{1,2})\.(?<minute>\d{2})\s*$"),

                ["romanian"] = Pair(
                    @"^Obținută\s+la\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.?\s+(?<year>\d{4})\s+la\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Obținută\s+la\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.?\s+la\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["turkish"] = Pair(
                    @"^Kazanma\s+Tarihi\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+(?<year>\d{4})\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Kazanma\s+Tarihi\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["greek"] = Pair(
                    @"^Ξεκλειδώθηκε\s+στις\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s+(?<year>\d{4}),\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Ξεκλειδώθηκε\s+στις\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+),\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["bulgarian"] = Pair(
                    @"^Откл\.\s+на\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.?\s+(?<year>\d{4})\s+в\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Откл\.\s+на\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.?\s+в\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["ukrainian"] = Pair(
                    @"^Здобуто\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.\s+(?<year>\d{4})\s+о\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^Здобуто\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\.\s+о\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["thai"] = Pair(
                    @"^ปลดล็อก\s+(?<day>\d{1,2})\s+(?<month>[\p{L}\p{M}\.]+)\s+(?<year>\d{4})\s*@\s*(?<hour>\d{1,2})\s*:\s*(?<minute>\d{2})\s*(?<meridiem>[ap]m)\s*$",
                    @"^ปลดล็อก\s+(?<day>\d{1,2})\s+(?<month>[\p{L}\p{M}\.]+)\s*@\s*(?<hour>\d{1,2})\s*:\s*(?<minute>\d{2})\s*(?<meridiem>[ap]m)\s*$"),

                ["vietnamese"] = Pair(
                    @"^Mở\s+khóa\s+vào\s+(?<day>\d{1,2})\s+(?<month>[\p{L}\d]+),\s*(?<year>\d{4})\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<meridiem>[ap]m)\s*$",
                    @"^Mở\s+khóa\s+vào\s+(?<day>\d{1,2})\s+(?<month>[\p{L}\d]+)\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<meridiem>[ap]m)\s*$"),

                ["koreana"] = Pair(
                    @"^(?<year>\d{4})년\s*(?<month>\d{1,2})월\s*(?<day>\d{1,2})일\s*(?<meridiem>오전|오후)\s*(?<hour>\d{1,2})시\s*(?<minute>\d{2})분에\s*획득\s*$",
                    @"^(?<month>\d{1,2})월\s*(?<day>\d{1,2})일\s*(?<meridiem>오전|오후)\s*(?<hour>\d{1,2})시\s*(?<minute>\d{2})분에\s*획득\s*$"),

                ["schinese"] = Pair(
                    @"^(?<year>\d{4})\s*年\s*(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日\s*(?<meridiem>上午|下午)\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*解锁\s*$",
                    @"^(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日\s*(?<meridiem>上午|下午)\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*解锁\s*$"),

                ["tchinese"] = Pair(
                    @"^解鎖於\s*(?<year>\d{4})\s*年\s*(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日\s*(?<meridiem>上午|下午)\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$",
                    @"^解鎖於\s*(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日\s*(?<meridiem>上午|下午)\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*$"),

                ["arabic"] = Pair(
                    @"^Unlocked\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+),\s*(?<year>\d{4})\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<meridiem>صباحًا|صباحا|مساءً|مساء)\s*$",
                    @"^Unlocked\s+(?<day>\d{1,2})\s+(?<month>[\p{L}]+)\s*@\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<meridiem>صباحًا|صباحا|مساءً|مساء)\s*$")
            };
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

            map["set"] = 9;

            foreach (var kvp in new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["ян"] = 1,
                ["янв"] = 1,
                ["апр"] = 4,
                ["квіт"] = 4,
                ["січ"] = 1,
                ["лют"] = 2,
                ["берез"] = 3,
                ["трав"] = 5,
                ["черв"] = 6,
                ["лип"] = 7,
                ["серп"] = 8,
                ["верес"] = 9,
                ["жовт"] = 10,
                ["листоп"] = 11,
                ["груд"] = 12,
                ["фев"] = 2,
                ["февр"] = 2,
                ["март"] = 3,
                ["май"] = 5,
                ["юни"] = 6,
                ["юли"] = 7,
                ["авг"] = 8,
                ["сеп"] = 9,
                ["септ"] = 9,
                ["окт"] = 10,
                ["ноем"] = 11,
                ["дек"] = 12,

                ["يناير"] = 1,
                ["مارس"] = 3,
                ["مايو"] = 5,
                ["يونيو"] = 6,
                ["يوليو"] = 7,
                ["أغسطس"] = 8,
                ["اغسطس"] = 8,
                ["سبتمبر"] = 9,
                ["أكتوبر"] = 10,
                ["اكتوبر"] = 10,
                ["نوفمبر"] = 11,
                ["ديسمبر"] = 12,
                ["فبراير"] = 2,
                ["أبريل"] = 4,
                ["ابريل"] = 4,

                ["ม.ค"] = 1,
                ["ก.พ"] = 2,
                ["มี.ค"] = 3,
                ["เม.ย"] = 4,
                ["พ.ค"] = 5,
                ["มิ.ย"] = 6,
                ["ก.ค"] = 7,
                ["ส.ค"] = 8,
                ["ก.ย"] = 9,
                ["ต.ค"] = 10,
                ["พ.ย"] = 11,
                ["ธ.ค"] = 12
            })
            {
                AddMonthToken(map, kvp.Key, kvp.Value);
            }

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
                map[token.Replace(" ", string.Empty)] = month;
            }
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

        private static CultureInfo[] BuildSupportedCultures()
        {
            return SupportedCultureNames
                .Select(name =>
                {
                    try { return new CultureInfo(name); }
                    catch { return null; }
                })
                .Where(c => c != null)
                .ToArray();
        }

        private static string NormalizeMonthToken(string raw)
        {
            var token = (raw ?? string.Empty)
                .ToLowerInvariant()
                .Trim()
                .Trim('.', ',', ';', ':', '،');

            // Thai combining marks are integral to month abbreviations (e.g. "มี.ค.").
            // Stripping them merges distinct tokens and breaks parsing.
            if (token.Any(c => c >= '\u0E00' && c <= '\u0E7F'))
            {
                return token;
            }

            return RemoveDiacritics(token);
        }

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

        public static string GetSteamTimezoneOffsetCookieValue()
        {
            return "-28800,0";
        }
    }
}

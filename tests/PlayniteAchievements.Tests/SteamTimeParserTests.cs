using System;
using PlayniteAchievements.Providers.Steam;
using Xunit;

namespace PlayniteAchievements.Tests
{
    public class SteamTimeParserTests
    {
        private static readonly TimeZoneInfo SteamTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        [Fact]
        public void EnglishWithYearParsesToUtc()
        {
            var parsed = Parse("Unlocked Apr 15, 2011 @ 12:13am", "english");
            var expected = SteamLocalToUtc(2011, 4, 15, 0, 13);

            Assert.True(parsed.HasValue);
            Assert.Equal(expected, parsed.Value);
        }

        [Fact]
        public void SpanishWithNoiseParsesToUtc()
        {
            var parsed = Parse("Desbloqueado el 5 ene. 2024 a las 07:15", "spanish");
            var expected = SteamLocalToUtc(2024, 1, 5, 7, 15);

            Assert.True(parsed.HasValue);
            Assert.Equal(expected, parsed.Value);
        }

        [Fact]
        public void German24HourSeparatorParsesToUtc()
        {
            var parsed = Parse("Freigeschaltet am 5 Mai 2024 um 19h58", "german");
            var expected = SteamLocalToUtc(2024, 5, 5, 19, 58);

            Assert.True(parsed.HasValue);
            Assert.Equal(expected, parsed.Value);
        }

        [Fact]
        public void DiacriticInsensitiveFrenchMonthParsesToUtc()
        {
            var parsed = Parse("Déverrouillé le 15 aout 2024 à 21:07", "french");
            var expected = SteamLocalToUtc(2024, 8, 15, 21, 7);

            Assert.True(parsed.HasValue);
            Assert.Equal(expected, parsed.Value);
        }

        [Fact]
        public void NumericDateFallsBackToCultureParsing()
        {
            var parsed = Parse("Unlocked 2024-04-05 @ 22:11", "english");
            var expected = SteamLocalToUtc(2024, 4, 5, 22, 11);

            Assert.True(parsed.HasValue);
            Assert.Equal(expected, parsed.Value);
        }

        [Fact]
        public void YearlessDateRollsBackWhenFutureRelativeToSteamNow()
        {
            var steamNow = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Unspecified);
            var parsed = Parse("Unlocked Dec 31 @ 11:59pm", "english", steamNow);
            var expected = SteamLocalToUtc(2025, 12, 31, 23, 59);

            Assert.True(parsed.HasValue);
            Assert.Equal(expected, parsed.Value);
        }

        [Fact]
        public void InvalidInputReturnsNull()
        {
            var parsed = Parse("Unlocked recently", "english");
            Assert.Null(parsed);
        }

        [Fact]
        public void VietnameseCompactMonthTokensParseForAllMonths()
        {
            var steamNow = new DateTime(2025, 12, 31, 12, 0, 0, DateTimeKind.Unspecified);

            for (var month = 1; month <= 12; month++)
            {
                var paddedText = $"Mở khóa vào 14 Thg{month:00} @ 7:04am";
                var paddedParsed = Parse(paddedText, "vietnamese", steamNow);
                var expected = SteamLocalToUtc(2025, month, 14, 7, 4);

                Assert.True(paddedParsed.HasValue, $"Failed for token Thg{month:00}");
                Assert.Equal(expected, paddedParsed.Value);

                var compactText = $"Mở khóa vào 14 Thg{month} @ 7:04am";
                var compactParsed = Parse(compactText, "vietnamese", steamNow);

                Assert.True(compactParsed.HasValue, $"Failed for token Thg{month}");
                Assert.Equal(expected, compactParsed.Value);
            }
        }

        private static DateTime? Parse(string text, string language, DateTime? steamNow = null)
        {
            var now = steamNow ?? new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
            return SteamTimeParser.TryParseSteamUnlockTime(text, language, now);
        }

        private static DateTime SteamLocalToUtc(int year, int month, int day, int hour, int minute)
        {
            var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, SteamTimeZone);
        }
    }
}

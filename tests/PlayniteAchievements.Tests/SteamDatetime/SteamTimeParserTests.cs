using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.Steam;

namespace PlayniteAchievements.Tests
{
    [TestClass]
    public class SteamTimeParserTests
    {
        private static readonly DateTime SteamNow = new DateTime(2026, 2, 15, 12, 0, 0, DateTimeKind.Unspecified);

        public static IEnumerable<object[]> AllLanguageCases()
        {
            foreach (var c in Cases())
            {
                yield return new object[] { c.Language, c.Text, c.ExpectedUtc };
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(AllLanguageCases), DynamicDataSourceType.Method)]
        public void ScrapedLanguageDateConvertsToUtc(string language, string text, DateTime expectedUtc)
        {
            var parsed = SteamTimeParser.TryParseSteamUnlockTime(text, language, SteamNow);
            Assert.IsTrue(parsed.HasValue);
            Assert.AreEqual(expectedUtc, parsed.Value);
        }

        [TestMethod]
        public void YearlessDateRollsBackBeforeUtcConversion()
        {
            var steamNow = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Unspecified);
            var parsed = SteamTimeParser.TryParseSteamUnlockTime("Unlocked Dec 31 @ 11:59pm", "english", steamNow);

            Assert.IsTrue(parsed.HasValue);
            Assert.AreEqual(new DateTime(2026, 1, 1, 7, 59, 0, DateTimeKind.Utc), parsed.Value);
        }

        [TestMethod]
        public void InvalidUnlockStringReturnsNull()
        {
            var parsed = SteamTimeParser.TryParseSteamUnlockTime("Unlocked recently", "english", SteamNow);
            Assert.IsNull(parsed);
        }

        private static IEnumerable<SteamCase> Cases()
        {
            var pastUtc = new DateTime(2025, 4, 24, 12, 4, 0, DateTimeKind.Utc);
            var currentUtc = new DateTime(2026, 1, 30, 1, 3, 0, DateTimeKind.Utc);

            yield return new SteamCase("english", "Unlocked Apr 24, 2025 @ 5:04am", pastUtc);
            yield return new SteamCase("english", "Unlocked Jan 29 @ 5:03pm", currentUtc);
            yield return new SteamCase("english", "Unlocked 24 Apr, 2025 @ 5:04am", pastUtc);
            yield return new SteamCase("english", "Unlocked 29 Jan @ 5:03pm", currentUtc);

            yield return new SteamCase("german", "Am 24. Apr. 2025 um 5:04 freigeschaltet", pastUtc);
            yield return new SteamCase("german", "Am 29. Jan. um 17:03 freigeschaltet", currentUtc);
            yield return new SteamCase("german", "Am 17. Mai 2025 um 19:45 freigeschaltet", new DateTime(2025, 5, 18, 2, 45, 0, DateTimeKind.Utc));
            yield return new SteamCase("german", "Am 3. Mai um 20:10 freigeschaltet", new DateTime(2025, 5, 4, 3, 10, 0, DateTimeKind.Utc));

            yield return new SteamCase("french", "Débloqué le 24 avr. 2025 à 5h04", pastUtc);
            yield return new SteamCase("french", "Débloqué le 29 janv. à 17h03", currentUtc);
            yield return new SteamCase("french", "Débloqué le 1 mars 2025 à 8h57", new DateTime(2025, 3, 1, 16, 57, 0, DateTimeKind.Utc));
            yield return new SteamCase("french", "Débloqué le 7 mars à 17h45", new DateTime(2025, 3, 8, 1, 45, 0, DateTimeKind.Utc));

            yield return new SteamCase("spanish", "Se desbloqueó el 24 ABR 2025 a las 5:04", pastUtc);
            yield return new SteamCase("spanish", "Se desbloqueó el 29 ENE a las 17:03", currentUtc);

            yield return new SteamCase("italian", "Sbloccato in data 24 apr 2025, ore 5:04", pastUtc);
            yield return new SteamCase("italian", "Sbloccato in data 29 gen, ore 17:03", currentUtc);

            yield return new SteamCase("russian", "Дата получения: 24 апр. 2025 г. в 5:04", pastUtc);
            yield return new SteamCase("russian", "Дата получения: 29 янв в 17:03", currentUtc);

            yield return new SteamCase("japanese", "アンロックした日 2025年4月24日 5時04分", pastUtc);
            yield return new SteamCase("japanese", "アンロックした日 1月29日 17時03分", currentUtc);

            yield return new SteamCase("portuguese", "Desbloqueada a 24 abr. 2025 às 5:04", pastUtc);
            yield return new SteamCase("portuguese", "Desbloqueada a 29 jan. às 17:03", currentUtc);

            yield return new SteamCase("brazilian", "Alcançada em 24/abr./2025 às 5:04", pastUtc);
            yield return new SteamCase("brazilian", "Alcançada em 29 de jan. às 17:03", currentUtc);

            yield return new SteamCase("polish", "Odblokowano: 24 kwietnia 2025 o 5:04", pastUtc);
            yield return new SteamCase("polish", "Odblokowano: 29 stycznia o 17:03", currentUtc);

            yield return new SteamCase("dutch", "Ontgrendeld op 24 apr 2025 om 5:04", pastUtc);
            yield return new SteamCase("dutch", "Ontgrendeld op 29 jan om 17:03", currentUtc);

            yield return new SteamCase("swedish", "Upplåst 24 apr, 2025 @ 5:04", pastUtc);
            yield return new SteamCase("swedish", "Upplåst 29 jan @ 17:03", currentUtc);

            yield return new SteamCase("finnish", "Avattu 24.4.2025 klo 5.04", pastUtc);
            yield return new SteamCase("finnish", "Avattu 29.1. klo 17.03", currentUtc);

            yield return new SteamCase("danish", "Låst op: 24. apr. 2025 kl. 5:04", pastUtc);
            yield return new SteamCase("danish", "Låst op: 29. jan. kl. 17:03", currentUtc);
            yield return new SteamCase("danish", "Låst op: 19. juni 2025 kl. 19:22", new DateTime(2025, 6, 20, 2, 22, 0, DateTimeKind.Utc));
            yield return new SteamCase("danish", "Låst op: 29. januar kl. 17:03", currentUtc);

            yield return new SteamCase("norwegian", "Låst opp 24. apr. 2025 kl. 5.04", pastUtc);
            yield return new SteamCase("norwegian", "Låst opp 29. jan. kl. 17.03", currentUtc);
            yield return new SteamCase("norwegian", "Låst opp 15. juni 2013 kl. 4.47", new DateTime(2013, 6, 15, 11, 47, 0, DateTimeKind.Utc));
            yield return new SteamCase("norwegian", "Låst opp 29. januar kl. 17.03", currentUtc);

            yield return new SteamCase("hungarian", "Feloldva: 2025. ápr. 24., 5:04", pastUtc);
            yield return new SteamCase("hungarian", "Feloldva: jan. 29., 17:03", currentUtc);

            yield return new SteamCase("czech", "Odemčeno 24. dub. 2025 v 5.04", pastUtc);
            yield return new SteamCase("czech", "Odemčeno 29. led. v 17.03", currentUtc);

            yield return new SteamCase("romanian", "Obținută la 24 apr. 2025 la 5:04", pastUtc);
            yield return new SteamCase("romanian", "Obținută la 29 ian. la 17:03", currentUtc);
            yield return new SteamCase("romanian", "Obținută la 4 mai 2014 la 4:17", new DateTime(2014, 5, 4, 11, 17, 0, DateTimeKind.Utc));
            yield return new SteamCase("romanian", "Obținută la 29 ianuarie la 17:03", currentUtc);

            yield return new SteamCase("turkish", "Kazanma Tarihi 24 Nis 2025 @ 5:04", pastUtc);
            yield return new SteamCase("turkish", "Kazanma Tarihi 29 Oca @ 17:03", currentUtc);

            yield return new SteamCase("greek", "Ξεκλειδώθηκε στις 24 Απρ 2025, 5:04", pastUtc);
            yield return new SteamCase("greek", "Ξεκλειδώθηκε στις 29 Ιαν, 17:03", currentUtc);

            yield return new SteamCase("bulgarian", "Откл. на 24 апр. 2025 в 5:04", pastUtc);
            yield return new SteamCase("bulgarian", "Откл. на 29 ян. в 17:03", currentUtc);
            yield return new SteamCase("bulgarian", "Откл. на 17 февр. в 19:08", new DateTime(2025, 2, 18, 3, 8, 0, DateTimeKind.Utc));
            yield return new SteamCase("bulgarian", "Откл. на 11 март 2025 в 11:29", new DateTime(2025, 3, 11, 18, 29, 0, DateTimeKind.Utc));
            yield return new SteamCase("bulgarian", "Откл. на 15 юни 2013 в 4:47", new DateTime(2013, 6, 15, 11, 47, 0, DateTimeKind.Utc));
            yield return new SteamCase("bulgarian", "Откл. на 4 май в 4:17", new DateTime(2025, 5, 4, 11, 17, 0, DateTimeKind.Utc));

            yield return new SteamCase("ukrainian", "Здобуто 24 квіт. 2025 о 5:04", pastUtc);
            yield return new SteamCase("ukrainian", "Здобуто 29 січ. о 17:03", currentUtc);
            yield return new SteamCase("ukrainian", "Здобуто 2 верес. 2025 о 19:24", new DateTime(2025, 9, 3, 2, 24, 0, DateTimeKind.Utc));
            yield return new SteamCase("ukrainian", "Здобуто 25 листоп. 2024 о 6:19", new DateTime(2024, 11, 25, 14, 19, 0, DateTimeKind.Utc));
            yield return new SteamCase("ukrainian", "Здобуто 11 берез. 2025 о 11:29", new DateTime(2025, 3, 11, 18, 29, 0, DateTimeKind.Utc));

            yield return new SteamCase("thai", "ปลดล็อก 24 เม.ย. 2025 @ 5: 04am", pastUtc);
            yield return new SteamCase("thai", "ปลดล็อก 29 ม.ค. @ 5: 03pm", currentUtc);
            yield return new SteamCase("thai", "ปลดล็อก 19 มิ.ย. 2025 @ 7: 22pm", new DateTime(2025, 6, 20, 2, 22, 0, DateTimeKind.Utc));
            yield return new SteamCase("thai", "ปลดล็อก 11 มี.ค. 2025 @ 11: 29am", new DateTime(2025, 3, 11, 18, 29, 0, DateTimeKind.Utc));

            yield return new SteamCase("vietnamese", "Mở khóa vào 24 Thg04, 2025 @ 5:04am", pastUtc);
            yield return new SteamCase("vietnamese", "Mở khóa vào 29 Thg01 @ 5:03pm", currentUtc);

            yield return new SteamCase("koreana", "2025년 4월 24일 오전 5시 04분에 획득", pastUtc);
            yield return new SteamCase("koreana", "2026년 1월 29일 오후 5시 03분에 획득", currentUtc);

            yield return new SteamCase("schinese", "2025 年 4 月 24 日 上午 5:04 解锁", pastUtc);
            yield return new SteamCase("schinese", "1 月 29 日 下午 5:03 解锁", currentUtc);

            yield return new SteamCase("tchinese", "解鎖於 2025 年 4 月 24 日 上午 5:04", pastUtc);
            yield return new SteamCase("tchinese", "解鎖於 1 月 29 日 下午 5:03", currentUtc);

            yield return new SteamCase("arabic", "Unlocked 24 أبريل, 2025 @ 5:04صباحًا", pastUtc);
            yield return new SteamCase("arabic", "Unlocked 29 يناير @ 5:03مساءً", currentUtc);
            yield return new SteamCase("arabic", "Unlocked 2 سبتمبر, 2025 @ 7:24مساءً", new DateTime(2025, 9, 3, 2, 24, 0, DateTimeKind.Utc));
            yield return new SteamCase("arabic", "Unlocked 19 ديسمبر, 2015 @ 7:01مساءً", new DateTime(2015, 12, 20, 3, 1, 0, DateTimeKind.Utc));
            yield return new SteamCase("arabic", "Unlocked 11 مارس, 2025 @ 11:29صباحًا", new DateTime(2025, 3, 11, 18, 29, 0, DateTimeKind.Utc));
        }

        private sealed class SteamCase
        {
            public SteamCase(string language, string text, DateTime expectedUtc)
            {
                Language = language;
                Text = text;
                ExpectedUtc = expectedUtc;
            }

            public string Language { get; }
            public string Text { get; }
            public DateTime ExpectedUtc { get; }
        }
    }
}

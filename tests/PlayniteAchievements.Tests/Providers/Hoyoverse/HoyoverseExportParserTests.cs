using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Hoyoverse;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Tests.Providers.Hoyoverse
{
    [TestClass]
    public class HoyoverseExportParserTests
    {
        [TestMethod]
        public void ReadUnlockedIds_ParsesPaimonMoeGenshinExport()
        {
            var path = WriteTempFile(".json", @"{""achievement"":{""wonders"":{""1001"":true,""1002"":false}}}");

            var ids = HoyoverseExportParser.ReadUnlockedIds(
                HoyoverseGameKind.GenshinImpact,
                path,
                new List<AchievementDetail>(),
                null);

            CollectionAssert.AreEquivalent(new[] { "1001" }, ids.ToList());
        }

        [TestMethod]
        public void ReadUnlockedIds_ParsesSeelieSharedExport()
        {
            var path = WriteTempFile(".json", @"{""achievements"":{""2001"":{""done"":true},""2002"":{""done"":false}}}");

            var ids = HoyoverseExportParser.ReadUnlockedIds(
                HoyoverseGameKind.HonkaiStarRail,
                path,
                new List<AchievementDetail>(),
                null);

            CollectionAssert.AreEquivalent(new[] { "2001" }, ids.ToList());
        }

        [TestMethod]
        public void ReadUnlockedIds_ParsesStarDbSharedExport()
        {
            var path = WriteTempFile(".json", @"{""user"":{""gi"":{""achievements"":[3001]},""hsr"":{""achievements"":[3002]},""zzz"":{""achievements"":[3003]}}}");

            var ids = HoyoverseExportParser.ReadUnlockedIds(
                HoyoverseGameKind.ZenlessZoneZero,
                path,
                new List<AchievementDetail>(),
                null);

            CollectionAssert.AreEquivalent(new[] { "3003" }, ids.ToList());
        }

        [TestMethod]
        public void ReadUnlockedIds_ParsesStarRailStationDatExport()
        {
            var payload = @"{""data"":{""stores"":{""1_achieve"":{""completeState"":{""group"":{""705481"":true,""705482"":false}}}}}}";
            var path = WriteTempFile(".dat", "srs" + HoyoverseLzStringUtf16Codec.CompressToUtf16(payload));
            var definitions = new List<AchievementDetail>
            {
                new AchievementDetail { ApiName = "official-guess", DisplayName = "Guess Who I Am" }
            };

            var ids = HoyoverseExportParser.ReadUnlockedIds(
                HoyoverseGameKind.HonkaiStarRail,
                path,
                definitions,
                null);

            CollectionAssert.AreEquivalent(new[] { "official-guess" }, ids.ToList());
        }

        [TestMethod]
        public void ReadUnlockedIds_ParsesRngMoeZzzExport()
        {
            var path = WriteTempFile(".json", @"{""data"":{""profiles"":{""default"":{""stores"":{""2"":{""enabled"":{""4001"":true,""4002"":false}}}}}}}");

            var ids = HoyoverseExportParser.ReadUnlockedIds(
                HoyoverseGameKind.ZenlessZoneZero,
                path,
                new List<AchievementDetail>(),
                null);

            CollectionAssert.AreEquivalent(new[] { "4001" }, ids.ToList());
        }

        private static string WriteTempFile(string extension, string content)
        {
            var directory = Path.Combine(Path.GetTempPath(), "PlayAch_HoyoverseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "export" + extension);
            File.WriteAllText(path, content);
            return path;
        }
    }
}

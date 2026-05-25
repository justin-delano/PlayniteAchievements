using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Hoyoverse;
using System.Linq;

namespace PlayniteAchievements.Tests.Providers.Hoyoverse
{
    [TestClass]
    public class HoyoverseDefinitionClientTests
    {
        [TestMethod]
        public void ParseGenshinDefinitions_FlattensPaimonMoePayload()
        {
            var json = @"{
              ""wonders"": {
                ""name"": ""Wonders of the World"",
                ""achievements"": {
                  ""1001"": { ""id"": 1001, ""name"": ""Open World"", ""desc"": ""Find the thing."", ""reward"": 5 },
                  ""1002"": { ""id"": 1002, ""name"": ""Hidden Prize"", ""desc"": ""Find the other thing."", ""reward"": 20 }
                }
              }
            }";

            var definitions = HoyoverseDefinitionClient.ParseGenshinDefinitions(json);

            Assert.AreEqual(2, definitions.Count);
            Assert.AreEqual("1001", definitions[0].ApiName);
            Assert.AreEqual("Wonders of the World", definitions[0].Category);
            Assert.AreEqual(RarityTier.Rare, definitions.Single(a => a.ApiName == "1002").Rarity);
        }

        [TestMethod]
        public void ParseHonkaiStarRailDefinitions_ResolvesTextMapAndSeries()
        {
            var achievements = @"[
              {
                ""AchievementID"": 2001,
                ""SeriesID"": 7,
                ""AchievementTitle"": { ""Hash"": ""10001"" },
                ""AchievementDesc"": { ""Hash"": ""10002"" },
                ""ParamList"": [{ ""Value"": ""3"" }],
                ""Rarity"": ""Mid""
              }
            ]";
            var series = @"[{ ""SeriesID"": 7, ""SeriesTitle"": { ""Hash"": ""20001"" } }]";
            var textMap = @"{
              ""10001"": ""Trailblazer"",
              ""10002"": ""Complete #1[i] missions."",
              ""20001"": ""The Rail Unto the Stars""
            }";

            var definitions = HoyoverseDefinitionClient.ParseHonkaiStarRailDefinitions(achievements, series, textMap);

            Assert.AreEqual(1, definitions.Count);
            Assert.AreEqual("2001", definitions[0].ApiName);
            Assert.AreEqual("Trailblazer", definitions[0].DisplayName);
            Assert.AreEqual("Complete 3 missions.", definitions[0].Description);
            Assert.AreEqual("The Rail Unto the Stars", definitions[0].Category);
            Assert.AreEqual(RarityTier.Uncommon, definitions[0].Rarity);
        }

        [TestMethod]
        public void ParseZenlessZoneZeroDefinitions_NormalizesAssetPayload()
        {
            var js = @"const achievements={100:{n:'Primer',a:[{id:3001,n:'Coffee Time',d:'Drink coffee.',r:5},{id:3002,n:'Rare Find',d:'Find it.',r:20}]}};export{achievements as default};";

            var definitions = HoyoverseDefinitionClient.ParseZenlessZoneZeroDefinitions(js);

            Assert.AreEqual(2, definitions.Count);
            Assert.AreEqual("3001", definitions[0].ApiName);
            Assert.AreEqual("Primer", definitions[0].Category);
            Assert.AreEqual(RarityTier.Rare, definitions.Single(a => a.ApiName == "3002").Rarity);
        }

        [TestMethod]
        public void FindZzzAchievementAsset_SupportsRelativeAndAbsoluteLocaleChunks()
        {
            var relativeIndex = @"const a=()=>import(""./locale/achievements-en-fa79791d.js"");";
            var absoluteIndex = @"const a=""/assets/locale/achievements-en-12345678.js"";";

            Assert.AreEqual(
                "/assets/locale/achievements-en-fa79791d.js",
                HoyoverseDefinitionClient.FindZzzAchievementAsset(relativeIndex, "en"));
            Assert.AreEqual(
                "/assets/locale/achievements-en-12345678.js",
                HoyoverseDefinitionClient.FindZzzAchievementAsset(absoluteIndex, "en"));
        }
    }
}

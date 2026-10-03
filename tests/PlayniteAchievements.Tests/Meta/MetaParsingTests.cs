using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Providers.Meta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Meta.Tests
{
    [TestClass]
    public class MetaParsingTests
    {
        // Shape of a live doc_id 7560363024007475 response, with made-up ids and titles.
        private const string FeedJson = @"{
  ""data"": { ""user"": { ""__typename"": ""User"", ""id"": ""100"", ""profile_info"": { ""modules"": [
    { ""__typename"": ""ProfileAchievementsModule"", ""achievements"": {
      ""edges"": [
        { ""node"": { ""id"": ""9001"", ""unlock_date_description"": ""Unlocked on Dec 25, 2020"", ""is_unlocked"": true,
            ""definition"": { ""id"": ""501"", ""title"": ""First"", ""rarity"": null,
              ""unlock_count_description_short"": ""2.3M players unlocked"",
              ""application_grouping"": { ""group_applications"": { ""nodes"": [ { ""id"": ""42"", ""display_name"": ""Game"" } ] } } } },
          ""cursor"": ""c1"" },
        { ""node"": { ""id"": ""9002"", ""unlock_date_description"": ""Unlocked on Mar 7, 2020"", ""is_unlocked"": true,
            ""definition"": { ""id"": ""502"", ""title"": ""Second"", ""unlock_count_description_short"": ""86.4K players unlocked"" } },
          ""cursor"": ""c2"" }
      ],
      ""count"": 2,
      ""page_info"": { ""end_cursor"": ""c2"", ""has_next_page"": true } } } ] } } },
  ""extensions"": { ""is_final"": true } }";

        [TestMethod]
        public void ParseUnlockDate_ReadsEnglishDate_AsLocalMidnight()
        {
            var parsed = MetaParsing.ParseUnlockDate("Unlocked on Dec 25, 2020");

            Assert.IsTrue(parsed.HasValue);
            Assert.AreEqual(DateTimeKind.Utc, parsed.Value.Kind);
            Assert.AreEqual(new DateTime(2020, 12, 25), parsed.Value.ToLocalTime().Date);
            Assert.AreEqual(TimeSpan.Zero, parsed.Value.ToLocalTime().TimeOfDay);
        }

        [TestMethod]
        public void ParseUnlockDate_IgnoresLocalizedPrefix()
        {
            // With a locale parameter only the prefix is translated; the date stays English.
            var parsed = MetaParsing.ParseUnlockDate("Freigeschaltet am Mar 7, 2020");

            Assert.AreEqual(new DateTime(2020, 3, 7), parsed?.ToLocalTime().Date);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("Unlocked")]
        [DataRow("Unlocked on Foo 40, 2020")]
        public void ParseUnlockDate_ReturnsNull_WhenNoDate(string text)
        {
            Assert.IsNull(MetaParsing.ParseUnlockDate(text));
        }

        [DataTestMethod]
        [DataRow("2.3M players unlocked", 2_300_000L)]
        [DataRow("86.4K players unlocked", 86_400L)]
        [DataRow("10.1M players unlocked", 10_100_000L)]
        [DataRow("512 players unlocked", 512L)]
        [DataRow("1 player unlocked", 1L)]
        public void ParseUnlockCount_ExpandsSuffixes(string text, long expected)
        {
            Assert.AreEqual(expected, MetaParsing.ParseUnlockCount(text));
        }

        [TestMethod]
        public void ParseUnlockCount_ReturnsNull_WhenAbsent()
        {
            Assert.IsNull(MetaParsing.ParseUnlockCount(null));
            Assert.IsNull(MetaParsing.ParseUnlockCount("players unlocked"));
        }

        [TestMethod]
        public void ReadUnlocks_ReadsFeedPage_AndPageInfo()
        {
            var response = JsonConvert.DeserializeObject<MetaFeedResponse>(FeedJson);
            var connection = MetaParsing.GetAchievementsConnection(response);

            Assert.IsNotNull(connection);
            Assert.IsTrue(connection.PageInfo.HasNextPage);
            Assert.AreEqual("c2", connection.PageInfo.EndCursor);

            var unlocks = MetaParsing.ReadUnlocks(connection).ToList();
            CollectionAssert.AreEqual(new[] { "501", "502" }, unlocks.Select(u => u.DefinitionId).ToList());
            Assert.AreEqual(2_300_000L, unlocks[0].GlobalUnlockCount);
            Assert.AreEqual(new DateTime(2020, 12, 25), unlocks[0].UnlockDateUtc?.ToLocalTime().Date);
        }

        [TestMethod]
        public void GetAchievementsConnection_ReturnsNull_ForNullUser()
        {
            var response = JsonConvert.DeserializeObject<MetaFeedResponse>(@"{""data"":{""user"":null}}");

            Assert.IsNull(MetaParsing.GetAchievementsConnection(response));
        }

        [TestMethod]
        public void ReadUnlocks_SkipsLockedAndIdlessEntries()
        {
            var connection = new MetaFeedConnection
            {
                Edges = new List<MetaFeedEdge>
                {
                    new MetaFeedEdge { Node = new MetaFeedNode { IsUnlocked = false, Definition = new MetaFeedDefinition { Id = "1" } } },
                    new MetaFeedEdge { Node = new MetaFeedNode { IsUnlocked = true, Definition = new MetaFeedDefinition { Id = " " } } },
                    new MetaFeedEdge { Node = null },
                    new MetaFeedEdge { Node = new MetaFeedNode { IsUnlocked = true, Definition = new MetaFeedDefinition { Id = "2" } } }
                }
            };

            var unlocks = MetaParsing.ReadUnlocks(connection).ToList();

            Assert.AreEqual(1, unlocks.Count);
            Assert.AreEqual("2", unlocks[0].DefinitionId);
        }

        [TestMethod]
        public void MapAchievements_JoinsDefinitionsWithUnlocksById()
        {
            var definitions = new List<MetaDefinition>
            {
                new MetaDefinition
                {
                    Id = "501", ApiName = "FirstApi", Title = "First", Description = "Do the first thing.",
                    UnlockedImageUri = "https://cdn/u.webp", LockedImageUri = "https://cdn/l.webp"
                },
                new MetaDefinition { Id = "503", ApiName = "SecretApi", Title = "Secret", IsSecret = true }
            };
            var unlockDay = MetaParsing.ParseUnlockDate("Unlocked on Dec 25, 2020");
            var unlocks = MetaParsing.IndexUnlocks(new[]
            {
                new MetaUnlock { DefinitionId = "501", UnlockDateUtc = unlockDay },
                new MetaUnlock { DefinitionId = "999" }
            });

            var achievements = MetaParsing.MapAchievements(definitions, unlocks);

            Assert.AreEqual(2, achievements.Count);

            var first = achievements[0];
            Assert.AreEqual("FirstApi", first.ApiName);
            Assert.AreEqual("First", first.DisplayName);
            Assert.AreEqual("Do the first thing.", first.Description);
            Assert.AreEqual("https://cdn/u.webp", first.UnlockedIconPath);
            Assert.AreEqual("https://cdn/l.webp", first.LockedIconPath);
            Assert.IsTrue(first.Unlocked);
            Assert.AreEqual(unlockDay, first.UnlockTimeUtc);
            Assert.IsNull(first.GlobalPercentUnlocked);

            var secret = achievements[1];
            Assert.IsFalse(secret.Unlocked);
            Assert.IsTrue(secret.Hidden);
            Assert.IsNull(secret.UnlockTimeUtc);
        }

        [TestMethod]
        public void MapAchievements_KeepsUnlockedState_WhenDateIsUnparsed()
        {
            var definitions = new List<MetaDefinition> { new MetaDefinition { Id = "501", Title = "First" } };
            var unlocks = MetaParsing.IndexUnlocks(new[] { new MetaUnlock { DefinitionId = "501", UnlockDateUtc = null } });

            var achievement = MetaParsing.MapAchievements(definitions, unlocks).Single();

            Assert.IsTrue(achievement.Unlocked);
            Assert.IsNull(achievement.UnlockTimeUtc);
            Assert.AreEqual("501", achievement.ApiName, "Falls back to the definition id when api_name is missing.");
        }

        [TestMethod]
        public void IndexUnlocks_KeepsFirstEntryPerDefinition()
        {
            var newer = new MetaUnlock { DefinitionId = "501", GlobalUnlockCount = 2 };
            var older = new MetaUnlock { DefinitionId = "501", GlobalUnlockCount = 1 };

            var index = MetaParsing.IndexUnlocks(new[] { newer, older });

            Assert.AreSame(newer, index["501"]);
        }
    }
}

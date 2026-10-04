using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.Ubisoft;
using System;
using System.Linq;

namespace PlayniteAchievements.Ubisoft.Tests
{
    [TestClass]
    public class UbisoftParsingTests
    {
        // Shape of connect.ubisoft.com's PRODloginData, with made-up values.
        private const string StoredLoginJson = @"{
  ""platformType"": ""uplay"", ""ticket"": ""ew0KICAidmVyIjogIjEiDQp9.payload.sig"", ""twoFactorAuthenticationTicket"": null,
  ""profileId"": ""11111111-2222-3333-4444-555555555555"", ""userId"": ""11111111-2222-3333-4444-555555555555"",
  ""nameOnPlatform"": ""Player"", ""environment"": ""Prod"", ""expiration"": ""2026-10-05T01:23:30.2489405Z"",
  ""spaceId"": ""0d2ae42d-4c27-4cb7-af6c-2099062302bb"", ""sessionId"": ""aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"",
  ""rememberMeTicket"": null, ""email"": ""player@example.com"", ""expirationDate"": 1791163229796 }";

        // Shape of a live GetAchievements response, with made-up titles and ids.
        private const string AchievementsJson = @"{ ""data"": { ""game"": {
  ""id"": ""aa6b1778-f274-4b52-8fce-f642173aa0e1"", ""name"": ""Game"",
  ""viewer"": { ""meta"": { ""id"": ""user-space"", ""achievements"": { ""totalCount"": 3, ""completedCount"": 1, ""nodes"": [
    { ""id"": ""7021-1"", ""achievementId"": 1, ""title"": ""First"", ""description"": ""Do the first thing"",
      ""icon"": ""https://ubiservices.cdn.ubi.com/space/achievement/hash_1.png"",
      ""viewer"": { ""meta"": { ""id"": ""m1"", ""completionDate"": ""2025-07-01T21:27:19"", ""isCompleted"": true } } },
    { ""id"": ""7021-2"", ""achievementId"": 2, ""title"": ""Second"", ""description"": ""Do the second thing"",
      ""icon"": ""https://ubiservices.cdn.ubi.com/space/achievement/hash_2.png"",
      ""viewer"": { ""meta"": { ""id"": ""m2"", ""completionDate"": null, ""isCompleted"": false } } },
    { ""id"": ""7021-41"", ""achievementId"": 41, ""title"": ""Last"", ""description"": """", ""icon"": """",
      ""viewer"": { ""meta"": { ""id"": ""m3"", ""completionDate"": null, ""isCompleted"": false } } }
  ] } } } } } }";

        [TestMethod]
        public void ParseSession_ReadsStoredLoginData()
        {
            var session = UbisoftParsing.ParseSession(StoredLoginJson);

            Assert.IsNotNull(session);
            Assert.AreEqual("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", session.SessionId);
            Assert.AreEqual("11111111-2222-3333-4444-555555555555", session.UserId);
            Assert.AreEqual("Player", session.NameOnPlatform);
            Assert.AreEqual(new DateTime(2026, 10, 5, 1, 23, 30, DateTimeKind.Utc).AddTicks(2489405), session.ExpiresUtc);
            Assert.AreEqual(DateTimeKind.Utc, session.ExpiresUtc.Kind);
        }

        [TestMethod]
        public void ParseSession_FallsBackToProfileId_WhenUserIdMissing()
        {
            var session = UbisoftParsing.ParseSession(
                @"{ ""ticket"": ""t"", ""sessionId"": ""s"", ""profileId"": ""p"", ""expiration"": ""2026-10-05T01:00:00Z"" }");

            Assert.AreEqual("p", session?.UserId);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("not json")]
        [DataRow(@"{ ""ticket"": ""t"", ""userId"": ""u"", ""expiration"": ""2026-10-05T01:00:00Z"" }")]
        [DataRow(@"{ ""ticket"": ""t"", ""sessionId"": ""s"", ""userId"": ""u"" }")]
        public void ParseSession_ReturnsNull_ForIncompleteRecords(string json)
        {
            Assert.IsNull(UbisoftParsing.ParseSession(json));
        }

        [TestMethod]
        public void IsUsable_RequiresTheMargin()
        {
            var now = new DateTime(2026, 10, 4, 22, 0, 0, DateTimeKind.Utc);
            var session = new UbisoftSession("t", "s", "u", null, now.AddMinutes(3));

            Assert.IsTrue(session.IsUsable(now, TimeSpan.FromMinutes(2)));
            Assert.IsFalse(session.IsUsable(now, TimeSpan.FromMinutes(5)));
        }

        [TestMethod]
        public void ParseUtc_ReadsOffsetlessCompletionDateAsUtc()
        {
            var parsed = UbisoftParsing.ParseUtc("2025-07-01T21:27:19");

            Assert.AreEqual(new DateTime(2025, 7, 1, 21, 27, 19, DateTimeKind.Utc), parsed);
            Assert.AreEqual(DateTimeKind.Utc, parsed.Value.Kind);
        }

        [TestMethod]
        public void IndexGameSpaces_KeepsOnlyGamesWithSpaceIds()
        {
            var response = UbisoftParsing.Deserialize<UbisoftEntitlementsResponse>(@"{ ""entitlements"": [
  { ""productId"": 64410, ""type"": ""package"", ""spaceId"": """" },
  { ""productId"": 7021, ""type"": ""game"", ""spaceId"": ""aa6b1778-f274-4b52-8fce-f642173aa0e1"" },
  { ""productId"": 6160, ""type"": ""addon"", ""spaceId"": ""should-not-appear"" },
  { ""productId"": 7022, ""type"": ""game"", ""spaceId"": """" },
  { ""productId"": 7021, ""type"": ""game"", ""spaceId"": ""second-entry"" }
] }");

            var index = UbisoftParsing.IndexGameSpaces(response.Entitlements);

            Assert.AreEqual(1, index.Count);
            Assert.AreEqual("aa6b1778-f274-4b52-8fce-f642173aa0e1", index[7021]);
        }

        [DataTestMethod]
        [DataRow("7021", 7021L)]
        [DataRow(" 437 ", 437L)]
        public void ParseProductId_ReadsLauncherIds(string value, long expected)
        {
            Assert.AreEqual(expected, UbisoftParsing.ParseProductId(value));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("-5")]
        [DataRow("aa6b1778-f274-4b52-8fce-f642173aa0e1")]
        public void ParseProductId_RejectsNonIds(string value)
        {
            Assert.IsNull(UbisoftParsing.ParseProductId(value));
        }

        [TestMethod]
        public void MapAchievements_MapsUnlocksIconsAndIds()
        {
            var response = UbisoftParsing.Deserialize<UbisoftGraphResponse>(AchievementsJson);

            var achievements = UbisoftParsing.MapAchievements(response.Data.Game.Viewer.Meta.Achievements);

            Assert.AreEqual(3, achievements.Count);

            var first = achievements[0];
            Assert.AreEqual("1", first.ApiName);
            Assert.AreEqual("First", first.DisplayName);
            Assert.AreEqual("Do the first thing", first.Description);
            Assert.AreEqual("https://ubiservices.cdn.ubi.com/space/achievement/hash_1.png", first.UnlockedIconPath);
            Assert.IsTrue(first.Unlocked);
            Assert.AreEqual(new DateTime(2025, 7, 1, 21, 27, 19, DateTimeKind.Utc), first.UnlockTimeUtc);

            Assert.IsFalse(achievements[1].Unlocked);
            Assert.IsNull(achievements[1].UnlockTimeUtc);

            var last = achievements.Single(a => a.ApiName == "41");
            Assert.IsNull(last.Description);
            Assert.IsNull(last.UnlockedIconPath);
        }

        [TestMethod]
        public void MapAchievements_ReturnsEmpty_ForMissingConnection()
        {
            Assert.AreEqual(0, UbisoftParsing.MapAchievements(null).Count);
        }
    }
}

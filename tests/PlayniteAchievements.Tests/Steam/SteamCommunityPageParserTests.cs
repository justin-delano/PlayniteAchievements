using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PlayniteAchievements.Providers.Steam;
using System.Linq;

namespace PlayniteAchievements.Steam.Tests
{
    [TestClass]
    public class SteamCommunityPageParserTests
    {
        [TestMethod]
        public void ParseFriends_MapsSteamIdNameAndAvatar()
        {
            const string html =
                "<div id=\"friends_list\">" +
                "<a class=\"selectable friend_block_v2 persona online\" data-steamid=\"76561198000000001\" data-search=\"Fallback Name\" href=\"https://steamcommunity.com/profiles/76561198000000001/\">" +
                "<div class=\"player_avatar\"><img src=\"https://avatars.example/avatar.jpg\" /></div>" +
                "<div class=\"friend_block_content\">Display Name<br><span class=\"friend_small_text\">Online</span></div>" +
                "</a></div>";

            var friends = SteamCommunityPageParser.ParseFriends(html);

            Assert.AreEqual(1, friends.Count);
            Assert.AreEqual("76561198000000001", friends[0].SteamId);
            Assert.AreEqual("Display Name", friends[0].DisplayName);
            Assert.AreEqual("https://avatars.example/avatar.jpg", friends[0].AvatarUrl);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeFriendsPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsXmlPayload()
        {
            const string xml =
                "<gamesList><games>" +
                "<game><appID>440</appID><hoursOnRecord>1.5</hoursOnRecord><hoursLast2Weeks>0.5</hoursLast2Weeks></game>" +
                "</games></gamesList>";

            var games = SteamCommunityPageParser.ParseOwnedGames(xml);

            Assert.AreEqual(1, games.Count);
            Assert.AreEqual(440, games[0].AppId);
            Assert.AreEqual(90, games[0].PlaytimeForever);
            Assert.AreEqual(30, games[0].Playtime2Weeks);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(xml));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsEscapedXmlViewerPayload()
        {
            const string html =
                "<html><body><div id=\"webkit-xml-viewer-source-xml\">" +
                "&lt;gamesList&gt;&lt;games&gt;" +
                "&lt;game&gt;&lt;appID&gt;440&lt;/appID&gt;&lt;hoursOnRecord&gt;2.0&lt;/hoursOnRecord&gt;&lt;/game&gt;" +
                "&lt;/games&gt;&lt;/gamesList&gt;" +
                "</div></body></html>";

            var game = SteamCommunityPageParser.ParseOwnedGames(html).Single();

            Assert.AreEqual(440, game.AppId);
            Assert.AreEqual(120, game.PlaytimeForever);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsMostPlayedProfileXmlPayload()
        {
            const string xml =
                "<profile><mostPlayedGames><mostPlayedGame>" +
                "<gameLink><![CDATA[https://steamcommunity.com/app/730]]></gameLink>" +
                "<hoursPlayed>3.0</hoursPlayed><hoursOnRecord>3,222</hoursOnRecord>" +
                "</mostPlayedGame></mostPlayedGames></profile>";

            var game = SteamCommunityPageParser.ParseOwnedGames(xml).Single();

            Assert.AreEqual(730, game.AppId);
            Assert.AreEqual(193320, game.PlaytimeForever);
            Assert.AreEqual(180, game.Playtime2Weeks);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(xml));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsRgGamesPayload()
        {
            const string html =
                "<script>var rgGames = [{\"appid\":570,\"playtime_forever\":120,\"playtime_2weeks\":15,\"rtime_last_played\":1710000000}];</script>";

            var game = SteamCommunityPageParser.ParseOwnedGames(html).Single();

            Assert.AreEqual(570, game.AppId);
            Assert.AreEqual(120, game.PlaytimeForever);
            Assert.AreEqual(15, game.Playtime2Weeks);
            Assert.AreEqual(1710000000L, game.LastPlayedUnixSeconds);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsSteamSsrOwnedGamesQuery()
        {
            const string queryData =
                "{\"mutations\":[],\"queries\":[{\"queryKey\":[\"OwnedGames\",\"76561198087595485\",\"english\"]," +
                "\"state\":{\"data\":[{\"appid\":730,\"name\":\"Counter-Strike 2\",\"playtime_forever\":156469}," +
                "{\"appid\":550,\"name\":\"Left 4 Dead 2\",\"playtime_forever\":281,\"playtime_2weeks\":12,\"rtime_last_played\":1710000000}]}}]}";
            var renderContext = "{\"queryData\":" + JsonConvert.SerializeObject(queryData) + "}";
            var html = "<script>window.SSR.renderContext=JSON.parse(" +
                       JsonConvert.SerializeObject(renderContext) +
                       ");</script>";

            var games = SteamCommunityPageParser.ParseOwnedGames(html)
                .OrderBy(game => game.AppId)
                .ToList();

            Assert.AreEqual(2, games.Count);
            Assert.AreEqual(550, games[0].AppId);
            Assert.AreEqual(281, games[0].PlaytimeForever);
            Assert.AreEqual(12, games[0].Playtime2Weeks);
            Assert.AreEqual(1710000000L, games[0].LastPlayedUnixSeconds);
            Assert.AreEqual(730, games[1].AppId);
            Assert.AreEqual(156469, games[1].PlaytimeForever);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsSteamSsrLoaderDataRecentlyPlayedFallback()
        {
            const string listData =
                "{\"listData\":{\"rgRecentlyPlayedGames\":[{\"appid\":2862420,\"name\":\"Tyr Playtest\"," +
                "\"playtime_forever\":111,\"playtime_2weeks\":111}]}}";
            var loaderData = JsonConvert.SerializeObject(new[] { "{}", listData });
            var html = "<script>window.SSR={};window.SSR.loaderData = " + loaderData + ";" +
                       "window.SSR.renderContext=JSON.parse(\"{}\");</script>";

            var game = SteamCommunityPageParser.ParseOwnedGames(html).Single();

            Assert.AreEqual(2862420, game.AppId);
            Assert.AreEqual(111, game.PlaytimeForever);
            Assert.AreEqual(111, game.Playtime2Weeks);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MergesPartialSsrWithRenderedRows()
        {
            const string listData =
                "{\"listData\":{\"rgRecentlyPlayedGames\":[{\"appid\":2862420,\"name\":\"Tyr Playtest\"," +
                "\"playtime_forever\":111,\"playtime_2weeks\":111}]}}";
            var loaderData = JsonConvert.SerializeObject(new[] { "{}", listData });
            var html = "<script>window.SSR={};window.SSR.loaderData = " + loaderData + ";</script>" +
                       "<div class=\"Panel\">" +
                       "<a href=\"https://store.steampowered.com/app/730\">Counter-Strike 2</a>" +
                       "<span>TOTAL PLAYED</span><span>2,607.8 hours</span>" +
                       "<a href=\"https://steamcommunity.com/id/test/stats/730/?tab=achievements\">ACHIEVEMENTS</a>" +
                       "</div>" +
                       "<div class=\"Panel\">" +
                       "<a href=\"https://store.steampowered.com/app/550\">Left 4 Dead 2</a>" +
                       "<span>TOTAL PLAYED</span><span>5 hours</span>" +
                       "</div>";

            var games = SteamCommunityPageParser.ParseOwnedGames(html)
                .OrderBy(game => game.AppId)
                .ToList();

            Assert.AreEqual(3, games.Count);
            Assert.AreEqual(550, games[0].AppId);
            Assert.AreEqual(300, games[0].PlaytimeForever);
            Assert.AreEqual(730, games[1].AppId);
            Assert.AreEqual(156468, games[1].PlaytimeForever);
            Assert.AreEqual(2862420, games[2].AppId);
            Assert.AreEqual(111, games[2].PlaytimeForever);
            Assert.AreEqual(111, games[2].Playtime2Weeks);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsModernHtmlRows()
        {
            const string html =
                "<div class=\"gameslistitems_GameListItem_abc\">" +
                "<a href=\"https://steamcommunity.com/app/72850\">The Elder Scrolls V: Skyrim</a>" +
                "<div>TOTAL PLAYED</div><div>1,549 hours</div>" +
                "<div>ACHIEVEMENTS</div><div>32/75</div>" +
                "</div>" +
                "<div class=\"gameslistitems_GameListItem_def\">" +
                "<a href=\"https://steamcommunity.com/app/292030/\">The Witcher 3: Wild Hunt</a>" +
                "<div>TOTAL PLAYED</div><div>476 hours</div>" +
                "</div>";

            var games = SteamCommunityPageParser.ParseOwnedGames(html)
                .OrderBy(game => game.AppId)
                .ToList();

            Assert.AreEqual(2, games.Count);
            Assert.AreEqual(72850, games[0].AppId);
            Assert.AreEqual(92940, games[0].PlaytimeForever);
            Assert.AreEqual(292030, games[1].AppId);
            Assert.AreEqual(28560, games[1].PlaytimeForever);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }

        [TestMethod]
        public void ParseOwnedGames_MapsModernHtmlRowsWithImageAppUrls()
        {
            const string html =
                "<div class=\"mtoll770TDI- Panel\" role=\"button\">" +
                "<img src=\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/72850/header.jpg\" alt=\"Skyrim\" />" +
                "<span>The Elder Scrolls V: Skyrim</span>" +
                "<span>TOTAL PLAYED</span><span>1,549 hours</span>" +
                "</div>";

            var game = SteamCommunityPageParser.ParseOwnedGames(html).Single();

            Assert.AreEqual(72850, game.AppId);
            Assert.AreEqual(92940, game.PlaytimeForever);
            Assert.IsTrue(SteamCommunityPageParser.LooksLikeOwnedGamesPayload(html));
        }
    }
}

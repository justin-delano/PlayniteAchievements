using HtmlAgilityPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.Exophase;

namespace PlayniteAchievements.Providers.Tests
{
    [TestClass]
    public class ExophaseApiClientTests
    {
        [TestMethod]
        public void ResolveAchievementIconUrl_BlizzardAwardImage_UsesDataNormal()
        {
            var node = LoadNode(@"
<li class=""col-12 locked t1 award visible"" data-earned=""0"">
    <div class=""box image hidden-toggle"">
        <img data-tippy-content=""Guardian""
             class=""award-image trophy-image visible""
             data-normal=""https://m.exophase.com/blizzard/awards/s/7de866e.png?8d203856ce4b449e702586e55c94fef2""
             width=""64""
             height=""64"" />
    </div>
</li>");

            var iconUrl = ExophaseApiClient.ResolveAchievementIconUrl(node);

            Assert.AreEqual(
                "https://m.exophase.com/blizzard/awards/s/7de866e.png?8d203856ce4b449e702586e55c94fef2",
                iconUrl);
        }

        [TestMethod]
        public void ResolveAchievementIconUrl_WithSrcOnly_FallsBackToSrc()
        {
            var node = LoadNode(@"
<li class=""col-12 award visible"">
    <img class=""award-image"" src=""https://m.exophase.com/steam/awards/s/example.png"" />
</li>");

            var iconUrl = ExophaseApiClient.ResolveAchievementIconUrl(node);

            Assert.AreEqual("https://m.exophase.com/steam/awards/s/example.png", iconUrl);
        }

        [TestMethod]
        public void ResolveAchievementIconUrl_WithProtocolRelativeUrl_NormalizesToHttps()
        {
            var node = LoadNode(@"
<li class=""col-12 award visible"">
    <img class=""award-image"" data-normal=""//m.exophase.com/blizzard/awards/s/example.png"" />
</li>");

            var iconUrl = ExophaseApiClient.ResolveAchievementIconUrl(node);

            Assert.AreEqual("https://m.exophase.com/blizzard/awards/s/example.png", iconUrl);
        }

        [TestMethod]
        public void ResolveImageUrl_WithLazyDataSrc_UsesDataSrc()
        {
            var node = LoadImageNode(@"
<div class=""col-image"">
    <img class=""lazyload""
         src=""data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==""
         data-src=""https://m.exophase.com/origin/games/s/titanfall-2.jpg?123"" />
</div>");

            var imageUrl = ExophaseApiClient.ResolveImageUrl(node);

            Assert.AreEqual("https://m.exophase.com/origin/games/s/titanfall-2.jpg?123", imageUrl);
        }

        [TestMethod]
        public void ResolveImageUrl_WithSrcSet_UsesLargestCandidate()
        {
            var node = LoadImageNode(@"
<div class=""col-image"">
    <img srcset=""https://m.exophase.com/origin/games/s/titanfall-2-small.jpg 1x,
                 //m.exophase.com/origin/games/l/titanfall-2-large.jpg 2x"" />
</div>");

            var imageUrl = ExophaseApiClient.ResolveImageUrl(node);

            Assert.AreEqual("https://m.exophase.com/origin/games/l/titanfall-2-large.jpg", imageUrl);
        }

        [TestMethod]
        public void ResolveImageUrl_WithRelativeGameCdnPath_MakesAbsolute()
        {
            var node = LoadImageNode(@"
<div class=""col-image"">
    <img data-src=""/origin/games/s/titanfall-2.jpg?123"" />
</div>");

            var imageUrl = ExophaseApiClient.ResolveImageUrl(node);

            Assert.AreEqual("https://m.exophase.com/origin/games/s/titanfall-2.jpg?123", imageUrl);
        }

        [TestMethod]
        public void ExtractSlugFromUrl_ProfileAchievementLinkWithFragment_ReturnsSlug()
        {
            var slug = ExophaseApiClient.ExtractSlugFromUrl(
                "https://www.exophase.com/game/titanfall-2-origin/achievements/#4768201");

            Assert.AreEqual("titanfall-2-origin", slug);
        }

        [TestMethod]
        public void ExtractSlugFromUrl_ProfileAchievementLinkWithQuery_ReturnsSlug()
        {
            var slug = ExophaseApiClient.ExtractSlugFromUrl(
                "https://www.exophase.com/game/titanfall-2-origin/achievements/?foo=bar");

            Assert.AreEqual("titanfall-2-origin", slug);
        }

        private static HtmlNode LoadNode(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode.SelectSingleNode("//li");
        }

        private static HtmlNode LoadImageNode(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode.SelectSingleNode("//div");
        }
    }
}

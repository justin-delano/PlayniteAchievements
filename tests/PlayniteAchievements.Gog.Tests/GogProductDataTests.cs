using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.GOG.Models;
using System.Collections.Generic;

namespace PlayniteAchievements.Gog.Tests
{
    [TestClass]
    public class GogProductDataTests
    {
        [TestMethod]
        public void PreferredBuildMetaUrl_PrefersListedThenNewestPublished()
        {
            var data = new GogProductData
            {
                Builds = new List<GogProductBuild>
                {
                    new GogProductBuild { Link = "https://example.com/unlisted-new", Listed = false, DatePublished = "2025-01-01T00:00:00Z" },
                    new GogProductBuild { Link = "https://example.com/listed-old", Listed = true, DatePublished = "2024-01-01T00:00:00Z" },
                    new GogProductBuild { Link = "https://example.com/listed-new", Listed = true, DatePublished = "2025-01-01T00:00:00Z" }
                }
            };

            Assert.AreEqual("https://example.com/listed-new", data.PreferredBuildMetaUrl);
        }

        [TestMethod]
        public void PreferredBuildMetaUrl_ReturnsNullWhenNoBuildsPresent()
        {
            var data = new GogProductData();
            Assert.IsNull(data.PreferredBuildMetaUrl);
        }

        [TestMethod]
        public void ResolvedClientId_FallsBackToCamelCaseField()
        {
            var data = new GogProductData
            {
                ClientId = null,
                ClientIdCamel = "camel-client-id"
            };

            Assert.AreEqual("camel-client-id", data.ResolvedClientId);
        }
    }
}

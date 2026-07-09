using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.Steam;

namespace PlayniteAchievements.Steam.Tests
{
    [TestClass]
    public class SteamImageUrlsTests
    {
        [TestMethod]
        public void Cover_BuildsLibraryPortraitUrl()
        {
            Assert.AreEqual(
                "https://cdn.akamai.steamstatic.com/steam/apps/620/library_600x900.jpg",
                SteamImageUrls.Cover(620));
        }

        [TestMethod]
        public void Icon_BuildsCapsuleUrl()
        {
            Assert.AreEqual(
                "https://cdn.akamai.steamstatic.com/steam/apps/620/capsule_231x87.jpg",
                SteamImageUrls.Icon(620));
        }

        [TestMethod]
        public void Cover_ReturnsNull_ForInvalidAppId()
        {
            Assert.IsNull(SteamImageUrls.Cover(0));
            Assert.IsNull(SteamImageUrls.Icon(-1));
        }
    }
}

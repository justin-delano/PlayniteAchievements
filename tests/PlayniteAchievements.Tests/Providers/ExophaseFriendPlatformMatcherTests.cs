using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.Exophase;

namespace PlayniteAchievements.Tests.Providers
{
    [TestClass]
    public class ExophaseFriendPlatformMatcherTests
    {
        [TestMethod]
        public void IsSameProviderPlatform_OriginFriendGame_DoesNotMatchSteamLibraryGame()
        {
            Assert.IsFalse(IsSameProviderPlatform(sourceName: "Steam", friendPlatform: "origin"));
        }

        [TestMethod]
        public void IsSameProviderPlatform_OriginFriendGame_MatchesEaLibraryGame()
        {
            Assert.IsTrue(IsSameProviderPlatform(sourceName: "EA app", friendPlatform: "origin"));
        }

        [TestMethod]
        public void IsSameProviderPlatform_SteamFriendGame_MatchesSteamLibraryGame()
        {
            Assert.IsTrue(IsSameProviderPlatform(sourceName: "Steam", friendPlatform: "steam"));
        }

        [TestMethod]
        public void IsSameProviderPlatform_UnknownFriendPlatform_DoesNotMapByTitleFallback()
        {
            Assert.IsFalse(IsSameProviderPlatform(sourceName: "Steam", friendPlatform: "pc"));
        }

        [TestMethod]
        public void IsSameProviderPlatform_PlayStationSlug_MatchesPlayStationPlatformFamily()
        {
            Assert.IsTrue(IsSameProviderPlatform(platformName: "PlayStation 5", friendPlatform: "ps5"));
        }

        [TestMethod]
        public void ExtractPlatformSlugFromFriendGameKey_UsesStoredPlatformPrefix()
        {
            Assert.AreEqual(
                "origin",
                ExophaseFriendPlatformMatcher.ExtractPlatformSlugFromFriendGameKey("origin|titanfall-2-origin"));
        }

        private static bool IsSameProviderPlatform(
            string sourceName = null,
            string platformName = null,
            string specificationId = null,
            string friendPlatform = null)
        {
            return ExophaseFriendPlatformMatcher.IsSameProviderPlatform(
                sourceName,
                string.IsNullOrWhiteSpace(platformName) ? null : new[] { platformName },
                string.IsNullOrWhiteSpace(specificationId) ? null : new[] { specificationId },
                friendPlatform);
        }
    }
}

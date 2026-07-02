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

        [DataTestMethod]
        [DataRow("psn", "trophies")]
        [DataRow("ps3", "trophies")]
        [DataRow("ps4", "trophies")]
        [DataRow("ps5", "trophies")]
        [DataRow("vita", "trophies")]
        [DataRow("ps2", "trophies")]
        [DataRow("psp", "trophies")]
        [DataRow("PSN", "trophies")]
        [DataRow("ubisoft", "challenges")]
        [DataRow("uplay", "challenges")]
        [DataRow("steam", "achievements")]
        [DataRow("xbox-360", "achievements")]
        [DataRow("origin", "achievements")]
        [DataRow("unknown", "achievements")]
        public void ResolveExophaseEndpoint_MapsPlatformFamilyToEndpoint(string platform, string expected)
        {
            Assert.AreEqual(expected, ExophaseFriendPlatformMatcher.ResolveExophaseEndpoint(platform));
        }

        [DataTestMethod]
        [DataRow("ps4", "PSN")]
        [DataRow("ps5", "PSN")]
        [DataRow("ps3", "PSN")]
        [DataRow("vita", "PSN")]
        [DataRow("xbox-360", "Xbox")]
        [DataRow("xbox-one", "Xbox")]
        [DataRow("uplay", "Ubisoft")]
        [DataRow("origin", "EA")]
        public void ResolveProviderPlatformKey_FoldsDerivedTokenIntoFamily(string derivedToken, string expected)
        {
            // The friend ownership filter compares these keys, so e.g. a game tagged "ps4" must fold
            // into "PSN" to match a coarse "psn" selection.
            Assert.AreEqual(expected, ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey(derivedToken));
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

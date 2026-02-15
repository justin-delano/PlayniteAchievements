using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.GOG;

namespace PlayniteAchievements.Gog.Tests
{
    [TestClass]
    public class GogAuthResultTests
    {
        [TestMethod]
        public void IsSuccess_IsTrueForAuthenticatedAndAlreadyAuthenticated()
        {
            var authenticated = GogAuthResult.Create(GogAuthOutcome.Authenticated, "LOCPlayAch_Settings_GogAuth_Verified");
            var alreadyAuthenticated = GogAuthResult.Create(GogAuthOutcome.AlreadyAuthenticated, "LOCPlayAch_Settings_GogAuth_AlreadyAuthenticated");

            Assert.IsTrue(authenticated.IsSuccess);
            Assert.IsTrue(alreadyAuthenticated.IsSuccess);
        }

        [TestMethod]
        public void IsSuccess_IsFalseForNonSuccessOutcomes()
        {
            var failed = GogAuthResult.Create(GogAuthOutcome.Failed, "LOCPlayAch_Settings_GogAuth_Failed");
            var timedOut = GogAuthResult.Create(GogAuthOutcome.TimedOut, "LOCPlayAch_Settings_GogAuth_TimedOut");
            var notAuthenticated = GogAuthResult.Create(GogAuthOutcome.NotAuthenticated, "LOCPlayAch_Settings_GogAuth_NotAuthenticated");

            Assert.IsFalse(failed.IsSuccess);
            Assert.IsFalse(timedOut.IsSuccess);
            Assert.IsFalse(notAuthenticated.IsSuccess);
        }
    }
}

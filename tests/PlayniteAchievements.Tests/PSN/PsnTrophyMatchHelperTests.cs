using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.PSN;
using PlayniteAchievements.Providers.PSN.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace PlayniteAchievements.Tests.PSN
{
    [TestClass]
    public class PsnTrophyMatchHelperTests
    {
        [TestMethod]
        public void TryResolveUserTrophy_FallsBackToTrophyId_WhenGroupIsFlattened()
        {
            var detail = new PsnTrophyDetail
            {
                TrophyGroupId = "003",
                TrophyId = 60
            };

            var userTrophies = new List<PsnUserTrophy>
            {
                new PsnUserTrophy
                {
                    TrophyGroupId = "default",
                    TrophyId = 60,
                    Earned = true
                }
            };

            var userByKey = PsnTrophyMatchHelper.BuildUserTrophyLookupByGroupAndId(userTrophies);
            var userById = PsnTrophyMatchHelper.BuildUserTrophyLookupById(userTrophies);

            var found = PsnTrophyMatchHelper.TryResolveUserTrophy(
                detail,
                userByKey,
                userById,
                out var match,
                out var usedIdFallback);

            Assert.IsTrue(found);
            Assert.IsNotNull(match);
            Assert.IsTrue(match.Earned);
            Assert.IsTrue(usedIdFallback);
        }

        [TestMethod]
        public void TryResolveUserTrophy_PrefersExactGroupAndIdMatch()
        {
            var detail = new PsnTrophyDetail
            {
                TrophyGroupId = "003",
                TrophyId = 60
            };

            var userTrophies = new List<PsnUserTrophy>
            {
                new PsnUserTrophy
                {
                    TrophyGroupId = "003",
                    TrophyId = 60,
                    Earned = false
                },
                new PsnUserTrophy
                {
                    TrophyGroupId = "default",
                    TrophyId = 60,
                    Earned = true
                }
            };

            var userByKey = PsnTrophyMatchHelper.BuildUserTrophyLookupByGroupAndId(userTrophies);
            var userById = PsnTrophyMatchHelper.BuildUserTrophyLookupById(userTrophies);

            var found = PsnTrophyMatchHelper.TryResolveUserTrophy(
                detail,
                userByKey,
                userById,
                out var match,
                out var usedIdFallback);

            Assert.IsTrue(found);
            Assert.IsNotNull(match);
            Assert.IsFalse(match.Earned);
            Assert.IsFalse(usedIdFallback);
        }

        [TestMethod]
        public void TryResolveUserTrophy_ExactBaseMatch_DoesNotUseFallback()
        {
            var detail = new PsnTrophyDetail
            {
                TrophyGroupId = "default",
                TrophyId = 10
            };

            var userTrophies = new List<PsnUserTrophy>
            {
                new PsnUserTrophy
                {
                    TrophyGroupId = "default",
                    TrophyId = 10,
                    Earned = true
                }
            };

            var userByKey = PsnTrophyMatchHelper.BuildUserTrophyLookupByGroupAndId(userTrophies);
            var userById = PsnTrophyMatchHelper.BuildUserTrophyLookupById(userTrophies);

            var found = PsnTrophyMatchHelper.TryResolveUserTrophy(
                detail,
                userByKey,
                userById,
                out var match,
                out var usedIdFallback);

            Assert.IsTrue(found);
            Assert.IsNotNull(match);
            Assert.IsTrue(match.Earned);
            Assert.IsFalse(usedIdFallback);
        }

        [TestMethod]
        public async Task GetStringWithSuffixRetryAsync_RetriesWithAlternateSuffixAfterFailure()
        {
            var attempts = new List<string>();
            var suffixes = new List<string> { "?npServiceName=trophy", string.Empty };

            var value = await PsnSuffixRetryHelper.GetStringWithSuffixRetryAsync(
                suffix =>
                {
                    attempts.Add(suffix);
                    if (attempts.Count == 1)
                    {
                        throw new HttpRequestException("404");
                    }

                    return Task.FromResult("ok");
                },
                suffixes);

            Assert.AreEqual("ok", value);
            CollectionAssert.AreEqual(suffixes, attempts);
        }

        [TestMethod]
        public void BuildSuffixCandidates_ReturnsExpectedOrder()
        {
            CollectionAssert.AreEqual(
                new List<string> { "?npServiceName=trophy", string.Empty },
                PsnSuffixRetryHelper.BuildSuffixCandidates("CUSA12345_00"));

            CollectionAssert.AreEqual(
                new List<string> { string.Empty, "?npServiceName=trophy" },
                PsnSuffixRetryHelper.BuildSuffixCandidates("PPSA22859_00"));
        }
    }
}

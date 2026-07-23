using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Achievements;

namespace PlayniteAchievements.Tests.Services
{
    [TestClass]
    public class AchievementCategoryTypeHelperTests
    {
        [TestMethod]
        public void Normalize_CanonicalizesHardcoreAndSoftcoreAliases()
        {
            Assert.AreEqual("Hardcore", AchievementCategoryTypeHelper.Normalize("hardcore"));
            Assert.AreEqual("Softcore", AchievementCategoryTypeHelper.Normalize("softcore"));
            Assert.AreEqual("Softcore", AchievementCategoryTypeHelper.Normalize("casual"));
        }

        [TestMethod]
        public void NormalizeOrDefault_ReturnsStableResultsAcrossRepeatedCalls()
        {
            foreach (var _ in Enumerable.Range(0, 3))
            {
                Assert.AreEqual("Softcore", AchievementCategoryTypeHelper.NormalizeOrDefault("casual"));
                Assert.AreEqual("Hardcore", AchievementCategoryTypeHelper.NormalizeOrDefault("hardcore"));
                Assert.AreEqual("Base|DLC|Missable", AchievementCategoryTypeHelper.NormalizeOrDefault("DLC, base; missable"));
                Assert.AreEqual("Default", AchievementCategoryTypeHelper.NormalizeOrDefault("not-a-category"));
                Assert.AreEqual("Default", AchievementCategoryTypeHelper.NormalizeOrDefault(null));
                Assert.AreEqual("Default", AchievementCategoryTypeHelper.NormalizeOrDefault(string.Empty));
                Assert.AreEqual("Default", AchievementCategoryTypeHelper.NormalizeOrDefault("   "));
            }
        }

        [TestMethod]
        public void NormalizeOrDefault_CaseVariantsReturnSameValue()
        {
            Assert.AreEqual(
                AchievementCategoryTypeHelper.NormalizeOrDefault("casual"),
                AchievementCategoryTypeHelper.NormalizeOrDefault("CASUAL"));
            Assert.AreEqual(
                AchievementCategoryTypeHelper.NormalizeOrDefault("dlc|missable"),
                AchievementCategoryTypeHelper.NormalizeOrDefault("Missable, DLC"));
        }

        [TestMethod]
        public void Normalize_CanonicalizesSubsetAliasAndOrdersBeforeUnlockMode()
        {
            Assert.AreEqual("Subset", AchievementCategoryTypeHelper.Normalize("subset"));
            // Subset precedes Hardcore/Softcore in canonical order regardless of input order.
            Assert.AreEqual("Subset|Hardcore", AchievementCategoryTypeHelper.Normalize("hardcore|subset"));
            Assert.AreEqual("Subset|Softcore", AchievementCategoryTypeHelper.Combine(new[] { "Subset", "Softcore" }));
        }

        [TestMethod]
        public void AssignableCategoryTypes_IncludesSubset()
        {
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList(),
                "Subset");
        }

        [TestMethod]
        public void AllowedCategoryTypes_IncludesDerivedTypes()
        {
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AllowedCategoryTypes.ToList(),
                AchievementCategoryTypeHelper.HardcoreCategoryType);
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AllowedCategoryTypes.ToList(),
                AchievementCategoryTypeHelper.SoftcoreCategoryType);
        }

        [TestMethod]
        public void AssignableCategoryTypes_ExcludesDefaultAndDerivedTypes()
        {
            var assignable = AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList();

            CollectionAssert.DoesNotContain(assignable, AchievementCategoryTypeHelper.DefaultCategoryType);
            CollectionAssert.DoesNotContain(assignable, AchievementCategoryTypeHelper.HardcoreCategoryType);
            CollectionAssert.DoesNotContain(assignable, AchievementCategoryTypeHelper.SoftcoreCategoryType);
            CollectionAssert.Contains(assignable, "Base");
        }
    }
}

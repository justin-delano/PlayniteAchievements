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

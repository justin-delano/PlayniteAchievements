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
        public void OverrideOrNull_DropsAnOverrideEqualToTheProviderType()
        {
            Assert.IsNull(AchievementCategoryTypeHelper.OverrideOrNull("Default", null), "Default on an untyped row is no override");
            Assert.IsNull(AchievementCategoryTypeHelper.OverrideOrNull(null, null));
            Assert.IsNull(AchievementCategoryTypeHelper.OverrideOrNull("progression", "Progression"));
        }

        [TestMethod]
        public void OverrideOrNull_KeepsAnOverrideThatDiffersFromTheProvider()
        {
            Assert.AreEqual("Default", AchievementCategoryTypeHelper.OverrideOrNull("Default", "Progression"), "unticking the provider's type is a real override");
            Assert.AreEqual("Progression", AchievementCategoryTypeHelper.OverrideOrNull("Progression", null));
        }

        [TestMethod]
        public void OverrideOrNull_NeverStoresDerivedTypes()
        {
            Assert.AreEqual("Base|Missable", AchievementCategoryTypeHelper.OverrideOrNull("Base|Hardcore|Missable", "Base|Hardcore"));
            Assert.IsNull(AchievementCategoryTypeHelper.OverrideOrNull("Base|Softcore", "Base|Hardcore"), "only the derived type differs");
            Assert.IsNull(AchievementCategoryTypeHelper.OverrideOrNull("Hardcore", null));
        }

        [TestMethod]
        public void WithCategoryType_IgnoresDerivedTypes()
        {
            Assert.AreEqual("Base", AchievementCategoryTypeHelper.WithCategoryType("Base", "Hardcore", include: true));
            Assert.AreEqual("Base|Softcore", AchievementCategoryTypeHelper.WithCategoryType("Base|Softcore", "Softcore", include: false));
        }

        [TestMethod]
        public void ApplyOverride_TakesDerivedTypesFromTheProviderOnly()
        {
            Assert.AreEqual("Base|Missable|Softcore", AchievementCategoryTypeHelper.ApplyOverride("Base|Softcore", "Base|Hardcore|Missable"));
            Assert.AreEqual("Missable", AchievementCategoryTypeHelper.ApplyOverride("Base", "Missable|Hardcore"));
            Assert.AreEqual("Hardcore", AchievementCategoryTypeHelper.ApplyOverride("Base|Hardcore", "Default"));
            Assert.AreEqual("Base|Hardcore", AchievementCategoryTypeHelper.ApplyOverride("Base|Hardcore", null));
            Assert.AreEqual("Base|Hardcore", AchievementCategoryTypeHelper.ApplyOverride("Base|Hardcore", "Softcore"), "an override of only derived types overrides nothing");
        }

        [TestMethod]
        public void Normalize_CanonicalizesHardcoreAndSoftcoreAliases()
        {
            Assert.AreEqual("Hardcore", AchievementCategoryTypeHelper.Normalize("hardcore"));
            Assert.AreEqual("Softcore", AchievementCategoryTypeHelper.Normalize("softcore"));
            Assert.AreEqual("Softcore", AchievementCategoryTypeHelper.Normalize("casual"));
        }

        [TestMethod]
        public void Normalize_CanonicalizesProgressionAliasesInCanonicalOrder()
        {
            Assert.AreEqual("Progression", AchievementCategoryTypeHelper.Normalize("progression"));
            Assert.AreEqual("Progression", AchievementCategoryTypeHelper.Normalize("story"));
            Assert.AreEqual("WinCondition", AchievementCategoryTypeHelper.Normalize("win_condition"));
            Assert.AreEqual("WinCondition", AchievementCategoryTypeHelper.Normalize("Win Condition"));
            Assert.AreEqual(
                "Base|Progression|WinCondition|Missable",
                AchievementCategoryTypeHelper.Normalize("Missable|WinCondition|Base|Progression"));
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList(),
                "Progression");
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList(),
                "WinCondition");
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
        public void ToDisplayText_DefaultRendersBlank()
        {
            Assert.AreEqual(string.Empty, AchievementCategoryTypeHelper.ToDisplayText((string)null));
            Assert.AreEqual(string.Empty, AchievementCategoryTypeHelper.ToDisplayText("Default"));
            Assert.AreEqual(string.Empty, AchievementCategoryTypeHelper.ToDisplayText("not-a-category"));
            Assert.AreEqual("DLC", AchievementCategoryTypeHelper.ToDisplayText("default|dlc"));
        }

        [TestMethod]
        public void ToCategoryTypeDisplayText_DefaultKeepsVisibleName()
        {
            Assert.AreEqual("Default", AchievementCategoryTypeHelper.ToCategoryTypeDisplayText("Default"));
            Assert.AreEqual("Default", AchievementCategoryTypeHelper.ToCategoryTypeDisplayText(null));
        }

        [TestMethod]
        public void ToCategoryLabelCellText_DefaultRendersBlank()
        {
            Assert.AreEqual(string.Empty, AchievementCategoryTypeHelper.ToCategoryLabelCellText(null));
            Assert.AreEqual(string.Empty, AchievementCategoryTypeHelper.ToCategoryLabelCellText("  "));
            Assert.AreEqual(string.Empty, AchievementCategoryTypeHelper.ToCategoryLabelCellText("Default"));
            Assert.AreEqual("Story DLC", AchievementCategoryTypeHelper.ToCategoryLabelCellText(" Story DLC "));
        }

        [TestMethod]
        public void AssignableCategoryTypes_IncludesSubset()
        {
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList(),
                "Subset");
        }

        [TestMethod]
        public void Normalize_CanonicalizesUpdateAliasAndOrdersAfterBase()
        {
            Assert.AreEqual("Update", AchievementCategoryTypeHelper.Normalize("update"));
            // Base precedes Update in canonical order regardless of input order.
            Assert.AreEqual("Base|Update", AchievementCategoryTypeHelper.Combine(new[] { "Base", "Update" }));
            Assert.AreEqual("Base|Update", AchievementCategoryTypeHelper.Normalize("update|base"));
        }

        [TestMethod]
        public void AssignableCategoryTypes_IncludesUpdate()
        {
            CollectionAssert.Contains(
                AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList(),
                "Update");
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

        [TestMethod]
        public void Normalize_CanonicalizesSideQuestAliasesBetweenProgressionAndWinCondition()
        {
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("sidequest"));
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("Side Quest"));
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("side-quest"));
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("side_quest"));
            Assert.AreEqual(
                "Progression|SideQuest|WinCondition",
                AchievementCategoryTypeHelper.Normalize("WinCondition|SideQuest|Progression"));
        }

        [TestMethod]
        public void Normalize_ReadsTheShippedSideProgressionTokenAsSideQuest()
        {
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("SideProgression"));
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("Side Progression"));
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("side-progression"));
            Assert.AreEqual("SideQuest", AchievementCategoryTypeHelper.Normalize("side_progression"));
            Assert.AreEqual("Base|SideQuest", AchievementCategoryTypeHelper.Normalize("SideProgression|Base"));
        }

        [TestMethod]
        public void Normalize_CanonicalizesTheArcAndTaskTypes()
        {
            Assert.AreEqual("PostGame", AchievementCategoryTypeHelper.Normalize("postgame"));
            Assert.AreEqual("PostGame", AchievementCategoryTypeHelper.Normalize("Post Game"));
            Assert.AreEqual("PostGame", AchievementCategoryTypeHelper.Normalize("post-game"));
            Assert.AreEqual("PostGame", AchievementCategoryTypeHelper.Normalize("post_game"));
            Assert.AreEqual("Completion", AchievementCategoryTypeHelper.Normalize("completion"));
            Assert.AreEqual("Completion", AchievementCategoryTypeHelper.Normalize("completionist"));
            Assert.AreEqual("Cumulative", AchievementCategoryTypeHelper.Normalize("cumulative"));
            Assert.AreEqual("Cumulative", AchievementCategoryTypeHelper.Normalize("grind"));
            Assert.AreEqual("Challenge", AchievementCategoryTypeHelper.Normalize("challenge"));
            Assert.AreEqual(
                "WinCondition|PostGame|Completion|Collectable|Cumulative|Challenge",
                AchievementCategoryTypeHelper.Normalize("challenge|cumulative|collectable|completion|post game|win condition"));
        }

        [TestMethod]
        public void Normalize_CanonicalizesMiscellaneousAliasesAfterUnobtainable()
        {
            Assert.AreEqual("Miscellaneous", AchievementCategoryTypeHelper.Normalize("miscellaneous"));
            Assert.AreEqual("Miscellaneous", AchievementCategoryTypeHelper.Normalize("misc"));
            Assert.AreEqual(
                "Difficulty|Missable|Miscellaneous",
                AchievementCategoryTypeHelper.Normalize("missable|misc|difficulty"));
        }

        [TestMethod]
        public void Normalize_OrdersStackableWithTheRunModifiersBeforeMissable()
        {
            Assert.AreEqual(
                "Challenge|Difficulty|Stackable|Missable|Unobtainable",
                AchievementCategoryTypeHelper.Normalize("stackable|unobtainable|missable|difficulty|challenge"));
        }

        [TestMethod]
        public void AssignableCategoryTypes_IncludesTheNewTypes()
        {
            var assignable = AchievementCategoryTypeHelper.AssignableCategoryTypes.ToList();

            foreach (var type in new[] { "SideQuest", "PostGame", "Completion", "Cumulative", "Challenge", "Miscellaneous" })
            {
                CollectionAssert.Contains(assignable, type);
            }

            CollectionAssert.DoesNotContain(assignable, "SideProgression");
        }

        [TestMethod]
        public void AllowedCategoryTypes_FollowsGroupedCanonicalOrder()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "Default", "Base", "DLC", "Update", "Subset",
                    "Singleplayer", "Multiplayer",
                    "Progression", "SideQuest", "WinCondition", "PostGame", "Completion",
                    "Collectable", "Cumulative",
                    "Challenge", "Difficulty", "Stackable",
                    "Missable", "Unobtainable",
                    "Miscellaneous",
                    "Softcore", "Hardcore"
                },
                AchievementCategoryTypeHelper.AllowedCategoryTypes.ToList());
        }

        [TestMethod]
        public void GetGroupTypeComponents_ReturnsOnlyGroupTypesInCanonicalOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "DLC", "Update" },
                AchievementCategoryTypeHelper.GetGroupTypeComponents("update|missable|dlc").ToList());
            Assert.AreEqual(0, AchievementCategoryTypeHelper.GetGroupTypeComponents("Missable|Hardcore").Count);
        }

        [TestMethod]
        public void GetNonGroupTypeComponents_ExcludesGroupTypes()
        {
            CollectionAssert.AreEqual(
                new[] { "Missable", "Hardcore" },
                AchievementCategoryTypeHelper.GetNonGroupTypeComponents("DLC|Missable|Hardcore").ToList());
        }

        [TestMethod]
        public void ReplaceGroupTypes_ReplacesGroupTagAndPreservesNonGroupTypes()
        {
            // DLC replaced by Base; Missable preserved; canonical order enforced.
            Assert.AreEqual(
                "Base|Missable",
                AchievementCategoryTypeHelper.ReplaceGroupTypes("DLC|Missable", new[] { "Base" }));
        }

        [TestMethod]
        public void ReplaceGroupTypes_NeverProducesConflictingGroupTags()
        {
            // Base is dropped, not unioned, so the result is DLC (plus preserved Hardcore) - never Base|DLC.
            var result = AchievementCategoryTypeHelper.ReplaceGroupTypes("Base|Hardcore", new[] { "DLC" });
            Assert.AreEqual("DLC|Hardcore", result);
            CollectionAssert.DoesNotContain(AchievementCategoryTypeHelper.ParseValues(result), "Base");
        }

        [TestMethod]
        public void ReplaceGroupTypes_MultiValueTargetGroupIsApplied()
        {
            Assert.AreEqual(
                "DLC|Update|Missable",
                AchievementCategoryTypeHelper.ReplaceGroupTypes("Subset|Missable", new[] { "DLC", "Update" }));
        }

        [TestMethod]
        public void ReplaceGroupTypes_EmptyTargetClearsGroupTagKeepingOthers()
        {
            Assert.AreEqual(
                "Missable",
                AchievementCategoryTypeHelper.ReplaceGroupTypes("DLC|Missable", System.Array.Empty<string>()));
        }

        [TestMethod]
        public void ReplaceGroupTypes_EmptyTargetOnGroupOnlyTypeYieldsDefault()
        {
            Assert.AreEqual(
                "Default",
                AchievementCategoryTypeHelper.ReplaceGroupTypes("DLC", null));
        }

        [TestMethod]
        public void ReplaceGroupTypes_PreservesDerivedUnlockModeTypes()
        {
            Assert.AreEqual(
                "Base|Softcore",
                AchievementCategoryTypeHelper.ReplaceGroupTypes("Subset|Softcore", new[] { "Base" }));
        }

        [TestMethod]
        public void ReplaceGroupTypes_NullAchievementTypeAdoptsTargetGroup()
        {
            Assert.AreEqual(
                "Base",
                AchievementCategoryTypeHelper.ReplaceGroupTypes(null, new[] { "Base" }));
        }

        [TestMethod]
        public void WithCategoryType_RemovesAutoAssignedGroupTypeKeepingOthers()
        {
            // Deselecting an auto-assigned DLC tag leaves the remaining types intact.
            Assert.AreEqual(
                "Update",
                AchievementCategoryTypeHelper.WithCategoryType("DLC|Update", "DLC", include: false));
        }

        [TestMethod]
        public void WithCategoryType_RemovingSoleTypeYieldsDefault()
        {
            Assert.AreEqual(
                "Default",
                AchievementCategoryTypeHelper.WithCategoryType("DLC", "DLC", include: false));
        }

        [TestMethod]
        public void WithCategoryType_RemovalIsCaseInsensitiveViaAlias()
        {
            Assert.AreEqual(
                "Base",
                AchievementCategoryTypeHelper.WithCategoryType("Base|DLC", "dlc", include: false));
        }

        [TestMethod]
        public void WithCategoryType_RemovingAbsentTypeLeavesValueUnchanged()
        {
            Assert.AreEqual(
                "Base|Missable",
                AchievementCategoryTypeHelper.WithCategoryType("Base|Missable", "DLC", include: false));
        }

        [TestMethod]
        public void WithCategoryType_AddsTypeInCanonicalOrder()
        {
            Assert.AreEqual(
                "Base|DLC",
                AchievementCategoryTypeHelper.WithCategoryType("DLC", "base", include: true));
        }

        [TestMethod]
        public void WithCategoryType_AddIsIdempotent()
        {
            Assert.AreEqual(
                "DLC",
                AchievementCategoryTypeHelper.WithCategoryType("DLC", "DLC", include: true));
        }

        [TestMethod]
        public void WithCategoryType_AddingToDefaultReplacesIt()
        {
            Assert.AreEqual(
                "DLC",
                AchievementCategoryTypeHelper.WithCategoryType("Default", "DLC", include: true));
        }

        [TestMethod]
        public void WithCategoryType_BlankTypeLeavesValueNormalizedUnchanged()
        {
            Assert.AreEqual(
                "Base|DLC",
                AchievementCategoryTypeHelper.WithCategoryType("dlc|base", null, include: false));
        }
    }
}

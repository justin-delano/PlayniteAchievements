using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Summaries;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.Services.Summaries
{
    [TestClass]
    public class CategorySummaryBuilderTests
    {
        [TestMethod]
        public void Build_GroupsByLabelInFirstSeenOrderWithAggregatedStats()
        {
            var items = new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true),
                DisplayItem("Base", unlocked: false),
                DisplayItem("DLC", unlocked: true)
            };

            var result = CategorySummaryBuilder.Build(items);

            Assert.AreEqual(2, result.Count);
            var dlc = (CategorySummaryItem)result[0];
            var baseCat = (CategorySummaryItem)result[1];

            // First-seen ordering: DLC appears before Base.
            Assert.AreEqual("DLC", dlc.CategoryLabel);
            Assert.AreEqual("Base", baseCat.CategoryLabel);

            // A non-default label is shown verbatim as the row name.
            Assert.AreEqual("DLC", dlc.GameName);

            Assert.AreEqual(2, dlc.TotalAchievements);
            Assert.AreEqual(2, dlc.UnlockedAchievements);
            Assert.AreEqual(1, baseCat.TotalAchievements);
            Assert.AreEqual(0, baseCat.UnlockedAchievements);
        }

        [TestMethod]
        public void Build_SharedCategoryImagesSurfacedOnlyWhenAllItemsAgree()
        {
            var shared = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true, categoryIcon: "icon.png", categoryCover: "cover.png"),
                DisplayItem("DLC", unlocked: false, categoryIcon: "icon.png", categoryCover: "cover.png")
            });
            var sharedCategory = (CategorySummaryItem)shared.Single();
            Assert.AreEqual("icon.png", sharedCategory.GameLogo);
            Assert.AreEqual("cover.png", sharedCategory.GameCoverPath);

            var mixed = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true, categoryIcon: "a.png", categoryCover: "a.png"),
                DisplayItem("DLC", unlocked: false, categoryIcon: "b.png", categoryCover: "b.png")
            });
            var mixedCategory = (CategorySummaryItem)mixed.Single();
            Assert.IsNull(mixedCategory.GameLogo);
            Assert.IsNull(mixedCategory.GameCoverPath);
        }

        [TestMethod]
        public void Build_SetsSharedGameIdOnlyWhenCategoryBucketAgrees()
        {
            var gameId = Guid.NewGuid();
            var shared = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true, playniteGameId: gameId),
                DisplayItem("DLC", unlocked: false, playniteGameId: gameId)
            });
            Assert.AreEqual(gameId, shared.Single().PlayniteGameId);

            var mixed = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true, playniteGameId: gameId),
                DisplayItem("DLC", unlocked: false, playniteGameId: Guid.NewGuid())
            });
            Assert.IsNull(mixed.Single().PlayniteGameId);
        }

        [TestMethod]
        public void Build_UsesCategoryOrderIndexBeforeFirstSeenRemainder()
        {
            var result = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("Base", unlocked: false, categoryOrderIndex: 1),
                DisplayItem("Event", unlocked: false),
                DisplayItem("DLC", unlocked: true, categoryOrderIndex: 0)
            });

            CollectionAssert.AreEqual(
                new[] { "DLC", "Base", "Event" },
                result.Cast<CategorySummaryItem>().Select(item => item.CategoryLabel).ToList());
        }

        [TestMethod]
        public void Build_BlankLabelsCollapseToASingleDefaultCategory()
        {
            var result = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem(null, unlocked: true),
                DisplayItem("", unlocked: false)
            });

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2, result[0].TotalAchievements);
        }

        [TestMethod]
        public void Build_NullOrEmptyReturnsEmptyList()
        {
            Assert.AreEqual(0, CategorySummaryBuilder.Build(null).Count);
            Assert.AreEqual(0, CategorySummaryBuilder.Build(new List<AchievementDisplayItem>()).Count);
        }

        private static AchievementDisplayItem DisplayItem(
            string label,
            bool unlocked,
            string categoryIcon = null,
            string categoryCover = null,
            int categoryOrderIndex = int.MaxValue,
            Guid? playniteGameId = null)
        {
            return new AchievementDisplayItem
            {
                Rarity = RarityTier.Common,
                PlayniteGameId = playniteGameId,
                CategoryLabel = label,
                Unlocked = unlocked,
                CategoryIconPath = categoryIcon,
                CategoryCoverPath = categoryCover,
                CategoryOrderIndex = categoryOrderIndex
            };
        }
    }
}

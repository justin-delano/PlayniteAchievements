using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Summaries;
using PlayniteAchievements.ViewModels;

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
        public void Build_SharedCoverSurfacedOnlyWhenAllItemsAgree()
        {
            var shared = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true, cover: "cover.png"),
                DisplayItem("DLC", unlocked: false, cover: "cover.png")
            });
            Assert.AreEqual("cover.png", ((CategorySummaryItem)shared.Single()).GameCoverPath);

            var mixed = CategorySummaryBuilder.Build(new List<AchievementDisplayItem>
            {
                DisplayItem("DLC", unlocked: true, cover: "a.png"),
                DisplayItem("DLC", unlocked: false, cover: "b.png")
            });
            Assert.IsNull(((CategorySummaryItem)mixed.Single()).GameCoverPath);
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

        private static AchievementDisplayItem DisplayItem(string label, bool unlocked, string cover = null)
        {
            return new AchievementDisplayItem
            {
                Rarity = RarityTier.Common,
                CategoryLabel = label,
                Unlocked = unlocked,
                GameCoverPath = cover
            };
        }
    }
}

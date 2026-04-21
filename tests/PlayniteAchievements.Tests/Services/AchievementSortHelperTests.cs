using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class AchievementSortHelperTests
    {
        [TestMethod]
        public void UnlockTime_TieBreaksByRarityBeforeTrophyType()
        {
            var unlockTime = DateTime.SpecifyKind(new DateTime(2026, 3, 1, 12, 0, 0), DateTimeKind.Utc);
            var items = new List<AchievementDisplayItem>
            {
                CreateItem("Gold Common", unlockTime, raritySortValue: 80, trophyType: "gold", points: 10),
                CreateItem("Bronze Rare", unlockTime, raritySortValue: 8, trophyType: "bronze", points: 10)
            };

            string sortPath = null;
            ListSortDirection? sortDirection = null;

            var handled = AchievementSortHelper.TrySortItems(
                items,
                "UnlockTime",
                ListSortDirection.Descending,
                AchievementSortScope.GameAchievements,
                ref sortPath,
                ref sortDirection);

            Assert.IsTrue(handled);
            CollectionAssert.AreEqual(
                new[] { "Bronze Rare", "Gold Common" },
                items.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void TrophyType_TieBreaksByRarity()
        {
            var items = new List<AchievementDisplayItem>
            {
                CreateItem("Gold Common", DateTime.SpecifyKind(new DateTime(2026, 3, 1, 12, 0, 0), DateTimeKind.Utc), raritySortValue: 80, trophyType: "gold", points: 10),
                CreateItem("Gold Rare", DateTime.SpecifyKind(new DateTime(2026, 3, 2, 12, 0, 0), DateTimeKind.Utc), raritySortValue: 8, trophyType: "gold", points: 10),
                CreateItem("Silver Common", DateTime.SpecifyKind(new DateTime(2026, 3, 3, 12, 0, 0), DateTimeKind.Utc), raritySortValue: 90, trophyType: "silver", points: 10)
            };

            string sortPath = null;
            ListSortDirection? sortDirection = null;

            var handled = AchievementSortHelper.TrySortItems(
                items,
                "TrophyType",
                ListSortDirection.Descending,
                AchievementSortScope.GameAchievements,
                ref sortPath,
                ref sortDirection);

            Assert.IsTrue(handled);
            CollectionAssert.AreEqual(
                new[] { "Gold Rare", "Gold Common", "Silver Common" },
                items.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void DefaultDetailSort_PrioritizesUnlockedThenLatestUnlockTime()
        {
            var sorted = AchievementSortHelper.CreateDefaultSortedDetailList(new List<AchievementDetail>
            {
                CreateDetail("Locked", unlocked: false),
                CreateDetail("Unlocked Older", unlocked: true, unlockTimeUtc: DateTime.SpecifyKind(new DateTime(2026, 3, 1, 10, 0, 0), DateTimeKind.Utc)),
                CreateDetail("Unlocked Newer", unlocked: true, unlockTimeUtc: DateTime.SpecifyKind(new DateTime(2026, 3, 2, 10, 0, 0), DateTimeKind.Utc)),
                CreateDetail("Unlocked No Time", unlocked: true)
            });

            CollectionAssert.AreEqual(
                new[] { "Unlocked Newer", "Unlocked Older", "Unlocked No Time", "Locked" },
                sorted.Select(item => item.DisplayName).ToArray());
        }

        [TestMethod]
        public void ResolveSelectedGameAchievements_DefaultFallsBackToAllAchievements_WhenDefaultOrderIsEmpty()
        {
            var first = CreateDetail("First", unlocked: true);
            var second = CreateDetail("Second", unlocked: false);
            var theme = new ModernThemeBindings
            {
                HasAchievements = true,
                AllAchievements = new List<AchievementDetail> { first, second },
                AchievementDefaultOrder = new List<AchievementDetail>()
            };

            var settings = new PersistedSettings
            {
                CompactListSortMode = CompactListSortMode.None,
                CompactListSortDescending = false
            };

            var resolved = AchievementSortHelper.ResolveSelectedGameAchievements(
                theme,
                settings,
                AchievementSortSurface.CompactList);

            CollectionAssert.AreEqual(new[] { first, second }, resolved);
        }

        private static AchievementDisplayItem CreateItem(
            string displayName,
            DateTime unlockTimeUtc,
            double raritySortValue,
            string trophyType,
            int points)
        {
            return new AchievementDisplayItem
            {
                DisplayName = displayName,
                UnlockTimeUtc = unlockTimeUtc,
                RaritySortValue = raritySortValue,
                TrophyType = trophyType,
                PointsValue = points,
                Unlocked = true
            };
        }

        private static AchievementDetail CreateDetail(
            string displayName,
            bool unlocked,
            DateTime? unlockTimeUtc = null,
            double? globalPercentUnlocked = 50)
        {
            return new AchievementDetail
            {
                ApiName = displayName,
                DisplayName = displayName,
                Unlocked = unlocked,
                UnlockTimeUtc = unlockTimeUtc,
                GlobalPercentUnlocked = globalPercentUnlocked
            };
        }
    }
}


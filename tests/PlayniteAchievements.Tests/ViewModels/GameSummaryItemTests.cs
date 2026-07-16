using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.ViewModels
{
    [TestClass]
    public class GameSummaryItemTests
    {
        [TestMethod]
        public void ScoreFractions_FormatWithoutSpaces()
        {
            WithUsCulture(() =>
            {
                var item = new GameSummaryItem
                {
                    CollectionScore = 1,
                    CollectionScoreTotal = 10,
                    PrestigeScore = 1234,
                    PrestigeScoreTotal = 5678
                };

                Assert.AreEqual("1/10", item.CollectionScoreFractionText);
                Assert.AreEqual("1,234/5,678", item.PrestigeScoreFractionText);
            });
        }

        [TestMethod]
        public void ScoreFractions_ClampNegativeValuesToZero()
        {
            WithUsCulture(() =>
            {
                var item = new GameSummaryItem
                {
                    CollectionScore = -1,
                    CollectionScoreTotal = -10,
                    PrestigeScore = -1234,
                    PrestigeScoreTotal = 5678
                };

                Assert.AreEqual("0/0", item.CollectionScoreFractionText);
                Assert.AreEqual("0/5,678", item.PrestigeScoreFractionText);
            });
        }

        [TestMethod]
        public void OwnedText_IsNotExposed()
        {
            Assert.IsNull(typeof(GameSummaryItem).GetProperty("OwnedText"));
        }

        [TestMethod]
        public void ProgressTier_MapsProgressQuartiles()
        {
            var expectations = new[]
            {
                (unlocked: 0, tier: RarityTier.Common),
                (unlocked: 24, tier: RarityTier.Common),
                (unlocked: 25, tier: RarityTier.Uncommon),
                (unlocked: 49, tier: RarityTier.Uncommon),
                (unlocked: 50, tier: RarityTier.Rare),
                (unlocked: 74, tier: RarityTier.Rare),
                (unlocked: 75, tier: RarityTier.UltraRare),
                (unlocked: 99, tier: RarityTier.UltraRare),
                (unlocked: 100, tier: RarityTier.UltraRare)
            };

            foreach (var expectation in expectations)
            {
                var item = new GameSummaryItem
                {
                    TotalAchievements = 100,
                    UnlockedAchievements = expectation.unlocked
                };

                Assert.AreEqual(expectation.tier, item.ProgressTier, $"unlocked={expectation.unlocked}");
            }
        }

        [TestMethod]
        public void ProgressTier_RaisesPropertyChangedWhenCountsChange()
        {
            var item = new GameSummaryItem { TotalAchievements = 100 };
            var changed = new List<string>();
            item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            item.UnlockedAchievements = 50;
            CollectionAssert.Contains(changed, nameof(GameSummaryItem.ProgressTier));

            changed.Clear();
            item.TotalAchievements = 200;
            CollectionAssert.Contains(changed, nameof(GameSummaryItem.ProgressTier));
        }

        private static void WithUsCulture(Action action)
        {
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            var previousUiCulture = Thread.CurrentThread.CurrentUICulture;

            try
            {
                var culture = CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                action();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                Thread.CurrentThread.CurrentUICulture = previousUiCulture;
            }
        }
    }
}

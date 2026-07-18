using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Tests.ViewModels
{
    [TestClass]
    public class AchievementToastViewModelTests
    {
        [TestMethod]
        public void Rarity_ExposesParsedToastRarityForThemeBindings()
        {
            var viewModel = new AchievementToastViewModel(
                new AchievementUnlockedEventArgs
                {
                    RarityTier = "UltraRare"
                },
                new PersistedSettings());

            Assert.AreEqual(RarityTier.UltraRare, viewModel.Rarity);
        }

        [TestMethod]
        public void Rarity_InvalidValueFallsBackToCommon()
        {
            var viewModel = new AchievementToastViewModel(
                new AchievementUnlockedEventArgs
                {
                    RarityTier = "not-a-tier"
                },
                new PersistedSettings());

            Assert.AreEqual(RarityTier.Common, viewModel.Rarity);
        }

        [TestMethod]
        public void CompletionNotification_UsesCompletedBadgeAsMainIconWithoutSecondaryBadge()
        {
            var viewModel = new AchievementToastViewModel(
                new AchievementUnlockedEventArgs
                {
                    CompletesGame = true,
                    IsGameCompletionNotification = true
                },
                new PersistedSettings
                {
                    ToastShowRarityBadge = true,
                    FrameShowRarityBadge = true
                });

            Assert.IsTrue(viewModel.IsCompleted);
            Assert.IsFalse(viewModel.IsCapstone);
            Assert.IsTrue(viewModel.UsesCompletedBadgeIcon);
            Assert.IsFalse(viewModel.ShowBadge);
            Assert.IsNull(viewModel.BadgeImage);
            Assert.IsFalse(viewModel.FrameShowBadge);
            Assert.IsNull(viewModel.FrameBadgeImage);
        }

        [TestMethod]
        public void CompletingUnlock_KeepsAchievementIconAndOwnBadge()
        {
            var viewModel = new AchievementToastViewModel(
                new AchievementUnlockedEventArgs
                {
                    IconPath = "achievement.png",
                    CompletesGame = true,
                    RarityTier = "Rare",
                    GlobalPercent = 9.3
                },
                new PersistedSettings
                {
                    ToastShowRarityBadge = true,
                    FrameShowRarityBadge = true
                });

            Assert.AreEqual("achievement.png", viewModel.IconSource);
            Assert.IsFalse(viewModel.UsesCompletedBadgeIcon);
            Assert.IsTrue(viewModel.ShowBadge);
            Assert.IsTrue(viewModel.FrameShowBadge);
            Assert.IsFalse(viewModel.FrameShowGameCompleteLine);
            Assert.IsFalse(viewModel.FrameShowGameCompleteSeparator);
        }
    }
}

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
    }
}

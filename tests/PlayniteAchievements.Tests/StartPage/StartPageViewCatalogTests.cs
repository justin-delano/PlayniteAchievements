using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.StartPage;

namespace PlayniteAchievements.Tests.StartPage
{
    [TestClass]
    public class StartPageViewCatalogTests
    {
        [TestMethod]
        public void Views_RegisterSixWidgetsWithExistingLocalizationKeys()
        {
            var views = StartPageViewCatalog.Views;

            Assert.AreEqual(6, views.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    StartPageWidgetKind.GamesOverviewGrid,
                    StartPageWidgetKind.RecentUnlocksGrid,
                    StartPageWidgetKind.CompletedGamesPie,
                    StartPageWidgetKind.ProviderPie,
                    StartPageWidgetKind.RarityPie,
                    StartPageWidgetKind.TrophyPie
                },
                views.Select(view => view.WidgetKind).ToArray());

            Assert.IsTrue(views.All(view => !view.NameKey.Contains("StartPage")));
            Assert.IsTrue(views.All(view => string.IsNullOrWhiteSpace(view.DescriptionKey)));
            Assert.AreEqual(views.Count, views.Select(view => view.ViewId).Distinct().Count());
        }

        [TestMethod]
        public void TryGetDefinition_ReturnsFalseForUnknownViewId()
        {
            var found = StartPageViewCatalog.TryGetDefinition("Unknown", out var definition);

            Assert.IsFalse(found);
            Assert.IsNull(definition);
        }
    }
}

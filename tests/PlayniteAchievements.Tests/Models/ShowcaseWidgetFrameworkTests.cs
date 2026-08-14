using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Tests.Models
{
    [TestClass]
    public class ShowcaseWidgetFrameworkTests
    {
        [DataTestMethod]
        [DataRow(259d, 500d, WidgetViewportDensity.Compact)]
        [DataRow(800d, 159d, WidgetViewportDensity.Compact)]
        [DataRow(260d, 160d, WidgetViewportDensity.Standard)]
        [DataRow(519d, 400d, WidgetViewportDensity.Standard)]
        [DataRow(520d, 320d, WidgetViewportDensity.Expanded)]
        public void Classify_UsesContainerThresholds(
            double width,
            double height,
            WidgetViewportDensity expected)
        {
            Assert.AreEqual(expected, WidgetViewportState.Classify(width, height).Density);
        }

        [DataTestMethod]
        [DataRow(600d, 300d, WidgetViewportOrientation.Wide)]
        [DataRow(300d, 600d, WidgetViewportOrientation.Tall)]
        [DataRow(400d, 400d, WidgetViewportOrientation.Balanced)]
        public void Classify_UsesContainerOrientation(
            double width,
            double height,
            WidgetViewportOrientation expected)
        {
            Assert.AreEqual(expected, WidgetViewportState.Classify(width, height).Orientation);
        }

        [DataTestMethod]
        [DataRow(30, TimelineRange.OneMonth)]
        [DataRow(90, TimelineRange.ThreeMonths)]
        [DataRow(365, TimelineRange.OneYear)]
        [DataRow(1095, TimelineRange.All)]
        public void TimelineRange_MigratesLegacyDayOptions(
            int days,
            TimelineRange expected)
        {
            var instance = new ShowcaseWidgetInstanceSettings
            {
                Kind = ShowcaseWidgetKind.Timeline
            };
            instance.SetOption("RangeDays", days);

            Assert.AreEqual(expected, ShowcaseTimelineOptions.GetRange(instance));
        }

        [TestMethod]
        public void TimelineRange_PersistsEstablishedRangeAndRemovesLegacyOption()
        {
            var instance = new ShowcaseWidgetInstanceSettings
            {
                Kind = ShowcaseWidgetKind.Timeline
            };
            instance.SetOption("RangeDays", 30);

            ShowcaseTimelineOptions.SetRange(instance, TimelineRange.OneYear);

            Assert.AreEqual(TimelineRange.OneYear, ShowcaseTimelineOptions.GetRange(instance));
            Assert.IsFalse(instance.Options.ContainsKey("RangeDays"));
        }

        [TestMethod]
        public void WidgetOptions_RejectInvalidEnumsAndClampNumericValues()
        {
            var instance = new ShowcaseWidgetInstanceSettings();
            instance.SetOption("Mode", 999);
            instance.SetOption("TopN", 100);
            instance.SetOption("Count", -5);
            instance.SetOption("IntervalSeconds", 1000);

            Assert.AreEqual(ShowcaseScoreMode.Dual, ShowcaseWidgetOptions.GetScoreMode(instance));
            Assert.AreEqual(25, ShowcaseWidgetOptions.GetTopN(instance));
            Assert.AreEqual(1, ShowcaseWidgetOptions.GetMosaicCount(instance));
            Assert.AreEqual(300, ShowcaseWidgetOptions.GetSlideshowIntervalSeconds(instance));
        }

        [TestMethod]
        public void WidgetOptions_ClampGridAndGameMosaicValues()
        {
            var instance = new ShowcaseWidgetInstanceSettings();
            instance.SetOption("Count", 999);
            instance.SetOption("Mode", 999);
            instance.SetOption("Source", 999);

            Assert.AreEqual(100, ShowcaseWidgetOptions.GetRecentCount(instance));
            Assert.AreEqual(200, ShowcaseWidgetOptions.GetGameListCount(instance));
            Assert.AreEqual(64, ShowcaseWidgetOptions.GetGameMosaicCount(instance));
            Assert.AreEqual(ShowcaseGameListSort.LastUnlock, ShowcaseWidgetOptions.GetGameListSort(instance));
            Assert.AreEqual(ShowcaseGameMosaicSource.Completed, ShowcaseWidgetOptions.GetGameMosaicSource(instance));
            Assert.IsFalse(ShowcaseWidgetOptions.GetHideCompleted(instance));

            instance.SetOption("Count", 0);
            Assert.AreEqual(1, ShowcaseWidgetOptions.GetRecentCount(instance));
            Assert.AreEqual(1, ShowcaseWidgetOptions.GetGameListCount(instance));
            Assert.AreEqual(1, ShowcaseWidgetOptions.GetGameMosaicCount(instance));
        }

        [TestMethod]
        public void WidgetFactory_SeedsDefaultsForGridMosaicAndCalendarKinds()
        {
            var recent = ShowcaseWidgetSettingsFactory.CreateDefault(ShowcaseWidgetKind.RecentAchievements);
            Assert.AreEqual(15, ShowcaseWidgetOptions.GetRecentCount(recent));

            var summaries = ShowcaseWidgetSettingsFactory.CreateDefault(ShowcaseWidgetKind.GameSummaries);
            Assert.AreEqual(ShowcaseGameListSort.LastUnlock, ShowcaseWidgetOptions.GetGameListSort(summaries));
            Assert.AreEqual(50, ShowcaseWidgetOptions.GetGameListCount(summaries));
            Assert.IsFalse(ShowcaseWidgetOptions.GetHideCompleted(summaries));

            var mosaic = ShowcaseWidgetSettingsFactory.CreateDefault(ShowcaseWidgetKind.GameMosaic);
            Assert.AreEqual(ShowcaseGameMosaicSource.Completed, ShowcaseWidgetOptions.GetGameMosaicSource(mosaic));
            Assert.AreEqual(24, ShowcaseWidgetOptions.GetGameMosaicCount(mosaic));

            var scores = ShowcaseWidgetSettingsFactory.CreateDefault(ShowcaseWidgetKind.Scores);
            Assert.AreEqual(ShowcaseScoreMode.Dual, ShowcaseWidgetOptions.GetScoreMode(scores));
            Assert.AreEqual(TimelineRange.ThreeMonths, ShowcaseTimelineOptions.GetRange(scores));

            var calendar = ShowcaseWidgetSettingsFactory.CreateDefault(ShowcaseWidgetKind.ActivityCalendar);
            Assert.AreEqual(0, calendar.Options.Count);
            Assert.IsTrue(ShowcaseWidgetCatalog.Get(ShowcaseWidgetKind.ActivityCalendar).AllowMultipleInstances);
        }

        [TestMethod]
        public void WidgetFactory_UsesTheSharedOptionContract()
        {
            var instance = ShowcaseWidgetSettingsFactory.CreateDefault(
                ShowcaseWidgetKind.ScreenshotSlideshow,
                " slideshow ");

            Assert.AreEqual("slideshow", instance.InstanceId);
            Assert.AreEqual(ShowcaseScreenshotVariant.All,
                ShowcaseWidgetOptions.GetScreenshotVariant(instance));
            Assert.AreEqual(8, ShowcaseWidgetOptions.GetSlideshowIntervalSeconds(instance));
            Assert.AreEqual(ShowcaseImageFitMode.Fill,
                ShowcaseWidgetOptions.GetImageFitMode(instance));
            Assert.IsTrue(ShowcaseWidgetOptions.GetShuffle(instance));
        }
    }
}

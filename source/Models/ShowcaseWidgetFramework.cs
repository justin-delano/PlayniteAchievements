using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Models
{
    public enum WidgetHostKind
    {
        Showcase,
        StartPage
    }

    public enum WidgetViewportDensity
    {
        Compact,
        Standard,
        Expanded
    }

    public enum WidgetViewportOrientation
    {
        Wide,
        Balanced,
        Tall
    }

    public sealed class WidgetViewportState
    {
        public double Width { get; private set; }

        public double Height { get; private set; }

        public WidgetViewportDensity Density { get; private set; }

        public WidgetViewportOrientation Orientation { get; private set; }

        public bool ShowDescriptions => Density == WidgetViewportDensity.Expanded;

        public bool ShowSecondaryStatistics => Density != WidgetViewportDensity.Compact;

        public bool ShowControls => Density == WidgetViewportDensity.Expanded;

        public bool ShowLegend => Density != WidgetViewportDensity.Compact;

        public static WidgetViewportState Classify(double width, double height)
        {
            width = Math.Max(0, width);
            height = Math.Max(0, height);

            var density = width < 260 || height < 160
                ? WidgetViewportDensity.Compact
                : width >= 520 && height >= 320
                    ? WidgetViewportDensity.Expanded
                    : WidgetViewportDensity.Standard;

            var ratio = height <= 0 ? 1 : width / height;
            var orientation = ratio >= 1.35
                ? WidgetViewportOrientation.Wide
                : ratio <= 0.74
                    ? WidgetViewportOrientation.Tall
                    : WidgetViewportOrientation.Balanced;

            return new WidgetViewportState
            {
                Width = width,
                Height = height,
                Density = density,
                Orientation = orientation
            };
        }
    }

    public sealed class WidgetHostContext
    {
        public WidgetHostKind HostKind { get; set; }

        public string PageId { get; set; }

        public string InstanceId { get; set; }

        public WidgetViewportState Viewport { get; set; } =
            WidgetViewportState.Classify(0, 0);
    }

    public sealed class ShowcaseWidgetDefinition
    {
        public ShowcaseWidgetKind Kind { get; set; }

        public string NameKey { get; set; }

        public string DescriptionKey { get; set; }

        public bool AllowMultipleInstances { get; set; }

        public bool SingleInstancePerPage { get; set; }
    }

    public static class ShowcaseWidgetCatalog
    {
        private static readonly IReadOnlyList<ShowcaseWidgetDefinition> DefinitionsValue =
            new List<ShowcaseWidgetDefinition>
            {
                Define(ShowcaseWidgetKind.Profile, "LOCPlayAch_Showcase_Widget_Profile", false, true),
                Define(ShowcaseWidgetKind.Scores, "LOCPlayAch_Showcase_Widget_Scores", false, false),
                Define(ShowcaseWidgetKind.Pie, "LOCPlayAch_Showcase_Widget_Pie", true, false),
                Define(ShowcaseWidgetKind.Timeline, "LOCPlayAch_Showcase_Widget_Timeline", true, false),
                Define(ShowcaseWidgetKind.Statistics, "LOCPlayAch_Showcase_Widget_Statistics", true, false),
                Define(ShowcaseWidgetKind.NativePoints, "LOCPlayAch_Showcase_Widget_NativePoints", true, false),
                Define(ShowcaseWidgetKind.PinnedAchievements, "LOCPlayAch_Showcase_Widget_PinnedAchievements", false, true),
                Define(ShowcaseWidgetKind.FavoriteGames, "LOCPlayAch_Showcase_Widget_FavoriteGames", false, true),
                Define(ShowcaseWidgetKind.IconMosaic, "LOCPlayAch_Showcase_Widget_IconMosaic", true, false),
                Define(ShowcaseWidgetKind.ScreenshotSlideshow, "LOCPlayAch_Showcase_Widget_ScreenshotSlideshow", true, false)
            };

        public static IReadOnlyList<ShowcaseWidgetDefinition> Definitions => DefinitionsValue;

        public static ShowcaseWidgetDefinition Get(ShowcaseWidgetKind kind)
        {
            return DefinitionsValue.First(definition => definition.Kind == kind);
        }

        private static ShowcaseWidgetDefinition Define(
            ShowcaseWidgetKind kind,
            string nameKey,
            bool allowMultipleInstances,
            bool singleInstancePerPage)
        {
            return new ShowcaseWidgetDefinition
            {
                Kind = kind,
                NameKey = nameKey,
                DescriptionKey = nameKey + "_Description",
                AllowMultipleInstances = allowMultipleInstances,
                SingleInstancePerPage = singleInstancePerPage
            };
        }
    }

    public static class ShowcaseTimelineOptions
    {
        private const string RangeOption = "TimelineRange";
        private const string LegacyRangeDaysOption = "RangeDays";

        public static TimelineRange GetRange(ShowcaseWidgetInstanceSettings instance)
        {
            if (instance?.Options != null &&
                instance.Options.TryGetValue(RangeOption, out var raw) &&
                Enum.TryParse(raw, true, out TimelineRange parsed) &&
                Enum.IsDefined(typeof(TimelineRange), parsed))
            {
                return parsed;
            }

            var legacyDays = instance?.GetOption(LegacyRangeDaysOption, 90) ?? 90;
            if (legacyDays <= 31)
            {
                return TimelineRange.OneMonth;
            }

            if (legacyDays <= 93)
            {
                return TimelineRange.ThreeMonths;
            }

            if (legacyDays <= 366)
            {
                return TimelineRange.OneYear;
            }

            return TimelineRange.All;
        }

        public static void SetRange(
            ShowcaseWidgetInstanceSettings instance,
            TimelineRange range)
        {
            if (instance == null)
            {
                return;
            }

            instance.SetOption(RangeOption, range);
            instance.Options?.Remove(LegacyRangeDaysOption);
        }
    }
}

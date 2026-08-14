using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Models
{
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
        public WidgetViewportDensity Density { get; private set; }

        public WidgetViewportOrientation Orientation { get; private set; }

        public bool ShowSecondaryStatistics => Density != WidgetViewportDensity.Compact;

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
                Density = density,
                Orientation = orientation
            };
        }
    }

    public sealed class ShowcaseWidgetDefinition
    {
        public ShowcaseWidgetKind Kind { get; set; }

        public string NameKey { get; set; }

        public string GlyphKey { get; set; }

        public bool AllowMultipleInstances { get; set; }

        public bool SingleInstancePerPage { get; set; }
    }

    public static class ShowcaseWidgetCatalog
    {
        private static readonly IReadOnlyList<ShowcaseWidgetDefinition> DefinitionsValue =
            new List<ShowcaseWidgetDefinition>
            {
                Define(ShowcaseWidgetKind.Profile, "LOCPlayAch_Showcase_Widget_Profile", "", false, true),
                Define(ShowcaseWidgetKind.Scores, "LOCPlayAch_Showcase_Widget_Scores", "", false, false),
                Define(ShowcaseWidgetKind.Pie, "LOCPlayAch_Showcase_Widget_Pie", "", true, false),
                Define(ShowcaseWidgetKind.Timeline, "LOCPlayAch_Showcase_Widget_Timeline", "", true, false),
                Define(ShowcaseWidgetKind.Statistics, "LOCPlayAch_Showcase_Widget_Statistics", "", true, false),
                Define(ShowcaseWidgetKind.NativePoints, "LOCPlayAch_Showcase_Widget_NativePoints", "", true, false),
                Define(ShowcaseWidgetKind.PinnedAchievements, "LOCPlayAch_Showcase_Widget_PinnedAchievements", "", false, true),
                Define(ShowcaseWidgetKind.FavoriteGames, "LOCPlayAch_Showcase_Widget_FavoriteGames", "", false, true),
                Define(ShowcaseWidgetKind.IconMosaic, "LOCPlayAch_Showcase_Widget_IconMosaic", "", true, false),
                Define(ShowcaseWidgetKind.ScreenshotSlideshow, "LOCPlayAch_Showcase_Widget_ScreenshotSlideshow", "", true, false),
                Define(ShowcaseWidgetKind.RecentAchievements, "LOCPlayAch_Showcase_Widget_RecentAchievements", "", true, false),
                Define(ShowcaseWidgetKind.GameSummaries, "LOCPlayAch_Showcase_Widget_GameSummaries", "", true, false),
                Define(ShowcaseWidgetKind.GameMosaic, "LOCPlayAch_Showcase_Widget_GameMosaic", "", true, false),
                Define(ShowcaseWidgetKind.ActivityCalendar, "LOCPlayAch_Showcase_Widget_ActivityCalendar", "", true, false)
            };

        public static IReadOnlyList<ShowcaseWidgetDefinition> Definitions => DefinitionsValue;

        public static ShowcaseWidgetDefinition Get(ShowcaseWidgetKind kind)
        {
            return DefinitionsValue.First(definition => definition.Kind == kind);
        }

        private static ShowcaseWidgetDefinition Define(
            ShowcaseWidgetKind kind,
            string nameKey,
            string glyphKey,
            bool allowMultipleInstances,
            bool singleInstancePerPage)
        {
            return new ShowcaseWidgetDefinition
            {
                Kind = kind,
                NameKey = nameKey,
                GlyphKey = glyphKey,
                AllowMultipleInstances = allowMultipleInstances,
                SingleInstancePerPage = singleInstancePerPage
            };
        }
    }

    /// <summary>
    /// Builds and maintains the grid surface keys used by showcase grid widgets. Multi-instance
    /// grid kinds persist column layout per widget instance under "&lt;BaseKey&gt;:&lt;instanceId&gt;";
    /// the single-instance pinned widgets keep their bare base key so re-adding them retains the
    /// layout. Orphaned per-instance surfaces are pruned against the live widget instances.
    /// </summary>
    public static class ShowcaseGridSurfaces
    {
        public const string PinnedAchievements = "ShowcasePinnedAchievements";
        public const string RecentAchievements = "ShowcaseRecentAchievements";
        public const string PinnedGames = "ShowcasePinnedGames";
        public const string GameSummaries = "ShowcaseGameSummaries";

        private const char InstanceSeparator = ':';

        public static string ForInstance(string baseKey, string instanceId)
        {
            return string.IsNullOrWhiteSpace(instanceId)
                ? baseKey
                : baseKey + InstanceSeparator + instanceId.Trim();
        }

        public static string GetBaseKey(string columnSettingsKey)
        {
            if (string.IsNullOrWhiteSpace(columnSettingsKey))
            {
                return columnSettingsKey;
            }

            var separator = columnSettingsKey.IndexOf(InstanceSeparator);
            return separator < 0 ? columnSettingsKey : columnSettingsKey.Substring(0, separator);
        }

        public static bool IsAchievementSurface(string columnSettingsKey)
        {
            var baseKey = GetBaseKey(columnSettingsKey);
            return string.Equals(baseKey, PinnedAchievements, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseKey, RecentAchievements, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGameSurface(string columnSettingsKey)
        {
            var baseKey = GetBaseKey(columnSettingsKey);
            return string.Equals(baseKey, PinnedGames, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseKey, GameSummaries, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes persisted per-instance grid surfaces whose widget instance no longer exists
        /// (dashboard or start-page hosted). Bare base keys are never pruned.
        /// </summary>
        public static void PruneOrphaned(GridOptionsCatalog catalog, ShowcaseSettings showcase)
        {
            if (catalog == null || showcase == null)
            {
                return;
            }

            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var widget in showcase.WidgetInstances ?? new List<ShowcaseWidgetInstanceSettings>())
            {
                if (!string.IsNullOrWhiteSpace(widget?.InstanceId))
                {
                    live.Add(widget.InstanceId.Trim());
                }
            }

            foreach (var widget in (showcase.StartPageInstances ??
                new Dictionary<string, ShowcaseWidgetInstanceSettings>()).Values)
            {
                if (!string.IsNullOrWhiteSpace(widget?.InstanceId))
                {
                    live.Add(widget.InstanceId.Trim());
                }
            }

            foreach (var key in catalog.Achievement.Keys
                .Where(key => IsOrphanedInstanceKey(key, IsAchievementSurface, live))
                .ToList())
            {
                catalog.RemoveAchievement(key);
            }

            foreach (var key in catalog.GameSummaries.Keys
                .Where(key => IsOrphanedInstanceKey(key, IsGameSurface, live))
                .ToList())
            {
                catalog.RemoveGameSummaries(key);
            }
        }

        private static bool IsOrphanedInstanceKey(
            string key,
            Func<string, bool> isShowcaseSurface,
            HashSet<string> liveInstanceIds)
        {
            if (string.IsNullOrWhiteSpace(key) || !isShowcaseSurface(key))
            {
                return false;
            }

            var separator = key.IndexOf(InstanceSeparator);
            if (separator < 0)
            {
                return false;
            }

            var instanceId = key.Substring(separator + 1).Trim();
            return instanceId.Length > 0 && !liveInstanceIds.Contains(instanceId);
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

    /// <summary>
    /// Owns persisted option names, defaults, and defensive range validation.
    /// Renderers and editors should not interpret the option dictionary directly.
    /// </summary>
    public static class ShowcaseWidgetOptions
    {
        private const string Mode = "Mode";
        private const string Grouping = "Grouping";
        private const string TopN = "TopN";
        private const string Source = "Source";
        private const string Count = "Count";
        private const string Variant = "Variant";
        private const string IntervalSeconds = "IntervalSeconds";
        private const string FitMode = "FitMode";
        private const string Shuffle = "Shuffle";
        private const string HideCompleted = "HideCompleted";

        public static ShowcaseScoreMode GetScoreMode(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Mode, ShowcaseScoreMode.Dual);

        public static void SetScoreMode(ShowcaseWidgetInstanceSettings settings, ShowcaseScoreMode value) =>
            settings?.SetOption(Mode, value);

        public static ShowcasePieMode GetPieMode(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Mode, ShowcasePieMode.CompletedGames);

        public static void SetPieMode(ShowcaseWidgetInstanceSettings settings, ShowcasePieMode value) =>
            settings?.SetOption(Mode, value);

        public static ShowcasePointsGrouping GetPointsGrouping(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Grouping, ShowcasePointsGrouping.Provider);

        public static void SetPointsGrouping(
            ShowcaseWidgetInstanceSettings settings,
            ShowcasePointsGrouping value) => settings?.SetOption(Grouping, value);

        public static int GetTopN(ShowcaseWidgetInstanceSettings settings) =>
            Clamp(settings?.GetOption(TopN, 8) ?? 8, 1, 25);

        public static void SetTopN(ShowcaseWidgetInstanceSettings settings, int value) =>
            settings?.SetOption(TopN, Clamp(value, 1, 25));

        public static ShowcaseFavoriteGameSource GetFavoriteSource(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Source, ShowcaseFavoriteGameSource.ShowcasePins);

        public static void SetFavoriteSource(
            ShowcaseWidgetInstanceSettings settings,
            ShowcaseFavoriteGameSource value) => settings?.SetOption(Source, value);

        public static ShowcaseMosaicSource GetMosaicSource(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Source, ShowcaseMosaicSource.Recent);

        public static void SetMosaicSource(
            ShowcaseWidgetInstanceSettings settings,
            ShowcaseMosaicSource value) => settings?.SetOption(Source, value);

        public static int GetMosaicCount(ShowcaseWidgetInstanceSettings settings) =>
            Clamp(settings?.GetOption(Count, 24) ?? 24, 1, 64);

        public static void SetMosaicCount(ShowcaseWidgetInstanceSettings settings, int value) =>
            settings?.SetOption(Count, Clamp(value, 1, 64));

        public static ShowcaseScreenshotVariant GetScreenshotVariant(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Variant, ShowcaseScreenshotVariant.All);

        public static void SetScreenshotVariant(
            ShowcaseWidgetInstanceSettings settings,
            ShowcaseScreenshotVariant value) => settings?.SetOption(Variant, value);

        public static int GetSlideshowIntervalSeconds(ShowcaseWidgetInstanceSettings settings) =>
            Clamp(settings?.GetOption(IntervalSeconds, 8) ?? 8, 1, 300);

        public static void SetSlideshowIntervalSeconds(
            ShowcaseWidgetInstanceSettings settings,
            int value) => settings?.SetOption(IntervalSeconds, Clamp(value, 1, 300));

        public static ShowcaseImageFitMode GetImageFitMode(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, FitMode, ShowcaseImageFitMode.Fill);

        public static void SetImageFitMode(
            ShowcaseWidgetInstanceSettings settings,
            ShowcaseImageFitMode value) => settings?.SetOption(FitMode, value);

        public static bool GetShuffle(ShowcaseWidgetInstanceSettings settings) =>
            settings?.GetOption(Shuffle, true) ?? true;

        public static void SetShuffle(ShowcaseWidgetInstanceSettings settings, bool value) =>
            settings?.SetOption(Shuffle, value);

        public static int GetRecentCount(ShowcaseWidgetInstanceSettings settings) =>
            Clamp(settings?.GetOption(Count, 15) ?? 15, 1, 100);

        public static void SetRecentCount(ShowcaseWidgetInstanceSettings settings, int value) =>
            settings?.SetOption(Count, Clamp(value, 1, 100));

        public static ShowcaseGameListSort GetGameListSort(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Mode, ShowcaseGameListSort.LastUnlock);

        public static void SetGameListSort(
            ShowcaseWidgetInstanceSettings settings,
            ShowcaseGameListSort value) => settings?.SetOption(Mode, value);

        public static int GetGameListCount(ShowcaseWidgetInstanceSettings settings) =>
            Clamp(settings?.GetOption(Count, 50) ?? 50, 1, 200);

        public static void SetGameListCount(ShowcaseWidgetInstanceSettings settings, int value) =>
            settings?.SetOption(Count, Clamp(value, 1, 200));

        public static bool GetHideCompleted(ShowcaseWidgetInstanceSettings settings) =>
            settings?.GetOption(HideCompleted, false) ?? false;

        public static void SetHideCompleted(ShowcaseWidgetInstanceSettings settings, bool value) =>
            settings?.SetOption(HideCompleted, value);

        public static ShowcaseGameMosaicSource GetGameMosaicSource(ShowcaseWidgetInstanceSettings settings) =>
            GetEnum(settings, Source, ShowcaseGameMosaicSource.Completed);

        public static void SetGameMosaicSource(
            ShowcaseWidgetInstanceSettings settings,
            ShowcaseGameMosaicSource value) => settings?.SetOption(Source, value);

        public static int GetGameMosaicCount(ShowcaseWidgetInstanceSettings settings) =>
            Clamp(settings?.GetOption(Count, 24) ?? 24, 1, 64);

        public static void SetGameMosaicCount(ShowcaseWidgetInstanceSettings settings, int value) =>
            settings?.SetOption(Count, Clamp(value, 1, 64));

        private static T GetEnum<T>(
            ShowcaseWidgetInstanceSettings settings,
            string key,
            T fallback)
            where T : struct
        {
            var value = settings?.GetOption(key, fallback) ?? fallback;
            return Enum.IsDefined(typeof(T), value) ? value : fallback;
        }

        private static int Clamp(int value, int minimum, int maximum) =>
            Math.Max(minimum, Math.Min(maximum, value));
    }

    public static class ShowcaseWidgetSettingsFactory
    {
        public static ShowcaseWidgetInstanceSettings CreateDefault(
            ShowcaseWidgetKind kind,
            string instanceId = null)
        {
            var settings = new ShowcaseWidgetInstanceSettings
            {
                Kind = kind
            };
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                settings.InstanceId = instanceId.Trim();
            }

            switch (kind)
            {
                case ShowcaseWidgetKind.Scores:
                    ShowcaseWidgetOptions.SetScoreMode(settings, ShowcaseScoreMode.Dual);
                    ShowcaseTimelineOptions.SetRange(settings, TimelineRange.ThreeMonths);
                    break;
                case ShowcaseWidgetKind.Pie:
                    ShowcaseWidgetOptions.SetPieMode(settings, ShowcasePieMode.CompletedGames);
                    break;
                case ShowcaseWidgetKind.Timeline:
                    ShowcaseTimelineOptions.SetRange(settings, TimelineRange.ThreeMonths);
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    ShowcaseWidgetOptions.SetPointsGrouping(settings, ShowcasePointsGrouping.Provider);
                    ShowcaseWidgetOptions.SetTopN(settings, 8);
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    ShowcaseWidgetOptions.SetFavoriteSource(settings, ShowcaseFavoriteGameSource.ShowcasePins);
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    ShowcaseWidgetOptions.SetMosaicSource(settings, ShowcaseMosaicSource.Recent);
                    ShowcaseWidgetOptions.SetMosaicCount(settings, 24);
                    break;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    ShowcaseWidgetOptions.SetScreenshotVariant(settings, ShowcaseScreenshotVariant.All);
                    ShowcaseWidgetOptions.SetShuffle(settings, true);
                    ShowcaseWidgetOptions.SetSlideshowIntervalSeconds(settings, 8);
                    ShowcaseWidgetOptions.SetImageFitMode(settings, ShowcaseImageFitMode.Fill);
                    break;
                case ShowcaseWidgetKind.RecentAchievements:
                    ShowcaseWidgetOptions.SetRecentCount(settings, 15);
                    break;
                case ShowcaseWidgetKind.GameSummaries:
                    ShowcaseWidgetOptions.SetGameListSort(settings, ShowcaseGameListSort.LastUnlock);
                    ShowcaseWidgetOptions.SetGameListCount(settings, 50);
                    ShowcaseWidgetOptions.SetHideCompleted(settings, false);
                    break;
                case ShowcaseWidgetKind.GameMosaic:
                    ShowcaseWidgetOptions.SetGameMosaicSource(settings, ShowcaseGameMosaicSource.Completed);
                    ShowcaseWidgetOptions.SetGameMosaicCount(settings, 24);
                    break;
                case ShowcaseWidgetKind.ActivityCalendar:
                    ShowcaseTimelineOptions.SetRange(settings, TimelineRange.OneYear);
                    break;
            }

            return settings;
        }
    }
}

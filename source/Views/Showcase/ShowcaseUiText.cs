using System;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Views.Showcase
{
    internal static class ShowcaseUiText
    {
        public static string Localize(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }

        public static string LocalizeValue(string key, string value)
        {
            return string.IsNullOrWhiteSpace(key) ? value ?? string.Empty : Localize(key);
        }

        public static string GetWidgetName(ShowcaseWidgetKind kind)
        {
            var definition = ShowcaseWidgetCatalog.Get(kind);
            return Localize(definition.NameKey);
        }

        public static string ScoreModeName(ShowcaseScoreMode value) =>
            EnumValueName("LOCPlayAch_Showcase_ScoreMode_", value);

        public static string PieModeName(ShowcasePieMode value) =>
            EnumValueName("LOCPlayAch_Showcase_PieMode_", value);

        public static string PointsGroupingName(ShowcasePointsGrouping value) =>
            EnumValueName("LOCPlayAch_Showcase_PointsGrouping_", value);

        public static string FavoriteSourceName(ShowcaseFavoriteGameSource value) =>
            EnumValueName("LOCPlayAch_Showcase_FavoriteSource_", value);

        public static string MosaicSourceName(ShowcaseMosaicSource value) =>
            EnumValueName("LOCPlayAch_Showcase_MosaicSource_", value);

        public static string ScreenshotVariantName(ShowcaseScreenshotVariant value) =>
            EnumValueName("LOCPlayAch_Showcase_ScreenshotVariant_", value);

        public static string GameListSortName(ShowcaseGameListSort value) =>
            EnumValueName("LOCPlayAch_Showcase_GameListSort_", value);

        public static string GameMosaicSourceName(ShowcaseGameMosaicSource value) =>
            EnumValueName("LOCPlayAch_Showcase_GameMosaicSource_", value);

        public static string FitModeName(ShowcaseImageFitMode value) =>
            EnumValueName("LOCPlayAch_Showcase_ImageFit_", value);

        public static string TimelineRangeName(TimelineRange range)
        {
            switch (range)
            {
                case TimelineRange.OneMonth:
                    return Localize("LOCPlayAch_TimeRange_1M");
                case TimelineRange.ThreeMonths:
                    return Localize("LOCPlayAch_TimeRange_3M");
                case TimelineRange.OneYear:
                    return Localize("LOCPlayAch_TimeRange_1Y");
                case TimelineRange.All:
                    return Localize("LOCPlayAch_Common_All");
                default:
                    return range.ToString();
            }
        }

        private static string EnumValueName<T>(string prefix, T value)
        {
            return Localize(prefix + Convert.ToString(value));
        }
    }
}

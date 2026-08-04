using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.StartPage
{
    public sealed class StartPageViewDefinition
    {
        public string ViewId { get; set; }

        public StartPageWidgetKind WidgetKind { get; set; }

        public string NameKey { get; set; }

        public string DescriptionKey { get; set; }

        public ShowcaseWidgetKind? ShowcaseWidgetKind { get; set; }

        public bool HasSettings { get; set; }

        public bool AllowMultipleInstances { get; set; }
    }

    public static class StartPageViewCatalog
    {
        public const string GameSummariesGridViewId = "PlayniteAchievements_GameSummariesGrid";
        public const string LegacyGamesOverviewGridViewId = "PlayniteAchievements_GamesOverviewGrid";
        public const string RecentUnlocksGridViewId = "PlayniteAchievements_RecentUnlocksGrid";
        public const string FriendsRecentUnlocksGridViewId = "PlayniteAchievements_FriendsRecentUnlocksGrid";
        public const string CompletedGamesPieViewId = "PlayniteAchievements_CompletedGamesPie";
        public const string ProviderPieViewId = "PlayniteAchievements_ProviderPie";
        public const string RarityPieViewId = "PlayniteAchievements_RarityPie";
        public const string TrophyPieViewId = "PlayniteAchievements_TrophyPie";
        public const string CollectionScoreCardViewId = "PlayniteAchievements_CollectionScoreCard";
        public const string PrestigeScoreCardViewId = "PlayniteAchievements_PrestigeScoreCard";
        public const string ShowcaseProfileViewId = "PlayniteAchievements_Showcase_Profile";
        public const string ShowcaseDualScoresViewId = "PlayniteAchievements_Showcase_DualScores";
        public const string ShowcaseTimelineViewId = "PlayniteAchievements_Showcase_Timeline";
        public const string ShowcaseStatisticsViewId = "PlayniteAchievements_Showcase_Statistics";
        public const string ShowcaseNativePointsViewId = "PlayniteAchievements_Showcase_NativePoints";
        public const string ShowcasePinnedAchievementsViewId = "PlayniteAchievements_Showcase_PinnedAchievements";
        public const string ShowcaseFavoriteGamesViewId = "PlayniteAchievements_Showcase_FavoriteGames";
        public const string ShowcaseIconMosaicViewId = "PlayniteAchievements_Showcase_IconMosaic";
        public const string ShowcaseScreenshotSlideshowViewId = "PlayniteAchievements_Showcase_ScreenshotSlideshow";

        private static readonly IReadOnlyList<StartPageViewDefinition> ViewDefinitions =
            new List<StartPageViewDefinition>
            {
                new StartPageViewDefinition
                {
                    ViewId = GameSummariesGridViewId,
                    WidgetKind = StartPageWidgetKind.GameSummariesGrid,
                    NameKey = "LOCPlayAch_Overview_GameSummaries",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = RecentUnlocksGridViewId,
                    WidgetKind = StartPageWidgetKind.RecentUnlocksGrid,
                    NameKey = "LOCPlayAch_RecentAchievements",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = FriendsRecentUnlocksGridViewId,
                    WidgetKind = StartPageWidgetKind.FriendsRecentUnlocksGrid,
                    NameKey = "LOCPlayAch_StartPage_FriendsRecentAchievements",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = CompletedGamesPieViewId,
                    WidgetKind = StartPageWidgetKind.CompletedGamesPie,
                    NameKey = "LOCPlayAch_Overview_GamesPieChart",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = ProviderPieViewId,
                    WidgetKind = StartPageWidgetKind.ProviderPie,
                    NameKey = "LOCPlayAch_Overview_ProviderDistribution",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = RarityPieViewId,
                    WidgetKind = StartPageWidgetKind.RarityPie,
                    NameKey = "LOCPlayAch_Overview_RarityPieChart",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = TrophyPieViewId,
                    WidgetKind = StartPageWidgetKind.TrophyPie,
                    NameKey = "LOCPlayAch_Overview_TrophyPieChart",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = CollectionScoreCardViewId,
                    WidgetKind = StartPageWidgetKind.CollectionScoreCard,
                    NameKey = "LOCPlayAch_Score_Collection",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = PrestigeScoreCardViewId,
                    WidgetKind = StartPageWidgetKind.PrestigeScoreCard,
                    NameKey = "LOCPlayAch_Score_Prestige",
                    DescriptionKey = null
                },
                Shared(
                    ShowcaseProfileViewId,
                    StartPageWidgetKind.ShowcaseProfile,
                    ShowcaseWidgetKind.Profile,
                    allowMultiple: false,
                    hasSettings: false),
                Shared(
                    ShowcaseDualScoresViewId,
                    StartPageWidgetKind.ShowcaseDualScores,
                    ShowcaseWidgetKind.Scores,
                    allowMultiple: false,
                    hasSettings: false),
                Shared(
                    ShowcaseTimelineViewId,
                    StartPageWidgetKind.ShowcaseTimeline,
                    ShowcaseWidgetKind.Timeline,
                    allowMultiple: true,
                    hasSettings: true),
                Shared(
                    ShowcaseStatisticsViewId,
                    StartPageWidgetKind.ShowcaseStatistics,
                    ShowcaseWidgetKind.Statistics,
                    allowMultiple: true,
                    hasSettings: false),
                Shared(
                    ShowcaseNativePointsViewId,
                    StartPageWidgetKind.ShowcaseNativePoints,
                    ShowcaseWidgetKind.NativePoints,
                    allowMultiple: true,
                    hasSettings: true),
                Shared(
                    ShowcasePinnedAchievementsViewId,
                    StartPageWidgetKind.ShowcasePinnedAchievements,
                    ShowcaseWidgetKind.PinnedAchievements,
                    allowMultiple: false,
                    hasSettings: false),
                Shared(
                    ShowcaseFavoriteGamesViewId,
                    StartPageWidgetKind.ShowcaseFavoriteGames,
                    ShowcaseWidgetKind.FavoriteGames,
                    allowMultiple: false,
                    hasSettings: true),
                Shared(
                    ShowcaseIconMosaicViewId,
                    StartPageWidgetKind.ShowcaseIconMosaic,
                    ShowcaseWidgetKind.IconMosaic,
                    allowMultiple: true,
                    hasSettings: true),
                Shared(
                    ShowcaseScreenshotSlideshowViewId,
                    StartPageWidgetKind.ShowcaseScreenshotSlideshow,
                    ShowcaseWidgetKind.ScreenshotSlideshow,
                    allowMultiple: true,
                    hasSettings: true)
            };

        public static IReadOnlyList<StartPageViewDefinition> Views => ViewDefinitions;

        public static bool TryGetDefinition(string viewId, out StartPageViewDefinition definition)
        {
            definition = ViewDefinitions.FirstOrDefault(view =>
                string.Equals(view.ViewId, viewId, StringComparison.Ordinal));
            if (definition == null &&
                string.Equals(viewId, LegacyGamesOverviewGridViewId, StringComparison.Ordinal))
            {
                definition = ViewDefinitions.FirstOrDefault(view =>
                    string.Equals(view.ViewId, GameSummariesGridViewId, StringComparison.Ordinal));
            }

            return definition != null;
        }

        private static StartPageViewDefinition Shared(
            string viewId,
            StartPageWidgetKind startPageKind,
            ShowcaseWidgetKind showcaseKind,
            bool allowMultiple,
            bool hasSettings)
        {
            var definition = ShowcaseWidgetCatalog.Get(showcaseKind);
            return new StartPageViewDefinition
            {
                ViewId = viewId,
                WidgetKind = startPageKind,
                ShowcaseWidgetKind = showcaseKind,
                NameKey = definition.NameKey,
                DescriptionKey = definition.DescriptionKey,
                AllowMultipleInstances = allowMultiple,
                HasSettings = hasSettings
            };
        }
    }
}

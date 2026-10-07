using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements.Scoring;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.StartPage
{
    public sealed class StartPageViewDefinition
    {
        public string ViewId { get; set; }

        public StartPageWidgetKind WidgetKind { get; set; }

        public string NameKey { get; set; }

        public ShowcaseWidgetKind? ShowcaseWidgetKind { get; set; }

        public bool HasSettings { get; set; }

        public bool AllowMultipleInstances { get; set; }

        /// <summary>
        /// Hidden views are omitted from the StartPage add list but still resolve by id, so
        /// views placed before their widget kind was collapsed or disabled keep rendering.
        /// </summary>
        public bool Hidden { get; set; }
    }

    public static class StartPageViewCatalog
    {
        public const string GameSummariesGridViewId = "PlayniteAchievements_GameSummariesGrid";
        public const string LegacyGamesOverviewGridViewId = "PlayniteAchievements_GamesOverviewGrid";
        public const string RecentUnlocksGridViewId = "PlayniteAchievements_RecentUnlocksGrid";
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
        public const string ShowcaseIconMosaicViewId = "PlayniteAchievements_Showcase_IconMosaic";
        public const string ShowcaseScreenshotSlideshowViewId = "PlayniteAchievements_Showcase_ScreenshotSlideshow";
        public const string ShowcaseActivityCalendarViewId = "PlayniteAchievements_Showcase_ActivityCalendar";

        private static readonly IReadOnlyList<StartPageViewDefinition> ViewDefinitions =
            new List<StartPageViewDefinition>
            {
                // The two self grids kept their original view ids when they moved onto the
                // showcase widget path, so widgets already placed on users' start pages keep
                // working; their per-instance surfaces are seeded from the fixed StartPage
                // surfaces on first load.
                new StartPageViewDefinition
                {
                    ViewId = GameSummariesGridViewId,
                    WidgetKind = StartPageWidgetKind.GameSummariesGrid,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.GameSummaries,
                    NameKey = "LOCPlayAch_Overview_GameSummaries",
                    HasSettings = true,
                    AllowMultipleInstances = true
                },
                new StartPageViewDefinition
                {
                    ViewId = RecentUnlocksGridViewId,
                    WidgetKind = StartPageWidgetKind.RecentUnlocksGrid,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.RecentAchievements,
                    NameKey = "LOCPlayAch_RecentAchievements",
                    HasSettings = true,
                    AllowMultipleInstances = true
                },
                // The four pie views ride the showcase Pie widget under their original view
                // ids; each seeds its pie mode at instance creation (see
                // GetOrCreateStartPageWidgetSettings) and edits per-widget pie options.
                new StartPageViewDefinition
                {
                    ViewId = CompletedGamesPieViewId,
                    WidgetKind = StartPageWidgetKind.CompletedGamesPie,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Pie,
                    NameKey = "LOCPlayAch_Overview_GamesPieChart",
                    HasSettings = true,
                    AllowMultipleInstances = true
                },
                new StartPageViewDefinition
                {
                    ViewId = ProviderPieViewId,
                    WidgetKind = StartPageWidgetKind.ProviderPie,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Pie,
                    NameKey = "LOCPlayAch_Overview_ProviderDistribution",
                    HasSettings = true,
                    AllowMultipleInstances = true
                },
                new StartPageViewDefinition
                {
                    ViewId = RarityPieViewId,
                    WidgetKind = StartPageWidgetKind.RarityPie,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Pie,
                    NameKey = "LOCPlayAch_Overview_RarityPieChart",
                    HasSettings = true,
                    AllowMultipleInstances = true
                },
                new StartPageViewDefinition
                {
                    ViewId = TrophyPieViewId,
                    WidgetKind = StartPageWidgetKind.TrophyPie,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Pie,
                    NameKey = "LOCPlayAch_Overview_TrophyPieChart",
                    HasSettings = true,
                    AllowMultipleInstances = true
                },
                // The two standalone score card views ride the showcase Scores widget under
                // their original ids, hidden from the add list: an already-placed view seeds its
                // card type (Collection or Prestige) at instance creation (see
                // GetOrCreateStartPageWidgetSettings). New score cards come from the Score Card
                // entry below.
                new StartPageViewDefinition
                {
                    ViewId = CollectionScoreCardViewId,
                    WidgetKind = StartPageWidgetKind.CollectionScoreCard,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Scores,
                    NameKey = "LOCPlayAch_Score_Collection",
                    HasSettings = true,
                    AllowMultipleInstances = true,
                    Hidden = true
                },
                new StartPageViewDefinition
                {
                    ViewId = PrestigeScoreCardViewId,
                    WidgetKind = StartPageWidgetKind.PrestigeScoreCard,
                    ShowcaseWidgetKind = PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Scores,
                    NameKey = "LOCPlayAch_Score_Prestige",
                    HasSettings = true,
                    AllowMultipleInstances = true,
                    Hidden = true
                },
                Shared(
                    ShowcaseProfileViewId,
                    StartPageWidgetKind.ShowcaseProfile,
                    ShowcaseWidgetKind.Profile,
                    hasSettings: true),
                // The one visible Score Card entry. It keeps the id of the former dual-score view,
                // so a placed one stays put; its saved Both choice reads as the Collection card.
                Shared(
                    ShowcaseDualScoresViewId,
                    StartPageWidgetKind.ShowcaseDualScores,
                    ShowcaseWidgetKind.Scores,
                    hasSettings: true),
                Shared(
                    ShowcaseTimelineViewId,
                    StartPageWidgetKind.ShowcaseTimeline,
                    ShowcaseWidgetKind.Timeline,
                    hasSettings: true),
                Shared(
                    ShowcaseStatisticsViewId,
                    StartPageWidgetKind.ShowcaseStatistics,
                    ShowcaseWidgetKind.Statistics,
                    hasSettings: false),
                // NativePoints stays resolvable for already-placed start-page views; Shared
                // marks it hidden from the add list because its showcase kind is hidden in the
                // widget catalog.
                Shared(
                    ShowcaseNativePointsViewId,
                    StartPageWidgetKind.ShowcaseNativePoints,
                    ShowcaseWidgetKind.NativePoints,
                    hasSettings: true),
                Shared(
                    ShowcaseIconMosaicViewId,
                    StartPageWidgetKind.ShowcaseIconMosaic,
                    ShowcaseWidgetKind.IconMosaic,
                    hasSettings: true),
                Shared(
                    ShowcaseScreenshotSlideshowViewId,
                    StartPageWidgetKind.ShowcaseScreenshotSlideshow,
                    ShowcaseWidgetKind.ScreenshotSlideshow,
                    hasSettings: true),
                Shared(
                    ShowcaseActivityCalendarViewId,
                    StartPageWidgetKind.ShowcaseActivityCalendar,
                    ShowcaseWidgetKind.ActivityCalendar,
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

        /// <summary>
        /// Seeds a new start page instance with the option its view id has always shown: the
        /// four pie views share the showcase Pie kind and each seeds its distribution, and the
        /// former standalone Prestige score card seeds the Prestige card. Every other view keeps
        /// the showcase defaults.
        /// </summary>
        public static void SeedViewOptions(string viewId, ShowcaseWidgetInstanceSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.Kind == PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Pie)
            {
                ShowcaseWidgetOptions.SetPieMode(settings, ResolvePieMode(viewId));
            }

            if (settings.Kind == PlayniteAchievements.Models.Settings.ShowcaseWidgetKind.Scores &&
                string.Equals(viewId, PrestigeScoreCardViewId, StringComparison.Ordinal))
            {
                ShowcaseWidgetOptions.SetScoreCardType(settings, ScoreCardType.Prestige);
            }
        }

        private static ShowcasePieMode ResolvePieMode(string viewId)
        {
            switch (viewId)
            {
                case ProviderPieViewId:
                    return ShowcasePieMode.Provider;
                case RarityPieViewId:
                    return ShowcasePieMode.Rarity;
                case TrophyPieViewId:
                    return ShowcasePieMode.Trophy;
                default:
                    return ShowcasePieMode.CompletedGames;
            }
        }

        private static StartPageViewDefinition Shared(
            string viewId,
            StartPageWidgetKind startPageKind,
            ShowcaseWidgetKind showcaseKind,
            bool hasSettings)
        {
            var definition = ShowcaseWidgetCatalog.Get(showcaseKind);
            return new StartPageViewDefinition
            {
                ViewId = viewId,
                WidgetKind = startPageKind,
                ShowcaseWidgetKind = showcaseKind,
                NameKey = definition.NameKey,
                AllowMultipleInstances = definition.AllowMultipleInstances,
                HasSettings = hasSettings,
                Hidden = definition.Hidden
            };
        }
    }
}

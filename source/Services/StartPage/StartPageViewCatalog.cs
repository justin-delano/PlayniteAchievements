using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.StartPage
{
    public sealed class StartPageViewDefinition
    {
        public string ViewId { get; set; }

        public StartPageWidgetKind WidgetKind { get; set; }

        public string NameKey { get; set; }

        public string DescriptionKey { get; set; }
    }

    public static class StartPageViewCatalog
    {
        public const string GamesOverviewGridViewId = "PlayniteAchievements_GamesOverviewGrid";
        public const string RecentUnlocksGridViewId = "PlayniteAchievements_RecentUnlocksGrid";
        public const string CompletedGamesPieViewId = "PlayniteAchievements_CompletedGamesPie";
        public const string ProviderPieViewId = "PlayniteAchievements_ProviderPie";
        public const string RarityPieViewId = "PlayniteAchievements_RarityPie";
        public const string TrophyPieViewId = "PlayniteAchievements_TrophyPie";

        private static readonly IReadOnlyList<StartPageViewDefinition> ViewDefinitions =
            new List<StartPageViewDefinition>
            {
                new StartPageViewDefinition
                {
                    ViewId = GamesOverviewGridViewId,
                    WidgetKind = StartPageWidgetKind.GamesOverviewGrid,
                    NameKey = "LOCPlayAch_Sidebar_GamesOverview",
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
                    ViewId = CompletedGamesPieViewId,
                    WidgetKind = StartPageWidgetKind.CompletedGamesPie,
                    NameKey = "LOCPlayAch_Sidebar_GamesPieChart",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = ProviderPieViewId,
                    WidgetKind = StartPageWidgetKind.ProviderPie,
                    NameKey = "LOCPlayAch_Sidebar_ProviderDistribution",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = RarityPieViewId,
                    WidgetKind = StartPageWidgetKind.RarityPie,
                    NameKey = "LOCPlayAch_Sidebar_RarityPieChart",
                    DescriptionKey = null
                },
                new StartPageViewDefinition
                {
                    ViewId = TrophyPieViewId,
                    WidgetKind = StartPageWidgetKind.TrophyPie,
                    NameKey = "LOCPlayAch_Sidebar_TrophyPieChart",
                    DescriptionKey = null
                }
            };

        public static IReadOnlyList<StartPageViewDefinition> Views => ViewDefinitions;

        public static bool TryGetDefinition(string viewId, out StartPageViewDefinition definition)
        {
            definition = ViewDefinitions.FirstOrDefault(view =>
                string.Equals(view.ViewId, viewId, StringComparison.Ordinal));
            return definition != null;
        }
    }
}

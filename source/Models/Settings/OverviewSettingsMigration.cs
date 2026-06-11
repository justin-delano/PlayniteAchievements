using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Migrates the old Sidebar/GamesOverview settings shape to Overview/GameSummaries.
    /// Runs before settings deserialization so existing user config values are preserved.
    /// </summary>
    public static class OverviewSettingsMigration
    {
        private static readonly (string OldName, string NewName)[] PropertyRenames =
        {
            ("ShowSidebarCollectionScoreCard", "ShowOverviewCollectionScoreCard"),
            ("ShowSidebarPrestigeScoreCard", "ShowOverviewPrestigeScoreCard"),
            ("ShowSidebarPieCharts", "ShowOverviewPieCharts"),
            ("ShowSidebarGamesPieChart", "ShowOverviewGamesPieChart"),
            ("ShowSidebarProviderPieChart", "ShowOverviewProviderPieChart"),
            ("ShowSidebarRarityPieChart", "ShowOverviewRarityPieChart"),
            ("ShowSidebarTrophyPieChart", "ShowOverviewTrophyPieChart"),
            ("ShowSidebarPiePercentages", "ShowOverviewPiePercentages"),
            ("SidebarPieSmallSliceMode", "OverviewPieSmallSliceMode"),
            ("ShowSidebarBarCharts", "ShowOverviewBarCharts"),
            ("ShowSidebarGameMetadata", "ShowOverviewGameMetadata"),
            ("ShowOverviewGridColumnHeaders", "ShowGameSummariesGridColumnHeaders"),
            ("GamesOverviewGridSortMode", "GameSummariesGridSortMode"),
            ("GamesOverviewGridSortDescending", "GameSummariesGridSortDescending"),
            ("SidebarSelectedGameGridSortMode", "OverviewSelectedGameGridSortMode"),
            ("SidebarSelectedGameGridSortDescending", "OverviewSelectedGameGridSortDescending"),
            ("SidebarOverviewGridRowHeight", "OverviewGameSummariesGridRowHeight"),
            ("SidebarRecentAchievementsGridRowHeight", "OverviewRecentAchievementsGridRowHeight"),
            ("SidebarSelectedGameGridRowHeight", "OverviewSelectedGameGridRowHeight"),
            ("StartPageGamesOverviewGridRowHeight", "StartPageGameSummariesGridRowHeight"),
            ("SidebarOverviewGridMaxRows", "OverviewGameSummariesGridMaxRows"),
            ("SidebarRecentAchievementsGridMaxRows", "OverviewRecentAchievementsGridMaxRows"),
            ("SidebarSelectedGameGridMaxRows", "OverviewSelectedGameGridMaxRows"),
            ("StartPageGamesOverviewGridMaxRows", "StartPageGameSummariesGridMaxRows"),
            ("StartPageGamesOverviewGrid", "StartPageGameSummariesGrid"),
            ("SidebarAchievementColumnWidths", "OverviewRecentAchievementColumnWidths"),
            ("SidebarAchievementColumnOrder", "OverviewRecentAchievementColumnOrder"),
            ("SidebarAchievementColumnAlignments", "OverviewRecentAchievementColumnAlignments"),
            ("SidebarGameColumnWidths", "OverviewSelectedGameAchievementColumnWidths"),
            ("SidebarGameColumnOrder", "OverviewSelectedGameAchievementColumnOrder"),
            ("SidebarGameColumnAlignments", "OverviewSelectedGameAchievementColumnAlignments"),
            ("GamesOverviewColumnVisibility", "GameSummariesColumnVisibility"),
            ("GamesOverviewColumnWidths", "GameSummariesColumnWidths"),
            ("GamesOverviewColumnOrder", "GameSummariesColumnOrder"),
            ("GamesOverviewColumnAlignments", "GameSummariesColumnAlignments"),
            ("StartPageGamesOverviewColumnVisibility", "StartPageGameSummariesColumnVisibility"),
            ("StartPageGamesOverviewColumnWidths", "StartPageGameSummariesColumnWidths"),
            ("StartPageGamesOverviewColumnOrder", "StartPageGameSummariesColumnOrder"),
            ("StartPageGamesOverviewColumnAlignments", "StartPageGameSummariesColumnAlignments"),
            ("SidebarOverviewLeftColumnRatio", "OverviewLeftColumnRatio"),
            ("SidebarTimelineRange", "OverviewTimelineRange")
        };

        private static readonly (string OldName, string NewName)[] GameSummaryColumnRenames =
        {
            ("OverviewGameName", "GameSummaryName"),
            ("OverviewPlatform", "GameSummaryPlatform"),
            ("OverviewLastPlayed", "GameSummaryLastPlayed"),
            ("OverviewPlaytime", "GameSummaryPlaytime"),
            ("OverviewProgression", "GameSummaryProgression"),
            ("OverviewCollectionScore", "GameSummaryCollectionScore"),
            ("OverviewPrestigeScore", "GameSummaryPrestigeScore"),
            ("OverviewProvider", "GameSummaryProvider")
        };

        private static readonly string[] GameSummaryColumnDictionaries =
        {
            "GameSummariesColumnVisibility",
            "GameSummariesColumnWidths",
            "GameSummariesColumnOrder",
            "GameSummariesColumnAlignments",
            "StartPageGameSummariesColumnVisibility",
            "StartPageGameSummariesColumnWidths",
            "StartPageGameSummariesColumnOrder",
            "StartPageGameSummariesColumnAlignments"
        };

        public static string MigrateFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            try
            {
                var root = JObject.Parse(json);
                var persisted = root["Persisted"] as JObject;
                if (persisted == null)
                {
                    return json;
                }

                var changed = false;
                foreach (var rename in PropertyRenames)
                {
                    changed |= MoveProperty(persisted, rename.OldName, rename.NewName);
                }

                foreach (var dictionaryName in GameSummaryColumnDictionaries)
                {
                    changed |= RenameColumnKeys(persisted[dictionaryName] as JObject);
                }

                return changed
                    ? root.ToString(Formatting.None)
                    : json;
            }
            catch (Exception)
            {
                return json;
            }
        }

        private static bool MoveProperty(JObject obj, string oldName, string newName)
        {
            if (obj == null || obj[oldName] == null)
            {
                return false;
            }

            if (obj[newName] == null)
            {
                obj[newName] = obj[oldName];
            }

            obj.Remove(oldName);
            return true;
        }

        private static bool RenameColumnKeys(JObject dictionary)
        {
            if (dictionary == null)
            {
                return false;
            }

            var changed = false;
            foreach (var rename in GameSummaryColumnRenames)
            {
                if (dictionary[rename.OldName] == null)
                {
                    continue;
                }

                if (dictionary[rename.NewName] == null)
                {
                    dictionary[rename.NewName] = dictionary[rename.OldName];
                }

                dictionary.Remove(rename.OldName);
                changed = true;
            }

            return changed;
        }
    }
}

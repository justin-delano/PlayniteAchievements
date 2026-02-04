using System;
using System.Collections.Generic;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Services.Sidebar
{
    public sealed class SidebarDataSnapshot
    {
        public List<AchievementDisplayItem> Achievements { get; set; } = new List<AchievementDisplayItem>();
        public List<GameOverviewItem> GamesOverview { get; set; } = new List<GameOverviewItem>();
        public List<RecentAchievementItem> RecentAchievements { get; set; } = new List<RecentAchievementItem>();

        public Dictionary<DateTime, int> GlobalUnlockCountsByDate { get; set; } =
            new Dictionary<DateTime, int>();

        public Dictionary<Guid, Dictionary<DateTime, int>> UnlockCountsByDateByGame { get; set; } =
            new Dictionary<Guid, Dictionary<DateTime, int>>();

        public int TotalGames { get; set; }
        public int TotalAchievements { get; set; }
        public int TotalUnlocked { get; set; }
        public int TotalCommon { get; set; }
        public int TotalUncommon { get; set; }
        public int TotalRare { get; set; }
        public int TotalUltraRare { get; set; }
        public int PerfectGames { get; set; }
        public double GlobalProgressionPercent { get; set; }
        public double LaunchedProgressionPercent { get; set; }
    }
}

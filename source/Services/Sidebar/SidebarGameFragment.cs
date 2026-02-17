using System;
using System.Collections.Generic;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Services.Sidebar
{
    public sealed class SidebarGameFragment
    {
        public string CacheKey { get; set; }
        public Guid? PlayniteGameId { get; set; }
        public string ProviderName { get; set; }

        public List<AchievementDisplayItem> Achievements { get; set; } = new List<AchievementDisplayItem>();
        public List<RecentAchievementItem> RecentAchievements { get; set; } = new List<RecentAchievementItem>();
        public GameOverviewItem GameOverview { get; set; }
        public Dictionary<DateTime, int> UnlockCountsByDate { get; set; } = new Dictionary<DateTime, int>();

        public int TotalAchievements { get; set; }
        public int UnlockedAchievements { get; set; }
        public int CommonCount { get; set; }
        public int UncommonCount { get; set; }
        public int RareCount { get; set; }
        public int UltraRareCount { get; set; }
        public bool IsPerfect { get; set; }
    }
}

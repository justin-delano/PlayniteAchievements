using System;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    internal sealed class GameSummaryRuntimeItem
    {
        public Guid GameId { get; set; }
        public string Name { get; set; }
        public string Platform { get; set; }
        public string CoverImagePath { get; set; }
        public int Progress { get; set; }
        public int GoldCount { get; set; }
        public int SilverCount { get; set; }
        public int BronzeCount { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime LastUnlockDate { get; set; }
        public int CommonUnlockCount { get; set; }
        public int UncommonUnlockCount { get; set; }
        public int RareUnlockCount { get; set; }
        public int UltraRareUnlockCount { get; set; }
    }
}

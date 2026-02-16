namespace PlayniteAchievements.Services.Database.Rows
{
    internal sealed class AchievementDefinitionRow
    {
        public long Id { get; set; }
        public long GameId { get; set; }
        public string ApiName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string IconUnlockedPath { get; set; }
        public string IconLockedPath { get; set; }
        public int? Points { get; set; }
        public string Category { get; set; }
        public long Hidden { get; set; }
        public double? GlobalPercentUnlocked { get; set; }
        public int? ProgressMax { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
    }
}

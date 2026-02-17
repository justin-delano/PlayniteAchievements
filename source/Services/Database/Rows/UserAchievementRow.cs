namespace PlayniteAchievements.Services.Database.Rows
{
    internal sealed class UserAchievementRow
    {
        public long Id { get; set; }
        public long UserGameProgressId { get; set; }
        public long AchievementDefinitionId { get; set; }
        public bool Unlocked { get; set; }
        public string UnlockTimeUtc { get; set; }
        public int? ProgressNum { get; set; }
        public int? ProgressDenom { get; set; }
        public string LastUpdatedUtc { get; set; }
        public string CreatedUtc { get; set; }
    }
}

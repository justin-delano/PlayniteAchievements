namespace PlayniteAchievements.Services.Database.Rows
{
    internal sealed class UserGameProgressRow
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long GameId { get; set; }
        public string CacheKey { get; set; }
        public long HasAchievements { get; set; }
        public long ExcludedByUser { get; set; }
        public long AchievementsUnlocked { get; set; }
        public long TotalAchievements { get; set; }
        public string LastUpdatedUtc { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
    }
}


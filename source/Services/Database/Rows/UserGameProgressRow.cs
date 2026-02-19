namespace PlayniteAchievements.Services.Database.Rows
{
    internal sealed class UserGameProgressRow
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long GameId { get; set; }
        public string CacheKey { get; set; }
        public long PlaytimeSeconds { get; set; }
        public long NoAchievements { get; set; }
        public long AchievementsUnlocked { get; set; }
        public long TotalAchievements { get; set; }
        public long IsCompleted { get; set; }
        public long ProviderIsCompleted { get; set; }
        public string CompletedMarkerApiName { get; set; }
        public string LastUpdatedUtc { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
    }
}


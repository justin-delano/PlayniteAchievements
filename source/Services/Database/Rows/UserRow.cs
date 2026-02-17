namespace PlayniteAchievements.Services.Database.Rows
{
    internal sealed class UserRow
    {
        public long Id { get; set; }
        public string ProviderName { get; set; }
        public string ExternalUserId { get; set; }
        public string DisplayName { get; set; }
        public long IsCurrentUser { get; set; }
        public string FriendSource { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
    }
}

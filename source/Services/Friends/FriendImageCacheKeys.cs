namespace PlayniteAchievements.Services.Friends
{
    /// <summary>
    /// Shared <c>DiskImageService</c> cache subfolders for friend imagery. Files within each
    /// folder are keyed by source-URI hash, so a single folder holds every avatar (or every
    /// unowned game cover/icon) without a folder per friend or per game. Clearing unowned data
    /// removes the whole <see cref="Games"/> folder in one call.
    /// </summary>
    public static class FriendImageCacheFolders
    {
        public const string Avatars = "friendavatars";
        public const string Games = "friendgames";
    }
}

namespace PlayniteAchievements.Providers.Steam
{
    /// <summary>
    /// Builds Steam CDN image URLs that are derivable purely from an app id.
    /// Used to supply cover and icon art for provider-only (unowned) friend games,
    /// which have no Playnite library entry to resolve images from.
    /// </summary>
    internal static class SteamImageUrls
    {
        private const string CdnHost = "https://cdn.akamai.steamstatic.com/steam/apps";

        public static string Cover(int appId)
        {
            return appId > 0
                ? $"{CdnHost}/{appId}/library_600x900.jpg"
                : null;
        }

        public static string Icon(int appId)
        {
            return appId > 0
                ? $"{CdnHost}/{appId}/capsule_231x87.jpg"
                : null;
        }
    }
}

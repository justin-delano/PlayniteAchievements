using System;

namespace PlayniteAchievements.Services
{
    internal static class InputValidator
    {
        public static bool HasSteamCredentials(string steamUserId, string steamApiKey) =>
            !string.IsNullOrWhiteSpace(steamUserId) && !string.IsNullOrWhiteSpace(steamApiKey);

        public static bool IsValidSteamId64(string steamId) =>
            !string.IsNullOrWhiteSpace(steamId) && ulong.TryParse(steamId, out _);

        public static bool HasValue<T>(T? nullable) where T : struct =>
            nullable.HasValue && !nullable.Value.Equals(default(T));
    }
}

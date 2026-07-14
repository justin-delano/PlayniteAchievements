using PlayniteAchievements.Services.Achievements;
using System;
using System.IO;

namespace PlayniteAchievements.Services.Images
{
    // Read side of provider-supplied default category images: probes the deterministic
    // cache path for (gameId, normalized category label) and returns it only when the file
    // exists. Defaults sit below user overrides and above the game icon/cover fallback.
    internal static class CategoryDefaultImageResolver
    {
        public static string Resolve(Guid? playniteGameId, string categoryLabel, CategoryImageKind kind)
        {
            if (!playniteGameId.HasValue || playniteGameId.Value == Guid.Empty)
            {
                return null;
            }

            var normalizedLabel = AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(categoryLabel);
            if (string.Equals(
                normalizedLabel,
                AchievementCategoryTypeHelper.DefaultCategoryLabel,
                StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var diskImageService = PlayniteAchievementsPlugin.Instance?.DiskImageService;
            if (diskImageService == null)
            {
                return null;
            }

            try
            {
                var path = diskImageService.GetDefaultCategoryImagePath(
                    playniteGameId.Value.ToString("D"),
                    normalizedLabel,
                    kind);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

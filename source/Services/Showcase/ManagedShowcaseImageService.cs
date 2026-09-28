using System;
using System.IO;

namespace PlayniteAchievements.Services.Showcase
{
    public static class ManagedShowcaseImageService
    {
        public static string Import(string sourcePath, string pluginUserDataPath, string slot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) ||
                    !File.Exists(sourcePath) ||
                    string.IsNullOrWhiteSpace(pluginUserDataPath))
                {
                    return null;
                }

                var extension = Path.GetExtension(sourcePath);
                switch ((extension ?? string.Empty).ToLowerInvariant())
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".bmp":
                    case ".gif":
                    case ".webp":
                        break;
                    default:
                        return null;
                }

                var directory = Path.Combine(
                    pluginUserDataPath,
                    "showcase",
                    "profile");
                Directory.CreateDirectory(directory);
                var normalizedSlot = string.Equals(slot, "background", StringComparison.OrdinalIgnoreCase)
                    ? "background"
                    : "avatar";
                var destination = Path.Combine(directory, normalizedSlot + extension.ToLowerInvariant());
                if (string.Equals(
                        Path.GetFullPath(sourcePath),
                        Path.GetFullPath(destination),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return destination;
                }

                File.Copy(sourcePath, destination, overwrite: true);
                return destination;
            }
            catch
            {
                // Profile art is optional. A locked/unreadable path must not prevent the rest of
                // the widget settings from being saved.
                return null;
            }
        }
    }
}

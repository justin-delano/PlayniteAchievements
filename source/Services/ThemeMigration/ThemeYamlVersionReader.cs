using System;
using System.IO;

namespace PlayniteAchievements.Services.ThemeMigration
{
    internal static class ThemeYamlVersionReader
    {
        private const string ThemeYamlFileName = "theme.yaml";

        public static bool TryReadThemeVersion(string themeDirectory, out string version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(themeDirectory))
            {
                return false;
            }

            try
            {
                var manifestPath = Path.Combine(themeDirectory, ThemeYamlFileName);
                if (!File.Exists(manifestPath))
                {
                    return false;
                }

                foreach (var rawLine in File.ReadLines(manifestPath))
                {
                    var line = rawLine?.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue;
                    }

                    // Support common Playnite manifest style: "Version: 1.2.3"
                    // Also tolerate lowercase keys and quoted values.
                    if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("Version:".Length).Trim();
                        value = value.Trim().Trim('"').Trim('\'').Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            version = value;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignore - version is optional
            }

            return false;
        }
    }
}


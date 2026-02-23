using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Text;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Utility class for expanding path variables in game paths.
    /// </summary>
    internal static class PathExpansion
    {
        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// Handles variables like {InstallDir}, {Name}, etc.
        /// </summary>
        public static string ExpandGamePath(IPlayniteAPI playniteApi, Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            try
            {
                // Use Playnite's built-in variable expansion
                var expanded = playniteApi?.ExpandGameVariables(game, path) ?? path;

                // Handle additional custom variables
                if (expanded.IndexOf("{InstallDir}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var installDir = game?.InstallDirectory;
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        expanded = ReplaceInsensitive(expanded, "{InstallDir}", installDir);
                    }
                }

                return expanded;
            }
            catch
            {
                return path;
            }
        }

        private static string ReplaceInsensitive(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            {
                return input;
            }

            var idx = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return input;
            }

            var sb = new StringBuilder(input.Length);
            var start = 0;
            while (idx >= 0)
            {
                sb.Append(input.Substring(start, idx - start));
                sb.Append(newValue ?? string.Empty);
                start = idx + oldValue.Length;
                idx = input.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
            }

            sb.Append(input.Substring(start));
            return sb.ToString();
        }
    }
}

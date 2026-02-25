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
        /// Handles variables like {InstallDir}, {Name}, {EmulatorDir}, etc.
        /// </summary>
        public static string ExpandGamePath(IPlayniteAPI playniteApi, Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            try
            {
                var expanded = path;

                // Handle {EmulatorDir} BEFORE Playnite's expansion, which may strip it to empty string
                if (path.IndexOf("{EmulatorDir}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var emulatorDir = GetEmulatorInstallDir(playniteApi, game);
                    if (!string.IsNullOrWhiteSpace(emulatorDir))
                    {
                        expanded = ReplaceInsensitive(expanded, "{EmulatorDir}", emulatorDir);
                    }
                }

                // Use Playnite's built-in variable expansion for remaining variables
                expanded = playniteApi?.ExpandGameVariables(game, expanded) ?? expanded;

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

        /// <summary>
        /// Gets the emulator install directory from the game's emulator action.
        /// </summary>
        private static string GetEmulatorInstallDir(IPlayniteAPI playniteApi, Game game)
        {
            if (game?.GameActions == null || playniteApi?.Database?.Emulators == null)
            {
                return null;
            }

            foreach (var action in game.GameActions)
            {
                if (action?.Type == GameActionType.Emulator && action.EmulatorId != Guid.Empty)
                {
                    var emulator = playniteApi.Database.Emulators.Get(action.EmulatorId);
                    if (emulator != null && !string.IsNullOrWhiteSpace(emulator.InstallDir))
                    {
                        return emulator.InstallDir;
                    }
                }
            }

            return null;
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

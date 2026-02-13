using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    /// <summary>
    /// Resolves Playnite platform specifications to RetroAchievements console IDs.
    /// Uses a JSON-driven registry for maintainable console mapping configuration.
    /// </summary>
    internal static class RaConsoleIdResolver
    {
        private static readonly ConsoleMappingRegistry Registry = ConsoleMappingRegistry.Instance;

        /// <summary>
        /// Attempts to resolve a game's platform to a RetroAchievements console ID.
        /// </summary>
        /// <param name="game">The game to resolve.</param>
        /// <param name="consoleId">The resolved console ID, or 0 if no match found.</param>
        /// <returns>True if a match was found; otherwise, false.</returns>
        public static bool TryResolve(Game game, out int consoleId)
        {
            consoleId = 0;
            if (game?.Platforms == null || game.Platforms.Count == 0)
            {
                return false;
            }

            foreach (var platform in game.Platforms)
            {
                if (platform == null) continue;

                if (TryResolveFromString(platform.SpecificationId, out consoleId))
                {
                    return true;
                }

                // Fall back to platform name if specification ID is missing or not recognized.
                // This supports custom platform metadata where SpecificationId can be non-standard.
                if (TryResolveFromString(platform.Name, out consoleId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve a platform string to a RetroAchievements console ID.
        /// </summary>
        /// <param name="value">The platform specification ID or name.</param>
        /// <param name="consoleId">The resolved console ID, or 0 if no match found.</param>
        /// <returns>True if a match was found; otherwise, false.</returns>
        private static bool TryResolveFromString(string value, out int consoleId)
        {
            return Registry.TryResolve(value, out consoleId);
        }
    }
}

using Playnite.SDK.Models;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    internal static class SuccessStoryIntegrationHelper
    {
        internal static GameAchievementData UpdateThemeProperties(PlayniteAchievementsPlugin plugin, Game newContext)
        {
            if (plugin == null)
            {
                return null;
            }

            if (newContext == null)
            {
                plugin.ThemeUpdateService?.RequestUpdate(null);
                return null;
            }

            var gameData = plugin.AchievementService.GetGameAchievementData(newContext.Id);
            if (gameData != null)
            {
                // Coalesced + async; safe to call from multiple controls.
                plugin.ThemeUpdateService?.RequestUpdate(newContext.Id);
                return gameData;
            }

            plugin.ThemeUpdateService?.RequestUpdate(null);
            return null;
        }
    }
}

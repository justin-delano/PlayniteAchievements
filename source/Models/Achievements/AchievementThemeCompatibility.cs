namespace PlayniteAchievements.Models.Achievements
{
    /// <summary>
    /// Theme compatibility extensions and aliases for achievement display.
    /// Provides compatibility with themes like SuccessStory and Aniki ReMake.
    /// </summary>
    public static class AchievementThemeCompatibility
    {
        // Theme compatibility is handled via properties on AchievementDetail:
        // - IsUnlock: maps to Unlocked status
        // - IsHidden: maps to Hidden status
        // - Name: maps to DisplayName
        // - Icon: provides display icon for theme consumption
        // - Percent: global unlock percent (0..100)
        // - DateUnlocked: local time of unlock
        // - GamerScore: rarity-based points for trophy tier selection
    }
}

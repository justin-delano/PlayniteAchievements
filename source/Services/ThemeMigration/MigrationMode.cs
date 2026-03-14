namespace PlayniteAchievements.Services.ThemeMigration
{
    /// <summary>
    /// Defines the type of theme migration to perform.
    /// </summary>
    public enum MigrationMode
    {
        /// <summary>
        /// Limited migration performs text replacements only (SuccessStory to PlayniteAchievements).
        /// Preserves existing control structures and bindings.
        /// </summary>
        Limited,

        /// <summary>
        /// Full migration performs text replacements plus replaces Legacy control elements
        /// with Native Desktop elements and LegacyData bindings with Theme bindings.
        /// </summary>
        Full
    }
}

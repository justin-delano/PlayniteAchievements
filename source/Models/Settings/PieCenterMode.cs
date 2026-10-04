namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Controls what a pie chart draws in its center.
    /// </summary>
    public enum PieCenterMode
    {
        /// <summary>A ring with the unlocked percentage in the hole.</summary>
        Percentage = 0,

        /// <summary>A ring with an empty hole.</summary>
        Empty = 1,

        /// <summary>A full pie with no hole.</summary>
        Filled = 2
    }
}

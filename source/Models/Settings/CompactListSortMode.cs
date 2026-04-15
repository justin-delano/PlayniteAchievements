namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Sort mode for modern compact list controls.
    /// None preserves canonical selected-game ordering: custom game order when configured,
    /// otherwise the shared default ordering.
    /// </summary>
    public enum CompactListSortMode
    {
        None = 0,
        UnlockTime = 1,
        Rarity = 2
    }
}

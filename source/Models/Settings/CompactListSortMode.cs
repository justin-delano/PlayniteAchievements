namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Sort mode for selected-game achievement list controls.
    /// None preserves the control's source order, which for the selected-game default surfaces
    /// means custom order when configured, otherwise provider order.
    /// </summary>
    public enum CompactListSortMode
    {
        None = 0,
        UnlockTime = 1,
        Rarity = 2,
        /// <summary>
        /// Preserves the developer-assigned display order (e.g. RetroAchievements DisplayOrder field).
        /// Falls back to source order (0) for providers that don't supply an explicit index.
        /// </summary>
        DisplayOrder = 3,
        /// <summary>
        /// Remembers the last column the user manually sorted by and restores it on navigation.
        /// The sort column and direction are persisted so they survive navigating away and back.
        /// </summary>
        Custom = 4
    }
}

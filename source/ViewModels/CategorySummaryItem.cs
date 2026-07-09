namespace PlayniteAchievements.ViewModels
{
    /// <summary>
    /// A <see cref="GameSummaryItem"/> that represents a rollup over one achievement category
    /// rather than a game. Carries the normalized category label as a stable key so the grid can
    /// map a clicked summary row back to its category when drilling into the achievement list.
    /// The display name (localized label) is held by <see cref="GameSummaryItem.GameName"/> so the
    /// existing game-summary column templates render it unchanged.
    /// </summary>
    public sealed class CategorySummaryItem : GameSummaryItem
    {
        public string CategoryLabel { get; set; }
    }
}

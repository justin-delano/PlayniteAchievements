namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// The Pie widget's control bar: platform and progress (Complete / In Progress) filters over
    /// the games the pie's totals are counted from.
    /// </summary>
    public sealed class PieControlBarAdapter : GameSummaryGridControlBarAdapter
    {
        public PieControlBarAdapter()
            : base(chartFilters: true)
        {
        }
    }
}

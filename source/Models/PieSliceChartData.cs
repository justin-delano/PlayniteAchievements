namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Data model for pie chart slices that carries icon metadata alongside the chart value.
    /// Used for custom tooltips that display provider/badge icons with counts.
    /// </summary>
    public class PieSliceChartData
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public string IconKey { get; set; }
        public string ColorHex { get; set; }
        public double ChartValue { get; set; }

        /// <summary>
        /// Number of unlocked achievements in this category.
        /// For non-locked slices, same as Count.
        /// </summary>
        public int UnlockedCount { get; set; }

        /// <summary>
        /// Total achievements in this category (including locked).
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// True if this represents a locked/remaining slice.
        /// Locked slices display just the count, not "unlocked / total".
        /// </summary>
        public bool IsLocked { get; set; }
    }
}

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
    }
}

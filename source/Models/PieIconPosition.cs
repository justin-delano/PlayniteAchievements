namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Position model for rendering icons at calculated positions around a pie chart.
    /// </summary>
    public class PieIconPosition
    {
        public string IconKey { get; set; }
        public string ColorHex { get; set; }
        public int Count { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}

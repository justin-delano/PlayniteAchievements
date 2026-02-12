namespace PlayniteAchievements.Models
{
    public sealed class ScanStatusSnapshot
    {
        public bool IsScanning { get; set; }
        public bool IsFinal { get; set; }
        public bool IsCanceled { get; set; }
        public double ProgressPercent { get; set; }
        public string Message { get; set; }
    }
}

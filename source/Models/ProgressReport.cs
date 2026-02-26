using System;

namespace PlayniteAchievements.Models
{
    public class ProgressReport
    {
        public Guid? OperationId { get; set; }
        public RefreshModeType? Mode { get; set; }
        public Guid? CurrentGameId { get; set; }
        public string Message { get; set; }
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public bool IsCanceled { get; set; }
        public double PercentComplete => TotalSteps > 0
            ? (double)CurrentStep / TotalSteps * 100
            : 0;
    }
}

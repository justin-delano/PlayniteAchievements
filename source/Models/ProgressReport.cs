using System;

namespace PlayniteAchievements.Models
{
    public class ProgressReport
    {
        public string Message { get; set; }
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public bool IsCanceled { get; set; }
        public double PercentComplete => TotalSteps > 0
            ? (double)CurrentStep / TotalSteps * 100
            : 0;
    }
}

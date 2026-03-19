using System;

namespace PlayniteAchievements.Models.Achievements
{
    internal static class AchievementCompletionPercentCalculator
    {
        internal static int ComputeRoundedPercent(int unlocked, int total)
        {
            if (total <= 0 || unlocked <= 0)
            {
                return 0;
            }

            if (unlocked >= total)
            {
                return 100;
            }

            var percent = unlocked * 100.0 / total;
            var rounded = (int)Math.Round(percent, 0, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(100, rounded));
        }
    }
}

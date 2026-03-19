namespace PlayniteAchievements.Models.Achievements
{
    internal static class AchievementRarityStatsCombiner
    {
        internal static AchievementRarityStats Combine(params AchievementRarityStats[] stats)
        {
            var combined = new AchievementRarityStats();
            if (stats == null)
            {
                return combined;
            }

            for (int i = 0; i < stats.Length; i++)
            {
                var item = stats[i];
                if (item == null)
                {
                    continue;
                }

                combined.Total += item.Total;
                combined.Unlocked += item.Unlocked;
                combined.Locked += item.Locked;
            }

            return combined;
        }
    }
}
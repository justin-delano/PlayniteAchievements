using System;
using System.Collections.Generic;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Hydration
{
    /// <summary>
    /// Hydrates AchievementDetail with non-persisted properties derived from plugin settings.
    /// </summary>
    public class AchievementDetailHydrator
    {
        private readonly PersistedSettings _settings;

        public AchievementDetailHydrator(PersistedSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Hydrates multiple AchievementDetail instances and applies manual capstone
        /// override from settings.
        /// </summary>
        public void HydrateAllWithCapstoneOverride(
            IEnumerable<AchievementDetail> details,
            Guid playniteGameId)
        {
            if (details == null)
            {
                return;
            }

            // Resolve manual capstone override from settings
            var hasManualCapstone = _settings.ManualCapstones
                .TryGetValue(playniteGameId, out var manualCapstone);

            if (!hasManualCapstone)
            {
                return;
            }

            foreach (var detail in details)
            {
                if (detail == null)
                {
                    continue;
                }

                detail.IsCapstone = string.Equals(
                    detail.ApiName,
                    manualCapstone,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Hydration
{
    /// <summary>
    /// Hydrates AchievementDetail with non-persisted properties derived from
    /// Playnite API and plugin settings.
    /// </summary>
    public class AchievementDetailHydrator
    {
        private readonly IPlayniteAPI _api;
        private readonly PersistedSettings _settings;

        public AchievementDetailHydrator(IPlayniteAPI api, PersistedSettings settings)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Hydrates a single AchievementDetail with Playnite Game reference
        /// and applies manual capstone override if configured.
        /// </summary>
        public void Hydrate(AchievementDetail detail, Guid? playniteGameId, string manualCapstone = null)
        {
            if (detail == null || !playniteGameId.HasValue)
            {
                return;
            }

            // Set Game reference from Playnite DB
            try
            {
                detail.Game = _api.Database.Games.Get(playniteGameId.Value);
            }
            catch
            {
                detail.Game = null;
            }

            // Apply manual capstone override if specified
            if (!string.IsNullOrEmpty(manualCapstone))
            {
                detail.IsCapstone = string.Equals(
                    detail.ApiName,
                    manualCapstone,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Hydrates multiple AchievementDetail instances with Playnite Game reference
        /// and applies manual capstone override from settings.
        /// </summary>
        public void HydrateAllWithCapstoneOverride(
            IEnumerable<AchievementDetail> details,
            Guid playniteGameId)
        {
            if (details == null)
            {
                return;
            }

            // Resolve game once
            Game game = null;
            try
            {
                game = _api.Database.Games.Get(playniteGameId);
            }
            catch
            {
                // Continue without game reference
            }

            // Resolve manual capstone override from settings
            var hasManualCapstone = _settings.ManualCapstones
                .TryGetValue(playniteGameId, out var manualCapstone);

            foreach (var detail in details)
            {
                if (detail == null)
                {
                    continue;
                }

                // Set Game reference
                detail.Game = game;

                // Apply capstone override if configured
                if (hasManualCapstone)
                {
                    detail.IsCapstone = string.Equals(
                        detail.ApiName,
                        manualCapstone,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}

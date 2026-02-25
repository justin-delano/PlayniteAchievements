using System;
using System.Collections.Generic;
using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Hydration
{
    /// <summary>
    /// Hydrates GameAchievementData with non-persisted properties derived from
    /// Playnite API and plugin settings.
    /// </summary>
    public class GameDataHydrator
    {
        private readonly IPlayniteAPI _api;
        private readonly PersistedSettings _settings;
        private readonly AchievementDetailHydrator _achievementHydrator;

        public GameDataHydrator(IPlayniteAPI api, PersistedSettings settings)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _achievementHydrator = new AchievementDetailHydrator(api, settings);
        }

        /// <summary>
        /// Hydrates a single GameAchievementData with non-persisted properties.
        /// </summary>
        public void Hydrate(GameAchievementData data)
        {
            if (data?.PlayniteGameId == null)
            {
                return;
            }

            var gameId = data.PlayniteGameId.Value;

            // Populate ExcludedByUser from settings
            data.ExcludedByUser = _settings.ExcludedGameIds.Contains(gameId);

            // Populate SortingName from Playnite database
            data.SortingName = GetSortingName(gameId) ?? data.GameName;

            // Hydrate achievements with Game reference and capstone override
            if (data.Achievements != null && data.Achievements.Count > 0)
            {
                _achievementHydrator.HydrateAllWithCapstoneOverride(data.Achievements, gameId);
            }
        }

        /// <summary>
        /// Hydrates multiple GameAchievementData instances with non-persisted properties.
        /// </summary>
        public void HydrateAll(IEnumerable<GameAchievementData> games)
        {
            if (games == null)
            {
                return;
            }

            foreach (var game in games)
            {
                Hydrate(game);
            }
        }

        private string GetSortingName(Guid playniteGameId)
        {
            try
            {
                var game = _api.Database.Games.Get(playniteGameId);
                return game?.SortingName;
            }
            catch
            {
                return null;
            }
        }
    }
}

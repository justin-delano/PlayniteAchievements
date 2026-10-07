using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services.Achievements;

namespace PlayniteAchievements.Services.Overview
{
    /// <summary>
    /// Splits an overview snapshot's unlock day counts into one stacked timeline series per
    /// platform, keyed by each game's effective provider key like the "Achievements by Platform"
    /// pie, and colored and named through <see cref="ProviderRegistry"/> so recolors apply.
    /// </summary>
    public static class TimelinePlatformSeries
    {
        public const string UnknownKey = "Unknown";

        /// <summary>Every game in the snapshot.</summary>
        public static IReadOnlyList<TimelineSeriesCounts> FromSnapshot(OverviewDataSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Array.Empty<TimelineSeriesCounts>();
            }

            var keyByGame = BuildKeyByGame(snapshot);
            var groups = UnlockDayCounts.GroupByKey(
                snapshot.GlobalUnlockCountsByDate,
                snapshot.UnlockCountsByDateByGame,
                gameId => keyByGame.TryGetValue(gameId, out var key) ? key : null,
                UnknownKey);
            return ToSeries(groups);
        }

        /// <summary>One game's counts as a single series under its platform.</summary>
        public static IReadOnlyList<TimelineSeriesCounts> ForGame(OverviewDataSnapshot snapshot, Guid gameId)
        {
            Dictionary<DateTime, int> counts = null;
            snapshot?.UnlockCountsByDateByGame?.TryGetValue(gameId, out counts);
            var key = snapshot?.GameSummaries?
                .FirstOrDefault(game => game?.PlayniteGameId == gameId)?
                .ProviderKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = UnknownKey;
            }

            return ToSeries(new Dictionary<string, Dictionary<DateTime, int>>
            {
                [key] = counts ?? new Dictionary<DateTime, int>()
            });
        }

        private static Dictionary<Guid, string> BuildKeyByGame(OverviewDataSnapshot snapshot)
        {
            var keyByGame = new Dictionary<Guid, string>();
            if (snapshot.GameSummaries == null)
            {
                return keyByGame;
            }

            foreach (var game in snapshot.GameSummaries)
            {
                if (game?.PlayniteGameId.HasValue == true && !keyByGame.ContainsKey(game.PlayniteGameId.Value))
                {
                    keyByGame[game.PlayniteGameId.Value] = game.ProviderKey;
                }
            }

            return keyByGame;
        }

        private static IReadOnlyList<TimelineSeriesCounts> ToSeries(Dictionary<string, Dictionary<DateTime, int>> groups)
        {
            return groups
                .Select(group => new TimelineSeriesCounts(
                    group.Key,
                    ProviderRegistry.GetLocalizedName(group.Key),
                    ProviderRegistry.ResolveProviderVisualsOrFallback(group.Key).colorHex,
                    group.Value))
                .ToList();
        }
    }
}

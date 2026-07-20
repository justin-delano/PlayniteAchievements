using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Cache;

namespace PlayniteAchievements.Services.Summaries
{
    /// <summary>
    /// Corrects persisted overview summary data for games with per-achievement filters. The
    /// stored aggregates cover all cached achievements; these helpers rebuild one game's summary
    /// row, recent-unlock rows, and timeline contribution from the filtered per-game projection
    /// so the summary fast path produces the same values as the hydrated path.
    /// </summary>
    internal static class OverviewSummaryFilterCorrections
    {
        /// <summary>
        /// Applies one game's filtered achievement projection to the summary data. An empty
        /// projection removes the game entirely, matching the hydrated path, which hides games
        /// with no visible achievements.
        /// </summary>
        public static void Apply(
            CachedSummaryData summaryData,
            Guid gameId,
            IReadOnlyList<AchievementDetail> visibleAchievements,
            bool isCompleted)
        {
            if (summaryData?.Games == null || gameId == Guid.Empty)
            {
                return;
            }

            var row = summaryData.Games.FirstOrDefault(game => game?.PlayniteGameId == gameId);
            if (row == null)
            {
                return;
            }

            visibleAchievements = visibleAchievements ?? Array.Empty<AchievementDetail>();
            if (visibleAchievements.Count == 0)
            {
                summaryData.Games.Remove(row);
                summaryData.RecentUnlocks = (summaryData.RecentUnlocks ?? new List<CachedRecentUnlockData>())
                    .Where(recent => recent?.PlayniteGameId != gameId)
                    .ToList();
                ReplaceTimelineCounts(
                    summaryData.GlobalUnlockCountsByDate,
                    summaryData.UnlockCountsByDateByGame,
                    gameId,
                    null);
                return;
            }

            var stats = AchievementStatsAccumulator.FromAchievements(visibleAchievements);
            row.HasAchievements = true;
            row.TotalAchievements = stats.TotalAchievements;
            row.UnlockedAchievements = stats.UnlockedAchievements;
            row.CommonCount = stats.CommonCount;
            row.UncommonCount = stats.UncommonCount;
            row.RareCount = stats.RareCount;
            row.UltraRareCount = stats.UltraRareCount;
            row.TotalCommonPossible = stats.TotalCommonPossible;
            row.TotalUncommonPossible = stats.TotalUncommonPossible;
            row.TotalRarePossible = stats.TotalRarePossible;
            row.TotalUltraRarePossible = stats.TotalUltraRarePossible;
            row.TrophyPlatinumCount = stats.TrophyPlatinumCount;
            row.TrophyGoldCount = stats.TrophyGoldCount;
            row.TrophySilverCount = stats.TrophySilverCount;
            row.TrophyBronzeCount = stats.TrophyBronzeCount;
            row.TrophyPlatinumTotal = stats.TrophyPlatinumTotal;
            row.TrophyGoldTotal = stats.TrophyGoldTotal;
            row.TrophySilverTotal = stats.TrophySilverTotal;
            row.TrophyBronzeTotal = stats.TrophyBronzeTotal;
            row.CollectionScore = stats.CollectionScore;
            row.PrestigeScore = stats.PrestigeScore;
            row.CollectionScoreTotal = stats.CollectionScoreTotal;
            row.PrestigeScoreTotal = stats.PrestigeScoreTotal;
            row.Points = stats.Points;
            row.IsCompleted = isCompleted;

            var visibleUnlockedApiNames = new HashSet<string>(
                visibleAchievements
                    .Where(achievement => achievement?.Unlocked == true &&
                                          !string.IsNullOrWhiteSpace(achievement.ApiName))
                    .Select(achievement => achievement.ApiName),
                StringComparer.OrdinalIgnoreCase);
            summaryData.RecentUnlocks = (summaryData.RecentUnlocks ?? new List<CachedRecentUnlockData>())
                .Where(recent => recent?.PlayniteGameId != gameId ||
                                 (recent.ApiName != null && visibleUnlockedApiNames.Contains(recent.ApiName)))
                .ToList();

            ReplaceTimelineCounts(
                summaryData.GlobalUnlockCountsByDate,
                summaryData.UnlockCountsByDateByGame,
                gameId,
                stats.UnlockCountsByDate);
        }

        /// <summary>
        /// Swaps one game's contribution to the unlock timeline: subtracts the currently
        /// recorded per-game counts from the global counts, then applies the replacement (null
        /// or empty removes the game entirely).
        /// </summary>
        internal static void ReplaceTimelineCounts(
            IDictionary<DateTime, int> globalCounts,
            IDictionary<Guid, Dictionary<DateTime, int>> countsByGame,
            Guid gameId,
            IReadOnlyDictionary<DateTime, int> replacementCounts)
        {
            if (globalCounts == null || countsByGame == null)
            {
                return;
            }

            if (countsByGame.TryGetValue(gameId, out var oldCounts) && oldCounts != null)
            {
                foreach (var kvp in oldCounts)
                {
                    if (!globalCounts.TryGetValue(kvp.Key, out var existing))
                    {
                        continue;
                    }

                    var remaining = existing - kvp.Value;
                    if (remaining > 0)
                    {
                        globalCounts[kvp.Key] = remaining;
                    }
                    else
                    {
                        globalCounts.Remove(kvp.Key);
                    }
                }
            }

            countsByGame.Remove(gameId);

            if (replacementCounts == null || replacementCounts.Count == 0)
            {
                return;
            }

            var newCounts = new Dictionary<DateTime, int>();
            foreach (var kvp in replacementCounts)
            {
                newCounts[kvp.Key] = kvp.Value;
                globalCounts.TryGetValue(kvp.Key, out var existing);
                globalCounts[kvp.Key] = existing + kvp.Value;
            }

            countsByGame[gameId] = newCounts;
        }
    }
}

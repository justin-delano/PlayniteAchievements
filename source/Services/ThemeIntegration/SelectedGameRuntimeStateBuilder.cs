using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    internal static class SelectedGameRuntimeStateBuilder
    {
        public static SelectedGameRuntimeState Build(
            Guid gameId,
            GameAchievementData data,
            double ultraRareThreshold,
            double rareThreshold,
            double uncommonThreshold)
        {
            if (data == null || !data.HasAchievements)
            {
                return SelectedGameRuntimeState.Empty;
            }

            var achievements = data.Achievements ?? new List<AchievementDetail>();
            if (achievements.Count == 0)
            {
                return new SelectedGameRuntimeState(
                    gameId,
                    data.LastUpdatedUtc,
                    false,
                    0,
                    0,
                    0,
                    0,
                    false,
                    new List<AchievementDetail>(),
                    new List<AchievementDetail>(),
                    new List<AchievementDetail>(),
                    new List<AchievementDetail>(),
                    new List<AchievementDetail>(),
                    new AchievementRarityStats(),
                    new AchievementRarityStats(),
                    new AchievementRarityStats(),
                    new AchievementRarityStats());
            }

            var total = achievements.Count;
            var game = data.Game;
            for (int i = 0; i < achievements.Count; i++)
            {
                if (achievements[i] != null)
                {
                    // Native compact lists resolve tooltip game name from AchievementDetail.Game.
                    // Ensure selected-game snapshots always carry this context.
                    achievements[i].Game = game;
                }
            }

            var unlocked = 0;
            for (int i = 0; i < achievements.Count; i++)
            {
                if (achievements[i]?.Unlocked == true)
                {
                    unlocked++;
                }
            }

            var locked = total - unlocked;
            var percent = total > 0 ? Math.Round(unlocked * 100.0 / total, 2) : 0;
            var all = achievements.ToList();
            var oldestFirst = all
                .OrderBy(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();
            var newestFirst = all
                .OrderByDescending(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();
            var rarityAsc = all
                .OrderBy(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();
            var rarityDesc = all
                .OrderByDescending(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            var common = new AchievementRarityStats();
            var uncommon = new AchievementRarityStats();
            var rare = new AchievementRarityStats();
            var ultra = new AchievementRarityStats();

            for (int i = 0; i < all.Count; i++)
            {
                var achievement = all[i];
                if (achievement?.GlobalPercentUnlocked.HasValue != true)
                {
                    continue;
                }

                var percentUnlocked = achievement.GlobalPercentUnlocked.Value;
                if (percentUnlocked <= 0)
                {
                    continue;
                }

                var target = percentUnlocked > uncommonThreshold
                    ? common
                    : (percentUnlocked > rareThreshold ? uncommon : (percentUnlocked > ultraRareThreshold ? rare : ultra));

                target.Total++;
                if (achievement.Unlocked)
                {
                    target.Unlocked++;
                }
                else
                {
                    target.Locked++;
                }
            }

            return new SelectedGameRuntimeState(
                gameId,
                data.LastUpdatedUtc,
                true,
                total,
                unlocked,
                locked,
                percent,
                data.IsCompleted,
                all,
                oldestFirst,
                newestFirst,
                rarityAsc,
                rarityDesc,
                common,
                uncommon,
                rare,
                ultra);
        }
    }
}

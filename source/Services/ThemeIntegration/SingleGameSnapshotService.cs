using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    /// <summary>
    /// Service for building single-game achievement snapshots.
    /// Creates immutable SingleGameSnapshot instances from GameAchievementData.
    /// </summary>
    public static class SingleGameSnapshotService
    {
        /// <summary>
        /// Build an immutable snapshot from cached data. Intended to be executed off the UI thread.
        /// </summary>
        public static SingleGameSnapshot BuildSnapshot(
            Guid gameId,
            GameAchievementData data,
            double ultraRareThreshold,
            double rareThreshold,
            double uncommonThreshold)
        {
            if (data == null || data.NoAchievements)
            {
                return null;
            }

            var achievements = data.Achievements ?? new List<AchievementDetail>();

            // Calculate basic counts (avoid LINQ to reduce allocations).
            int total = achievements.Count;
            int unlocked = 0;
            for (int i = 0; i < achievements.Count; i++)
            {
                if (achievements[i]?.Unlocked == true)
                {
                    unlocked++;
                }
            }

            int locked = total - unlocked;
            double percent = total > 0 ? Math.Round(unlocked * 100.0 / total, 2) : 0;
            bool is100Percent = unlocked == total && total > 0;

            // Make exactly one copy for theme bindings.
            // This avoids exposing provider-owned lists while also avoiding duplicate copies.
            var all = achievements.ToList();

            // SuccessStory compatibility expects these lists to contain *all* achievements:
            // - Ascending: locked (null date) first, then unlocked by date.
            // - Descending: unlocked newest first, locked (null date) last.
            // Themes (e.g. Aniki ReMake) toggle visibility based on unlock state.
            var asc = all
                .OrderBy(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            var desc = all
                .OrderByDescending(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            // Rarity-sorted lists (ascending = rarest first, descending = common first)
            var rarityAsc = all
                .OrderBy(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            var rarityDesc = all
                .OrderByDescending(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            // Calculate rarity stats in a single pass.
            // NOTE: We intentionally match previous boundary behavior:
            // - percent == threshold belongs to the lower bucket.
            // - percent <= 0 is excluded from buckets (minPercent is exclusive).
            var common = new AchievementRarityStats();
            var uncommon = new AchievementRarityStats();
            var rare = new AchievementRarityStats();
            var ultra = new AchievementRarityStats();

            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null)
                {
                    continue;
                }

                var p = a.GlobalPercentUnlocked ?? 100;
                if (p <= 0)
                {
                    continue;
                }

                var target = p > uncommonThreshold
                    ? common
                    : (p > rareThreshold ? uncommon : (p > ultraRareThreshold ? rare : ultra));

                target.Total++;
                if (a.Unlocked)
                {
                    target.Unlocked++;
                }
                else
                {
                    target.Locked++;
                }
            }

            return new SingleGameSnapshot(
                gameId,
                data.LastUpdatedUtc,
                total,
                unlocked,
                locked,
                percent,
                is100Percent,
                all,
                asc,
                desc,
                rarityAsc,
                rarityDesc,
                common,
                uncommon,
                rare,
                ultra);
        }
    }
}

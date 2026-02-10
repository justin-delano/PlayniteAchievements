using System;
using System.Collections.Generic;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Immutable, precomputed theme-integration data for a single game selection.
    /// Built off the UI thread, then applied to <see cref="PlayniteAchievementsSettings"/> on the UI thread.
    /// </summary>
    public sealed class SingleGameSnapshot
    {
        public Guid GameId { get; }
        public DateTime LastUpdatedUtc { get; }

        public int Total { get; }
        public int Unlocked { get; }
        public int Locked { get; }
        public double Percent { get; }

        public bool Is100Percent { get; }

        public List<AchievementDetail> AllAchievements { get; }
        public List<AchievementDetail> UnlockDateAsc { get; }
        public List<AchievementDetail> UnlockDateDesc { get; }
        public List<AchievementDetail> RarityAsc { get; }
        public List<AchievementDetail> RarityDesc { get; }

        public AchievementRarityStats Common { get; }
        public AchievementRarityStats Uncommon { get; }
        public AchievementRarityStats Rare { get; }
        public AchievementRarityStats UltraRare { get; }

        public SingleGameSnapshot(
            Guid gameId,
            DateTime lastUpdatedUtc,
            int total,
            int unlocked,
            int locked,
            double percent,
            bool is100Percent,
            List<AchievementDetail> allAchievements,
            List<AchievementDetail> unlockDateAsc,
            List<AchievementDetail> unlockDateDesc,
            List<AchievementDetail> rarityAsc,
            List<AchievementDetail> rarityDesc,
            AchievementRarityStats common,
            AchievementRarityStats uncommon,
            AchievementRarityStats rare,
            AchievementRarityStats ultraRare)
        {
            GameId = gameId;
            LastUpdatedUtc = lastUpdatedUtc;
            Total = total;
            Unlocked = unlocked;
            Locked = locked;
            Percent = percent;
            Is100Percent = is100Percent;
            AllAchievements = allAchievements ?? new List<AchievementDetail>();
            UnlockDateAsc = unlockDateAsc ?? new List<AchievementDetail>();
            UnlockDateDesc = unlockDateDesc ?? new List<AchievementDetail>();
            RarityAsc = rarityAsc ?? new List<AchievementDetail>();
            RarityDesc = rarityDesc ?? new List<AchievementDetail>();
            Common = common ?? new AchievementRarityStats();
            Uncommon = uncommon ?? new AchievementRarityStats();
            Rare = rare ?? new AchievementRarityStats();
            UltraRare = ultraRare ?? new AchievementRarityStats();
        }
    }
}

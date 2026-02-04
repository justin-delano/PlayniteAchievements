using System;
using System.Runtime.Serialization;
using PlayniteAchievements.Models.Achievement;

namespace PlayniteAchievements.Models.Achievements
{
    /// <summary>
    /// Individual achievement detail with schema metadata and user unlock progress.
    /// </summary>
    public sealed class AchievementDetail
    {
        public string ApiName { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public string IconUrl { get; set; }

        [IgnoreDataMember]
        public string IconDisplay => IconUrl ?? AchievementIconResolver.GetDefaultIcon();

        public bool Hidden { get; set; }

        public DateTime? UnlockTimeUtc { get; set; }

        public double? GlobalPercentUnlocked { get; set; }

        [IgnoreDataMember]
        public bool Unlocked => UnlockTimeUtc.HasValue;

        [IgnoreDataMember]
        public bool IsUnlock => Unlocked;

        [IgnoreDataMember]
        public bool IsHidden => Hidden;

        [IgnoreDataMember]
        public string Name => DisplayName;

        [IgnoreDataMember]
        public string Icon => AchievementIconResolver.GetDisplayIcon(Unlocked, IconUrl);

        /// <summary>
        /// Global unlock percent (0..100). Some providers may return 0..1.
        /// </summary>
        [IgnoreDataMember]
        public double Percent
        {
            get
            {
                if (GlobalPercentUnlocked is double v)
                {
                    if (v > 0 && v <= 1) return v * 100.0;
                    return v;
                }

                return 0;
            }
        }

        [IgnoreDataMember]
        public DateTime? DateUnlocked
        {
            get
            {
                if (!UnlockTimeUtc.HasValue) return null;

                var utc = UnlockTimeUtc.Value;
                if (utc == DateTime.MinValue)
                {
                    return null;
                }
                if (utc.Kind == DateTimeKind.Unspecified)
                {
                    utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                }

                return utc.ToLocalTime();
            }
        }

        /// <summary>
        /// SuccessStory/Aniki-compatible points field. Some themes use this to select trophy-tier art.
        /// We don't have real gamerscore for most providers, so map rarity tiers to stable point buckets.
        /// </summary>
        [IgnoreDataMember]
        public int GamerScore
        {
            get
            {
                var percent = GlobalPercentUnlocked ?? 100;
                var tier = RarityHelper.GetRarityTier(percent);

                // Use values Aniki expects in its triggers (10/25/50/90/180).
                // - UltraRare -> 180 (platinum-ish)
                // - Rare -> 90 (gold-ish)
                // - Uncommon -> 50 (silver-ish)
                // - Common -> 25 (bronze-ish)
                // - Unknown/0 -> 10 (fallback)
                if (tier == RarityTier.UltraRare) return 180;
                if (tier == RarityTier.Rare) return 90;
                if (tier == RarityTier.Uncommon) return 50;
                if (tier == RarityTier.Common) return 25;
                return 10;
            }
        }
    }
}

using System.Windows.Media;

namespace PlayniteAchievements.Models.Achievement
{
    /// <summary>
    /// Helper for determining achievement rarity based on configurable thresholds.
    /// </summary>
    public static class RarityHelper
    {
        // Default thresholds
        private static double _ultraRareThreshold = 5;
        private static double _rareThreshold = 20;
        private static double _uncommonThreshold = 50;

        // Rarity brushes
        private static readonly SolidColorBrush CommonBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xC3, 0x4A));
        private static readonly SolidColorBrush UncommonBrush = new SolidColorBrush(Color.FromRgb(0x03, 0xA9, 0xF4));
        private static readonly SolidColorBrush RareBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
        private static readonly SolidColorBrush UltraRareBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63));

        static RarityHelper()
        {
            CommonBrush.Freeze();
            UncommonBrush.Freeze();
            RareBrush.Freeze();
            UltraRareBrush.Freeze();
        }

        /// <summary>
        /// Configure the rarity thresholds from settings.
        /// </summary>
        public static void Configure(double ultraRare, double rare, double uncommon)
        {
            _ultraRareThreshold = ultraRare;
            _rareThreshold = rare;
            _uncommonThreshold = uncommon;
        }

        public static double UltraRareThreshold => _ultraRareThreshold;
        public static double RareThreshold => _rareThreshold;
        public static double UncommonThreshold => _uncommonThreshold;

        /// <summary>
        /// Gets the rarity tier for a given global unlock percentage.
        /// </summary>
        public static RarityTier GetRarityTier(double globalPercent)
        {
            if (globalPercent <= _ultraRareThreshold) return RarityTier.UltraRare;
            if (globalPercent <= _rareThreshold) return RarityTier.Rare;
            if (globalPercent <= _uncommonThreshold) return RarityTier.Uncommon;
            return RarityTier.Common;
        }

        /// <summary>
        /// Gets the badge icon resource key for a given global unlock percentage.
        /// </summary>
        public static string GetRarityIconKey(double globalPercent)
        {
            return GetRarityTier(globalPercent) switch
            {
                RarityTier.UltraRare => "BadgePlatinumHexagon",
                RarityTier.Rare => "BadgeGoldPentagon",
                RarityTier.Uncommon => "BadgeSilverSquare",
                _ => "BadgeBronzeTriangle"
            };
        }

        /// <summary>
        /// Gets the rarity brush for a given global unlock percentage.
        /// </summary>
        public static SolidColorBrush GetRarityBrush(double globalPercent)
        {
            return GetRarityTier(globalPercent) switch
            {
                RarityTier.UltraRare => UltraRareBrush,
                RarityTier.Rare => RareBrush,
                RarityTier.Uncommon => UncommonBrush,
                _ => CommonBrush
            };
        }
    }

    public enum RarityTier
    {
        Common,
        Uncommon,
        Rare,
        UltraRare
    }
}

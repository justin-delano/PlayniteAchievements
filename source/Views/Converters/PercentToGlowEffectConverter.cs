using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.Converters
{
    internal static class SuccessStoryRarityPalette
    {
        // SuccessStory-like palette:
        // - Ultra Rare: Gold
        // - Rare: Silver
        // - Everything else: Transparent (no highlight)
        internal static readonly Color UltraRareGold = Color.FromRgb(0xFF, 0xD7, 0x00); // Gold
        internal static readonly Color RareSilver = Color.FromRgb(0xC0, 0xC0, 0xC0);    // Silver

        /// <summary>
        /// Gets the color associated with a rarity tier.
        /// </summary>
        internal static Color GetColor(RarityTier tier) => tier switch
        {
            RarityTier.UltraRare => UltraRareGold,
            RarityTier.Rare => RareSilver,
            _ => Colors.Transparent
        };
    }

    /// <summary>
    /// Multi-value converter that returns a DropShadowEffect with color based on rarity.
    /// Expects values: [GlobalPercentUnlocked, UltraRareThreshold, RareThreshold, UncommonThreshold]
    /// </summary>
    public class PercentToGlowEffectConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 4)
                return null;

            if (values[0] is double percent &&
                values[1] is double ultraRareThreshold &&
                values[2] is double rareThreshold &&
                values[3] is double _)
            {
                // Use canonical RarityHelper for classification
                var tier = RarityHelper.GetRarityTier(percent);
                var color = SuccessStoryRarityPalette.GetColor(tier);

                // Common/uncommon: no border/glow.
                if (tier == RarityTier.Common || tier == RarityTier.Uncommon)
                    return null;

                return new DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Color = color,
                    Opacity = 1.0
                };
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

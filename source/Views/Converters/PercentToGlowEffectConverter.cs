using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Rarity classification tier based on achievement unlock percentage.
    /// </summary>
    public enum RarityTier
    {
        UltraRare,
        Rare,
        Common
    }

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
    /// Helper class for classifying achievement rarity based on percentage thresholds.
    /// </summary>
    internal static class RarityClassifier
    {
        /// <summary>
        /// Classifies a percentage unlock rate into a rarity tier.
        /// </summary>
        /// <param name="percent">The global unlock percentage.</param>
        /// <param name="ultraRareThreshold">Threshold below which achievements are UltraRare.</param>
        /// <param name="rareThreshold">Threshold below which achievements are Rare.</param>
        /// <returns>The classified rarity tier.</returns>
        internal static RarityTier Classify(double percent, double ultraRareThreshold, double rareThreshold)
        {
            if (percent < ultraRareThreshold)
                return RarityTier.UltraRare;
            if (percent < rareThreshold)
                return RarityTier.Rare;
            return RarityTier.Common;
        }
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
                var tier = RarityClassifier.Classify(percent, ultraRareThreshold, rareThreshold);
                var color = SuccessStoryRarityPalette.GetColor(tier);

                // Common/uncommon: no border/glow.
                if (tier == RarityTier.Common)
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

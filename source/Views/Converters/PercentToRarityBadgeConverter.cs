using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Converts achievement percentage to the appropriate rarity badge icon from RarityIcons.xaml.
    /// Uses settings thresholds to determine rarity tier.
    ///
    /// Rarity tiers:
    /// - Ultra Rare (< UltraRareThreshold): BadgePlatinumHexagon
    /// - Rare (< RareThreshold): BadgeGoldPentagon
    /// - Uncommon (< UncommonThreshold): BadgeSilverSquare
    /// - Common (>= UncommonThreshold): BadgeBronzeTriangle
    /// </summary>
    public class PercentToRarityBadgeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 4)
                return null;

            // values[0] = GlobalPercentUnlocked (double)
            // values[1] = UltraRareThreshold (double)
            // values[2] = RareThreshold (double)
            // values[3] = UncommonThreshold (double)

            if (values[0] is double percent &&
                values[1] is double ultraRareThreshold &&
                values[2] is double rareThreshold &&
                values[3] is double uncommonThreshold)
            {
                string badgeResourceKey;

                if (percent < ultraRareThreshold)
                    badgeResourceKey = "BadgePlatinumHexagon";
                else if (percent < rareThreshold)
                    badgeResourceKey = "BadgeGoldPentagon";
                else if (percent < uncommonThreshold)
                    badgeResourceKey = "BadgeSilverSquare";
                else
                    badgeResourceKey = "BadgeBronzeTriangle";

                // Try to find the resource in the application resources
                try
                {
                    if (Application.Current.TryFindResource(badgeResourceKey) is DrawingImage badgeImage)
                    {
                        return badgeImage;
                    }
                }
                catch
                {
                    // Resource not found, return null
                }
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

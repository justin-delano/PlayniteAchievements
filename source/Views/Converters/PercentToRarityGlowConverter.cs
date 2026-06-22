using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Single-value converter that returns a DropShadowEffect with color based on achievement rarity.
    /// Glow parameters: BlurRadius=20, ShadowDepth=0, Opacity=1.0
    /// Colors:
    /// - Ultra Rare: Light blue (#4FC3F7)
    /// - Rare: Gold (#FFD700)
    /// - Uncommon: Silver (#C0C0C0)
    /// - Common: No glow (null)
    /// </summary>
    public class PercentToRarityGlowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is RarityTier tier))
            {
                return null;
            }

            return RarityAppearanceHelper.GetGlow(tier, 20);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Compact variant with smaller BlurRadius (8 instead of 20) for tight layouts.
    /// </summary>
    public class PercentToCompactRarityGlowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is RarityTier tier))
            {
                return null;
            }

            return RarityAppearanceHelper.GetGlow(tier, 8);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

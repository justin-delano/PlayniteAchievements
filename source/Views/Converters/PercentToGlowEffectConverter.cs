using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Multi-value converter that returns a DropShadowEffect with color based on rarity.
    /// Expects values: [GlobalPercentUnlocked, UltraRareThreshold, RareThreshold, UncommonThreshold]
    /// </summary>
    public class PercentToGlowEffectConverter : IMultiValueConverter
    {
        private static readonly DropShadowEffect UltraRareGlow;
        private static readonly DropShadowEffect RareGlow;

        static PercentToGlowEffectConverter()
        {
            UltraRareGlow = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 0,
                Color = Color.FromRgb(0x4F, 0xC3, 0xF7),
                Opacity = 1.0
            };
            UltraRareGlow.Freeze();

            RareGlow = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 0,
                Color = Color.FromRgb(0xFF, 0xD7, 0x00),
                Opacity = 1.0
            };
            RareGlow.Freeze();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 4)
                return null;

            if (values[0] == null)
                return null;

            if (values[0] is double percent &&
                values[1] is double _ &&
                values[2] is double _ &&
                values[3] is double _)
            {
                var tier = RarityHelper.GetRarityTier(percent);

                return tier switch
                {
                    RarityTier.UltraRare => UltraRareGlow,
                    RarityTier.Rare => RareGlow,
                    _ => null
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

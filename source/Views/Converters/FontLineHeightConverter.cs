using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// The font's own line pitch, <c>FontFamily.LineSpacing × FontSize</c> rounded up to a whole
    /// pixel, for a fixed
    /// <c>TextBlock.LineHeight</c>. Values: [0] FontFamily, [1] FontSize.
    /// </summary>
    /// <remarks>
    /// A TextBlock and a TextBox left on the default MaxHeight stacking each size a wrapped line
    /// from the glyphs on it, and they do not always agree, so text that swaps between the two
    /// visibly changes its line spacing. Pinning both to the same pitch with BlockLineHeight keeps
    /// the lines where they are; the TextBox honors the attached TextBlock.LineHeight.
    /// </remarks>
    public sealed class FontLineHeightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null ||
                values.Length < 2 ||
                !(values[0] is FontFamily family) ||
                !(values[1] is double size) ||
                double.IsNaN(size) ||
                size <= 0)
            {
                return DependencyProperty.UnsetValue;
            }

            // Whole pixels, so a wrapped block is a whole number of pixels tall. At the font's
            // fractional pitch (Roboto 14: 16.8) a layout-rounded TextBlock grew to the next pixel
            // while the TextBox centered the unrounded height, putting the TextBox's first line
            // 0.2px lower, which shows as the text jumping when the box takes over.
            return Math.Ceiling(family.LineSpacing * size);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

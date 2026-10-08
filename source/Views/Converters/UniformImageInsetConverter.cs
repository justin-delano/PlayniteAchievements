using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Insets from an Image element's box to the picture it draws, so an overlay given the result
    /// as its Margin covers the picture exactly. Bindings, in order: Source, ActualWidth,
    /// ActualHeight and Stretch. Only Uniform stretch letterboxes; every other stretch, and an
    /// image with no source yet, draws across the whole box and gets no inset.
    /// </summary>
    public class UniformImageInsetConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 4 ||
                !(values[0] is ImageSource source) ||
                !(values[1] is double boxWidth) ||
                !(values[2] is double boxHeight) ||
                !(values[3] is Stretch stretch) ||
                stretch != Stretch.Uniform)
            {
                return new Thickness(0);
            }

            var sourceWidth = source.Width;
            var sourceHeight = source.Height;
            if (sourceWidth <= 0 || sourceHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
            {
                return new Thickness(0);
            }

            var scale = Math.Min(boxWidth / sourceWidth, boxHeight / sourceHeight);
            var insetX = Math.Max(0, (boxWidth - sourceWidth * scale) / 2);
            var insetY = Math.Max(0, (boxHeight - sourceHeight * scale) / 2);
            return new Thickness(insetX, insetY, insetX, insetY);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("UniformImageInsetConverter does not support ConvertBack.");
        }
    }
}

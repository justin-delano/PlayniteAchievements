using System;
using System.Globalization;
using System.Windows.Data;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Sizes image cells from the available column width. Platform badges keep row-aware uniform sizing.
    /// ConverterParameter: mode,dimension,horizontalPadding,verticalPadding,fallbackSize.
    /// mode is icon, cover, platform, or auto. dimension is width or height.
    /// </summary>
    public class ImageFitDimensionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var parts = (parameter?.ToString() ?? "icon,width").Split(',');
            var mode = parts.Length > 0 ? parts[0].Trim().ToLowerInvariant() : "icon";
            var dimension = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "width";
            var useCover = mode == "cover" ||
                (mode == "auto" && values != null && values.Length > 2 && values[2] is bool boolValue && boolValue);
            var aspectRatio = useCover ? 2d / 3d : 1d;
            var horizontalPadding = ParseDouble(parts, 2, 8d);
            var verticalPadding = ParseDouble(parts, 3, 8d);
            var fallbackSize = ParseDouble(parts, 4, 12d);

            var cellWidth = GetFiniteDouble(values, 1);
            var rowHeight = GetFiniteDouble(values, 0);
            var availableWidth = cellWidth.HasValue
                ? Math.Max(1d, cellWidth.Value - horizontalPadding)
                : fallbackSize;
            var availableHeight = rowHeight.HasValue
                ? Math.Max(1d, rowHeight.Value - verticalPadding)
                : fallbackSize / aspectRatio;
            var fitWidth = mode == "icon" || mode == "cover" || mode == "auto";
            var height = fitWidth
                ? availableWidth / aspectRatio
                : Math.Min(availableHeight, availableWidth / aspectRatio);
            if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            {
                height = fallbackSize / aspectRatio;
            }

            return dimension == "height" ? height : height * aspectRatio;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static double? GetFiniteDouble(object[] values, int index)
        {
            if (values == null || values.Length <= index || !(values[index] is double value))
            {
                return null;
            }

            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0
                ? (double?)null
                : value;
        }

        private static double ParseDouble(string[] parts, int index, double fallback)
        {
            if (parts == null ||
                parts.Length <= index ||
                !double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return fallback;
            }

            return Math.Max(0d, value);
        }
    }
}

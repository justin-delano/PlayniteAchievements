using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Converts control height to font size (approximately 1/3 of height).
    /// Used for dynamically sizing text based on parent control height.
    /// </summary>
    public class HeightToFontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double height && height > 0)
            {
                return height * 0.3;
            }
            return 12.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Performs mathematical operations on binding values.
    /// Used for calculating widths/heights by adding/subtracting values.
    /// ConverterParameter: "+" for addition, "-" for subtraction
    /// </summary>
    public class ValueOperationConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double first &&
                values[1] is double second)
            {
                string operation = parameter?.ToString() ?? "-";

                if (operation == "+")
                    return first + second;
                else if (operation == "-")
                    return first - second;
                else if (operation == "*")
                    return first * second;
                else if (operation == "/" && second != 0)
                    return first / second;
            }

            return values[0];
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a resource key string to the actual resource (DrawingImage) from application resources.
    /// Used for dynamically loading icons based on data binding.
    /// </summary>
    public class ResourceKeyToImageSourceConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string resourceKey && !string.IsNullOrEmpty(resourceKey))
            {
                try
                {
                    return Application.Current.FindResource(resourceKey);
                }
                catch
                {
                    // Resource not found, return null
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(values != null && values.Length > 0 ? values[0] : null, targetType, parameter, culture);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a provider icon key and color hex to a colored DrawingImage.
    /// Takes IconKey as the binding value and ColorHex as the converter parameter.
    /// </summary>
    public class ProviderIconConverter : IMultiValueConverter
    {
        // Cache of colored provider icon images keyed by geometry resource key plus color hex
        // (the rendered image depends on both). Geo* geometry resources are defined statically
        // and are not rewritten at runtime. Only successful resolutions are cached so
        // late-loading resource dictionaries can still be found. Converters run on the UI
        // thread only, so an unlocked Dictionary is acceptable.
        private static readonly Dictionary<string, DrawingImage> IconImageCache = new Dictionary<string, DrawingImage>();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is string iconKey &&
                values[1] is string colorHex &&
                !string.IsNullOrEmpty(iconKey) &&
                !string.IsNullOrEmpty(colorHex))
            {
                try
                {
                    // Try to find a "Geo" + iconName resource (e.g., GeoSteam for ProviderIconSteam)
                    string geoKey = "Geo" + iconKey.Replace("ProviderIcon", "");
                    string cacheKey = geoKey + "|" + colorHex;

                    if (IconImageCache.TryGetValue(cacheKey, out var cachedImage))
                    {
                        return cachedImage;
                    }

                    var geometry = Application.Current.TryFindResource(geoKey) as Geometry;
                    if (geometry != null)
                    {
                        // Parse the color
                        if (ColorConverter.ConvertFromString(colorHex) is Color color)
                        {
                            // Create a new DrawingImage with the color applied
                            var drawingImage = new DrawingImage();
                            drawingImage.Drawing = new GeometryDrawing
                            {
                                Geometry = geometry,
                                Brush = new SolidColorBrush(color)
                            };
                            drawingImage.Freeze();
                            IconImageCache[cacheKey] = drawingImage;
                            return drawingImage;
                        }
                    }
                }
                catch
                {
                    // Fall through to null
                }
            }
            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Provides fallback to theme-specified default images when games don't have cover/logo/icon metadata.
    /// Returns the path string when available (for async loading) or the theme default ImageSource when null/empty.
    /// ConverterParameter: "icon" for DefaultGameIcon, "cover" for DefaultGameCover
    /// </summary>
    public class DefaultGameImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // If value is a non-empty string, return it (AsyncImage will handle async loading)
            if (value is string str && !string.IsNullOrEmpty(str))
            {
                return value;
            }

            // If null/empty, return the appropriate default resource from the theme
            string imageType = parameter?.ToString();
            string resourceKey = imageType == "cover" ? "DefaultGameCover" : "DefaultGameIcon";

            try
            {
                var defaultImage = Application.Current.TryFindResource(resourceKey);
                if (defaultImage != null)
                {
                    return defaultImage;
                }
            }
            catch
            {
                // Resource not found, fall through to null
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Shared ConvertBack logic for the nullable numeric text converters. Blank input maps to
    /// null, unparseable input maps to Binding.DoNothing, and a ConverterParameter minimum
    /// clamps positive values from below. Numeric parsing stays type-specific via the supplied
    /// TryParse implementation (int and double accept different formats).
    /// </summary>
    internal static class NullableNumericTextHelper
    {
        internal delegate bool TryParseNumber<T>(string text, CultureInfo culture, out T value);

        internal static bool TryParseDouble(string text, CultureInfo culture, out double value)
        {
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out value);
        }

        internal static bool TryParseInt(string text, CultureInfo culture, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out value);
        }

        internal static object ConvertBack<T>(object value, object parameter, CultureInfo culture, TryParseNumber<T> tryParse)
            where T : struct, IComparable<T>
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (tryParse(text, culture, out var parsed))
            {
                var minimum = ParseMinimum(parameter, culture, tryParse);
                if (minimum.CompareTo(default(T)) > 0 && parsed.CompareTo(default(T)) > 0 && parsed.CompareTo(minimum) < 0)
                {
                    return minimum;
                }

                return parsed;
            }

            return Binding.DoNothing;
        }

        private static T ParseMinimum<T>(object parameter, CultureInfo culture, TryParseNumber<T> tryParse)
            where T : struct
        {
            if (parameter == null)
            {
                return default(T);
            }

            var parameterText = parameter.ToString();
            if (string.IsNullOrWhiteSpace(parameterText))
            {
                return default(T);
            }

            if (tryParse(parameterText, CultureInfo.InvariantCulture, out var invariantValue))
            {
                return invariantValue;
            }

            return tryParse(parameterText, culture, out var cultureValue)
                ? cultureValue
                : default(T);
        }
    }

    /// <summary>
    /// Converts a TextBox string to nullable double, treating blank input as null.
    /// This is used for settings fields where an empty string means "unlimited".
    /// </summary>
    public class NullableDoubleTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue.ToString(culture);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return NullableNumericTextHelper.ConvertBack<double>(value, parameter, culture, NullableNumericTextHelper.TryParseDouble);
        }
    }

    /// <summary>
    /// Converts a TextBox string to nullable int, treating blank input as null.
    /// </summary>
    public class NullableIntTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue.ToString(culture);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return NullableNumericTextHelper.ConvertBack<int>(value, parameter, culture, NullableNumericTextHelper.TryParseInt);
        }
    }

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

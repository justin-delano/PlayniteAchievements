using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Defines the conversion mode for boolean values.
    /// </summary>
    public enum BooleanConvertMode
    {
        /// <summary>
        /// Passes through boolean value unchanged (bool to bool).
        /// </summary>
        Direct,

        /// <summary>
        /// Inverts the boolean value (bool to inverted bool).
        /// </summary>
        Inverted,

        /// <summary>
        /// Converts boolean to Visibility (true = Visible, false = Collapsed).
        /// </summary>
        ToVisibility,

        /// <summary>
        /// Converts boolean to inverted Visibility (true = Collapsed, false = Visible).
        /// </summary>
        InvertedToVisibility
    }

    /// <summary>
    /// Unified boolean converter supporting multiple conversion modes.
    /// </summary>
    public class BooleanConverter : IValueConverter
    {
        /// <summary>
        /// Gets or sets the conversion mode.
        /// </summary>
        public BooleanConvertMode Mode { get; set; } = BooleanConvertMode.ToVisibility;

        /// <summary>
        /// Converts a boolean value based on the specified Mode.
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool boolValue))
            {
                return Mode == BooleanConvertMode.InvertedToVisibility
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return Mode switch
            {
                BooleanConvertMode.Direct => boolValue,
                BooleanConvertMode.Inverted => !boolValue,
                BooleanConvertMode.ToVisibility => boolValue ? Visibility.Visible : Visibility.Collapsed,
                BooleanConvertMode.InvertedToVisibility => boolValue ? Visibility.Collapsed : Visibility.Visible,
                _ => Visibility.Collapsed
            };
        }

        /// <summary>
        /// Converts a value back to boolean based on the specified Mode.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Mode switch
            {
                BooleanConvertMode.Direct when value is bool boolValue => boolValue,
                BooleanConvertMode.Inverted when value is bool boolValue => !boolValue,
                BooleanConvertMode.ToVisibility when value is Visibility visibility => visibility == Visibility.Visible,
                BooleanConvertMode.InvertedToVisibility when value is Visibility visibility => visibility != Visibility.Visible,
                _ => false
            };
        }
    }
}

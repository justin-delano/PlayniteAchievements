using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Selects which icon URI to display based on the locked state.
    /// Values: [0]=IsLocked (bool), [1]=IconUrl (string).
    /// Applies grayscale prefix when locked.
    /// </summary>
    public sealed class LockedIconSelectorConverter : IMultiValueConverter
    {
        private const string GrayPrefix = "gray:";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return DependencyProperty.UnsetValue;
            }

            bool isLocked = values[0] is bool b && b;
            string iconUrl = values[1] as string;

            if (string.IsNullOrWhiteSpace(iconUrl))
            {
                return DependencyProperty.UnsetValue;
            }

            return isLocked ? GrayPrefix + iconUrl : iconUrl;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Selects which icon URI to display based on the locked state.
    /// Values: [0]=IsLocked (bool), [1]=LockedIcon (string), [2]=UnlockedIcon (string).
    /// </summary>
    public sealed class LockedIconSelectorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
            {
                return DependencyProperty.UnsetValue;
            }

            bool isLocked = values[0] is bool b && b;
            string lockedIcon = values[1] as string;
            string unlockedIcon = values[2] as string;

            if (isLocked && !string.IsNullOrWhiteSpace(lockedIcon))
            {
                return lockedIcon;
            }

            if (!string.IsNullOrWhiteSpace(unlockedIcon))
            {
                return unlockedIcon;
            }

            // Fall back to locked icon if unlocked icon is missing.
            if (!string.IsNullOrWhiteSpace(lockedIcon))
            {
                return lockedIcon;
            }

            return DependencyProperty.UnsetValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


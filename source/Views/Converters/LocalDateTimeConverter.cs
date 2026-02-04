using System;
using System.Globalization;
using System.Windows.Data;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Converts UTC DateTime to local time string.
    /// Matches SuccessStory's LocalDateTimeConverter behavior.
    /// </summary>
    public class LocalDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                // Convert UTC to local
                DateTime localTime = dateTime.Kind == DateTimeKind.Utc
                    ? dateTime.ToLocalTime()
                    : dateTime;

                // Return formatted string
                return localTime.ToString("yyyy-MM-dd HH:mm");
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return dateTimeOffset.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

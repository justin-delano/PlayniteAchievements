using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Views.Converters
{
    /// <summary>
    /// Converts RarityTier enum to the corresponding badge DrawingImage from resources.
    /// </summary>
    public class RarityTierToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RarityTier tier)
            {
                var resourceKey = tier.ToIconKey();
                try
                {
                    if (Application.Current.TryFindResource(resourceKey) is DrawingImage badgeImage)
                    {
                        return badgeImage;
                    }
                }
                catch
                {
                    // Resource not found
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

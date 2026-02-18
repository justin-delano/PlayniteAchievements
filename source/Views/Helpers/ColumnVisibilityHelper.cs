using System.Windows;

namespace PlayniteAchievements.Views.Helpers
{
    public static class ColumnVisibilityHelper
    {
        public static readonly DependencyProperty ColumnKeyProperty =
            DependencyProperty.RegisterAttached(
                "ColumnKey",
                typeof(string),
                typeof(ColumnVisibilityHelper),
                new PropertyMetadata(null));

        public static void SetColumnKey(DependencyObject element, string value)
        {
            element?.SetValue(ColumnKeyProperty, value);
        }

        public static string GetColumnKey(DependencyObject element)
        {
            return element?.GetValue(ColumnKeyProperty) as string;
        }
    }
}

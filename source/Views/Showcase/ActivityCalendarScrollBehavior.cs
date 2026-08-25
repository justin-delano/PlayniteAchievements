using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Scroll handling for the activity calendar's horizontal ScrollViewer: pins the view to the
    /// right end (the most recent weeks) whenever the calendar's extent width changes, and maps
    /// the vertical mouse wheel to horizontal scrolling, which a ScrollViewer with vertical
    /// scrolling disabled otherwise swallows.
    /// </summary>
    public static class ActivityCalendarScrollBehavior
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(ActivityCalendarScrollBehavior),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject element) =>
            (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) =>
            element.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is ScrollViewer viewer))
            {
                return;
            }

            viewer.ScrollChanged -= OnScrollChanged;
            viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            if (e.NewValue is bool enabled && enabled)
            {
                viewer.ScrollChanged += OnScrollChanged;
                viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // The calendar ends at today, so any extent width change (first layout, a new range,
            // a font size change) re-pins the view to the most recent weeks. User scrolling only
            // changes the offset, never the extent, so it is left alone.
            if (e.ExtentWidthChange != 0 && sender is ScrollViewer viewer)
            {
                viewer.ScrollToRightEnd();
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!(sender is ScrollViewer viewer) || viewer.ScrollableWidth <= 0)
            {
                return;
            }

            viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}

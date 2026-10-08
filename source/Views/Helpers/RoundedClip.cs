using System.Windows;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Clips an element to a rounded rectangle of its own size. A Border's CornerRadius rounds only
    /// its own background and border, and ClipToBounds clips to the plain rectangle, so art inside
    /// a rounded Border keeps square corners without this.
    /// </summary>
    public static class RoundedClip
    {
        public static readonly DependencyProperty RadiusProperty =
            DependencyProperty.RegisterAttached(
                "Radius",
                typeof(double),
                typeof(RoundedClip),
                new FrameworkPropertyMetadata(0d, OnRadiusChanged));

        public static void SetRadius(DependencyObject element, double value) =>
            element?.SetValue(RadiusProperty, value);

        public static double GetRadius(DependencyObject element) =>
            element == null ? 0d : (double)element.GetValue(RadiusProperty);

        private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement element))
            {
                return;
            }

            // The handler lives on the element itself, so it never outlives it.
            element.SizeChanged -= OnSizeChanged;
            if ((double)e.NewValue > 0)
            {
                element.SizeChanged += OnSizeChanged;
            }

            Apply(element);
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
            Apply((FrameworkElement)sender);

        private static void Apply(FrameworkElement element)
        {
            var radius = GetRadius(element);
            if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                element.ClearValue(UIElement.ClipProperty);
                return;
            }

            var clip = new RectangleGeometry(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight),
                radius,
                radius);
            clip.Freeze();
            element.Clip = clip;
        }
    }
}

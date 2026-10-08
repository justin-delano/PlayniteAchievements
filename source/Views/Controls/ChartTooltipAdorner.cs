using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Shows a chart's hover tooltip in the window's adorner layer, at a point in the adorned
    /// element's coordinates. Unlike the Popup LiveCharts uses, which is its own window, the
    /// tooltip renders in the chart's window, so a new slice's content and position reach the
    /// screen in the same frame rather than one after the other. It never takes the mouse.
    /// </summary>
    internal sealed class ChartTooltipAdorner : Adorner
    {
        private readonly UIElement _tooltip;
        private System.Func<Size, Point> _placement;

        public ChartTooltipAdorner(UIElement adornedElement, UIElement tooltip)
            : base(adornedElement)
        {
            _tooltip = tooltip;
            AddVisualChild(tooltip);
            IsHitTestVisible = false;
        }

        /// <summary>
        /// Places the tooltip by its size, which is only known once its new content has been
        /// measured; the placement runs in this adorner's layout pass, after that measure.
        /// </summary>
        public void Place(System.Func<Size, Point> placement)
        {
            _placement = placement;
            InvalidateMeasure();
            InvalidateArrange();
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _tooltip;

        protected override Size MeasureOverride(Size constraint)
        {
            _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = _tooltip.DesiredSize;
            _tooltip.Arrange(new Rect(_placement?.Invoke(size) ?? default(Point), size));
            return finalSize;
        }
    }
}

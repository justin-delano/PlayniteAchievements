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
        private Point _position;

        public ChartTooltipAdorner(UIElement adornedElement, UIElement tooltip)
            : base(adornedElement)
        {
            _tooltip = tooltip;
            AddVisualChild(tooltip);
            IsHitTestVisible = false;
        }

        public void MoveTo(Point position)
        {
            _position = position;
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
            _tooltip.Arrange(new Rect(_position, _tooltip.DesiredSize));
            return finalSize;
        }
    }
}

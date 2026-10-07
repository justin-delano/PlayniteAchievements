using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Lays out a pie beside its legend: the first child (the pie) gets a square sized to the
    /// space left over beside the second child (the legend), on the side
    /// <see cref="LegendPosition"/> names, and the pie + legend pair is centered horizontally so
    /// the space before the pair matches the space after it. A collapsed legend takes no width
    /// and no spacing, which leaves the pie centered on its own.
    /// </summary>
    public sealed class PieLegendLayoutPanel : Panel
    {
        private const double LegendSpacing = 16.0;

        public static readonly DependencyProperty LegendPositionProperty =
            DependencyProperty.Register(nameof(LegendPosition), typeof(PieLegendPosition), typeof(PieLegendLayoutPanel),
                new FrameworkPropertyMetadata(PieLegendPosition.Right, FrameworkPropertyMetadataOptions.AffectsArrange));

        public PieLegendPosition LegendPosition
        {
            get => (PieLegendPosition)GetValue(LegendPositionProperty);
            set => SetValue(LegendPositionProperty, value);
        }

        private UIElement Pie => InternalChildren.Count > 0 ? InternalChildren[0] : null;

        private UIElement Legend => InternalChildren.Count > 1 ? InternalChildren[1] : null;

        protected override Size MeasureOverride(Size availableSize)
        {
            var legend = Legend;
            legend?.Measure(availableSize);
            var legendSize = legend?.DesiredSize ?? new Size();
            var legendWidth = ResolveLegendWidth(legendSize.Width);

            // An unbounded height (an Auto row) gives no square to claim: the pie reports its own
            // desired height and the host's sizing sets the height it is arranged into.
            if (double.IsInfinity(availableSize.Height))
            {
                var pieWidth = double.IsInfinity(availableSize.Width)
                    ? double.PositiveInfinity
                    : Math.Max(0, availableSize.Width - legendWidth);
                Pie?.Measure(new Size(pieWidth, double.PositiveInfinity));
                var pieDesired = Pie?.DesiredSize ?? new Size();

                return new Size(
                    pieDesired.Width + legendWidth,
                    Math.Max(pieDesired.Height, legendSize.Height));
            }

            var side = ResolvePieSide(availableSize, legendWidth);
            Pie?.Measure(new Size(side, side));

            // Report the content's width, never the whole width offered: WPF clips an element whose
            // desired size exceeds the slot it is arranged into, and that clip cuts off the radial
            // icons drawn outside the pie.
            return new Size(
                side + legendWidth,
                Math.Max(side, legendSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var legendDesiredWidth = Legend?.DesiredSize.Width ?? 0;
            var legendWidth = ResolveLegendWidth(legendDesiredWidth);
            var side = ResolvePieSide(finalSize, legendWidth);
            var left = Math.Max(0, (finalSize.Width - side - legendWidth) / 2.0);
            var pieTop = (finalSize.Height - side) / 2.0;

            if (LegendPosition == PieLegendPosition.Left)
            {
                Legend?.Arrange(new Rect(left, 0, legendDesiredWidth, finalSize.Height));
                Pie?.Arrange(new Rect(left + legendWidth, pieTop, side, side));
            }
            else
            {
                Pie?.Arrange(new Rect(left, pieTop, side, side));
                Legend?.Arrange(new Rect(left + side + (legendWidth - legendDesiredWidth), 0, legendDesiredWidth, finalSize.Height));
            }

            return finalSize;
        }

        /// <summary>The legend's width plus the gap between it and the pie, or zero when it is collapsed.</summary>
        private static double ResolveLegendWidth(double legendDesiredWidth) =>
            legendDesiredWidth > 0 ? legendDesiredWidth + LegendSpacing : 0;

        private static double ResolvePieSide(Size size, double legendWidth)
        {
            var width = double.IsInfinity(size.Width) ? double.PositiveInfinity : size.Width - legendWidth;
            var side = Math.Min(width, size.Height);
            return double.IsInfinity(side) || double.IsNaN(side) ? 0 : Math.Max(0, side);
        }
    }
}

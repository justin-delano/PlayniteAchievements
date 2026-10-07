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
    /// and no spacing, which leaves the pie centered on its own. <see cref="HorizontalInset"/> is
    /// kept clear on both sides, so content drawn outside the pie stays within the panel.
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

        /// <summary>
        /// Width kept clear at each side of the pie + legend pair, for what the pie draws beyond its
        /// square (radial icons). It only shrinks the pie when the width, not the height, limits it.
        /// </summary>
        public static readonly DependencyProperty HorizontalInsetProperty =
            DependencyProperty.Register(nameof(HorizontalInset), typeof(double), typeof(PieLegendLayoutPanel),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public double HorizontalInset
        {
            get => (double)GetValue(HorizontalInsetProperty);
            set => SetValue(HorizontalInsetProperty, value);
        }

        private UIElement Pie => InternalChildren.Count > 0 ? InternalChildren[0] : null;

        private UIElement Legend => InternalChildren.Count > 1 ? InternalChildren[1] : null;

        // The legend's reserved width only grows while the offered width stays the same, so a data
        // change that narrows a count ("1,234" to "575") does not shrink the legend and resize the pie.
        private double reservedLegendWidth;
        private double reservedForAvailableWidth = double.NaN;

        protected override Size MeasureOverride(Size availableSize)
        {
            var legend = Legend;
            legend?.Measure(availableSize);
            var legendSize = legend?.DesiredSize ?? new Size();
            ReserveLegendWidth(legendSize.Width, availableSize.Width);
            var legendWidth = ResolveLegendWidth(reservedLegendWidth);
            var insets = 2.0 * Math.Max(0, HorizontalInset);

            // An unbounded height (an Auto row) gives no square to claim: the pie reports its own
            // desired height and the host's sizing sets the height it is arranged into.
            if (double.IsInfinity(availableSize.Height))
            {
                var pieWidth = double.IsInfinity(availableSize.Width)
                    ? double.PositiveInfinity
                    : Math.Max(0, availableSize.Width - legendWidth - insets);
                Pie?.Measure(new Size(pieWidth, double.PositiveInfinity));
                var pieDesired = Pie?.DesiredSize ?? new Size();

                return new Size(
                    pieDesired.Width + legendWidth + insets,
                    Math.Max(pieDesired.Height, legendSize.Height));
            }

            var side = ResolvePieSide(availableSize, legendWidth + insets);
            Pie?.Measure(new Size(side, side));

            // Report the content's width, never the whole width offered: WPF clips an element whose
            // desired size exceeds the slot it is arranged into, and that clip cuts off the radial
            // icons drawn outside the pie.
            return new Size(
                side + legendWidth + insets,
                Math.Max(side, legendSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var legendDesiredWidth = Legend?.DesiredSize.Width > 0 ? reservedLegendWidth : 0;
            var legendWidth = ResolveLegendWidth(legendDesiredWidth);
            var inset = Math.Max(0, HorizontalInset);
            var side = ResolvePieSide(finalSize, legendWidth + (2.0 * inset));
            var left = inset + Math.Max(0, (finalSize.Width - (2.0 * inset) - side - legendWidth) / 2.0);
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

        /// <summary>
        /// Keeps the widest legend seen at the current offered width; a collapsed legend or a new
        /// offered width (a resize) starts over from the legend's own width.
        /// </summary>
        private void ReserveLegendWidth(double legendDesiredWidth, double availableWidth)
        {
            if (legendDesiredWidth <= 0)
            {
                reservedLegendWidth = 0;
            }
            else if (!availableWidth.Equals(reservedForAvailableWidth))
            {
                reservedLegendWidth = legendDesiredWidth;
            }
            else
            {
                reservedLegendWidth = Math.Max(reservedLegendWidth, legendDesiredWidth);
            }

            reservedForAvailableWidth = availableWidth;
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

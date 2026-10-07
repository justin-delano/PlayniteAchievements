using System;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Sizes itself and its child as a square whose side is the width it is offered.
    /// </summary>
    /// <remarks>
    /// For an icon cell whose size follows its column. Binding Height to ActualWidth does the same
    /// a pass late: ActualWidth is zero until the first arrange, so a new row measures without the
    /// icon, and a virtualizing grid packs rows into the viewport at that short height before they
    /// grow. In the Manage Achievements editor that built 16 rows where 9 fit, on every reset.
    /// Taking the side from the measure constraint settles the height in the first pass.
    /// </remarks>
    public sealed class SquareDecorator : Decorator
    {
        protected override Size MeasureOverride(Size constraint)
        {
            var side = ResolveSide(constraint);
            Child?.Measure(new Size(side, side));
            return new Size(side, side);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            // Centered in whatever the cell gives, which is taller than the square when another
            // cell sets the row's height.
            var side = Math.Min(arrangeSize.Width, arrangeSize.Height);
            Child?.Arrange(new Rect(
                (arrangeSize.Width - side) / 2,
                (arrangeSize.Height - side) / 2,
                side,
                side));
            return arrangeSize;
        }

        private double ResolveSide(Size constraint)
        {
            if (!double.IsInfinity(constraint.Width))
            {
                return constraint.Width;
            }

            // Offered unbounded width, as when a column auto-sizes: fall back to the height
            // bound, then to the child's own size, so the square is never infinite.
            if (!double.IsInfinity(constraint.Height))
            {
                return constraint.Height;
            }

            if (Child == null)
            {
                return 0;
            }

            Child.Measure(constraint);
            return Math.Max(Child.DesiredSize.Width, Child.DesiredSize.Height);
        }
    }
}

using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// The glossy fill charts give a colored shape (a pie slice, a bar or a bar segment): lighter
    /// toward its top-left and deeper toward its bottom-right, around the color itself. The
    /// gradient is relative to each shape's bounds, so every shape carries its own shine.
    /// </summary>
    public static class ChartGloss
    {
        // Offset of the stop that carries the color itself.
        private const double BaseOffset = 0.5;

        public static Brush Create(Color color)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(Blend(color, Colors.White, 0.35), 0));
            brush.GradientStops.Add(new GradientStop(color, BaseOffset));
            brush.GradientStops.Add(new GradientStop(Blend(color, Colors.Black, 0.30), 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>Whether <paramref name="brush"/> is the gloss <see cref="Create"/> makes for <paramref name="color"/>.</summary>
        public static bool IsGlossOf(Brush brush, Color color)
        {
            return brush is LinearGradientBrush gradient &&
                gradient.GradientStops.Any(stop => stop.Offset == BaseOffset && stop.Color == color);
        }

        public static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                from.A,
                (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
                (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
                (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
        }
    }
}

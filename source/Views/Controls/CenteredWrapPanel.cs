using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// A horizontal wrap panel that centers each line in the arranged width. Given unbounded
    /// width it lays everything on one line, as a horizontal stack panel does.
    /// </summary>
    public sealed class CenteredWrapPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
            foreach (UIElement child in InternalChildren)
            {
                child?.Measure(childConstraint);
            }

            var width = 0d;
            var height = 0d;
            foreach (var line in BuildLines(availableSize.Width))
            {
                width = Math.Max(width, line.Width);
                height += line.Height;
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var top = 0d;
            foreach (var line in BuildLines(finalSize.Width))
            {
                var x = Math.Max(0, (finalSize.Width - line.Width) / 2);
                for (var i = line.Start; i < line.End; i++)
                {
                    var child = InternalChildren[i];
                    if (child == null)
                    {
                        continue;
                    }

                    child.Arrange(new Rect(x, top, child.DesiredSize.Width, line.Height));
                    x += child.DesiredSize.Width;
                }

                top += line.Height;
            }

            return finalSize;
        }

        private List<Line> BuildLines(double maxWidth)
        {
            var lines = new List<Line>();
            var line = new Line();
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var size = InternalChildren[i]?.DesiredSize ?? default(Size);
                if (line.End > line.Start && line.Width + size.Width > maxWidth)
                {
                    lines.Add(line);
                    line = new Line { Start = i, End = i };
                }

                line.End = i + 1;
                line.Width += size.Width;
                line.Height = Math.Max(line.Height, size.Height);
            }

            if (line.End > line.Start)
            {
                lines.Add(line);
            }

            return lines;
        }

        private struct Line
        {
            public int Start;
            public int End;
            public double Width;
            public double Height;
        }
    }
}

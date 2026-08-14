using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PlayniteAchievements.ViewModels.Showcase.Widgets;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Draws the activity heatmap as a single visual instead of one element per day cell.
    /// A year (or more) of cells as retained Border elements makes page switches and
    /// scrolling drag; OnRender keeps the whole calendar one drawing and serves tooltips
    /// from mouse hit tests.
    /// </summary>
    public sealed class ActivityCalendarHeatmap : FrameworkElement
    {
        private const double MonthLabelHeight = 14;
        private const double MonthLabelFontSize = 9;
        private const double CellCornerRadius = 2;
        private const double CellMargin = 1;
        private static readonly double[] IntensityOpacity = { 0.30, 0.55, 0.78, 1.0 };

        public static readonly DependencyProperty WeeksProperty =
            DependencyProperty.Register(
                nameof(Weeks),
                typeof(IReadOnlyList<ActivityCalendarWeekViewModel>),
                typeof(ActivityCalendarHeatmap),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<ActivityCalendarWeekViewModel> Weeks
        {
            get => (IReadOnlyList<ActivityCalendarWeekViewModel>)GetValue(WeeksProperty);
            set => SetValue(WeeksProperty, value);
        }

        /// <summary>Cell box size including margins; matches the view model's weekday row height.</summary>
        public static readonly DependencyProperty CellBoxProperty =
            DependencyProperty.Register(
                nameof(CellBox),
                typeof(double),
                typeof(ActivityCalendarHeatmap),
                new FrameworkPropertyMetadata(
                    12d,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public double CellBox
        {
            get => (double)GetValue(CellBoxProperty);
            set => SetValue(CellBoxProperty, value);
        }

        public static readonly DependencyProperty ShowMonthLabelsProperty =
            DependencyProperty.Register(
                nameof(ShowMonthLabels),
                typeof(bool),
                typeof(ActivityCalendarHeatmap),
                new FrameworkPropertyMetadata(
                    true,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowMonthLabels
        {
            get => (bool)GetValue(ShowMonthLabelsProperty);
            set => SetValue(ShowMonthLabelsProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var cellBox = Math.Max(1, CellBox);
            var width = (Weeks?.Count ?? 0) * cellBox;
            var height = (ShowMonthLabels ? MonthLabelHeight : 0) + 7 * cellBox;
            return new Size(Math.Max(0, width), Math.Max(0, height));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var weeks = Weeks;
            if (weeks == null || weeks.Count == 0)
            {
                return;
            }

            // A transparent backdrop keeps mouse hit testing alive over the gaps between cells.
            drawingContext.DrawRectangle(
                Brushes.Transparent,
                null,
                new Rect(0, 0, RenderSize.Width, RenderSize.Height));

            var cellBox = Math.Max(1, CellBox);
            var cell = Math.Max(1, cellBox - CellMargin * 2);
            var top = ShowMonthLabels ? MonthLabelHeight : 0;

            var accent = TryFindResource("PlayAch.Brush.Accent") as Brush ?? Brushes.SteelBlue;
            var empty = TryFindResource("PlayAch.Brush.Overlay.Tint.08") as Brush ??
                new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            var text = TryFindResource("PlayAch.Brush.Text") as Brush ?? Brushes.Gray;
            var intensityBrushes = BuildIntensityBrushes(accent);

            var fontFamily = TextElement.GetFontFamily(this) ?? new FontFamily("Segoe UI");
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            for (var weekIndex = 0; weekIndex < weeks.Count; weekIndex++)
            {
                var week = weeks[weekIndex];
                if (week == null)
                {
                    continue;
                }

                var x = weekIndex * cellBox;
                if (ShowMonthLabels && !string.IsNullOrEmpty(week.MonthLabel))
                {
                    var label = new FormattedText(
                        week.MonthLabel,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        MonthLabelFontSize,
                        text,
                        pixelsPerDip);
                    drawingContext.PushOpacity(0.7);
                    drawingContext.DrawText(label, new Point(x + CellMargin, 1));
                    drawingContext.Pop();
                }

                var days = week.Days;
                if (days == null)
                {
                    continue;
                }

                for (var dayIndex = 0; dayIndex < days.Count && dayIndex < 7; dayIndex++)
                {
                    var day = days[dayIndex];
                    if (day == null || day.Intensity < 0)
                    {
                        continue;
                    }

                    var rect = new Rect(
                        x + CellMargin,
                        top + dayIndex * cellBox + CellMargin,
                        cell,
                        cell);
                    var brush = day.Intensity == 0
                        ? empty
                        : intensityBrushes[Math.Min(day.Intensity, 4) - 1];
                    drawingContext.DrawRoundedRectangle(brush, null, rect, CellCornerRadius, CellCornerRadius);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ToolTip = HitTestTooltip(e.GetPosition(this));
        }

        private string HitTestTooltip(Point position)
        {
            var weeks = Weeks;
            if (weeks == null || weeks.Count == 0)
            {
                return null;
            }

            var cellBox = Math.Max(1, CellBox);
            var top = ShowMonthLabels ? MonthLabelHeight : 0;
            var weekIndex = (int)(position.X / cellBox);
            var dayIndex = (int)((position.Y - top) / cellBox);
            if (weekIndex < 0 || weekIndex >= weeks.Count || dayIndex < 0 || dayIndex >= 7)
            {
                return null;
            }

            var days = weeks[weekIndex]?.Days;
            return days != null && dayIndex < days.Count ? days[dayIndex]?.Tooltip : null;
        }

        private static Brush[] BuildIntensityBrushes(Brush accent)
        {
            var brushes = new Brush[IntensityOpacity.Length];
            var solid = accent as SolidColorBrush;
            for (var index = 0; index < IntensityOpacity.Length; index++)
            {
                Brush clone;
                if (solid != null)
                {
                    clone = new SolidColorBrush(solid.Color) { Opacity = IntensityOpacity[index] };
                }
                else
                {
                    clone = accent.CloneCurrentValue();
                    clone.Opacity = IntensityOpacity[index];
                }

                if (clone.CanFreeze)
                {
                    clone.Freeze();
                }

                brushes[index] = clone;
            }

            return brushes;
        }
    }
}

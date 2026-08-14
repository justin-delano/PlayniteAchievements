using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PlayniteAchievements.ViewModels.Showcase.Widgets;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Cell and label sizes shared by the heatmap and its weekday gutter, derived from the
    /// element's inherited font size so the calendar scales with the plugin's text sizing
    /// instead of stretching to fill the widget.
    /// </summary>
    internal readonly struct ActivityCalendarMetrics
    {
        public ActivityCalendarMetrics(double fontSize)
        {
            var baseline = Math.Max(8, double.IsNaN(fontSize) ? 12 : fontSize);
            CellBox = Math.Ceiling(baseline) + 2;
            LabelFontSize = Math.Max(7, Math.Round(baseline * 0.75));
            MonthBandHeight = Math.Ceiling(LabelFontSize) + 5;
        }

        /// <summary>Cell box size including margins; also the weekday label row height.</summary>
        public double CellBox { get; }

        public double LabelFontSize { get; }

        public double MonthBandHeight { get; }

        public static ActivityCalendarMetrics For(FrameworkElement element)
        {
            return new ActivityCalendarMetrics(TextElement.GetFontSize(element));
        }
    }

    /// <summary>
    /// Draws the activity heatmap as a single visual instead of one element per day cell.
    /// A year (or more) of cells as retained Border elements makes page switches and
    /// scrolling drag; OnRender keeps the whole calendar one drawing and serves tooltips
    /// from mouse hit tests.
    /// </summary>
    public sealed class ActivityCalendarHeatmap : FrameworkElement
    {
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

        public ActivityCalendarHeatmap()
        {
            Loaded += (_, __) => PlayniteAchievements.Models.Achievements.RarityAppearanceHelper
                .AppearanceChanged += OnAppearanceChanged;
            Unloaded += (_, __) => PlayniteAchievements.Models.Achievements.RarityAppearanceHelper
                .AppearanceChanged -= OnAppearanceChanged;

            // The tooltip service only arms its hover timer when the pointer enters an element
            // that ALREADY has a tooltip; a value first assigned during MouseMove never opens
            // until something (like scrolling) re-enters the element. Arm with an empty value
            // and suppress it when the pointer is not over a day cell.
            ToolTip = string.Empty;
            ToolTipOpening += OnToolTipOpening;
        }

        private void OnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (!(ToolTip is string tooltip) || string.IsNullOrEmpty(tooltip))
            {
                e.Handled = true;
            }
        }

        private void OnAppearanceChanged(object sender, EventArgs e)
        {
            _intensityBrushes = null;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var metrics = ActivityCalendarMetrics.For(this);
            var width = (Weeks?.Count ?? 0) * metrics.CellBox;
            var height = (ShowMonthLabels ? metrics.MonthBandHeight : 0) + 7 * metrics.CellBox;
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

            var metrics = ActivityCalendarMetrics.For(this);
            var cellBox = metrics.CellBox;
            var cell = Math.Max(1, cellBox - CellMargin * 2);
            var top = ShowMonthLabels ? metrics.MonthBandHeight : 0;

            EnsureRenderResources();
            var empty = _emptyBrush;
            var text = _textBrush;
            var intensityBrushes = _intensityBrushes;
            var typeface = _typeface;
            var pixelsPerDip = _pixelsPerDip;

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
                        metrics.LabelFontSize,
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

        private Brush _emptyBrush;
        private Brush _textBrush;
        private Brush[] _intensityBrushes;
        private Typeface _typeface;
        private double _pixelsPerDip;

        /// <summary>
        /// Resolves the theme brushes, the derived intensity ramp, and the typeface once instead
        /// of on every render pass. Dropped when the appearance changes so a recolor is picked up.
        /// </summary>
        private void EnsureRenderResources()
        {
            if (_intensityBrushes != null)
            {
                return;
            }

            var accent = TryFindResource("PlayAch.Brush.Accent") as Brush ?? Brushes.SteelBlue;
            _emptyBrush = TryFindResource("PlayAch.Brush.Overlay.Tint.08") as Brush ??
                new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            _textBrush = TryFindResource("PlayAch.Brush.Text") as Brush ?? Brushes.Gray;
            _intensityBrushes = BuildIntensityBrushes(accent);

            var fontFamily = TextElement.GetFontFamily(this) ?? new FontFamily("Segoe UI");
            _typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _intensityBrushes = null;
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // Never assign null: the empty string keeps the tooltip service armed (see ctor).
            ToolTip = HitTestTooltip(e.GetPosition(this)) ?? string.Empty;
        }

        private string HitTestTooltip(Point position)
        {
            var weeks = Weeks;
            if (weeks == null || weeks.Count == 0)
            {
                return null;
            }

            var metrics = ActivityCalendarMetrics.For(this);
            var cellBox = metrics.CellBox;
            var top = ShowMonthLabels ? metrics.MonthBandHeight : 0;
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

    /// <summary>
    /// Draws the weekday labels beside the heatmap using the same font-derived metrics, so
    /// the label rows line up with the cell rows. A separate element lets the labels sit
    /// outside the horizontal scroll area and stay visible while the calendar scrolls.
    /// </summary>
    public sealed class ActivityCalendarDayGutter : FrameworkElement
    {
        private const double RightPadding = 4;

        public static readonly DependencyProperty LabelsProperty =
            DependencyProperty.Register(
                nameof(Labels),
                typeof(IReadOnlyList<string>),
                typeof(ActivityCalendarDayGutter),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<string> Labels
        {
            get => (IReadOnlyList<string>)GetValue(LabelsProperty);
            set => SetValue(LabelsProperty, value);
        }

        /// <summary>Reserves the heatmap's month-label band so the first rows align.</summary>
        public static readonly DependencyProperty ShowMonthBandProperty =
            DependencyProperty.Register(
                nameof(ShowMonthBand),
                typeof(bool),
                typeof(ActivityCalendarDayGutter),
                new FrameworkPropertyMetadata(
                    true,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowMonthBand
        {
            get => (bool)GetValue(ShowMonthBandProperty);
            set => SetValue(ShowMonthBandProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var labels = Labels;
            if (labels == null || labels.Count == 0)
            {
                return new Size(0, 0);
            }

            var metrics = ActivityCalendarMetrics.For(this);
            var width = 0d;
            foreach (var text in BuildLabelTexts(metrics))
            {
                width = Math.Max(width, text.Width);
            }

            return new Size(
                width + RightPadding,
                (ShowMonthBand ? metrics.MonthBandHeight : 0) + 7 * metrics.CellBox);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var labels = Labels;
            if (labels == null || labels.Count == 0)
            {
                return;
            }

            var metrics = ActivityCalendarMetrics.For(this);
            var top = ShowMonthBand ? metrics.MonthBandHeight : 0;
            var index = 0;
            drawingContext.PushOpacity(0.7);
            foreach (var text in BuildLabelTexts(metrics))
            {
                var y = top + index * metrics.CellBox + (metrics.CellBox - text.Height) / 2;
                drawingContext.DrawText(text, new Point(0, y));
                index++;
            }

            drawingContext.Pop();
        }

        private IEnumerable<FormattedText> BuildLabelTexts(ActivityCalendarMetrics metrics)
        {
            var labels = Labels;
            var textBrush = TryFindResource("PlayAch.Brush.Text") as Brush ?? Brushes.Gray;
            var fontFamily = TextElement.GetFontFamily(this) ?? new FontFamily("Segoe UI");
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (var index = 0; index < labels.Count && index < 7; index++)
            {
                yield return new FormattedText(
                    labels[index] ?? string.Empty,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    metrics.LabelFontSize,
                    textBrush,
                    pixelsPerDip);
            }
        }
    }
}

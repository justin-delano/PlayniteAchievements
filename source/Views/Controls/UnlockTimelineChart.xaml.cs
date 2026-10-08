using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using Separator = LiveCharts.Wpf.Separator;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// The unlocks-over-time column chart shared by the overview, the single-game window, the
    /// Showcase Timeline widget, and the Modern theme bar chart. Hosts bind the
    /// <see cref="ViewModels.TimelineViewModel"/> outputs; the control owns the LiveCharts axis
    /// configuration (explicit X and Y ceilings, integer Y step, boundary-only X labels), keeps
    /// LiveCharts redrawing when labels change in place, reports how many axis labels fit its
    /// width, and shows a caption when the window holds no unlocks.
    /// </summary>
    public partial class UnlockTimelineChart : UserControl
    {
        /// <summary>Horizontal room one short date label needs, including breathing space.</summary>
        private const double PixelsPerTick = 56;
        private const int MinTicks = 3;
        private const int MaxTicks = 10;

        /// <summary>
        /// Narrowest column unit worth drawing: the column style pads 1 px, so anything under 3 px
        /// per bar is a hairline, and below the padding LiveCharts draws nothing at all.
        /// </summary>
        private const double PixelsPerBar = 3;
        private const double AxisLabelGutter = 44;
        private const int MinBars = 24;

        public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
            nameof(Series), typeof(SeriesCollection), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
            nameof(Labels), typeof(IList<string>), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        /// <summary>
        /// Bumped by the view model once a pass is fully applied. The control then forces one
        /// synchronous LiveCharts redraw: the library otherwise only starts a coalescing timer on
        /// each value or label edit, and it never re-reads in-place label edits at all.
        /// </summary>
        public static readonly DependencyProperty RenderRevisionProperty = DependencyProperty.Register(
            nameof(RenderRevision), typeof(int), typeof(UnlockTimelineChart), new PropertyMetadata(0, OnRenderRevisionChanged));

        public static readonly DependencyProperty TooltipLabelsProperty = DependencyProperty.Register(
            nameof(TooltipLabels), typeof(IList<string>), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty XAxisMaxProperty = DependencyProperty.Register(
            nameof(XAxisMax), typeof(double), typeof(UnlockTimelineChart), new PropertyMetadata(1d));

        public static readonly DependencyProperty YAxisMaxProperty = DependencyProperty.Register(
            nameof(YAxisMax), typeof(double), typeof(UnlockTimelineChart), new PropertyMetadata(1d));

        public static readonly DependencyProperty YAxisStepProperty = DependencyProperty.Register(
            nameof(YAxisStep), typeof(double), typeof(UnlockTimelineChart), new PropertyMetadata(1d));

        public static readonly DependencyProperty YLabelFormatterProperty = DependencyProperty.Register(
            nameof(YLabelFormatter), typeof(Func<double, string>), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
            nameof(IsEmpty), typeof(bool), typeof(UnlockTimelineChart), new PropertyMetadata(false));

        public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
            nameof(EmptyText), typeof(string), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty LabelsRotationProperty = DependencyProperty.Register(
            nameof(LabelsRotation), typeof(double), typeof(UnlockTimelineChart), new PropertyMetadata(0d));

        /// <summary>
        /// Axis labels that fit the current width. The control sets it from its size; hosts bind
        /// it OneWayToSource into the view model, which re-plans its ticks.
        /// </summary>
        public static readonly DependencyProperty MaxTickCountProperty = DependencyProperty.Register(
            nameof(MaxTickCount), typeof(int), typeof(UnlockTimelineChart), new PropertyMetadata(8));

        /// <summary>
        /// Bars that fit the current width. Set from the size like <see cref="MaxTickCount"/>; hosts
        /// bind it OneWayToSource so the view model escalates the bar unit instead of overflowing.
        /// </summary>
        public static readonly DependencyProperty MaxBarCountProperty = DependencyProperty.Register(
            nameof(MaxBarCount), typeof(int), typeof(UnlockTimelineChart), new PropertyMetadata(400));

        public static readonly DependencyProperty AxisForegroundProperty = DependencyProperty.Register(
            nameof(AxisForeground), typeof(Brush), typeof(UnlockTimelineChart), new PropertyMetadata(null, OnAxisForegroundChanged));

        /// <summary>
        /// The axis lines' brush: the axis label color at half strength. The label color always
        /// reads against the chart's surface, where the border brush can fall back to a background
        /// color and vanish.
        /// </summary>
        private static readonly DependencyProperty AxisLineBrushProperty = DependencyProperty.Register(
            nameof(AxisLineBrush), typeof(Brush), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        private const double AxisLineOpacity = 0.5;

        private Brush AxisLineBrush => (Brush)GetValue(AxisLineBrushProperty);

        private static void OnAxisForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            Brush line = null;
            if (e.NewValue is Brush brush)
            {
                line = brush.CloneCurrentValue();
                line.Opacity = brush.Opacity * AxisLineOpacity;
                if (line.CanFreeze)
                {
                    line.Freeze();
                }
            }

            d.SetValue(AxisLineBrushProperty, line);
        }

        public static readonly DependencyProperty SeparatorStrokeProperty = DependencyProperty.Register(
            nameof(SeparatorStroke), typeof(Brush), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty TooltipSurfaceBrushProperty = DependencyProperty.Register(
            nameof(TooltipSurfaceBrush), typeof(Brush), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty TooltipOutlineBrushProperty = DependencyProperty.Register(
            nameof(TooltipOutlineBrush), typeof(Brush), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty TooltipForegroundProperty = DependencyProperty.Register(
            nameof(TooltipForeground), typeof(Brush), typeof(UnlockTimelineChart), new PropertyMetadata(null));

        /// <summary>The column to shade as the active selection; -1 shades none.</summary>
        public static readonly DependencyProperty HighlightedIndexProperty = DependencyProperty.Register(
            nameof(HighlightedIndex), typeof(int), typeof(UnlockTimelineChart), new PropertyMetadata(-1, OnHighlightedIndexChanged));

        /// <summary>A click on a column, carrying its position along the axis.</summary>
        public static readonly RoutedEvent ColumnClickedEvent = EventManager.RegisterRoutedEvent(
            "ColumnClicked",
            RoutingStrategy.Bubble,
            typeof(ChartClickEventHandler),
            typeof(UnlockTimelineChart));

        private readonly Axis _axisX;
        private readonly Axis _axisY;
        private readonly CartesianChartTooltip _tooltip;

        public UnlockTimelineChart()
        {
            InitializeComponent();

            // Resource references are local values, so a brush set on the usage wins (the Modern
            // theme host passes Playnite's popup keys).
            SetResourceReference(AxisForegroundProperty, "PlayAch.Brush.Text");
            SetResourceReference(SeparatorStrokeProperty, "PlayAch.Brush.Border");
            SetResourceReference(TooltipSurfaceBrushProperty, "PlayAch.Brush.PopupSurface");
            SetResourceReference(TooltipOutlineBrushProperty, "PlayAch.Brush.PopupBorder");
            SetResourceReference(TooltipForegroundProperty, "PlayAch.Brush.Text");
            SetResourceReference(EmptyTextProperty, "LOCPlayAch_Timeline_NoUnlocksInPeriod");

            // Step 1 keeps one label slot per bar; the view model blanks the slots between ticks,
            // because a larger Step would skip slots by count rather than by calendar boundary.
            _axisX = new Axis
            {
                ShowLabels = true,
                MinValue = 0,
                Separator = new Separator { Step = 1, IsEnabled = false }
            };
            _axisX.SetBinding(Axis.LabelsProperty, Bind(nameof(Labels)));
            _axisX.SetBinding(Axis.MaxValueProperty, Bind(nameof(XAxisMax)));
            _axisX.SetBinding(Axis.LabelsRotationProperty, Bind(nameof(LabelsRotation)));
            _axisX.SetBinding(Axis.ForegroundProperty, Bind(nameof(AxisForeground)));
            _axisX.SetResourceReference(Axis.FontSizeProperty, "PlayAch.FontSize.Caption");

            // Explicit ceiling and step: without them an all-zero series collapses the axis to
            // 0..0.5 and a later update keeps the ceiling LiveCharts rounded on the previous pass.
            var ySeparator = new Separator { Opacity = 0.2 };
            ySeparator.SetBinding(Separator.StrokeProperty, Bind(nameof(SeparatorStroke)));
            ySeparator.SetBinding(Separator.StepProperty, Bind(nameof(YAxisStep)));
            _axisY = new Axis
            {
                MinValue = 0,
                Separator = ySeparator
            };
            _axisY.SetBinding(Axis.MaxValueProperty, Bind(nameof(YAxisMax)));
            _axisY.SetBinding(Axis.LabelFormatterProperty, Bind(nameof(YLabelFormatter)));
            _axisY.SetBinding(Axis.ForegroundProperty, Bind(nameof(AxisForeground)));
            _axisY.SetResourceReference(Axis.FontSizeProperty, "PlayAch.FontSize.Caption");

            // Axis lines: the baseline under the bars and the line up the left edge, in the
            // axis label color at half strength (AxisLineBrush). LiveCharts has no
            // axis line, so each is an empty section on the plot's edge; an empty section's
            // rectangle is as thick as StrokeThickness, and with no stroke the fill draws it.
            // The X axis shifts sections half a column to centre them on bars, so the left
            // edge is half a column before the first.
            _axisY.Sections.Add(CreateAxisLine(0));
            _axisX.Sections.Add(CreateAxisLine(-0.5));

            Chart.AxisX.Add(_axisX);
            Chart.AxisY.Add(_axisY);

            _tooltip = new CartesianChartTooltip { StackedRows = true };
            _tooltip.SetBinding(CartesianChartTooltip.SurfaceBrushProperty, Bind(nameof(TooltipSurfaceBrush)));
            _tooltip.SetBinding(CartesianChartTooltip.OutlineBrushProperty, Bind(nameof(TooltipOutlineBrush)));
            _tooltip.SetBinding(ForegroundProperty, Bind(nameof(TooltipForeground)));
            _tooltip.SetBinding(CartesianChartTooltip.HeaderLabelsProperty, Bind(nameof(TooltipLabels)));
            Chart.DataTooltip = _tooltip;

            // A selected column stands out by the others fading, styled on the drawn bars
            // themselves so plain and stacked (platform split) columns match and line up exactly.
            // Every draw can create new bars, so the styling runs again after each one.
            Chart.UpdaterTick += sender => ApplyHighlight();
            Chart.DataClick += OnChartDataClick;

            SizeChanged += OnSizeChanged;
        }

        /// <summary>How strongly the unselected columns fade while one is selected.</summary>
        private const double UnselectedColumnOpacity = 0.18;

        /// <summary>The outline drawn round the selected column, in the text color for contrast with the accent fill.</summary>
        private const double SelectedColumnStroke = 1.5;

        public int HighlightedIndex
        {
            get => (int)GetValue(HighlightedIndexProperty);
            set => SetValue(HighlightedIndexProperty, value);
        }

        private static void OnHighlightedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((UnlockTimelineChart)d).ApplyHighlight();
        }

        // Each stacked segment of the selected column gets the outline, so the platform split
        // reads as one outlined column with its segment edges inside.
        private void ApplyHighlight()
        {
            var index = HighlightedIndex;
            var outline = index < 0 ? null : TryFindResource("PlayAch.Brush.Text") as Brush;
            foreach (var series in Series ?? new SeriesCollection())
            {
                if (series?.Values == null)
                {
                    continue;
                }

                foreach (var point in series.Values.GetPoints(series))
                {
                    if (!(BarOf(point?.View) is System.Windows.Shapes.Rectangle bar))
                    {
                        continue;
                    }

                    var selected = (int)Math.Round(point.X) == index;
                    bar.Opacity = index < 0 || selected ? 1 : UnselectedColumnOpacity;
                    bar.Stroke = selected ? outline : null;
                    bar.StrokeThickness = selected ? SelectedColumnStroke : 0;
                }
            }
        }

        private static System.Reflection.PropertyInfo _barProperty;

        // LiveCharts 0.9.7 draws each column through its internal ColumnPointView, whose public
        // Rectangle property is the drawn bar; it is reached by reflection since the type is internal.
        private static object BarOf(object pointView)
        {
            if (pointView == null)
            {
                return null;
            }

            var property = _barProperty;
            if (property == null || property.DeclaringType != pointView.GetType())
            {
                property = pointView.GetType().GetProperty("Rectangle");
                if (property == null)
                {
                    return null;
                }

                _barProperty = property;
            }

            return property.GetValue(pointView);
        }

        private void OnChartDataClick(object sender, ChartPoint point)
        {
            if (point == null || double.IsNaN(point.X))
            {
                return;
            }

            RaiseEvent(new ChartClickEventArgs(ColumnClickedEvent, this) { Index = (int)Math.Round(point.X) });
        }

        public SeriesCollection Series
        {
            get => (SeriesCollection)GetValue(SeriesProperty);
            set => SetValue(SeriesProperty, value);
        }

        public IList<string> Labels
        {
            get => (IList<string>)GetValue(LabelsProperty);
            set => SetValue(LabelsProperty, value);
        }

        public int RenderRevision
        {
            get => (int)GetValue(RenderRevisionProperty);
            set => SetValue(RenderRevisionProperty, value);
        }

        public IList<string> TooltipLabels
        {
            get => (IList<string>)GetValue(TooltipLabelsProperty);
            set => SetValue(TooltipLabelsProperty, value);
        }

        public double XAxisMax
        {
            get => (double)GetValue(XAxisMaxProperty);
            set => SetValue(XAxisMaxProperty, value);
        }

        public double YAxisMax
        {
            get => (double)GetValue(YAxisMaxProperty);
            set => SetValue(YAxisMaxProperty, value);
        }

        public double YAxisStep
        {
            get => (double)GetValue(YAxisStepProperty);
            set => SetValue(YAxisStepProperty, value);
        }

        public Func<double, string> YLabelFormatter
        {
            get => (Func<double, string>)GetValue(YLabelFormatterProperty);
            set => SetValue(YLabelFormatterProperty, value);
        }

        public bool IsEmpty
        {
            get => (bool)GetValue(IsEmptyProperty);
            set => SetValue(IsEmptyProperty, value);
        }

        public string EmptyText
        {
            get => (string)GetValue(EmptyTextProperty);
            set => SetValue(EmptyTextProperty, value);
        }

        public double LabelsRotation
        {
            get => (double)GetValue(LabelsRotationProperty);
            set => SetValue(LabelsRotationProperty, value);
        }

        public int MaxTickCount
        {
            get => (int)GetValue(MaxTickCountProperty);
            set => SetValue(MaxTickCountProperty, value);
        }

        public int MaxBarCount
        {
            get => (int)GetValue(MaxBarCountProperty);
            set => SetValue(MaxBarCountProperty, value);
        }

        public Brush AxisForeground
        {
            get => (Brush)GetValue(AxisForegroundProperty);
            set => SetValue(AxisForegroundProperty, value);
        }

        public Brush SeparatorStroke
        {
            get => (Brush)GetValue(SeparatorStrokeProperty);
            set => SetValue(SeparatorStrokeProperty, value);
        }

        public Brush TooltipSurfaceBrush
        {
            get => (Brush)GetValue(TooltipSurfaceBrushProperty);
            set => SetValue(TooltipSurfaceBrushProperty, value);
        }

        public Brush TooltipOutlineBrush
        {
            get => (Brush)GetValue(TooltipOutlineBrushProperty);
            set => SetValue(TooltipOutlineBrushProperty, value);
        }

        public Brush TooltipForeground
        {
            get => (Brush)GetValue(TooltipForegroundProperty);
            set => SetValue(TooltipForegroundProperty, value);
        }

        private Binding Bind(string path) => new Binding(path) { Source = this };

        private AxisSection CreateAxisLine(double value)
        {
            var line = new AxisSection
            {
                Value = value,
                SectionWidth = 0,
                StrokeThickness = 1,
                Stroke = null,
                IsHitTestVisible = false
            };
            line.SetBinding(AxisSection.FillProperty, new Binding { Path = new PropertyPath(AxisLineBrushProperty), Source = this });
            return line;
        }

        private static void OnRenderRevisionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UnlockTimelineChart chart && chart.IsLoaded)
            {
                // force: true runs the updater tick now; the value edits before this only armed
                // its timer, so without it the new bars appear a beat after the click.
                chart.Chart.Update(false, true);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || double.IsNaN(e.NewSize.Width) || e.NewSize.Width <= 0)
            {
                return;
            }

            var ticks = (int)Math.Floor(e.NewSize.Width / PixelsPerTick);
            MaxTickCount = Math.Max(MinTicks, Math.Min(MaxTicks, ticks));

            var bars = (int)Math.Floor((e.NewSize.Width - AxisLabelGutter) / PixelsPerBar);
            MaxBarCount = Math.Max(MinBars, bars);
        }
    }
}

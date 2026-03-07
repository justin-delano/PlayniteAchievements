using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiveCharts;
using LiveCharts.Wpf;
using LiveCharts.Wpf.Points;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// A pie chart with icons positioned at the midpoint of each slice on a circle around the chart.
    /// </summary>
    public partial class PieChartWithRadialIcons : UserControl
    {
        private static readonly double IconSize = 18.0;
        private const double SliceHighlightOffset = 5.0;
        private const double LiveChartsRotationOffset = 45.0;
        private static readonly Duration SliceAnimationDuration = new Duration(TimeSpan.FromMilliseconds(150));
        private static readonly PropertyInfo PiePointViewSliceProperty =
            typeof(PieSlice).Assembly
                .GetType("LiveCharts.Wpf.Points.PiePointView")?
                .GetProperty("Slice", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly IEasingFunction SliceAnimationEasing = new QuadraticEase
        {
            EasingMode = EasingMode.EaseOut
        };
        private List<PieSeries> subscribedSeries = new List<PieSeries>();
        private bool calculationScheduled;
        private string hoveredSliceLabel;

        /// <summary>
        /// Event raised when a pie slice is clicked.
        /// Provides the label of the clicked slice.
        /// </summary>
        public event EventHandler<string> SliceClick;

        public static readonly DependencyProperty PieSeriesProperty =
            DependencyProperty.Register(nameof(PieSeries), typeof(SeriesCollection), typeof(PieChartWithRadialIcons),
                new PropertyMetadata(null, OnPieSeriesChanged));

        public static readonly DependencyProperty LegendItemsProperty =
            DependencyProperty.Register(nameof(LegendItems), typeof(ObservableCollection<LegendItem>), typeof(PieChartWithRadialIcons),
                new PropertyMetadata(null, OnLegendItemsChanged));

        public static readonly DependencyProperty IconOffsetProperty =
            DependencyProperty.Register(nameof(IconOffset), typeof(double), typeof(PieChartWithRadialIcons),
                new PropertyMetadata(12.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty HighlightedLabelsProperty =
            DependencyProperty.Register(nameof(HighlightedLabels), typeof(ObservableCollection<string>), typeof(PieChartWithRadialIcons),
                new PropertyMetadata(null, OnHighlightedLabelsChanged));

        public SeriesCollection PieSeries
        {
            get => (SeriesCollection)GetValue(PieSeriesProperty);
            set => SetValue(PieSeriesProperty, value);
        }

        public ObservableCollection<LegendItem> LegendItems
        {
            get => (ObservableCollection<LegendItem>)GetValue(LegendItemsProperty);
            set => SetValue(LegendItemsProperty, value);
        }

        public double IconOffset
        {
            get => (double)GetValue(IconOffsetProperty);
            set => SetValue(IconOffsetProperty, value);
        }

        public ObservableCollection<string> HighlightedLabels
        {
            get => (ObservableCollection<string>)GetValue(HighlightedLabelsProperty);
            set => SetValue(HighlightedLabelsProperty, value);
        }

        public ObservableCollection<PieIconPosition> IconPositions { get; } = new ObservableCollection<PieIconPosition>();

        public PieChartWithRadialIcons()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
        }

        /// <summary>
        /// Schedule position calculation to run after LiveCharts completes rendering.
        /// Multiple calls are deduplicated to a single calculation.
        /// </summary>
        private void ScheduleCalculation()
        {
            if (calculationScheduled)
            {
                return;
            }
            calculationScheduled = true;
            // Use ContextIdle to ensure we run after LiveCharts render pass completes
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                calculationScheduled = false;
                CalculatePositions();
            }));
        }

        private static void OnPieSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PieChartWithRadialIcons)d;
            control.UnsubscribeFromSeries();
            if (e.OldValue is SeriesCollection oldSeries)
            {
                oldSeries.CollectionChanged -= control.OnSeriesCollectionChanged;
            }
            if (e.NewValue is SeriesCollection newSeries)
            {
                newSeries.CollectionChanged += control.OnSeriesCollectionChanged;
                control.SubscribeToSeries(newSeries);
            }
            control.ScheduleCalculation();
        }

        private void UnsubscribeFromSeries()
        {
            foreach (var series in subscribedSeries)
            {
                if (series is INotifyPropertyChanged notify)
                {
                    notify.PropertyChanged -= OnSeriesPropertyChanged;
                }
                if (series.Values is INotifyCollectionChanged chartValues)
                {
                    chartValues.CollectionChanged -= OnChartValuesChanged;
                }
            }
            subscribedSeries.Clear();
        }

        private void SubscribeToSeries(SeriesCollection collection)
        {
            foreach (var series in collection.OfType<PieSeries>())
            {
                if (series is INotifyPropertyChanged notify)
                {
                    notify.PropertyChanged += OnSeriesPropertyChanged;
                }
                if (series.Values is INotifyCollectionChanged chartValues)
                {
                    chartValues.CollectionChanged += OnChartValuesChanged;
                }
                subscribedSeries.Add(series);
            }
        }

        private void OnSeriesPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Values")
            {
                ScheduleCalculation();
            }
        }

        private void OnChartValuesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleCalculation();
        }

        private static void OnLegendItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PieChartWithRadialIcons)d;
            if (e.OldValue is ObservableCollection<LegendItem> oldItems)
            {
                oldItems.CollectionChanged -= control.OnLegendItemsCollectionChanged;
            }
            if (e.NewValue is ObservableCollection<LegendItem> newItems)
            {
                newItems.CollectionChanged += control.OnLegendItemsCollectionChanged;
            }
            control.ScheduleCalculation();
        }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PieChartWithRadialIcons)d).ScheduleCalculation();
        }

        private static void OnHighlightedLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PieChartWithRadialIcons)d;
            if (e.OldValue is ObservableCollection<string> oldLabels)
            {
                oldLabels.CollectionChanged -= control.OnHighlightedLabelsCollectionChanged;
            }
            if (e.NewValue is ObservableCollection<string> newLabels)
            {
                newLabels.CollectionChanged += control.OnHighlightedLabelsCollectionChanged;
            }
            control.ApplySliceTransforms();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleCalculation();
        }

        private void OnSeriesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UnsubscribeFromSeries();
            if (PieSeries != null)
            {
                SubscribeToSeries(PieSeries);
            }
            ScheduleCalculation();
        }

        private void OnLegendItemsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ScheduleCalculation();
        }

        private void OnHighlightedLabelsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ApplySliceTransforms();
        }

        private void CalculatePositions()
        {
            if (PieSeries == null || PieSeries.Count == 0 || LegendItems == null || LegendItems.Count == 0)
            {
                IconPositions.Clear();
                ClearSliceTransforms();
                return;
            }

            var seriesList = PieSeries.OfType<PieSeries>().ToList();
            var chartValues = seriesList
                .Select(s =>
                {
                    var values = s.Values as ChartValues<PieSliceChartData>;
                    return values?.Count > 0 ? values[0].ChartValue : 0;
                })
                .ToList();

            if (chartValues.Count == 0 || chartValues.All(v => v == 0))
            {
                IconPositions.Clear();
                ClearSliceTransforms();
                return;
            }

            double totalValue = chartValues.Sum();
            var margin = Chart?.Margin ?? new Thickness(0);
            double availableWidth = Math.Max(0, ActualWidth - margin.Left - margin.Right);
            double availableHeight = Math.Max(0, ActualHeight - margin.Top - margin.Bottom);
            double controlSize = Math.Min(availableWidth, availableHeight);
            if (controlSize <= 0)
            {
                return;
            }

            double pieRadius = controlSize / 2.0;
            double iconRadius = pieRadius + IconOffset;
            double centerX = margin.Left + (availableWidth / 2.0);
            double centerY = margin.Top + (availableHeight / 2.0);

            // Build highlight set for computing offsets in single pass
            var highlighted = new HashSet<string>(
                (HighlightedLabels ?? Enumerable.Empty<string>())
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => label.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(hoveredSliceLabel))
            {
                highlighted.Add(hoveredSliceLabel);
            }

            double currentAngle = LiveChartsRotationOffset;
            var positions = new List<PieIconPosition>();
            var sliceTransformData = new List<(PieSlice Slice, double OffsetX, double OffsetY)>();

            for (int i = 0; i < chartValues.Count && i < LegendItems.Count; i++)
            {
                double sliceArc = (chartValues[i] / totalValue) * 360.0;
                double midpointAngle = currentAngle + (sliceArc / 2.0);
                double angleRadians = midpointAngle * Math.PI / 180.0;

                var legend = LegendItems[i];
                var series = seriesList[i];
                var isHighlighted = !string.IsNullOrWhiteSpace(series?.Title) && highlighted.Contains(series.Title);
                var highlightOffset = isHighlighted ? SliceHighlightOffset : 0.0;
                var offsetX = highlightOffset * Math.Sin(angleRadians);
                var offsetY = -highlightOffset * Math.Cos(angleRadians);

                // Collect slice transform data for batch application
                var slice = GetPieSlice(series);
                if (slice != null)
                {
                    sliceTransformData.Add((slice, offsetX, offsetY));
                }

                // Only show icon if count > 0
                if (legend.Count > 0)
                {
                    // Clockwise from top: x = sin(θ), y = -cos(θ)
                    double x = centerX + iconRadius * Math.Sin(angleRadians) - IconSize / 2.0;
                    double y = centerY - iconRadius * Math.Cos(angleRadians) - IconSize / 2.0;

                    positions.Add(new PieIconPosition
                    {
                        Label = legend.Label,
                        IconKey = legend.IconKey,
                        ColorHex = legend.ColorHex,
                        Count = legend.Count,
                        X = x,
                        Y = y,
                        OffsetX = offsetX,
                        OffsetY = offsetY
                    });
                }

                currentAngle += sliceArc;
            }

            // Apply all updates in sequence (but computed in single pass above)
            SynchronizePositions(positions);
            ApplySliceTransformsBatch(sliceTransformData);
        }

        private void ApplySliceTransformsBatch(List<(PieSlice Slice, double OffsetX, double OffsetY)> transformData)
        {
            foreach (var (slice, offsetX, offsetY) in transformData)
            {
                SetSliceTransform(slice, offsetX, offsetY);
            }
        }

        private void SynchronizePositions(IReadOnlyList<PieIconPosition> positions)
        {
            // Remove extra positions from the end
            while (IconPositions.Count > positions.Count)
            {
                IconPositions.RemoveAt(IconPositions.Count - 1);
            }

            // Update existing or add new positions
            for (int i = 0; i < positions.Count; i++)
            {
                var newPos = positions[i];
                if (i < IconPositions.Count)
                {
                    // Update existing position in-place
                    var existing = IconPositions[i];
                    existing.Label = newPos.Label;
                    existing.IconKey = newPos.IconKey;
                    existing.ColorHex = newPos.ColorHex;
                    existing.Count = newPos.Count;
                    existing.X = newPos.X;
                    existing.Y = newPos.Y;
                }
                else
                {
                    // Add new position
                    IconPositions.Add(newPos);
                }
            }
        }

        private void OnPieChartDataClick(object sender, ChartPoint chartPoint)
        {
            // Only respond to left clicks on actual data points
            if (chartPoint == null || Mouse.LeftButton != MouseButtonState.Pressed) return;

            // Get the label from the PieSeries that was clicked
            if (chartPoint.SeriesView is PieSeries series && !string.IsNullOrEmpty(series.Title))
            {
                SliceClick?.Invoke(this, series.Title);
            }
        }

        private void OnPieChartDataHover(object sender, ChartPoint chartPoint)
        {
            hoveredSliceLabel = (chartPoint?.SeriesView as PieSeries)?.Title;
            ApplySliceTransforms();
        }

        private void OnPieChartMouseLeave(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(hoveredSliceLabel))
            {
                return;
            }

            hoveredSliceLabel = null;
            ApplySliceTransforms();
        }

        private void ApplySliceTransforms(IReadOnlyList<double> chartValues = null)
        {
            var seriesList = PieSeries?.OfType<PieSeries>().ToList();
            if (seriesList == null || seriesList.Count == 0)
            {
                return;
            }

            chartValues = chartValues ?? seriesList
                .Select(series =>
                {
                    var values = series.Values as ChartValues<PieSliceChartData>;
                    return values?.Count > 0 ? values[0].ChartValue : 0;
                })
                .ToList();

            if (chartValues.Count == 0 || chartValues.All(value => value == 0))
            {
                ClearSliceTransforms();
                return;
            }

            var highlighted = new HashSet<string>(
                (HighlightedLabels ?? Enumerable.Empty<string>())
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => label.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(hoveredSliceLabel))
            {
                highlighted.Add(hoveredSliceLabel);
            }

            var totalValue = chartValues.Sum();
            if (totalValue <= 0)
            {
                ClearSliceTransforms();
                return;
            }

            double currentAngle = LiveChartsRotationOffset;
            var iconOffsets = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < seriesList.Count; i++)
            {
                var series = seriesList[i];
                var sliceValue = i < chartValues.Count ? chartValues[i] : 0;
                var sliceArc = (sliceValue / totalValue) * 360.0;
                var midpointAngle = currentAngle + (sliceArc / 2.0);
                var angleRadians = midpointAngle * Math.PI / 180.0;
                var isHighlighted = !string.IsNullOrWhiteSpace(series.Title) && highlighted.Contains(series.Title);
                var offset = isHighlighted ? SliceHighlightOffset : 0.0;
                var slice = GetPieSlice(series);
                var offsetX = offset * Math.Sin(angleRadians);
                var offsetY = -offset * Math.Cos(angleRadians);

                if (!string.IsNullOrWhiteSpace(series.Title))
                {
                    iconOffsets[series.Title] = (offsetX, offsetY);
                }

                SetSliceTransform(
                    slice,
                    offsetX,
                    offsetY);

                currentAngle += sliceArc;
            }

            ApplyIconOffsets(iconOffsets);
        }

        private void ClearSliceTransforms()
        {
            if (PieSeries == null)
            {
                return;
            }

            foreach (var series in PieSeries.OfType<PieSeries>())
            {
                SetSliceTransform(GetPieSlice(series), 0, 0);
            }

            ClearIconOffsets();
        }

        private static PieSlice GetPieSlice(PieSeries series)
        {
            if (series == null)
            {
                return null;
            }

            var chartPoint = series.ChartPoints?.FirstOrDefault();
            if (chartPoint == null)
            {
                return null;
            }

            var pointView = chartPoint.View;
            if (pointView == null || PiePointViewSliceProperty == null)
            {
                return null;
            }

            return PiePointViewSliceProperty?.GetValue(pointView) as PieSlice;
        }

        private void ApplyIconOffsets(IReadOnlyDictionary<string, (double X, double Y)> iconOffsets)
        {
            if (IconPositions.Count == 0)
            {
                return;
            }

            foreach (var iconPosition in IconPositions)
            {
                var offset = !string.IsNullOrWhiteSpace(iconPosition.Label) &&
                             iconOffsets != null &&
                             iconOffsets.TryGetValue(iconPosition.Label, out var resolvedOffset)
                    ? resolvedOffset
                    : (0.0, 0.0);

                iconPosition.OffsetX = offset.Item1;
                iconPosition.OffsetY = offset.Item2;
            }
        }

        private void ClearIconOffsets()
        {
            if (IconPositions.Count == 0)
            {
                return;
            }

            foreach (var iconPosition in IconPositions)
            {
                iconPosition.OffsetX = 0;
                iconPosition.OffsetY = 0;
            }
        }

        private static void SetSliceTransform(PieSlice slice, double x, double y)
        {
            if (slice == null)
            {
                return;
            }

            if (!(slice.RenderTransform is TranslateTransform transform))
            {
                transform = new TranslateTransform();
                slice.RenderTransform = transform;
            }

            AnimateTransformAxis(transform, TranslateTransform.XProperty, x);
            AnimateTransformAxis(transform, TranslateTransform.YProperty, y);
        }

        private static void AnimateTransformAxis(TranslateTransform transform, DependencyProperty property, double target)
        {
            var current = property == TranslateTransform.XProperty ? transform.X : transform.Y;
            if (Math.Abs(current - target) < 0.01)
            {
                if (property == TranslateTransform.XProperty)
                {
                    transform.X = target;
                }
                else
                {
                    transform.Y = target;
                }
                return;
            }

            var animation = new DoubleAnimation
            {
                To = target,
                Duration = SliceAnimationDuration,
                EasingFunction = SliceAnimationEasing,
                FillBehavior = FillBehavior.HoldEnd
            };

            transform.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
}

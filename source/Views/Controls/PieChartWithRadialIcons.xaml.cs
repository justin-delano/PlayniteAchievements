using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// A pie chart with icons positioned at the midpoint of each slice on a circle around the chart.
    /// </summary>
    public partial class PieChartWithRadialIcons : UserControl
    {
        private static readonly double IconSize = 18.0;
        private List<PieSeries> subscribedSeries = new List<PieSeries>();
        private bool calculationScheduled;

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

        private void CalculatePositions()
        {
            if (PieSeries == null || PieSeries.Count == 0 || LegendItems == null || LegendItems.Count == 0)
            {
                IconPositions.Clear();
                return;
            }

            var chartValues = PieSeries
                .OfType<PieSeries>()
                .Select(s =>
                {
                    var values = s.Values as ChartValues<PieSliceChartData>;
                    return values?.Count > 0 ? values[0].ChartValue : 0;
                })
                .ToList();

            if (chartValues.Count == 0 || chartValues.All(v => v == 0))
            {
                IconPositions.Clear();
                return;
            }

            double totalValue = chartValues.Sum();
            double controlSize = Math.Min(ActualWidth, ActualHeight);
            if (controlSize <= 0)
            {
                return;
            }

            double pieRadius = (controlSize - 8) / 2.0;
            double iconRadius = pieRadius + IconOffset;
            double centerX = ActualWidth / 2.0;
            double centerY = ActualHeight / 2.0;

            // LiveCharts renders first slice at approximately 1:00 position
            // Offset by -30° to align icons with slice midpoints
            const double LiveChartsRotationOffset = 45;
            double currentAngle = LiveChartsRotationOffset;
            var positions = new List<PieIconPosition>();

            for (int i = 0; i < chartValues.Count && i < LegendItems.Count; i++)
            {
                double sliceArc = (chartValues[i] / totalValue) * 360.0;
                double midpointAngle = currentAngle + (sliceArc / 2.0);
                double angleRadians = midpointAngle * Math.PI / 180.0;

                var legend = LegendItems[i];

                // Only show icon if count > 0
                if (legend.Count > 0)
                {
                    // Clockwise from top: x = sin(θ), y = -cos(θ)
                    double x = centerX + iconRadius * Math.Sin(angleRadians) - IconSize / 2.0;
                    double y = centerY - iconRadius * Math.Cos(angleRadians) - IconSize / 2.0;

                    positions.Add(new PieIconPosition
                    {
                        IconKey = legend.IconKey,
                        ColorHex = legend.ColorHex,
                        Count = legend.Count,
                        X = x,
                        Y = y
                    });
                }

                currentAngle += sliceArc;
            }

            SynchronizePositions(positions);
        }

        private void SynchronizePositions(IReadOnlyList<PieIconPosition> positions)
        {
            IconPositions.Clear();
            foreach (var pos in positions)
            {
                IconPositions.Add(pos);
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>One compact-sparkline bar: pixel height and a count tooltip.</summary>
    public sealed class TimelineBarViewModel
    {
        public TimelineBarViewModel(double barHeight, string tooltip)
        {
            BarHeight = barHeight;
            Tooltip = tooltip;
        }

        public double BarHeight { get; }

        public string Tooltip { get; }
    }

    /// <summary>A selectable timeline range button.</summary>
    public sealed class TimelineRangeOptionViewModel
    {
        public TimelineRangeOptionViewModel(string label, TimelineRange value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public TimelineRange Value { get; }
    }

    /// <summary>
    /// Backs the Timeline widget. Compact shows a hand-rolled bar sparkline; larger viewports reuse
    /// the existing LiveCharts column chart via TimelineViewModel, with range buttons when expanded.
    /// </summary>
    public sealed class TimelineWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private const double SparklineMaxHeight = 80d;

        private readonly TimelineViewModel _timeline = new TimelineViewModel();
        private bool _isCompact;
        private bool _showRangeButtons;
        private bool _showChart;
        private bool _showSparkline;
        private bool _showEmpty;

        public TimelineWidgetViewModel()
        {
            SetRangeCommand = new RelayCommand(SetRange);
            Ranges = new[]
            {
                Option(TimelineRange.OneMonth, "LOCPlayAch_TimeRange_1M"),
                Option(TimelineRange.ThreeMonths, "LOCPlayAch_TimeRange_3M"),
                Option(TimelineRange.OneYear, "LOCPlayAch_TimeRange_1Y"),
                Option(TimelineRange.All, "LOCPlayAch_Common_All")
            };
        }

        public TimelineViewModel Timeline => _timeline;

        public bool ShowChart { get => _showChart; private set => SetValue(ref _showChart, value); }

        public bool ShowSparkline { get => _showSparkline; private set => SetValue(ref _showSparkline, value); }

        public bool ShowEmpty { get => _showEmpty; private set => SetValue(ref _showEmpty, value); }

        public bool ShowRangeButtons { get => _showRangeButtons; private set => SetValue(ref _showRangeButtons, value); }

        public BulkObservableCollection<TimelineBarViewModel> SparklineBars { get; } =
            new BulkObservableCollection<TimelineBarViewModel>();

        public IReadOnlyList<TimelineRangeOptionViewModel> Ranges { get; }

        public RelayCommand SetRangeCommand { get; }

        protected override void Refresh()
        {
            var counts = Projection?.Timeline ?? new Dictionary<DateTime, int>();
            _timeline.TimelineRange = ShowcaseTimelineOptions.GetRange(Projection?.Instance);
            _timeline.SetCounts(counts.ToDictionary(pair => pair.Key, pair => pair.Value));

            _isCompact = Density == WidgetViewportDensity.Compact;
            ShowRangeButtons = Density == WidgetViewportDensity.Expanded;

            if (_isCompact)
            {
                var values = counts
                    .OrderBy(pair => pair.Key)
                    .Select(pair => Math.Max(0, pair.Value))
                    .ToList();
                values = values.Skip(Math.Max(0, values.Count - 14)).ToList();
                var maximum = values.Count > 0 ? Math.Max(1, values.Max()) : 1;
                SparklineBars.ReplaceAll(values.Select(value => new TimelineBarViewModel(
                    Math.Max(2, SparklineMaxHeight * value / maximum),
                    value.ToString("N0", FormattingCulture.Current))));
                ShowSparkline = values.Count > 0;
                ShowEmpty = values.Count == 0;
                ShowChart = false;
            }
            else
            {
                SparklineBars.Clear();
                ShowSparkline = false;
                ShowEmpty = false;
                ShowChart = true;
            }
        }

        private void SetRange(object parameter)
        {
            if (parameter is TimelineRange range)
            {
                ShowcaseTimelineOptions.SetRange(Projection?.Instance, range);
                ShowcaseConfigurationCommit.Commit();
            }
        }

        private static TimelineRangeOptionViewModel Option(TimelineRange value, string labelKey) =>
            new TimelineRangeOptionViewModel(ResourceProvider.GetString(labelKey), value);
    }
}

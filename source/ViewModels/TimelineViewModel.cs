using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Logging;
using Playnite.SDK;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public class TimelineViewModel : ObservableObject
    {
        private readonly ILogger _logger = PluginLogger.GetLogger(nameof(TimelineViewModel));

        private readonly object _sync = new object();
        private Dictionary<DateTime, int> _countsByDate = new Dictionary<DateTime, int>();
        private int _updateVersion;

        public TimelineViewModel()
        {
            SetTimeRangeCommand = new RelayCommand(param =>
            {
                if (Enum.TryParse<TimelineRange>(param?.ToString(), out var range))
                    TimelineRange = range;
            });
        }

        public void SetCounts(IDictionary<DateTime, int> countsByDate)
        {
            lock (_sync)
            {
                _countsByDate = countsByDate != null
                    ? new Dictionary<DateTime, int>(countsByDate)
                    : new Dictionary<DateTime, int>();
            }

            UpdateTimelineData();
        }

        private TimelineRange _timelineRange = TimelineRange.OneYear;
        public TimelineRange TimelineRange
        {
            get => _timelineRange;
            set
            {
                if (SetValueAndReturn(ref _timelineRange, value))
                {
                    UpdateTimelineData();
                }
            }
        }

        public SeriesCollection TimelineSeries { get; } = new SeriesCollection();
        public ObservableCollection<string> TimelineLabels { get; } = new ObservableCollection<string>();
        public Func<double, string> YAxisFormatter { get; } = value => value.ToString("N0");
        public ICommand SetTimeRangeCommand { get; }

        public void UpdateTimelineData()
        {
            var version = Interlocked.Increment(ref _updateVersion);

            _ = Task.Run(() =>
            {
                try
                {
                    Dictionary<DateTime, int> localCounts;
                    lock (_sync)
                    {
                        localCounts = _countsByDate;
                    }

                    var endDate = DateTime.UtcNow;
                    var startDate = GetStartDateForRange(TimelineRange, localCounts);

                    var values = new List<int>();
                    var labels = new List<string>();

                    if (TimelineRange == TimelineRange.OneYear || TimelineRange == TimelineRange.All)
                    {
                        // Bars every month
                        var monthlyData = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (var kvp in localCounts)
                        {
                            var monthKey = kvp.Key.ToString("yyyy-MM");
                            if (!monthlyData.ContainsKey(monthKey))
                                monthlyData[monthKey] = 0;
                            monthlyData[monthKey] += kvp.Value;
                        }

                        var allDates = localCounts.Keys;
                        var firstDate = allDates.Any() ? allDates.Min() : startDate;
                        var effectiveStartDate = TimelineRange == TimelineRange.All ? firstDate : startDate;

                        var currentMonth = new DateTime(effectiveStartDate.Year, effectiveStartDate.Month, 1);
                        while (currentMonth <= endDate)
                        {
                            var monthKey = currentMonth.ToString("yyyy-MM");
                            values.Add(monthlyData.TryGetValue(monthKey, out var count) ? count : 0);

                            if (TimelineRange == TimelineRange.All)
                            {
                                // Axis ticks every 6 months for All time
                                if (currentMonth.Month == 1 || currentMonth.Month == 7)
                                {
                                    labels.Add(currentMonth.ToString("MMM yy"));
                                }
                                else
                                {
                                    labels.Add(string.Empty);
                                }
                            }
                            else // OneYear
                            {
                                labels.Add(currentMonth.ToString("MMM yy"));
                            }
                            currentMonth = currentMonth.AddMonths(1);
                        }
                    }
                    else if (TimelineRange == TimelineRange.ThreeMonths)
                    {
                        // Daily bars, axis ticks every 2 weeks
                        var currentDate = startDate.Date;
                        var nextLabelDate = currentDate;
                        while (currentDate <= endDate.Date)
                        {
                            values.Add(localCounts.TryGetValue(currentDate, out var count) ? count : 0);
                            if (currentDate >= nextLabelDate)
                            {
                                labels.Add(currentDate.ToString("M/d"));
                                nextLabelDate = currentDate.AddDays(14);
                            }
                            else
                            {
                                labels.Add(string.Empty);
                            }
                            currentDate = currentDate.AddDays(1);
                        }
                    }
                    else
                    {
                        // Daily bars and labels for other short ranges
                        var currentDate = startDate.Date;
                        while (currentDate <= endDate.Date)
                        {
                            values.Add(localCounts.TryGetValue(currentDate, out var count) ? count : 0);
                            labels.Add(currentDate.ToString("M/d"));
                            currentDate = currentDate.AddDays(1);
                        }
                    }

                    System.Windows.Application.Current?.Dispatcher?.InvokeIfNeeded(() =>
                    {
                        if (version != _updateVersion)
                        {
                            return;
                        }

                        if (values.Any())
                        {
                            if (TimelineSeries.Count == 0)
                            {
                                TimelineSeries.Add(new ColumnSeries
                                {
                                    Title = "Achievements",
                                    Values = new ChartValues<int>(values)
                                });
                            }
                            else
                            {
                                CollectionHelper.SynchronizeValueCollection((ChartValues<int>)TimelineSeries[0].Values, values);
                            }
                        }
                        else
                        {
                            while (TimelineSeries.Count > 0)
                            {
                                TimelineSeries.RemoveAt(0);
                            }
                        }

                        CollectionHelper.SynchronizeValueCollection(TimelineLabels, labels);
                    });
                }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, "Timeline update failed.");
                }
            });
        }

        private static DateTime GetStartDateForRange(TimelineRange range, IDictionary<DateTime, int> counts)
        {
            var now = DateTime.UtcNow;
            return range switch
            {
                TimelineRange.SevenDays => now.AddDays(-7),
                TimelineRange.FourteenDays => now.AddDays(-14),
                TimelineRange.OneMonth => now.AddMonths(-1),
                TimelineRange.ThreeMonths => now.AddMonths(-3),
                TimelineRange.OneYear => now.AddYears(-1),
                TimelineRange.All =>
                    counts != null && counts.Count > 0
                        ? counts.Keys.Min().Date
                        : now.Date,
                _ => now.AddDays(-14)
            };
        }
    }
}

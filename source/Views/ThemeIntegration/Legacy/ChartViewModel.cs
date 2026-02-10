using System;
using System.Collections.Generic;
using System.Linq;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    public class ChartViewModel : ObservableObject
    {
        private bool hideChartOptions;
        public bool HideChartOptions { get => hideChartOptions; set => SetValue(ref hideChartOptions, value); }

        private bool allPeriod = true;
        public bool AllPeriod { get => allPeriod; set => SetValue(ref allPeriod, value); }

        private bool cutPeriod;
        public bool CutPeriod { get => cutPeriod; set => SetValue(ref cutPeriod, value); }

        private bool cutEnabled = true;
        public bool CutEnabled { get => cutEnabled; set => SetValue(ref cutEnabled, value); }

        private bool disableAnimations = true;
        public bool DisableAnimations { get => disableAnimations; set => SetValue(ref disableAnimations, value); }

        private bool enableAxisLabel = true;
        public bool EnableAxisLabel { get => enableAxisLabel; set => SetValue(ref enableAxisLabel, value); }

        private bool enableOrdinatesLabel = true;
        public bool EnableOrdinatesLabel { get => enableOrdinatesLabel; set => SetValue(ref enableOrdinatesLabel, value); }

        private int labelsRotation;
        public int LabelsRotation { get => labelsRotation; set => SetValue(ref labelsRotation, value); }

        private double chartHeight = double.NaN;
        public double ChartHeight { get => chartHeight; set => SetValue(ref chartHeight, value); }

        private SeriesCollection series;
        public SeriesCollection Series { get => series; set => SetValue(ref series, value); }

        private IList<string> labels;
        public IList<string> Labels { get => labels; set => SetValue(ref labels, value); }

        private Func<double, string> formatter = value => value < 0 ? string.Empty : value.ToString("N0");
        public Func<double, string> Formatter { get => formatter; set => SetValue(ref formatter, value); }

        public void UpdateFromAchievements(IEnumerable<AchievementDetail> listAchUnlockDateAsc)
        {
            var unlocked = (listAchUnlockDateAsc ?? Enumerable.Empty<AchievementDetail>())
                .Where(a => a.Unlocked && a.UnlockTimeUtc.HasValue)
                .Select(a => a.UnlockTimeUtc.Value.Date)
                .ToList();

            if (!unlocked.Any())
            {
                Series = null;
                Labels = null;
                CutEnabled = true;
                return;
            }

            DateTime end = unlocked.Max();
            DateTime start;

            if (AllPeriod)
            {
                start = unlocked.Min();
            }
            else
            {
                // SuccessStory uses a fixed abscissa window when not in AllPeriod.
                start = end.AddDays(-29);
            }

            var spanDays = (end - start).TotalDays + 1;

            // SuccessStory auto-forces CutPeriod for long ranges.
            bool effectiveCut = AllPeriod && CutPeriod;
            if (AllPeriod && spanDays > 30)
            {
                effectiveCut = true;
                CutPeriod = true;
                CutEnabled = false;
            }
            else
            {
                CutEnabled = true;
            }

            // Build buckets
            var buckets = new SortedDictionary<DateTime, int>();

            if (effectiveCut)
            {
                if (spanDays > 365)
                {
                    // Monthly buckets
                    foreach (var date in unlocked)
                    {
                        var key = new DateTime(date.Year, date.Month, 1);
                        buckets[key] = buckets.TryGetValue(key, out var v) ? v + 1 : 1;
                    }

                    var current = new DateTime(start.Year, start.Month, 1);
                    var last = new DateTime(end.Year, end.Month, 1);
                    while (current <= last)
                    {
                        if (!buckets.ContainsKey(current))
                        {
                            buckets[current] = 0;
                        }
                        current = current.AddMonths(1);
                    }
                }
                else
                {
                    // Weekly buckets (Monday)
                    foreach (var date in unlocked)
                    {
                        int delta = ((int)date.DayOfWeek + 6) % 7; // Monday=0
                        var key = date.AddDays(-delta);
                        buckets[key] = buckets.TryGetValue(key, out var v) ? v + 1 : 1;
                    }

                    int startDelta = ((int)start.DayOfWeek + 6) % 7;
                    int endDelta = ((int)end.DayOfWeek + 6) % 7;
                    var current = start.AddDays(-startDelta);
                    var last = end.AddDays(-endDelta);
                    while (current <= last)
                    {
                        if (!buckets.ContainsKey(current))
                        {
                            buckets[current] = 0;
                        }
                        current = current.AddDays(7);
                    }
                }
            }
            else
            {
                // Daily buckets
                foreach (var date in unlocked)
                {
                    buckets[date] = buckets.TryGetValue(date, out var v) ? v + 1 : 1;
                }

                var current = start.Date;
                while (current <= end.Date)
                {
                    if (!buckets.ContainsKey(current))
                    {
                        buckets[current] = 0;
                    }
                    current = current.AddDays(1);
                }
            }

            var values = new ChartValues<int>(buckets.Values.ToList());
            var labelList = buckets.Keys.Select(d =>
            {
                if (effectiveCut && spanDays > 365)
                {
                    return d.ToString("MMM yy");
                }
                if (effectiveCut)
                {
                    return d.ToString("M/d");
                }
                return d.ToString("M/d");
            }).ToList();

            Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = string.Empty,
                    Values = values,
                    PointGeometry = null
                }
            };

            Labels = labelList;

            // Hide axis labels when they would be unreadable (same intent as SS)
            EnableAxisLabel = !(AllPeriod && Labels.Count > 16);
        }
    }
}

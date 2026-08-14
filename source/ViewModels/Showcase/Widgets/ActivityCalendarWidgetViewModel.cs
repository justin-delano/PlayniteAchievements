using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>A single heatmap cell. Intensity -1 marks a placeholder that pads a partial week.</summary>
    public sealed class ActivityCalendarDayViewModel
    {
        public ActivityCalendarDayViewModel(int intensity, string tooltip, double size)
        {
            Intensity = intensity;
            Tooltip = tooltip;
            Size = size;
        }

        public int Intensity { get; }

        public string Tooltip { get; }

        public double Size { get; }
    }

    /// <summary>One Sunday-first week column with an optional month label.</summary>
    public sealed class ActivityCalendarWeekViewModel
    {
        public ActivityCalendarWeekViewModel(
            string monthLabel,
            IReadOnlyList<ActivityCalendarDayViewModel> days)
        {
            MonthLabel = monthLabel ?? string.Empty;
            Days = days;
        }

        public string MonthLabel { get; }

        public IReadOnlyList<ActivityCalendarDayViewModel> Days { get; }
    }

    /// <summary>
    /// Backs the ActivityCalendar widget: a contributions-style heatmap of unlocks per
    /// day. Density controls the trailing window (weeks), cell size, and label/legend
    /// visibility; the projection owns counts and intensity bucketing.
    /// </summary>
    public sealed class ActivityCalendarWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private double _weekdayRowHeight = 12;
        private bool _showMonthLabels = true;
        private bool _showWeekdayLabels = true;
        private bool _showLegend = true;
        private bool _showEmpty;

        public BulkObservableCollection<ActivityCalendarWeekViewModel> Weeks { get; } =
            new BulkObservableCollection<ActivityCalendarWeekViewModel>();

        public BulkObservableCollection<string> WeekdayLabels { get; } =
            new BulkObservableCollection<string>();

        public BulkObservableCollection<ActivityCalendarDayViewModel> LegendCells { get; } =
            new BulkObservableCollection<ActivityCalendarDayViewModel>();

        /// <summary>Cell box height including margins, so weekday labels stay row-aligned.</summary>
        public double WeekdayRowHeight
        {
            get => _weekdayRowHeight;
            private set => SetValue(ref _weekdayRowHeight, value);
        }

        public bool ShowMonthLabels
        {
            get => _showMonthLabels;
            private set => SetValue(ref _showMonthLabels, value);
        }

        public bool ShowWeekdayLabels
        {
            get => _showWeekdayLabels;
            private set => SetValue(ref _showWeekdayLabels, value);
        }

        public bool ShowLegend
        {
            get => _showLegend;
            private set => SetValue(ref _showLegend, value);
        }

        public bool ShowEmpty
        {
            get => _showEmpty;
            private set => SetValue(ref _showEmpty, value);
        }

        protected override void Refresh()
        {
            var calendar = Projection?.ActivityCalendar ?? new ShowcaseActivityCalendar();
            var compact = Density == WidgetViewportDensity.Compact;
            var weeksToShow = Density == WidgetViewportDensity.Expanded ? 53 : compact ? 13 : 26;
            var cellSize = Density == WidgetViewportDensity.Expanded ? 13d : compact ? 7d : 10d;
            WeekdayRowHeight = cellSize + 2;
            ShowMonthLabels = !compact;
            ShowWeekdayLabels = !compact;
            ShowLegend = !compact;
            ShowEmpty = calendar.TotalCount == 0;

            var culture = FormattingCulture.Current;
            var tooltipFormat = ResourceProvider.GetString("LOCPlayAch_Showcase_ActivityTooltipFormat");

            var days = calendar.Days ?? Array.Empty<ShowcaseActivityDay>();
            var weeks = new List<ActivityCalendarWeekViewModel>();
            for (var index = 0; index < days.Count; index += 7)
            {
                var cells = new List<ActivityCalendarDayViewModel>(7);
                string monthLabel = null;
                for (var offset = 0; offset < 7; offset++)
                {
                    var dayIndex = index + offset;
                    if (dayIndex >= days.Count)
                    {
                        cells.Add(new ActivityCalendarDayViewModel(-1, null, cellSize));
                        continue;
                    }

                    var day = days[dayIndex];
                    if (day.Date.Day == 1)
                    {
                        monthLabel = day.Date.ToString("MMM", culture);
                    }

                    var tooltip = string.Format(
                        culture,
                        tooltipFormat,
                        day.Date.ToString("d", culture),
                        day.Count);
                    cells.Add(new ActivityCalendarDayViewModel(day.Intensity, tooltip, cellSize));
                }

                weeks.Add(new ActivityCalendarWeekViewModel(monthLabel, cells));
            }

            if (weeks.Count > weeksToShow)
            {
                weeks = weeks.Skip(weeks.Count - weeksToShow).ToList();
            }

            Weeks.ReplaceAll(weeks);
            LegendCells.ReplaceAll(Enumerable.Range(0, 5)
                .Select(intensity => new ActivityCalendarDayViewModel(intensity, null, cellSize)));

            var dayNames = culture.DateTimeFormat.AbbreviatedDayNames;
            WeekdayLabels.ReplaceAll(new[]
            {
                string.Empty,
                dayNames[(int)DayOfWeek.Monday],
                string.Empty,
                dayNames[(int)DayOfWeek.Wednesday],
                string.Empty,
                dayNames[(int)DayOfWeek.Friday],
                string.Empty
            });
        }
    }
}

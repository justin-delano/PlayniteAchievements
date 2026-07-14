using System;
using System.Globalization;
using Playnite.SDK;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Calendar bucket a date falls into relative to "now", from most to least recent.
    /// </summary>
    public enum RelativeDateBucket
    {
        Today,
        Yesterday,
        ThisWeek,
        ThisMonth,
        ThisYear,
        LongAgo
    }

    /// <summary>
    /// Maps a local date to a relative calendar bucket and its localized label.
    /// Bucket computation is pure (no localization or clock dependency) so it can be unit tested;
    /// label resolution is kept separate.
    /// </summary>
    public static class RelativeDateFormatter
    {
        /// <summary>
        /// Determines the relative bucket for <paramref name="localValue"/> against <paramref name="localNow"/>.
        /// Both arguments are expected in local time. More specific buckets take precedence.
        /// </summary>
        public static RelativeDateBucket GetBucket(DateTime localValue, DateTime localNow)
        {
            var valueDate = localValue.Date;
            var today = localNow.Date;

            // Treat future values as Today.
            if (valueDate >= today)
            {
                return RelativeDateBucket.Today;
            }

            if (valueDate == today.AddDays(-1))
            {
                return RelativeDateBucket.Yesterday;
            }

            if (valueDate >= StartOfWeek(today))
            {
                return RelativeDateBucket.ThisWeek;
            }

            if (valueDate.Year == today.Year && valueDate.Month == today.Month)
            {
                return RelativeDateBucket.ThisMonth;
            }

            if (valueDate.Year == today.Year)
            {
                return RelativeDateBucket.ThisYear;
            }

            return RelativeDateBucket.LongAgo;
        }

        /// <summary>
        /// Number of whole calendar years between <paramref name="localValue"/> and <paramref name="localNow"/>;
        /// 1 for any date in the immediately prior calendar year, matching the calendar-based bucketing.
        /// </summary>
        public static int GetYearsAgo(DateTime localValue, DateTime localNow)
        {
            return localNow.Year - localValue.Year;
        }

        /// <summary>
        /// Returns the localized label for <paramref name="localValue"/> relative to <paramref name="localNow"/>.
        /// </summary>
        public static string ToRelativeLabel(DateTime localValue, DateTime localNow)
        {
            switch (GetBucket(localValue, localNow))
            {
                case RelativeDateBucket.Today:
                    return ResourceProvider.GetString("LOCPlayAch_Common_Date_Today");
                case RelativeDateBucket.Yesterday:
                    return ResourceProvider.GetString("LOCPlayAch_Common_Date_Yesterday");
                case RelativeDateBucket.ThisWeek:
                    return ResourceProvider.GetString("LOCPlayAch_Common_Date_ThisWeek");
                case RelativeDateBucket.ThisMonth:
                    return ResourceProvider.GetString("LOCPlayAch_Common_Date_ThisMonth");
                case RelativeDateBucket.ThisYear:
                    return ResourceProvider.GetString("LOCPlayAch_Common_Date_ThisYear");
                default:
                    var years = GetYearsAgo(localValue, localNow);
                    return years == 1
                        ? ResourceProvider.GetString("LOCPlayAch_Common_Date_OneYearAgo")
                        : string.Format(CultureInfo.CurrentCulture, ResourceProvider.GetString("LOCPlayAch_Common_Date_YearsAgo"), years);
            }
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int offset = (7 + (date.DayOfWeek - firstDayOfWeek)) % 7;
            return date.AddDays(-offset);
        }
    }
}

using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Achievements
{
    /// <summary>
    /// One stacked segment of an unlocks-over-time chart: per-day counts (keys are local calendar
    /// days) for a single platform, with the title and color its segments and tooltip row use.
    /// </summary>
    public sealed class TimelineSeriesCounts
    {
        public TimelineSeriesCounts(string key, string title, string colorHex, IReadOnlyDictionary<DateTime, int> countsByDate)
        {
            Key = key ?? string.Empty;
            Title = title ?? string.Empty;
            ColorHex = colorHex;
            CountsByDate = countsByDate ?? new Dictionary<DateTime, int>();
        }

        /// <summary>Stable identity across feeds; the effective provider key.</summary>
        public string Key { get; }

        public string Title { get; }

        /// <summary>Segment color as #RRGGBB or #AARRGGBB.</summary>
        public string ColorHex { get; }

        public IReadOnlyDictionary<DateTime, int> CountsByDate { get; }
    }
}

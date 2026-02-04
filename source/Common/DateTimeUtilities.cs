using System;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Centralized utilities for DateTime conversions and normalization.
    /// Ensures consistent handling of DateTimeKind across the application.
    /// </summary>
    public static class DateTimeUtilities
    {
        /// <summary>
        /// Ensures a DateTime value has Utc kind, converting from Local or Unspecified as needed.
        /// </summary>
        public static DateTime AsUtcKind(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc) return dt;
            if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        /// <summary>
        /// Ensures a nullable DateTime value has Utc kind.
        /// </summary>
        public static DateTime? AsUtcKind(DateTime? dt) => dt.HasValue ? AsUtcKind(dt.Value) : (DateTime?)null;

        /// <summary>
        /// Converts a DateTime from Utc to Local time, handling all DateTimeKind cases.
        /// </summary>
        public static DateTime AsLocalFromUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Local) return dt;
            if (dt.Kind == DateTimeKind.Utc) return dt.ToLocalTime();
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
        }

        /// <summary>
        /// Converts a nullable DateTime from Utc to Local time.
        /// </summary>
        public static DateTime? AsLocalFromUtc(DateTime? dt) => dt.HasValue ? AsLocalFromUtc(dt.Value) : (DateTime?)null;
    }
}

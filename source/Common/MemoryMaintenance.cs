using System;
using System.Runtime;
using Playnite.SDK;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Process-level memory maintenance shared across refresh paths.
    /// </summary>
    internal static class MemoryMaintenance
    {
        /// <summary>
        /// Requests a one-time Large Object Heap compaction (applied on the next full blocking
        /// collection) when a scan did enough work to meaningfully inflate the LOH. Large web
        /// scrapes push multi-megabyte HTML strings onto the LOH, which .NET does not return to
        /// the OS on its own; this hands that working set back after a substantial run.
        /// Gated by <paramref name="workVolume"/> so small or no-op runs (single-game refreshes,
        /// in-game poller ticks, empty periodic updates) never trigger a collection.
        /// Call from a background thread only — <see cref="GC.Collect()"/> blocks.
        /// </summary>
        public static void CompactLargeObjectHeapAfterLargeScan(int workVolume, int threshold, ILogger logger = null)
        {
            if (workVolume < threshold)
            {
                return;
            }

            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "One-time LOH-compacting collection failed.");
            }
        }
    }
}

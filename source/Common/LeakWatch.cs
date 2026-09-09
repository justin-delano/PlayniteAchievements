using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Counts how many instances of a tracked kind are still reachable after a collection.
    /// Process totals say memory grew; this says which object graph is still rooted. Tracking
    /// holds only weak references, so it never keeps an instance alive itself.
    /// Gated by <see cref="MemoryDiagnostics.Enabled"/>.
    /// </summary>
    internal static class LeakWatch
    {
        // Per kind, so one chatty kind cannot crowd the others out.
        private const int MaxTrackedPerKind = 256;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, List<WeakReference>> Tracked =
            new Dictionary<string, List<WeakReference>>(StringComparer.Ordinal);

        public static void Track(string kind, object instance)
        {
            if (!MemoryDiagnostics.Enabled || instance == null || string.IsNullOrWhiteSpace(kind))
            {
                return;
            }

            lock (Sync)
            {
                if (!Tracked.TryGetValue(kind, out var list))
                {
                    list = new List<WeakReference>();
                    Tracked[kind] = list;
                }

                list.Add(new WeakReference(instance));
                if (list.Count <= MaxTrackedPerKind)
                {
                    return;
                }

                // Drop collected entries first; only trim live ones if still over budget, and
                // from the oldest end (an old instance still alive is the interesting one, but
                // an unbounded list would itself become a memory problem).
                list.RemoveAll(reference => !reference.IsAlive);
                if (list.Count > MaxTrackedPerKind)
                {
                    list.RemoveRange(0, list.Count - MaxTrackedPerKind);
                }
            }
        }

        /// <summary>
        /// Live instance count per tracked kind, as "kind:live/seen". Call after forcing a
        /// collection so the counts mean "still rooted" rather than "not yet collected".
        /// </summary>
        public static string DescribeLive()
        {
            if (!MemoryDiagnostics.Enabled)
            {
                return string.Empty;
            }

            lock (Sync)
            {
                if (Tracked.Count == 0)
                {
                    return "none";
                }

                var parts = new List<string>();
                foreach (var pair in Tracked.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    var live = pair.Value.Count(reference => reference.IsAlive);
                    parts.Add($"{pair.Key}:{live}/{pair.Value.Count}");
                }

                return string.Join(",", parts);
            }
        }
    }
}

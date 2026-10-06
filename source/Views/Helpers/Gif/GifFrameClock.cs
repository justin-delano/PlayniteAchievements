using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PlayniteAchievements.Views.Helpers.Gif
{
    /// <summary>
    /// One background thread that fires callbacks at <see cref="Stopwatch"/> deadlines for every
    /// playing GIF. It waits on a high-resolution waitable timer (Windows 10 1803+) so a 20 ms frame
    /// lands within about a millisecond instead of on the 15.6 ms system tick; older systems fall
    /// back to the regular timer. Callbacks run on the clock thread and must only hand work off.
    /// </summary>
    internal static class GifFrameClock
    {
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;

        private struct Entry
        {
            internal long Due;
            internal Action Callback;
        }

        private sealed class TimerWaitHandle : WaitHandle
        {
            internal TimerWaitHandle(SafeWaitHandle handle)
            {
                SafeWaitHandle = handle;
            }
        }

        private static readonly object Sync = new object();
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly List<Action> DueScratch = new List<Action>();
        private static Thread _thread;
        private static SafeWaitHandle _timer;
        private static WaitHandle[] _waitHandles;

        internal static long Now => Stopwatch.GetTimestamp();

        internal static long FromMilliseconds(double milliseconds) =>
            (long)(milliseconds * Stopwatch.Frequency / 1000.0);

        /// <summary>Runs <paramref name="callback"/> on the clock thread at or after <paramref name="due"/>.</summary>
        internal static void Schedule(long due, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            lock (Sync)
            {
                var earliest = true;
                foreach (var entry in Entries)
                {
                    if (entry.Due <= due)
                    {
                        earliest = false;
                        break;
                    }
                }

                Entries.Add(new Entry { Due = due, Callback = callback });
                EnsureThread();
                if (earliest)
                {
                    Wake.Set();
                }
            }
        }

        private static void EnsureThread()
        {
            if (_thread != null)
            {
                return;
            }

            _timer = CreateTimer();
            _waitHandles = _timer != null
                ? new WaitHandle[] { new TimerWaitHandle(_timer), Wake }
                : null;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "GifFrameClock",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        private static void Run()
        {
            while (true)
            {
                long wait;
                lock (Sync)
                {
                    var now = Now;
                    var next = long.MaxValue;
                    for (var i = Entries.Count - 1; i >= 0; i--)
                    {
                        var entry = Entries[i];
                        if (entry.Due <= now)
                        {
                            DueScratch.Add(entry.Callback);
                            Entries.RemoveAt(i);
                        }
                        else if (entry.Due < next)
                        {
                            next = entry.Due;
                        }
                    }

                    wait = next == long.MaxValue ? -1 : next - now;
                }

                if (DueScratch.Count > 0)
                {
                    foreach (var callback in DueScratch)
                    {
                        try
                        {
                            callback();
                        }
                        catch
                        {
                        }
                    }

                    DueScratch.Clear();
                    continue;
                }

                if (wait < 0)
                {
                    Wake.WaitOne();
                }
                else if (_waitHandles != null && ArmTimer(wait))
                {
                    WaitHandle.WaitAny(_waitHandles);
                }
                else
                {
                    var milliseconds = (int)Math.Ceiling(wait * 1000.0 / Stopwatch.Frequency);
                    Wake.WaitOne(Math.Max(1, milliseconds));
                }
            }
        }

        private static bool ArmTimer(long stopwatchTicks)
        {
            // Negative due time is relative, in 100 ns units.
            var due = -Math.Max(1L, (long)(stopwatchTicks * 10_000_000.0 / Stopwatch.Frequency));
            return SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
        }

        private static SafeWaitHandle CreateTimer()
        {
            try
            {
                var handle = CreateWaitableTimerExW(
                    IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TimerAllAccess);
                }

                if (!handle.IsInvalid)
                {
                    return handle;
                }

                handle.Dispose();
            }
            catch
            {
            }

            return null;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(
            IntPtr timerAttributes, string timerName, uint flags, uint desiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(
            SafeWaitHandle timer,
            ref long dueTime,
            int period,
            IntPtr completionRoutine,
            IntPtr completionRoutineArgument,
            [MarshalAs(UnmanagedType.Bool)] bool resume);
    }
}

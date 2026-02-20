using PlayniteAchievements.Models;
using System;
using System.Diagnostics;

namespace PlayniteAchievements.Services
{
    internal sealed class RebuildProgressReporter
    {
        private readonly Action<ProviderRefreshUpdate> _callback;
        private readonly long _minIntervalTicks;
        private long _lastEmitTimestamp;
        private int _currentIndex;
        public int OverallCount { get; }

        public RebuildProgressReporter(Action<ProviderRefreshUpdate> callback, int overallCount, int minMs = 50)
        {
            _callback = callback;
            OverallCount = Math.Max(0, overallCount);
            _minIntervalTicks = (long)(Math.Max(0, minMs) * (double)Stopwatch.Frequency / 1000.0);
        }

        public void Step() => _currentIndex = Math.Min(OverallCount, _currentIndex + 1);

        public void Emit(ProviderRefreshUpdate u, bool force = false)
        {
            if (u == null || _callback == null) return;

            u.CurrentIndex = Math.Min(_currentIndex, OverallCount);
            u.TotalItems = OverallCount;

            if (force)
            {
                _callback(u);
                _lastEmitTimestamp = Stopwatch.GetTimestamp();
                return;
            }

            if (ShouldEmitNow())
            {
                _callback(u);
            }
        }

        private bool ShouldEmitNow()
        {
            if (_minIntervalTicks <= 0)
            {
                _lastEmitTimestamp = Stopwatch.GetTimestamp();
                return true;
            }

            var now = Stopwatch.GetTimestamp();
            if (_lastEmitTimestamp == 0 || (now - _lastEmitTimestamp) >= _minIntervalTicks)
            {
                _lastEmitTimestamp = now;
                return true;
            }

            return false;
        }
    }
}

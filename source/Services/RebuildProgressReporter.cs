using PlayniteAchievements.Models;
using System;

namespace PlayniteAchievements.Services
{
    internal sealed class RebuildProgressReporter
    {
        private readonly Action<ProviderScanUpdate> _callback;
        private readonly TimeSpan _min;
        private DateTime _last;
        private int _currentCounter;
        public int OverallCount { get; }

        public RebuildProgressReporter(Action<ProviderScanUpdate> callback, int overallCount, int minMs = 50)
        {
            _callback = callback;
            OverallCount = Math.Max(0, overallCount);
            _min = TimeSpan.FromMilliseconds(Math.Max(0, minMs));
        }

        public void Step() => _currentCounter++;

        public void Emit(ProviderScanUpdate u, bool force = false)
        {
            if (u == null || _callback == null) return;

            u.CurrentIndex = Math.Min(_currentCounter, OverallCount);
            u.TotalItems = OverallCount;

            var now = DateTime.UtcNow;

            if (force || (now - _last) >= _min)
            {
                _callback(u);
                _last = now;
            }
        }
    }
}

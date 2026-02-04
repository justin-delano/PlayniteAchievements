using System;
using System.Diagnostics;
using Playnite.SDK;

namespace PlayniteAchievements.Common
{
    public static class PerfTrace
    {
        public static IDisposable Measure(string name, ILogger logger, bool enabled)
        {
            if (!enabled)
            {
                return NoopDisposable.Instance;
            }

            return new Scope(name, logger);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string _name;
            private readonly ILogger _logger;
            private readonly Stopwatch _sw;

            public Scope(string name, ILogger logger)
            {
                _name = string.IsNullOrWhiteSpace(name) ? "perf" : name;
                _logger = logger;
                _sw = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                try
                {
                    _sw.Stop();
                    _logger?.Debug($"[perf] {_name}: {_sw.ElapsedMilliseconds}ms");
                }
                catch
                {
                    // no-op
                }
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();
            public void Dispose() { }
        }
    }
}


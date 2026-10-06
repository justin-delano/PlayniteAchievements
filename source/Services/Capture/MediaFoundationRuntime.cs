using System;
using System.Threading;
using SharpDX.MediaFoundation;

namespace PlayniteAchievements.Services.Capture
{
    /// <summary>
    /// Process-wide Media Foundation lifetime. SharpDX's MediaManager suppresses duplicate Startup
    /// calls but does not isolate independent Shutdown calls, so every capture/export consumer must
    /// share one managed lease count or one exporter can shut Media Foundation down under a recorder.
    /// </summary>
    internal static class MediaFoundationRuntime
    {
        private static readonly object Gate = new object();
        private static int _leases;

        public static IDisposable Acquire()
        {
            lock (Gate)
            {
                if (_leases == 0)
                {
                    MediaManager.Startup();
                }

                checked
                {
                    _leases++;
                }
            }

            return new Lease();
        }

        private static void Release()
        {
            lock (Gate)
            {
                if (_leases <= 0)
                {
                    return;
                }

                _leases--;
                if (_leases == 0)
                {
                    try
                    {
                        MediaManager.Shutdown();
                    }
                    catch
                    {
                        // Teardown must remain non-throwing; there are no active consumers left.
                    }
                }
            }
        }

        private sealed class Lease : IDisposable
        {
            private int _active = 1;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _active, 0) == 1)
                {
                    Release();
                }
            }
        }
    }
}

using System.Windows.Media.Animation;
using Microsoft.Win32;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Gives the plugin's own free-running animations an explicit frame rate at the display's
    /// refresh rate, so they do not inherit a lower process-wide default.
    ///
    /// Another extension sharing Playnite's process (UniPlaySong) overrides
    /// <c>Timeline.DesiredFrameRateProperty</c>'s default to 60 for every timeline. Inherited, that
    /// held an animation to about 58 updates per second on a 165 Hz display, and slowed the whole
    /// render loop to about 90 Hz while only such animations ran; an explicit rate at or above the
    /// refresh rate restored 161 Hz, even beside another capped animation.
    ///
    /// The rate is the fastest connected monitor's, plus 1 Hz: Windows reports whole hertz (a
    /// 59.94 Hz panel reads 59), and a requested rate below the real one throttles the render loop,
    /// while one above it costs nothing. One value serves every window, since shared clocks span
    /// monitors. It is re-read when display settings change.
    /// </summary>
    internal static class AnimationFrameRate
    {
        private const int MarginHz = 1;
        private static int _cached = -1;

        static AnimationFrameRate()
        {
            try
            {
                SystemEvents.DisplaySettingsChanged += (s, e) => _cached = -1;
            }
            catch
            {
                // Without the notification the rate stays at its first reading.
            }
        }

        /// <summary>The frame rate to request, or 0 when no monitor could be read.</summary>
        public static int Current
        {
            get
            {
                var rate = _cached;
                if (rate < 0)
                {
                    var hz = ToastWindowPlacer.MaxMonitorRefreshHz();
                    rate = hz > 0 ? hz + MarginHz : 0;
                    _cached = rate;
                }

                return rate;
            }
        }

        /// <summary>
        /// Sets <paramref name="timeline"/>'s desired frame rate to <see cref="Current"/>. Call before
        /// the timeline is frozen or begun; a frozen timeline, or no readable monitor, is left as is.
        /// </summary>
        public static T Apply<T>(T timeline) where T : Timeline
        {
            var rate = Current;
            if (timeline != null && rate > 0 && !timeline.IsFrozen)
            {
                Timeline.SetDesiredFrameRate(timeline, rate);
            }

            return timeline;
        }
    }
}

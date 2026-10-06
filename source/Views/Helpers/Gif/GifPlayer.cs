using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlayniteAchievements.Views.Helpers.Gif
{
    /// <summary>
    /// Plays one GIF into one <see cref="WriteableBitmap"/> that every Image showing the file
    /// shares. Frames are composited on the thread pool, held until their deadline on
    /// <see cref="GifFrameClock"/>, then copied to the bitmap on the UI thread; the UI thread only
    /// copies the changed rows. Deadlines advance by each frame's own delay rather than from the
    /// time the frame was shown, so timer jitter never adds up and playback keeps the file's rate.
    /// Plays while at least one viewer is active; pausing keeps the position.
    /// </summary>
    internal sealed class GifPlayer : IDisposable
    {
        private enum Stage
        {
            /// <summary>The next frame is being composited on the thread pool.</summary>
            Composing,

            /// <summary>The next frame is composited and waits for its deadline.</summary>
            Ready,

            /// <summary>The next frame is queued to the dispatcher for presentation.</summary>
            Posted,

            /// <summary>Single-frame image, failed, or disposed: nothing further runs.</summary>
            Stopped
        }

        private readonly object _sync = new object();
        private readonly GifCanvas _canvas;
        private readonly Dispatcher _dispatcher;
        private readonly WriteableBitmap _bitmap;
        private readonly Action _onClock;
        private readonly Action _present;
        private readonly WaitCallback _compose;

        private Stage _stage;
        private int _activeViewers;
        private long _deadline;
        private long _suspendedAt;
        private int _nextIndex;
        private ImageSource _grayscaleView;

        private GifPlayer(GifCanvas canvas, Dispatcher dispatcher)
        {
            _canvas = canvas;
            _dispatcher = dispatcher;
            _bitmap = new WriteableBitmap(canvas.Image.Width, canvas.Image.Height, 96, 96, PixelFormats.Bgra32, null);
            _onClock = OnClock;
            _present = Present;
            _compose = Compose;
        }

        internal BitmapSource Bitmap => _bitmap;

        internal int FrameCount => _canvas.Image.Frames.Length;

        internal bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _stage != Stage.Stopped && _activeViewers > 0;
                }
            }
        }

        /// <summary>Raised once, on the UI thread, when a frame cannot be composited.</summary>
        internal event Action<Exception> Failed;

        /// <summary>
        /// Parses and composites the first frame off the UI thread, then creates the bitmap on the
        /// calling thread, which must be <paramref name="dispatcher"/>'s.
        /// </summary>
        internal static async Task<GifPlayer> CreateAsync(byte[] payload, Dispatcher dispatcher)
        {
            var canvas = await Task.Run(() =>
            {
                var created = new GifCanvas(GifImage.Parse(payload));
                created.Render(0);
                return created;
            }).ConfigureAwait(true);

            var player = new GifPlayer(canvas, dispatcher);
            player.CopyToBitmap();
            player.AfterPresent(GifFrameClock.Now);
            return player;
        }

        /// <summary>A live grayscale presentation of the shared bitmap, built once per player.</summary>
        internal ImageSource GetGrayscaleView()
        {
            return _grayscaleView ?? (_grayscaleView = NativeGifAnimation.CreateGrayscaleView(_bitmap));
        }

        /// <summary>Counts a viewer in or out of the active set; playback runs while any is active.</summary>
        internal void SetViewerActive(bool active)
        {
            lock (_sync)
            {
                if (active)
                {
                    _activeViewers++;
                    if (_activeViewers == 1)
                    {
                        // Shift the pending deadline by the time spent suspended so a resumed GIF
                        // continues where it stopped instead of rushing to catch up.
                        _deadline += GifFrameClock.Now - _suspendedAt;
                        if (_stage == Stage.Ready)
                        {
                            ScheduleLocked();
                        }
                    }
                }
                else if (_activeViewers > 0)
                {
                    _activeViewers--;
                    if (_activeViewers == 0)
                    {
                        _suspendedAt = GifFrameClock.Now;
                    }
                }
            }
        }

        private void AfterPresent(long presentedDeadline)
        {
            lock (_sync)
            {
                if (_stage == Stage.Stopped)
                {
                    return;
                }

                var frames = _canvas.Image.Frames;
                if (frames.Length <= 1)
                {
                    _stage = Stage.Stopped;
                    return;
                }

                var shown = _canvas.RenderedIndex;
                _deadline = presentedDeadline + GifFrameClock.FromMilliseconds(frames[shown].DelayMs);
                _nextIndex = (shown + 1) % frames.Length;
                _stage = Stage.Composing;
                if (_activeViewers == 0)
                {
                    _suspendedAt = GifFrameClock.Now;
                }
            }

            ThreadPool.UnsafeQueueUserWorkItem(_compose, null);
        }

        private void Compose(object state)
        {
            int index;
            lock (_sync)
            {
                if (_stage != Stage.Composing)
                {
                    return;
                }

                index = _nextIndex;
            }

            try
            {
                _canvas.Render(index);
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }

            lock (_sync)
            {
                if (_stage != Stage.Composing)
                {
                    return;
                }

                _stage = Stage.Ready;
                if (_activeViewers > 0)
                {
                    ScheduleLocked();
                }
            }
        }

        private void ScheduleLocked()
        {
            var now = GifFrameClock.Now;

            // More than one frame late (a stalled dispatcher, a long suspension): restart the
            // schedule from now rather than showing the backlog in a burst.
            var frameDelay = GifFrameClock.FromMilliseconds(_canvas.Image.Frames[_canvas.RenderedIndex].DelayMs);
            if (now - _deadline > frameDelay)
            {
                _deadline = now;
            }

            GifFrameClock.Schedule(_deadline, _onClock);
        }

        private void OnClock()
        {
            lock (_sync)
            {
                // A stale entry from before a suspension, or the frame was already handed off.
                if (_stage != Stage.Ready || _activeViewers == 0 || GifFrameClock.Now < _deadline)
                {
                    return;
                }

                _stage = Stage.Posted;
            }

            _dispatcher.BeginInvoke(DispatcherPriority.Render, _present);
        }

        private void Present()
        {
            long deadline;
            lock (_sync)
            {
                if (_stage != Stage.Posted)
                {
                    return;
                }

                deadline = _deadline;
            }

            try
            {
                CopyToBitmap();
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }

            AfterPresent(deadline);
        }

        private void CopyToBitmap()
        {
            var dirty = _canvas.DirtyRect;
            if (dirty.IsEmpty)
            {
                return;
            }

            var width = _canvas.Image.Width;
            var pixels = _canvas.Pixels;
            _bitmap.Lock();
            try
            {
                var stride = _bitmap.BackBufferStride;
                var backBuffer = _bitmap.BackBuffer;
                for (var row = dirty.Y; row < dirty.Y + dirty.Height; row++)
                {
                    Marshal.Copy(
                        pixels,
                        row * width + dirty.X,
                        backBuffer + row * stride + dirty.X * 4,
                        dirty.Width);
                }

                _bitmap.AddDirtyRect(dirty);
            }
            finally
            {
                _bitmap.Unlock();
            }
        }

        private void Fail(Exception exception)
        {
            lock (_sync)
            {
                if (_stage == Stage.Stopped)
                {
                    return;
                }

                _stage = Stage.Stopped;
            }

            _dispatcher.BeginInvoke(new Action(() => Failed?.Invoke(exception)));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _stage = Stage.Stopped;
            }
        }
    }
}

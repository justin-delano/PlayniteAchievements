using PlayniteAchievements.Views.Helpers.Gif;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Attaches one Image to the shared <see cref="GifPlayer"/> for its file. Every Image showing
    /// the same file on the same UI thread displays one bitmap, decoded once at the GIF's native
    /// size. The player runs while any attached Image is active: started, not paused for a
    /// notification slide, and loaded and visible.
    /// </summary>
    internal sealed class NativeGifAnimation : IDisposable
    {
        private readonly Image _image;
        private readonly GifPlayerCache.Lease _lease;
        private readonly ImageSource _fallback;
        private readonly bool _applyGray;
        private readonly Action<Exception> _onError;
        private readonly Action _onSourceReady;
        private bool _started;
        private bool _paused;
        private bool _offscreen;
        private bool _counted;
        private bool _disposed;

        private NativeGifAnimation(
            Image image,
            string sourceIdentity,
            GifPlayerCache.Lease lease,
            ImageSource fallback,
            bool applyGray,
            Action<Exception> onError,
            Action onSourceReady)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            SourceIdentity = sourceIdentity;
            _lease = lease ?? throw new ArgumentNullException(nameof(lease));
            _fallback = fallback;
            _applyGray = applyGray;
            _onError = onError;
            _onSourceReady = onSourceReady;
        }

        internal string SourceIdentity { get; }

        internal Image Target => _image;

        internal event EventHandler Failed;

        internal static async Task<NativeGifAnimation> CreateAsync(
            Image image,
            string sourceIdentity,
            string localPath,
            ImageSource fallback,
            bool applyGray,
            CancellationToken cancellationToken,
            Action<Exception> onError = null,
            Action onSourceReady = null)
        {
            var lease = await GifPlayerCache
                .AcquireAsync(localPath, image.Dispatcher, cancellationToken)
                .ConfigureAwait(true);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new NativeGifAnimation(
                    image,
                    sourceIdentity,
                    lease,
                    fallback,
                    applyGray,
                    onError,
                    onSourceReady);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        internal void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NativeGifAnimation));
            }

            var player = _lease.Player;
            player.Failed += OnPlayerFailed;
            _image.Source = _applyGray ? player.GetGrayscaleView() : player.Bitmap;
            _started = true;
            _offscreen = !_image.IsLoaded || !_image.IsVisible;
            UpdateActivity();
            _onSourceReady?.Invoke();
        }

        /// <summary>
        /// Holds this Image out of playback for the notification slide's span. The shared player
        /// keeps running if another Image on the same file is still active.
        /// </summary>
        internal void Pause()
        {
            _paused = true;
            UpdateActivity();
        }

        internal void Resume()
        {
            _paused = false;
            UpdateActivity();
        }

        /// <summary>Unloaded or hidden Images stop counting toward playback; they keep their frame.</summary>
        internal void SetOffscreen(bool offscreen)
        {
            _offscreen = offscreen;
            UpdateActivity();
        }

        private void UpdateActivity()
        {
            var active = _started && !_disposed && !_paused && !_offscreen;
            if (active != _counted)
            {
                _counted = active;
                _lease.Player.SetViewerActive(active);
            }
        }

        private void OnPlayerFailed(Exception exception)
        {
            Fail(exception ?? new InvalidOperationException("GIF animation failed."));
        }

        private void Fail(Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            _onError?.Invoke(exception);
            Dispose();
            _image.Source = _fallback;
            _onSourceReady?.Invoke();
            Failed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Builds a live grayscale presentation over a mutable source. FormatConvertedBitmap
        /// follows subsequent WriteableBitmap invalidations; the opacity mask comes from the
        /// original BGRA source so transparent GIF pixels remain transparent.
        /// </summary>
        internal static ImageSource CreateGrayscaleView(BitmapSource source)
        {
            if (source == null)
            {
                return null;
            }

            var grayscale = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            var bounds = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            var group = new DrawingGroup
            {
                OpacityMask = new ImageBrush(source)
                {
                    Stretch = Stretch.Fill
                }
            };
            group.Children.Add(new ImageDrawing(grayscale, bounds));
            return new DrawingImage(group);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UpdateActivity();
            if (_started)
            {
                _lease.Player.Failed -= OnPlayerFailed;
            }

            _lease.Dispose();
        }
    }

    /// <summary>
    /// Shares one <see cref="GifPlayer"/> per file per UI thread. The player's bitmap belongs to
    /// the dispatcher that created it, so Images on another UI thread get their own player. The
    /// last lease released disposes the player.
    /// </summary>
    internal static class GifPlayerCache
    {
        internal sealed class Entry
        {
            internal string Key;
            internal Task<GifPlayer> CreateTask;
            internal NativeGifPayloadCache.Lease Payload;
            internal int LeaseCount;
        }

        internal sealed class Lease : IDisposable
        {
            private Entry _entry;

            internal Lease(Entry entry, GifPlayer player)
            {
                _entry = entry;
                Player = player;
            }

            internal GifPlayer Player { get; }

            public void Dispose()
            {
                var entry = Interlocked.Exchange(ref _entry, null);
                if (entry != null)
                {
                    Release(entry);
                }
            }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Must be called on <paramref name="dispatcher"/>'s thread.</summary>
        internal static async Task<Lease> AcquireAsync(
            string localPath, Dispatcher dispatcher, CancellationToken cancellationToken)
        {
            var payload = await NativeGifPayloadCache
                .AcquireAsync(localPath, cancellationToken)
                .ConfigureAwait(true);

            var key = string.Concat(payload.Key, "\u001f", dispatcher.Thread.ManagedThreadId.ToString());
            Entry entry;
            var created = false;
            lock (Sync)
            {
                if (!Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry
                    {
                        Key = key,
                        Payload = payload,
                        CreateTask = GifPlayer.CreateAsync(payload.PayloadReference, dispatcher)
                    };
                    Entries[key] = entry;
                    created = true;
                }

                entry.LeaseCount++;
            }

            if (!created)
            {
                payload.Dispose();
            }

            try
            {
                var player = await entry.CreateTask.ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                return new Lease(entry, player);
            }
            catch
            {
                Release(entry);
                throw;
            }
        }

        private static void Release(Entry entry)
        {
            lock (Sync)
            {
                entry.LeaseCount--;
                if (entry.LeaseCount > 0)
                {
                    return;
                }

                if (Entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                {
                    Entries.Remove(entry.Key);
                }
            }

            entry.Payload.Dispose();
            entry.CreateTask.ContinueWith(
                task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        task.Result.Dispose();
                    }
                },
                TaskScheduler.Default);
        }

        internal static int ActiveEntryCount
        {
            get
            {
                lock (Sync)
                {
                    return Entries.Count;
                }
            }
        }
    }

    /// <summary>
    /// Shares immutable compressed GIF bytes between active players. The file is read once into
    /// memory, so the original managed image file remains replaceable while it is on screen.
    /// </summary>
    internal static class NativeGifPayloadCache
    {
        internal sealed class Entry
        {
            internal string Key;
            internal Task<byte[]> LoadTask;
            internal int LeaseCount;
        }

        internal sealed class Lease : IDisposable
        {
            private Entry _entry;
            private readonly byte[] _bytes;

            internal Lease(Entry entry, byte[] bytes)
            {
                _entry = entry;
                _bytes = bytes;
                Key = entry.Key;
            }

            /// <summary>The file's identity: full path, length and last write time.</summary>
            internal string Key { get; }

            internal byte[] PayloadReference => _bytes;

            public void Dispose()
            {
                var entry = Interlocked.Exchange(ref _entry, null);
                if (entry != null)
                {
                    Release(entry);
                }
            }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        internal static async Task<Lease> AcquireAsync(string localPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(localPath))
            {
                throw new ArgumentException("A local GIF path is required.", nameof(localPath));
            }

            var fullPath = Path.GetFullPath(localPath);
            var key = BuildKey(fullPath);
            Entry entry;
            lock (Sync)
            {
                if (!Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry
                    {
                        Key = key,
                        LoadTask = Task.Run(() => File.ReadAllBytes(fullPath))
                    };
                    Entries[key] = entry;
                }

                entry.LeaseCount++;
            }

            try
            {
                var bytes = await entry.LoadTask.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (bytes == null || bytes.Length == 0)
                {
                    throw new InvalidDataException("The GIF file was empty.");
                }

                return new Lease(entry, bytes);
            }
            catch
            {
                Release(entry);
                throw;
            }
        }

        private static string BuildKey(string fullPath)
        {
            var info = new FileInfo(fullPath);
            return string.Concat(
                fullPath,
                "\u001f",
                info.Length.ToString(),
                "\u001f",
                info.LastWriteTimeUtc.Ticks.ToString());
        }

        private static void Release(Entry entry)
        {
            lock (Sync)
            {
                entry.LeaseCount--;
                if (entry.LeaseCount <= 0 &&
                    Entries.TryGetValue(entry.Key, out var current) &&
                    ReferenceEquals(current, entry))
                {
                    Entries.Remove(entry.Key);
                }
            }
        }

        internal static int ActiveEntryCount
        {
            get
            {
                lock (Sync)
                {
                    return Entries.Count;
                }
            }
        }
    }
}

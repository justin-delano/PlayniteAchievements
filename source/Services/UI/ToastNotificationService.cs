using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Services.UI
{
    internal sealed class ToastNotificationService : IDisposable
    {
        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly Action _ensureResourcesLoaded;
        private readonly Func<int?> _getRunningGameProcessId;
        private readonly UnlockScreenshotService _screenshotService;
        private readonly AchievementToastTemplateResolver _templateResolver;
        private readonly Queue<AchievementToastViewModel> _queue = new Queue<AchievementToastViewModel>();
        private bool _processing;
        private bool _disposed;
        private Window _activeWindow;
        // The corner the current wave uses, resolved once per wave (theme override or plugin
        // setting). Read by the per-frame positioning path so it isn't re-resolved every frame.
        private ToastScreenCorner _activePosition = ToastScreenCorner.BottomRight;

        public ToastNotificationService(
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            Action ensureResourcesLoaded,
            Func<int?> getRunningGameProcessId = null)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
            _ensureResourcesLoaded = ensureResourcesLoaded;
            _getRunningGameProcessId = getRunningGameProcessId;
            _screenshotService = new UnlockScreenshotService(logger);
            _templateResolver = new AchievementToastTemplateResolver(api, logger);
            PlayniteAchievementsPlugin.AchievementUnlocked += OnAchievementUnlocked;
        }

        private void OnAchievementUnlocked(object sender, AchievementUnlockedEventArgs e)
        {
            if (_disposed || !ShouldShow(e))
            {
                return;
            }

            var dispatcher = GetDispatcher();
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                EnqueueOnUi(e);
                return;
            }

            dispatcher.BeginInvoke(new Action(() => EnqueueOnUi(e)), DispatcherPriority.Background);
        }

        private bool ShouldShow(AchievementUnlockedEventArgs args)
        {
            if (args == null)
            {
                return false;
            }

            // Preview toasts fired from the settings panel always show, regardless of the
            // user's notification enablement toggles.
            if (args.IsPreview)
            {
                return true;
            }

            var persisted = _settings?.Persisted;
            if (persisted?.EnableNotifications != true)
            {
                return false;
            }

            return args.IsFriendUnlock
                ? persisted.EnableFriendUnlockToasts
                : persisted.EnableUnlockToasts;
        }

        private void EnqueueOnUi(AchievementUnlockedEventArgs args)
        {
            if (_disposed || !ShouldShow(args))
            {
                return;
            }

            _queue.Enqueue(new AchievementToastViewModel(args, _settings?.Persisted));
            if (!_processing)
            {
                _processing = true;
                _ = ProcessQueueAsync();
            }
        }

        /// <summary>
        /// Drops any queued (not-yet-shown) unlock toasts. Called when a game stops so stale
        /// unlocks from the session don't pop after the game has closed. Any toast already on
        /// screen finishes its animation.
        /// </summary>
        public void ClearPending()
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = GetDispatcher();
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                _queue.Clear();
                return;
            }

            dispatcher.BeginInvoke(new Action(() => _queue.Clear()), DispatcherPriority.Background);
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                await Task.Delay(125).ConfigureAwait(true);
                while (!_disposed && _queue.Count > 0)
                {
                    var wave = DequeueNextWave();
                    if (wave.Count == 0)
                    {
                        break;
                    }

                    await ShowWaveAsync(wave).ConfigureAwait(true);
                    await Task.Delay(250).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Toast notification queue failed.");
            }
            finally
            {
                _processing = false;
                if (!_disposed && _queue.Count > 0)
                {
                    _processing = true;
                    _ = ProcessQueueAsync();
                }
            }
        }

        private List<AchievementToastViewModel> DequeueNextWave()
        {
            var max = Math.Max(1, _settings?.Persisted?.MaxConcurrentToasts ?? 3);
            var result = new List<AchievementToastViewModel>(max);
            if (_queue.Count == 0)
            {
                return result;
            }

            var isFriendWave = _queue.Peek().IsFriendUnlock;
            while (_queue.Count > 0 && result.Count < max && _queue.Peek().IsFriendUnlock == isFriendWave)
            {
                result.Add(_queue.Dequeue());
            }

            return result;
        }

        private async Task ShowWaveAsync(IReadOnlyList<AchievementToastViewModel> wave)
        {
            if (wave == null || wave.Count == 0)
            {
                return;
            }

            _ensureResourcesLoaded?.Invoke();

            // Resolve the corner once for this wave: a theme override wins, otherwise the plugin
            // setting. Positioning (including the per-frame game-window follow) and slide direction
            // both read the resolved value.
            _activePosition = EffectivePosition();

            // Play the sound first, then show the toast after a short delay so the audio onset and
            // the slide-in visually align.
            PlayWaveSound(wave);
            await Task.Delay(450).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            var window = PlayniteUiProvider.CreateBorderlessTopmostWindow(
                _api,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName") ?? "Playnite Achievements");
            _activeWindow = window;

            var items = new ItemsControl
            {
                ItemsSource = wave,
                IsHitTestVisible = false
            };
            var template = _templateResolver.ResolveTemplate();
            if (template != null)
            {
                items.ItemTemplate = template;
            }

            window.Content = items;
            window.Opacity = 0;
            window.Loaded += (s, e) => PlaceWindow(window);
            window.ContentRendered += (s, e) => PlaceWindow(window);

            EventHandler onRendering = null;
            try
            {
                window.Show();
                PlaceWindow(window);
                SlideIn(window);

                // Let the toast finish sliding in and paint, then capture (so the toast is in the
                // frame), then hold for the remaining display time.
                const int captureDelayMs = 300;
                await Task.Delay(captureDelayMs).ConfigureAwait(true);
                if (_disposed)
                {
                    return;
                }

                // Release the slide-in animation so placement can move Top directly, and snap to
                // the game window corner now that the toast is fully laid out.
                window.BeginAnimation(Window.TopProperty, null);
                PlaceWindow(window);

                MaybeCaptureScreenshot(wave);

                // Follow the game window every rendered frame (smooth while dragging). The handle
                // is resolved once — for launcher games that's the foreground game window at show
                // time, which stays valid even if focus later changes.
                var gameHwnd = _screenshotService.ResolveGameWindowHandle(_getRunningGameProcessId?.Invoke());
                if (gameHwnd != IntPtr.Zero)
                {
                    onRendering = (s, e) =>
                    {
                        try
                        {
                            PlaceWindowToHandle(window, gameHwnd);
                        }
                        catch
                        {
                            // Ignore transient placement failures (e.g. window closing).
                        }
                    };
                    CompositionTarget.Rendering += onRendering;
                }

                var durationMs = EffectiveDurationSeconds() * 1000;
                var remainingMs = Math.Max(0, durationMs - captureDelayMs);
                try
                {
                    AnimateCountdownBars(window, remainingMs);
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "Toast countdown animation failed.");
                }

                await Task.Delay(remainingMs).ConfigureAwait(true);

                if (onRendering != null)
                {
                    CompositionTarget.Rendering -= onRendering;
                    onRendering = null;
                }

                SlideOut(window);
                await Task.Delay(210).ConfigureAwait(true);
            }
            finally
            {
                if (onRendering != null)
                {
                    CompositionTarget.Rendering -= onRendering;
                }

                try
                {
                    window.Close();
                }
                catch
                {
                }

                if (ReferenceEquals(_activeWindow, window))
                {
                    _activeWindow = null;
                }
            }
        }

        /// <summary>
        /// Fires a single UniPlaySong sound for the wave, using the rarest tier present so a burst
        /// of unlocks does not stack overlapping sounds. UniPlaySong owns enablement and audio
        /// selection for the "playniteachievements/&lt;tier&gt;" URI; if it is not installed the URI
        /// is unhandled and the call is ignored.
        /// </summary>
        private void PlayWaveSound(IReadOnlyList<AchievementToastViewModel> wave)
        {
            var tier = wave?
                .OrderByDescending(vm => vm.SoundTierRank)
                .Select(vm => vm.SoundTierSegment)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tier))
            {
                return;
            }

            try
            {
                Process.Start($"playnite://uniplaysong/playniteachievements/{tier}");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Toast unlock sound URI could not be launched.");
            }
        }

        /// <summary>
        /// For your own (non-preview) unlock waves, captures the game monitor once (toast is on
        /// screen at this point) and saves the same image to one file per achievement in the wave.
        /// The GDI blit runs on the UI thread; the disk writes are offloaded so file I/O never
        /// blocks the toast.
        /// </summary>
        private void MaybeCaptureScreenshot(IReadOnlyList<AchievementToastViewModel> wave)
        {
            if (wave == null || wave.Count == 0)
            {
                return;
            }

            var first = wave[0];
            if (first.IsPreview || first.IsFriendUnlock)
            {
                return;
            }

            var persisted = _settings?.Persisted;
            var baseDir = persisted?.UnlockScreenshotDirectory;
            if (persisted?.EnableUnlockScreenshots != true || string.IsNullOrWhiteSpace(baseDir))
            {
                return;
            }

            var bitmap = _screenshotService.CaptureGameWindow(_getRunningGameProcessId?.Invoke());
            if (bitmap == null)
            {
                return;
            }

            var targets = wave
                .Select(vm => new
                {
                    vm.ProviderKey,
                    vm.GameName,
                    vm.AchievementName,
                    vm.AchievementNumber,
                    vm.TotalCount
                })
                .ToList();

            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var t in targets)
                    {
                        _screenshotService.Save(
                            bitmap,
                            baseDir,
                            t.ProviderKey,
                            t.GameName,
                            t.AchievementName,
                            t.AchievementNumber,
                            t.TotalCount);
                    }
                }
                finally
                {
                    bitmap.Dispose();
                }
            });
        }

        private void PlaceWindow(Window window)
        {
            if (window != null)
            {
                PositionInArea(window, ResolvePlacementArea(window));
            }
        }

        /// <summary>
        /// Repositions the toast within a known game window handle (cheap per-frame path used while
        /// following the game window). Leaves the toast where it is if the handle can't be measured.
        /// </summary>
        private void PlaceWindowToHandle(Window window, IntPtr gameHwnd)
        {
            if (window == null || gameHwnd == IntPtr.Zero)
            {
                return;
            }

            if (_screenshotService.TryGetClientBounds(gameHwnd, out var pixelBounds))
            {
                var dip = ConvertPhysicalToDip(window, pixelBounds);
                if (dip.Width > 0 && dip.Height > 0)
                {
                    PositionInArea(window, dip);
                }
            }
        }

        private void PositionInArea(Window window, Rect area)
        {
            if (window == null)
            {
                return;
            }

            var margin = 24d;
            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(width) || width <= 0) width = 410;
            if (double.IsNaN(height) || height <= 0) height = 138;

            switch (_activePosition)
            {
                case ToastScreenCorner.TopLeft:
                    window.Left = area.Left + margin;
                    window.Top = area.Top + margin;
                    break;
                case ToastScreenCorner.TopRight:
                    window.Left = area.Right - width - margin;
                    window.Top = area.Top + margin;
                    break;
                case ToastScreenCorner.BottomLeft:
                    window.Left = area.Left + margin;
                    window.Top = area.Bottom - height - margin;
                    break;
                case ToastScreenCorner.BottomRight:
                default:
                    window.Left = area.Right - width - margin;
                    window.Top = area.Bottom - height - margin;
                    break;
            }
        }

        /// <summary>
        /// The rectangle (in WPF device-independent units) the toast is positioned within: the
        /// running game's window when one is resolvable, otherwise the primary work area. Clamping
        /// to the game window keeps the toast over the game and inside the game-window screenshot.
        /// </summary>
        private Rect ResolvePlacementArea(Window window)
        {
            var pixelBounds = _screenshotService?.TryGetGameWindowBounds(_getRunningGameProcessId?.Invoke());
            if (pixelBounds.HasValue)
            {
                var dip = ConvertPhysicalToDip(window, pixelBounds.Value);
                if (dip.Width > 0 && dip.Height > 0)
                {
                    return dip;
                }
            }

            return SystemParameters.WorkArea;
        }

        private static Rect ConvertPhysicalToDip(Window window, System.Drawing.Rectangle rect)
        {
            try
            {
                var target = PresentationSource.FromVisual(window)?.CompositionTarget;
                if (target != null)
                {
                    var transform = target.TransformFromDevice;
                    var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
                    var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));
                    return new Rect(topLeft, bottomRight);
                }
            }
            catch
            {
                // Fall through to raw (assume 1:1 device scale).
            }

            return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        private void SlideIn(Window window)
        {
            if (window == null)
            {
                return;
            }

            var resting = window.Top;
            var distance = SlideDistance(window);
            var start = SlideFromBottom() ? resting + distance : resting - distance;

            window.Opacity = 1;
            // Slight overshoot so the toast pops past its resting point and settles. The easing and
            // duration come from the themeable PlayAch.Storyboard.ToastSlideIn storyboard; the fallback
            // keeps the original feel if a theme override is absent or malformed.
            RunTopSlide(
                window,
                AchievementToastTemplateResolver.SlideInStoryboardKey,
                start,
                resting,
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
                240);
        }

        private void SlideOut(Window window)
        {
            if (window == null)
            {
                return;
            }

            var resting = window.Top;
            var distance = SlideDistance(window);
            var end = SlideFromBottom() ? resting + distance : resting - distance;

            RunTopSlide(
                window,
                AchievementToastTemplateResolver.SlideOutStoryboardKey,
                resting,
                end,
                new CubicEase { EasingMode = EasingMode.EaseIn },
                200);
        }

        private static double SlideDistance(Window window)
        {
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(height) || height <= 0)
            {
                height = 138;
            }

            return height + 40;
        }

        private bool SlideFromBottom()
        {
            switch (_activePosition)
            {
                case ToastScreenCorner.TopLeft:
                case ToastScreenCorner.TopRight:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// The corner the toast uses: a theme override (string resource
        /// <see cref="AchievementToastTemplateResolver.PositionResourceKey"/>, e.g. "TopRight") when
        /// present and valid, otherwise the plugin's ToastPosition setting.
        /// </summary>
        private ToastScreenCorner EffectivePosition()
        {
            var setting = _settings?.Persisted?.ToastPosition ?? ToastScreenCorner.BottomRight;
            try
            {
                var raw = _templateResolver?.ResolveResourceValue(
                    AchievementToastTemplateResolver.PositionResourceKey);
                var text = raw?.ToString().Trim();
                if (!string.IsNullOrEmpty(text) &&
                    Enum.TryParse(text, ignoreCase: true, result: out ToastScreenCorner parsed) &&
                    Enum.IsDefined(typeof(ToastScreenCorner), parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to resolve toast position theme override.");
            }

            return setting;
        }

        /// <summary>
        /// The toast display time in seconds: a theme override (numeric or string resource
        /// <see cref="AchievementToastTemplateResolver.DurationSecondsResourceKey"/>) when present and
        /// valid, otherwise the plugin's ToastDurationSeconds setting. Clamped to a 2s minimum to
        /// match the setting's own clamp.
        /// </summary>
        private int EffectiveDurationSeconds()
        {
            var setting = Math.Max(2, _settings?.Persisted?.ToastDurationSeconds ?? 6);
            try
            {
                var raw = _templateResolver?.ResolveResourceValue(
                    AchievementToastTemplateResolver.DurationSecondsResourceKey);
                if (raw is double d)
                {
                    return Math.Max(2, (int)Math.Round(d));
                }

                if (raw is int i)
                {
                    return Math.Max(2, i);
                }

                var text = raw?.ToString().Trim();
                if (!string.IsNullOrEmpty(text) &&
                    double.TryParse(
                        text,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return Math.Max(2, (int)Math.Round(parsed));
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to resolve toast duration theme override.");
            }

            return setting;
        }

        /// <summary>
        /// Animates every countdown bar in the wave (one per toast) from full to empty over the
        /// display duration, so it reads as an auto-dismiss timer. The animation's shape (from/to,
        /// easing) comes from the themeable PlayAch.Storyboard.ToastCountdown storyboard; its duration
        /// is always the toast's runtime display time so the bar depletes exactly as it dismisses.
        /// </summary>
        private void AnimateCountdownBars(DependencyObject root, int milliseconds)
        {
            var duration = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
            foreach (var bar in FindCountdownBars(root))
            {
                // Assign a fresh, mutable ScaleTransform: a Freezable declared inline in a
                // DataTemplate is frozen/shared, and BeginAnimation on a frozen transform throws.
                var scale = new ScaleTransform(1.0, 1.0);
                bar.RenderTransform = scale;

                var animation = ResolveAnimation(AchievementToastTemplateResolver.CountdownStoryboardKey)
                    ?? new DoubleAnimation(1.0, 0.0, duration) { FillBehavior = FillBehavior.HoldEnd };
                // The countdown must track the actual display time, so the runtime duration always
                // wins over whatever placeholder the storyboard authored.
                animation.Duration = duration;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            }
        }

        private static IEnumerable<FrameworkElement> FindCountdownBars(DependencyObject root)
        {
            var results = new List<FrameworkElement>();
            if (root == null)
            {
                return results;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement element &&
                    string.Equals(element.Name, "PART_ToastCountdown", StringComparison.Ordinal))
                {
                    results.Add(element);
                }

                results.AddRange(FindCountdownBars(child));
            }

            return results;
        }

        /// <summary>
        /// Slides the window's Top between two positions using the animation authored in the given
        /// themeable storyboard (easing and duration), patching only the runtime start/end positions.
        /// Falls back to a code-built animation with the supplied easing/duration when no storyboard
        /// resource resolves, so a missing or malformed theme override never breaks the toast. Applied
        /// via BeginAnimation (not Storyboard.Begin) so the existing release step
        /// (window.BeginAnimation(Window.TopProperty, null)) and per-frame repositioning are unchanged.
        /// </summary>
        private void RunTopSlide(
            Window window,
            string storyboardKey,
            double from,
            double to,
            IEasingFunction fallbackEasing,
            int fallbackMilliseconds)
        {
            if (window == null)
            {
                return;
            }

            var animation = ResolveAnimation(storyboardKey) ?? new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(fallbackMilliseconds),
                FillBehavior = FillBehavior.HoldEnd,
                EasingFunction = fallbackEasing
            };

            animation.From = from;
            animation.To = to;
            window.BeginAnimation(Window.TopProperty, animation);
        }

        /// <summary>
        /// Resolves the first <see cref="DoubleAnimation"/> from a themeable toast storyboard and
        /// returns a detached, mutable clone the caller can patch (from/to/duration) and apply. Returns
        /// null when no storyboard resolves or it declares no DoubleAnimation, signalling the caller to
        /// use its code-built fallback. Only the first DoubleAnimation is used; the window slide and
        /// countdown each drive a single property.
        /// </summary>
        private DoubleAnimation ResolveAnimation(string storyboardKey)
        {
            try
            {
                var storyboard = _templateResolver?.ResolveStoryboard(storyboardKey);
                var animation = storyboard == null ? null : GetFirstDoubleAnimation(storyboard);
                return (DoubleAnimation)animation?.Clone();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to resolve toast storyboard '{storyboardKey}'.");
                return null;
            }
        }

        private static DoubleAnimation GetFirstDoubleAnimation(Storyboard storyboard)
        {
            foreach (var child in storyboard.Children)
            {
                if (child is DoubleAnimation animation)
                {
                    return animation;
                }
            }

            return null;
        }

        private Dispatcher GetDispatcher()
        {
            return _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
        }

        public void Dispose()
        {
            _disposed = true;
            PlayniteAchievementsPlugin.AchievementUnlocked -= OnAchievementUnlocked;
            _queue.Clear();
            try
            {
                _activeWindow?.Close();
            }
            catch
            {
            }
        }
    }
}

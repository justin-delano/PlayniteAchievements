// Prices the notification's hold phase - the seconds the card rests on screen - and checks that
// cheaper layerings look the same.
//
//   HoldProbe.exe [--card-width 2328] [--card-height 496] [--glow 72] [--repeats 5] [--seconds 4]
//                 [--load N] [--only A,B] [--shape notch|rect] [--fps 50]
//
// The card mirrors the bundled AchievementToast.xaml: a glow layer carrying the DropShadowEffect
// (pulsed through Effect.Opacity by an animation clock, as RarityGlowPulse does with
// Target="Effect") over an invisible-surface caster and a backing copy of the background, then an
// effect-free content layer with the visible background copy and text lines carrying the nested
// text shadows. The background is a synthetic animated bitmap updated like GifPlayer updates its
// WriteableBitmap: whole frames copied in on the UI thread at Render priority on a fixed cadence.
// Its alpha is binary and the same in every frame, as a GIF's is unless its transparency moves.
// The window is DWM-composited, as revealed notifications are.
//
// Each variant is measured for the composition rate the UI thread sustained, DWM's own composed
// frames, and this process's CPU time (UI plus render thread). Variants meant to look identical are
// then frozen at the same background frame and pulse value and compared on screen against Shipped.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class HoldProbe
{
    private enum Variant
    {
        /// <summary>As shipped: the animated background is drawn inside the glow effect too.</summary>
        Shipped,

        /// <summary>The backing copy inside the effect is a still of one frame; the visible copy animates.</summary>
        FrozenBacking,

        /// <summary>
        /// FrozenBacking, with the glow layer cached (BitmapCache) at a fixed effect opacity and the
        /// pulse moved to the layer's own Opacity, which composites without re-running the effect.
        /// </summary>
        CachedGlow,

        /// <summary>Shipped with the pulse off. Prices the pulse; not a candidate.</summary>
        PulseOff,

        /// <summary>Shipped with the background not animating. Prices the animation; not a candidate.</summary>
        StaticBackground,

        /// <summary>No glow layer at all. Prices the glow; not a candidate.</summary>
        NoGlow,

        /// <summary>Shipped with the text lines' shadows removed. Prices them; not a candidate.</summary>
        NoTextShadow,

        /// <summary>
        /// CachedGlow, with each text line (and its shadow pair) cached too, so a composed frame
        /// that changes only the pulse or the background re-runs no text effect.
        /// </summary>
        CachedGlowText,

        /// <summary>Shipped, with only the text lines cached.</summary>
        CachedText,

        /// <summary>
        /// FrozenBacking, with the glow layer moved to its own window directly behind the card
        /// window. WPF tracks redraws per window, so a pulse step redraws only the glow window and
        /// the card's text shadows re-run only when the background changes.
        /// </summary>
        SplitGlow,

        /// <summary>
        /// SplitGlow, with the text lines in a third window in front of the card window. Nothing in
        /// that window changes after its first frame, so the text shadows are computed once.
        /// </summary>
        SplitAll,

        /// <summary>SplitAll with the backing copy left animating, for backgrounds whose alpha is not binary.</summary>
        SplitAllLive,
    }

    private static readonly Variant[] Candidates =
    {
        Variant.FrozenBacking, Variant.CachedGlow, Variant.CachedText, Variant.CachedGlowText, Variant.SplitGlow, Variant.SplitAll, Variant.SplitAllLive,
    };

    private static bool CachesGlow(Variant v) => v == Variant.CachedGlow || v == Variant.CachedGlowText;

    private static bool CachesText(Variant v) => v == Variant.CachedText || v == Variant.CachedGlowText;

    private static bool SplitsGlow(Variant v) => v == Variant.SplitGlow || v == Variant.SplitAll || v == Variant.SplitAllLive;

    private const double PulseMin = 0.35;
    private const double PulseMax = 1.0;
    private const double PulseSeconds = 1.5;
    private const double FrozenPulse = 0.6;
    private const int FrozenFrame = 5;
    private const int FrameCount = 12;

    private static double _cardWidth = 2328;
    private static double _cardHeight = 496;
    private static double _glow = 72;
    private static int _repeats = 5;
    private static double _seconds = 4;
    private static int _load;
    private static bool _notch = true;
    private static bool _soft;
    // The card's LayoutTransform, as the plugin's fit and DPI compensation apply one; a fractional
    // value is where a cached layer would resample.
    private static double _scale = 1;
    private static string _dumpDir;
    private static bool _compareOnly;
    private static double _fps = 50;
    private static HashSet<string> _only;
    private static double _periodMs;

    [STAThread]
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--loadwindow") >= 0)
        {
            RunLoadWindow();
            return 0;
        }

        for (var i = 0; i < args.Length; i++)
        {
            Func<double> next = () => double.Parse(args[++i], CultureInfo.InvariantCulture);
            switch (args[i])
            {
                case "--card-width": _cardWidth = next(); break;
                case "--card-height": _cardHeight = next(); break;
                case "--glow": _glow = next(); break;
                case "--repeats": _repeats = Math.Max(1, (int)next()); break;
                case "--seconds": _seconds = Math.Max(1, next()); break;
                case "--load": _load = Math.Max(1, (int)next()); break;
                case "--fps": _fps = Math.Max(1, next()); break;
                case "--shape": var shape = args[++i]; _notch = shape == "notch"; _soft = shape == "soft"; break;
                case "--scale": _scale = next(); break;
                case "--dump": _dumpDir = args[++i]; break;
                case "--compare-only": _compareOnly = true; break;
                case "--only":
                    _only = new HashSet<string>(args[++i].Split(','), StringComparer.OrdinalIgnoreCase);
                    break;
            }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var loads = new List<Process>();
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                _periodMs = DisplayPeriodMs();
                Console.WriteLine(
                    "Display composes every {0:0.00} ms ({1:0.0} Hz). Card {2}x{3}, glow {4}, background {5} fps, shape {6}.",
                    _periodMs, 1000 / _periodMs, _cardWidth, _cardHeight, _glow, _fps, _soft ? "soft" : (_notch ? "notch" : "rect"));
                if (_load > 0)
                {
                    var exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                    for (var i = 0; i < _load; i++)
                    {
                        loads.Add(Process.Start(new ProcessStartInfo(exe, "--loadwindow") { UseShellExecute = false }));
                    }

                    Console.WriteLine("GPU load: {0} child render process(es).", _load);
                    await Task.Delay(2000);
                }

                Console.WriteLine();
                await RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + ex);
            }
            finally
            {
                foreach (var p in loads)
                {
                    try { p.Kill(); } catch { }
                }

                app.Shutdown();
            }
        }));
        app.Run();
        return 0;
    }

    private sealed class Result
    {
        public double TickHz;
        public double DwmHz;
        public double CpuMsPerSecond;
        public double BackgroundHz;
    }

    private static async Task RunAsync()
    {
        var frames = BuildFrames();
        var results = new Dictionary<Variant, List<Result>>();
        foreach (Variant variant in Enum.GetValues(typeof(Variant)))
        {
            if (_only != null && !_only.Contains(variant.ToString()))
            {
                continue;
            }

            results[variant] = new List<Result>();
        }

        // Interleaved so drift in the machine's state spreads across every variant.
        for (var run = 0; run < (_compareOnly ? 0 : _repeats); run++)
        {
            foreach (var variant in results.Keys.ToList())
            {
                results[variant].Add(await MeasureAsync(variant, frames));
            }
        }

        Console.WriteLine(
            "{0,-17} {1,9} {2,8} {3,9} {4,11} {5,8}",
            "variant", "tickHz", "% max", "dwmHz", "cpu ms/s", "bgHz");
        foreach (var pair in results.Where(p => p.Value.Count > 0))
        {
            var median = Median(pair.Value);
            Console.WriteLine(
                "{0,-17} {1,9:0.0} {2,7:0}% {3,9:0.0} {4,11:0.0} {5,8:0.0}",
                pair.Key, median.TickHz, 100 * median.TickHz * _periodMs / 1000, median.DwmHz,
                median.CpuMsPerSecond, median.BackgroundHz);
        }

        Console.WriteLine();
        Console.WriteLine("  tickHz: composed frames the UI thread got per second; dwmHz: DWM's own composed");
        Console.WriteLine("  frames per second; cpu: this process's CPU time per wall second (UI + render");
        Console.WriteLine("  thread); bgHz: background frames presented per second. Medians of the runs.");

        if (results.ContainsKey(Variant.Shipped))
        {
            Console.WriteLine();
            // A solid backdrop under every frozen capture: transparent areas of the card would
            // otherwise read whatever is behind it, such as this console as it prints.
            var backdrop = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0x90)),
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
                Width = (_cardWidth + (2 * _glow)) * _scale + 200,
                Height = (_cardHeight + (2 * _glow)) * _scale + 200,
            };
            backdrop.Show();

            // Shipped against itself first: the floor any capture-to-capture difference sits on.
            var reference = await CaptureFrozenAsync(Variant.Shipped, frames);
            Compare(Variant.Shipped, reference, await CaptureFrozenAsync(Variant.Shipped, frames));
            foreach (var candidate in Candidates.Where(results.ContainsKey))
            {
                var pixels = await CaptureFrozenAsync(candidate, frames);
                Compare(candidate, reference, pixels);
            }

            backdrop.Close();
        }
    }

    private static Result Median(List<Result> runs)
    {
        Func<Func<Result, double>, double> mid = f =>
        {
            var sorted = runs.Select(f).OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        };
        return new Result
        {
            TickHz = mid(r => r.TickHz),
            DwmHz = mid(r => r.DwmHz),
            CpuMsPerSecond = mid(r => r.CpuMsPerSecond),
            BackgroundHz = mid(r => r.BackgroundHz),
        };
    }

    private static async Task<Result> MeasureAsync(Variant variant, BitmapSource[] frames)
    {
        Card card = null;
        var window = BuildWindow(variant, frames, out card);
        try
        {
            card.Behind?.Show();
            window.Show();
            card.Front?.Show();
            await WaitFrames(10);
            card.Start(animate: variant != Variant.StaticBackground);
            await Task.Delay(1000);

            var ticks = 0;
            var last = TimeSpan.Zero;
            EventHandler count = (s, e) =>
            {
                var t = ((RenderingEventArgs)e).RenderingTime;
                if (t != last)
                {
                    last = t;
                    ticks++;
                }
            };

            var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpu0 = process.TotalProcessorTime;
            var dwm0 = DwmFrameCount();
            var bg0 = card.Presented;
            var clock = Stopwatch.StartNew();
            CompositionTarget.Rendering += count;
            await Task.Delay(TimeSpan.FromSeconds(_seconds));
            CompositionTarget.Rendering -= count;
            var wall = clock.Elapsed.TotalSeconds;
            process.Refresh();
            var cpu = (process.TotalProcessorTime - cpu0).TotalMilliseconds;

            return new Result
            {
                TickHz = ticks / wall,
                DwmHz = (DwmFrameCount() - dwm0) / wall,
                CpuMsPerSecond = cpu / wall,
                BackgroundHz = (card.Presented - bg0) / wall,
            };
        }
        finally
        {
            card.Stop();
            window.Close();
            card.Behind?.Close();
            card.Front?.Close();
            await Task.Delay(300);
        }
    }

    /// <summary>
    /// Shows <paramref name="variant"/> with its background held at one frame and its pulse held at
    /// one value, and reads the card back from the screen.
    /// </summary>
    private static async Task<int[]> CaptureFrozenAsync(Variant variant, BitmapSource[] frames)
    {
        Card card = null;
        var window = BuildWindow(variant, frames, out card);
        try
        {
            window.Left = 40;
            window.Top = 40;
            card.Behind?.Show();
            window.Show();
            card.Front?.Show();
            card.Freeze(FrozenFrame, FrozenPulse);
            await WaitFrames(10);
            await Task.Delay(400);

            var topLeft = window.PointToScreen(new Point(0, 0));
            var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
            var width = (int)Math.Round(bottomRight.X - topLeft.X);
            var height = (int)Math.Round(bottomRight.Y - topLeft.Y);
            using (var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, new System.Drawing.Size(width, height));
                }

                var data = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var pixels = new int[width * height + 2];
                Marshal.Copy(data.Scan0, pixels, 2, width * height);
                bitmap.UnlockBits(data);
                pixels[0] = width;
                pixels[1] = height;
                return pixels;
            }
        }
        finally
        {
            card.Stop();
            window.Close();
            card.Behind?.Close();
            card.Front?.Close();
            await Task.Delay(300);
        }
    }

    private static void Compare(Variant variant, int[] reference, int[] candidate)
    {
        if (reference[0] != candidate[0] || reference[1] != candidate[1])
        {
            Console.WriteLine("{0,-17} size differs: {1}x{2} vs {3}x{4}", variant, candidate[0], candidate[1], reference[0], reference[1]);
            return;
        }

        var differing = 0;
        var maxDelta = 0;
        var over2 = 0;
        for (var i = 2; i < reference.Length; i++)
        {
            if (reference[i] == candidate[i])
            {
                continue;
            }

            differing++;
            var delta = 0;
            for (var shift = 0; shift < 24; shift += 8)
            {
                delta = Math.Max(delta, Math.Abs(((reference[i] >> shift) & 0xFF) - ((candidate[i] >> shift) & 0xFF)));
            }

            maxDelta = Math.Max(maxDelta, delta);
            if (delta > 2)
            {
                over2++;
            }
        }

        Console.WriteLine(
            "{0,-17} vs Shipped on screen: {1} of {2} pixels differ, {3} by more than 2 levels, max {4} levels",
            variant, differing, reference.Length - 2, over2, maxDelta);

        if (_dumpDir != null)
        {
            System.IO.Directory.CreateDirectory(_dumpDir);
            var width = reference[0];
            var height = reference[1];
            var diff = new int[reference.Length];
            for (var i = 2; i < reference.Length; i++)
            {
                var delta = 0;
                for (var shift = 0; shift < 24; shift += 8)
                {
                    delta = Math.Max(delta, Math.Abs(((reference[i] >> shift) & 0xFF) - ((candidate[i] >> shift) & 0xFF)));
                }

                // Any difference shows: brighter red for larger, grey where equal to keep the layout readable.
                diff[i] = delta == 0
                    ? unchecked((int)0xFF202020)
                    : unchecked((int)0xFF000000) | (Math.Min(255, 64 + (delta * 8)) << 16);
            }

            Save(System.IO.Path.Combine(_dumpDir, variant + "_candidate.png"), candidate, width, height);
            Save(System.IO.Path.Combine(_dumpDir, variant + "_diff.png"), diff, width, height);
            Save(System.IO.Path.Combine(_dumpDir, "Shipped_reference.png"), reference, width, height);
        }
    }

    private static void Save(string path, int[] pixels, int width, int height)
    {
        using (var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            var data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 2, data.Scan0, width * height);
            bitmap.UnlockBits(data);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    /// <summary>The card and its moving parts for one variant.</summary>
    private sealed class Card
    {
        public WriteableBitmap Live;
        public BitmapSource[] Frames;
        public DropShadowEffect Glow;
        public UIElement PulseElement;
        public bool PulseOnEffect;
        public bool Pulse;
        public int Presented;
        public Window Behind;
        public Window Front;
        private Thread _thread;
        private volatile bool _running;
        private int _index;
        private AnimationClock _clock;

        public void Start(bool animate)
        {
            if (Pulse)
            {
                var animation = new DoubleAnimation(PulseMin, PulseMax, TimeSpan.FromSeconds(PulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                _clock = animation.CreateClock();
                if (PulseOnEffect)
                {
                    Glow.ApplyAnimationClock(DropShadowEffect.OpacityProperty, _clock);
                }
                else
                {
                    PulseElement.ApplyAnimationClock(UIElement.OpacityProperty, _clock);
                }
            }

            if (!animate)
            {
                return;
            }

            // Like GifFrameClock: deadlines chained off a Stopwatch on one thread, the copy into the
            // shared bitmap handed to the UI thread at Render priority.
            var dispatcher = Dispatcher.CurrentDispatcher;
            _running = true;
            _thread = new Thread(() =>
            {
                var period = 1000.0 / _fps;
                var clock = Stopwatch.StartNew();
                var due = period;
                while (_running)
                {
                    var wait = due - clock.Elapsed.TotalMilliseconds;
                    if (wait > 1)
                    {
                        Thread.Sleep((int)wait);
                        continue;
                    }

                    due += period;
                    dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        if (_running)
                        {
                            _index = (_index + 1) % Frames.Length;
                            CopyFrame(Frames[_index], Live);
                            Presented++;
                        }
                    }));
                }
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Freeze(int frame, double pulse)
        {
            CopyFrame(Frames[frame], Live);
            if (Glow != null)
            {
                Glow.Opacity = PulseOnEffect ? pulse : Glow.Opacity;
            }

            if (!PulseOnEffect && PulseElement != null)
            {
                PulseElement.Opacity = pulse;
            }
        }

        public void Stop()
        {
            _running = false;
            if (_clock != null)
            {
                _clock.Controller?.Stop();
            }
        }
    }

    private static readonly Dictionary<BitmapSource, byte[]> FramePixels = new Dictionary<BitmapSource, byte[]>();

    private static void CopyFrame(BitmapSource frame, WriteableBitmap target)
    {
        var stride = target.PixelWidth * 4;
        if (!FramePixels.TryGetValue(frame, out var pixels))
        {
            pixels = new byte[stride * target.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            FramePixels[frame] = pixels;
        }

        target.WritePixels(new Int32Rect(0, 0, target.PixelWidth, target.PixelHeight), pixels, stride, 0);
    }

    private static Window BuildWindow(Variant variant, BitmapSource[] frames, out Card card)
    {
        var live = new WriteableBitmap(frames[0]);
        card = new Card
        {
            Live = live,
            Frames = frames,
            Pulse = variant != Variant.PulseOff && variant != Variant.NoGlow,
            PulseOnEffect = !CachesGlow(variant),
        };

        var root = new Grid { Width = _cardWidth, Height = _cardHeight, Margin = new Thickness(_glow) };
        var outerRoot = new Grid
        {
            LayoutTransform = Math.Abs(_scale - 1) > 1e-6 ? new ScaleTransform(_scale, _scale) : null,
        };
        outerRoot.Children.Add(root);

        if (variant != Variant.NoGlow)
        {
            var glow = new DropShadowEffect
            {
                BlurRadius = _glow,
                ShadowDepth = 0,
                Color = Color.FromRgb(0xFF, 0xB0, 0x30),
                Opacity = PulseMax,
                RenderingBias = RenderingBias.Performance,
            };
            card.Glow = glow;

            // The backing copy: the background's own shape for the glow to wrap. Inside the effect,
            // so it renders through the effect's intermediate.
            var backingSource = variant == Variant.FrozenBacking || variant == Variant.SplitGlow || variant == Variant.SplitAll || CachesGlow(variant)
                ? (ImageSource)frames[0]
                : live;
            var glowLayer = new Grid { Effect = glow };
            glowLayer.Children.Add(new Border { CornerRadius = new CornerRadius(8) });
            glowLayer.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new ImageBrush(backingSource) { Stretch = Stretch.UniformToFill },
            });

            if (CachesGlow(variant))
            {
                glowLayer.CacheMode = new BitmapCache { RenderAtScale = 1, SnapsToDevicePixels = false };
                card.PulseElement = glowLayer;
            }

            if (SplitsGlow(variant))
            {
                // Same geometry as the card window, so the two line up pixel for pixel.
                var glowRoot = new Grid { Width = _cardWidth, Height = _cardHeight, Margin = new Thickness(_glow) };
                glowRoot.Children.Add(glowLayer);
                var glowOuter = new Grid
                {
                    LayoutTransform = Math.Abs(_scale - 1) > 1e-6 ? new ScaleTransform(_scale, _scale) : null,
                };
                glowOuter.Children.Add(glowRoot);
                card.Behind = MakeWindow(glowOuter);
            }
            else
            {
                root.Children.Add(glowLayer);
            }
        }

        // The content layer: no effect of its own, the visible background, and text with the
        // template's nested shadow pair per line.
        var content = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true };
        var contentGrid = new Grid();
        contentGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new ImageBrush(live) { Stretch = Stretch.UniformToFill },
        });
        var lines = new StackPanel { Margin = new Thickness(_cardHeight * 0.6, _cardHeight * 0.12, 20, 0) };
        foreach (var text in new[] { "Achievement unlocked", "A rather long achievement name here", "Description line for the card", "Game name - 4.2% of players" })
        {
            var shadows = variant != Variant.NoTextShadow;
            var inner = new TextBlock
            {
                Text = text,
                FontSize = _cardHeight * 0.13,
                Foreground = Brushes.White,
                Effect = shadows ? new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.9 } : null,
            };
            var outer = new Grid
            {
                Effect = shadows ? new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 1 } : null,
            };
            if (CachesText(variant))
            {
                outer.CacheMode = new BitmapCache { RenderAtScale = 1, SnapsToDevicePixels = false };
            }

            outer.Children.Add(inner);
            lines.Children.Add(outer);
        }

        if (variant == Variant.SplitAll || variant == Variant.SplitAllLive)
        {
            // Same geometry and clip as the content layer, without the background.
            var textClip = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = lines };
            var textRoot = new Grid { Width = _cardWidth, Height = _cardHeight, Margin = new Thickness(_glow) };
            textRoot.Children.Add(textClip);
            var textOuter = new Grid
            {
                LayoutTransform = Math.Abs(_scale - 1) > 1e-6 ? new ScaleTransform(_scale, _scale) : null,
            };
            textOuter.Children.Add(textRoot);
            card.Front = MakeWindow(textOuter);
        }
        else
        {
            contentGrid.Children.Add(lines);
        }

        content.Child = contentGrid;
        root.Children.Add(content);

        return MakeWindow(outerRoot);
    }

    private static Window MakeWindow(FrameworkElement content)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = Brushes.Transparent,
            UseLayoutRounding = true,
            Left = 40,
            Top = 40,
            Content = content,
        };
        window.SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        };
        return window;
    }

    /// <summary>
    /// Synthetic background frames: a moving color field with a fixed binary alpha shape (rounded
    /// corners and, for the notch shape, a transparent bite out of the right edge).
    /// </summary>
    private static BitmapSource[] BuildFrames()
    {
        var width = (int)_cardWidth;
        var height = (int)_cardHeight;
        var stride = width * 4;
        var frames = new BitmapSource[FrameCount];
        var radius = height * 0.15;
        for (var f = 0; f < FrameCount; f++)
        {
            var pixels = new byte[stride * height];
            var phase = 2 * Math.PI * f / FrameCount;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var coverage = Coverage(x, y, width, height, radius);
                    var i = (y * stride) + (x * 4);
                    if (coverage <= 0)
                    {
                        continue;
                    }

                    // Premultiplied, as Pbgra32 stores it.
                    var u = (double)x / width;
                    var v = (double)y / height;
                    pixels[i] = (byte)Math.Round(coverage * (128 + (100 * Math.Sin((u * 9) + phase))));
                    pixels[i + 1] = (byte)Math.Round(coverage * (128 + (100 * Math.Sin((v * 7) - phase))));
                    pixels[i + 2] = (byte)Math.Round(coverage * (128 + (100 * Math.Sin(((u + v) * 5) + (2 * phase)))));
                    pixels[i + 3] = (byte)Math.Round(coverage * 255);
                }
            }

            var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            frame.Freeze();
            frames[f] = frame;
        }

        return frames;
    }

    /// <summary>
    /// How opaque the background is at a pixel, the same in every frame. Binary for notch and rect,
    /// as a GIF's alpha is; the soft shape fades over a band at its edge, as a translucent WebM can.
    /// </summary>
    private static double Coverage(int x, int y, int width, int height, double radius)
    {
        if (!_soft)
        {
            return InsideShape(x, y, width, height, radius) ? 1d : 0d;
        }

        const double band = 48;
        var cx = x < radius ? radius : (x > width - radius ? width - radius : x);
        var cy = y < radius ? radius : (y > height - radius ? height - radius : y);
        var dx = x - cx;
        var dy = y - cy;
        var inset = radius - Math.Sqrt((dx * dx) + (dy * dy));
        var edge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
        var depth = Math.Min(inset, edge);
        return depth <= 0 ? 0d : Math.Min(1d, depth / band);
    }

    private static bool InsideShape(int x, int y, int width, int height, double radius)
    {
        var cx = x < radius ? radius : (x > width - radius ? width - radius : x);
        var cy = y < radius ? radius : (y > height - radius ? height - radius : y);
        var dx = x - cx;
        var dy = y - cy;
        if ((dx * dx) + (dy * dy) > radius * radius)
        {
            return false;
        }

        if (_notch)
        {
            var nx = x - (width * 0.9);
            var ny = y - (height * 0.5);
            if ((nx * nx) + (ny * ny) < (height * 0.25) * (height * 0.25))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The display's refresh period from DWM, independent of whether anything is animating.</summary>
    private static double DisplayPeriodMs()
    {
        var info = new DwmTimingInfo { cbSize = (uint)Marshal.SizeOf(typeof(DwmTimingInfo)) };
        if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) == 0 && info.rateRefreshNum > 0)
        {
            return 1000.0 * info.rateRefreshDen / info.rateRefreshNum;
        }

        return 1000.0 / 60;
    }

    private static async Task<double> MeasureDisplayPeriodAsync()
    {
        var times = new List<double>();
        var last = TimeSpan.Zero;
        var tcs = new TaskCompletionSource<bool>();
        EventHandler h = null;
        h = (s, e) =>
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            if (t == last)
            {
                return;
            }

            if (last != TimeSpan.Zero)
            {
                times.Add((t - last).TotalMilliseconds);
            }

            last = t;
            if (times.Count >= 120)
            {
                CompositionTarget.Rendering -= h;
                tcs.TrySetResult(true);
            }
        };
        CompositionTarget.Rendering += h;
        await tcs.Task;
        times.Sort();
        return times[times.Count / 2];
    }

    private static Task WaitFrames(int n)
    {
        var tcs = new TaskCompletionSource<bool>();
        var count = 0;
        EventHandler h = null;
        h = (s, e) =>
        {
            if (++count >= n)
            {
                CompositionTarget.Rendering -= h;
                tcs.TrySetResult(true);
            }
        };
        CompositionTarget.Rendering += h;
        return tcs.Task;
    }

    private static void RunLoadWindow()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
        var canvas = new Canvas { Width = 1280, Height = 800, Background = Brushes.Black };
        for (var i = 0; i < 6; i++)
        {
            var blur = new BlurEffect { Radius = 60, RenderingBias = RenderingBias.Quality };
            var rotate = new RotateTransform();
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = 700,
                Height = 500,
                Fill = new LinearGradientBrush(Colors.OrangeRed, Colors.DarkSlateBlue, i * 60),
                Effect = blur,
                RenderTransform = rotate,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(rect, 40 + (i * 80));
            Canvas.SetTop(rect, 30 + (i * 30));
            canvas.Children.Add(rect);
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.3 + (0.2 * i))) { RepeatBehavior = RepeatBehavior.Forever });
            blur.BeginAnimation(BlurEffect.RadiusProperty,
                new DoubleAnimation(30, 90, TimeSpan.FromSeconds(0.7 + (0.1 * i))) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });
        }

        var window = new Window
        {
            Title = "HoldProbe GPU load",
            Width = 1300,
            Height = 840,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 300,
            Top = 200,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = canvas,
        };
        app.Run(window);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DwmTimingInfo
    {
        public uint cbSize;
        public uint rateRefreshNum, rateRefreshDen;
        public ulong qpcRefreshPeriod;
        public uint rateComposeNum, rateComposeDen;
        public ulong qpcVBlank, cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose, cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame, cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed, qpcFrameDisplayed, cRefreshFrameDisplayed, cFrameComplete, qpcFrameComplete;
        public ulong cFramePending, qpcFramePending, cFramesDisplayed, cFramesComplete, cFramesPending;
        public ulong cFramesAvailable, cFramesDropped, cFramesMissed, cRefreshNextDisplayed, cRefreshNextPresented;
        public ulong cRefreshesDisplayed, cRefreshesPresented, cRefreshStarted, cPixelsReceived, cPixelsDrawn;
        public ulong cBuffersEmpty;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTimingInfo info);

    private static ulong DwmFrameCount()
    {
        var info = new DwmTimingInfo { cbSize = (uint)Marshal.SizeOf(typeof(DwmTimingInfo)) };
        return DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) == 0 ? info.cFrame : 0UL;
    }
}

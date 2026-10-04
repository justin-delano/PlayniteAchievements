// Answers the question the storyboard slide was made for: how close to the monitor's refresh rate does
// the notification's motion actually run, and which mechanism gets closest.
//
// The metric is the composition rate sustained DURING the motion, not the animation's own value changes.
// A WPF timeline advances once per composed frame by construction, so counting value changes only ever
// re-measures the render loop. What can actually go wrong is the render loop itself slowing down,
// because each frame of this motion costs real work: the toast is a per-pixel-alpha layered window, so
// every frame is a full surface update to the OS, and the old mechanism additionally issued a
// cross-process SetWindowPos per frame.
//
// Variants are measured against the display's own period read from the OS, never against the run's own
// mean -- deriving the target from the run being judged hands a starved run a lenient target.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

internal static class SlideCadenceProbe
{
    private const int SlideDurationMs = 240;
    // Card geometry and effect weight, overridable from the command line. The defaults are the
    // original 442x138 card with a 12-radius shadow, which on any reasonably quick display pins
    // every Transform variant at 100% of refresh - so the BitmapCache and padding variants have no
    // headroom in which a win could appear, and measuring them there proves nothing. A user card is
    // configurable up to several times that width and, with the border glow on, carries a 36-radius
    // gaussian; at 200% display scale that is ~19x the pixel area and a 72-device-pixel blur.
    //
    // Scale is not settable from here, so reproduce a scaled card by its device-pixel equivalent:
    // --card-width 2328 --card-height 496 --glow 72 is the same rasterization work at 100% as a
    // 1164x248 card with a 36-radius glow at 200%.
    private static double CardWidthDip = 442d;
    private static double CardHeightDip = 138d;
    private static double CardGlowRadius = 12d;

    /// <summary>
    /// Gives each text line the real template's nested effect pair - an effect on the line's wrapper
    /// and another on the text inside it - which costs two full-width render intermediates per line
    /// instead of one.
    /// </summary>
    private static bool NestedTextShadows;
    private const double TravelPaddingDip = 40d;

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    private enum Mechanism
    {
        /// <summary>The pre-storyboard slide: move the layered HWND once per composed frame.</summary>
        WindowMove,

        /// <summary>What ships now: a storyboard on the card host's translate, window stationary.</summary>
        Transform,

        /// <summary>As shipped, plus a bitmap cache on the card so a frame is a blit, not a re-raster.</summary>
        TransformCached,

        /// <summary>
        /// Stationary window sized to the card only. The card clips, so this is not a usable mode -- it
        /// isolates how much of the per-frame cost is the padded window's larger layered surface.
        /// </summary>
        TransformNoPadding,

        /// <summary>
        /// As shipped, plus the overlay-track sampling a recording-enabled wave really does on the UI
        /// thread during the slide: a RenderTargetBitmap of the card, a memcmp against the previous
        /// frame and an XOR, paced at the recording rate. This is the only thing in the running app
        /// that competes with the slide for the render loop.
        /// </summary>
        TransformWithSampling,

        /// <summary>
        /// As shipped, but the window is not layered: AllowsTransparency off, a transparent
        /// composition background, and the DWM frame extended over the whole client area. DWM then
        /// alpha-composites the window's GPU redirection surface directly, with no per-frame
        /// software readback and no UpdateLayeredWindow call.
        /// </summary>
        TransformDwm,

        /// <summary>
        /// As shipped, but the storyboard is paused and seeked every composed frame to the real time
        /// since Begin (a Stopwatch), instead of advancing on WPF's predicted frame time. Same curve,
        /// same duration; only the time base differs.
        /// </summary>
        TransformClock,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    /// <summary>
    /// Whether to check, once per window kind, that the transparent padding really shows what is
    /// behind it. A non-layered window that renders opaque would win the cadence comparison for the
    /// wrong reason, so this sanity check guards the result.
    /// </summary>
    private static bool VerifyTransparency;

    /// <summary>
    /// Sets this process's GPU scheduling priority class (gdi32 D3DKMT, process-wide, so it covers
    /// WPF's own render device). 0 idle .. 2 normal .. 4 high, 5 realtime. Returns an NTSTATUS.
    /// </summary>
    [DllImport("gdi32.dll")]
    private static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DWM_TIMING_INFO
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
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO info);

    /// <summary>
    /// DWM's own composed-frame counter, system-wide. Read across a slide it gives the rate DWM
    /// itself composed at, which no window in this process can exceed.
    /// </summary>
    private static ulong DwmFrameCount()
    {
        var info = new DWM_TIMING_INFO { cbSize = (uint)Marshal.SizeOf(typeof(DWM_TIMING_INFO)) };
        return DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) == 0 ? info.cFrame : 0UL;
    }

    /// <summary>Mechanisms to run, by name; null runs all of them.</summary>
    private static HashSet<string> Only;

    /// <summary>Recording rate the sampling variant paces itself at, mirroring RecordingFps.</summary>
    private const int RecordingFps = 60;

    private sealed class Result
    {
        public Mechanism Mechanism;
        public int Frames;
        public double SpanMs;
        public double MedianMs;
        public double MaxGapMs;
        public double DwmHz;
        public double SkewSdMs = double.NaN;
        public double ErrMeanPx = double.NaN;
        public double ErrSdPx = double.NaN;
    }

    /// <summary>
    /// Per-frame accuracy of a transform slide against the real clock. Skew is the real time at the
    /// frame callback minus WPF's RenderingTime; only its spread matters, since a constant offset is
    /// just latency. Error is the card's offset minus where the curve puts it at the real time, in
    /// DIP: a steady error is invisible, its spread is what reads as uneven steps.
    /// </summary>
    private sealed class SlideAccuracy
    {
        private readonly List<double> _skew = new List<double>();
        private readonly List<double> _error = new List<double>();

        public void Add(double renderingMs, double wallMs, double errorDip)
        {
            _skew.Add(wallMs - renderingMs);
            _error.Add(errorDip);
        }

        public double SkewSdMs => StdDev(_skew);

        public double ErrMeanPx => _error.Count == 0 ? double.NaN : Mean(_error);

        public double ErrSdPx => StdDev(_error);

        private static double Mean(List<double> values)
        {
            var sum = 0d;
            foreach (var v in values)
            {
                sum += v;
            }

            return sum / values.Count;
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count < 2)
            {
                return double.NaN;
            }

            var mean = Mean(values);
            var sum = 0d;
            foreach (var v in values)
            {
                sum += (v - mean) * (v - mean);
            }

            return Math.Sqrt(sum / (values.Count - 1));
        }
    }

    private static double _displayPeriodMs = 1000d / 60d;

    [STAThread]
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--loadwindow") >= 0)
        {
            RunLoadWindow();
            return 0;
        }

        var repeats = 5;
        var load = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--repeats" && i + 1 < args.Length)
            {
                repeats = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture));
            }
            else if (args[i] == "--load")
            {
                load = i + 1 < args.Length && int.TryParse(args[i + 1], out var n) ? Math.Max(1, n) : 2;
            }
            else if (args[i] == "--card-width" && i + 1 < args.Length)
            {
                CardWidthDip = Math.Max(1d, double.Parse(args[++i], CultureInfo.InvariantCulture));
            }
            else if (args[i] == "--card-height" && i + 1 < args.Length)
            {
                CardHeightDip = Math.Max(1d, double.Parse(args[++i], CultureInfo.InvariantCulture));
            }
            else if (args[i] == "--glow" && i + 1 < args.Length)
            {
                CardGlowRadius = Math.Max(0d, double.Parse(args[++i], CultureInfo.InvariantCulture));
            }
            else if (args[i] == "--nested")
            {
                NestedTextShadows = true;
            }
            else if (args[i] == "--gpu-priority" && i + 1 < args.Length)
            {
                var priority = int.Parse(args[++i], CultureInfo.InvariantCulture);
                var status = D3DKMTSetProcessSchedulingPriorityClass(
                    System.Diagnostics.Process.GetCurrentProcess().Handle, priority);
                Console.WriteLine("GPU scheduling priority class {0}: status=0x{1:X8}", priority, status);
            }
            else if (args[i] == "--verify")
            {
                VerifyTransparency = true;
            }
            else if (args[i] == "--only" && i + 1 < args.Length)
            {
                Only = new HashSet<string>(args[++i].Split(','), StringComparer.OrdinalIgnoreCase);
            }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var results = new List<Result>();
        var loadProcesses = new List<System.Diagnostics.Process>();

        app.Startup += async (s, e) =>
        {
            try
            {
                _displayPeriodMs = ResolveDisplayPeriodMs();
                Console.WriteLine(
                    "Display composes every {0:0.00} ms ({1:0.0} Hz). Slide is {2} ms.",
                    _displayPeriodMs, 1000d / _displayPeriodMs, SlideDurationMs);
                Console.WriteLine(
                    "Ideal frame count for a slide at that rate: {0:0}.", SlideDurationMs / _displayPeriodMs);

                // GPU contention arrives from OTHER processes when a game is running, so the load
                // lives in child processes: their render threads and command queues compete with
                // this one at the GPU and DWM, never inside this process's render loop.
                if (load > 0)
                {
                    var exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                    for (var i = 0; i < load; i++)
                    {
                        loadProcesses.Add(System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(exe, "--loadwindow")
                            {
                                UseShellExecute = false,
                            }));
                    }

                    Console.WriteLine("GPU load: {0} child render process(es), animated blur.", load);
                    await System.Threading.Tasks.Task.Delay(2000);
                }

                Console.WriteLine();

                if (VerifyTransparency)
                {
                    await VerifyTransparencyOf(Mechanism.Transform);
                    await VerifyTransparencyOf(Mechanism.TransformDwm);
                    Console.WriteLine();
                }

                foreach (Mechanism mechanism in Enum.GetValues(typeof(Mechanism)))
                {
                    if (Only != null && !Only.Contains(mechanism.ToString()))
                    {
                        continue;
                    }

                    for (var run = 0; run < repeats; run++)
                    {
                        var result = await RunOne(mechanism);
                        if (result != null)
                        {
                            results.Add(result);
                        }
                    }
                }

                Report(results);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("probe failed: " + ex);
            }
            finally
            {
                foreach (var child in loadProcesses)
                {
                    try
                    {
                        child.Kill();
                    }
                    catch
                    {
                    }
                }

                app.Shutdown();
            }
        };

        app.Run();
        return 0;
    }

    /// <summary>
    /// One GPU-load window: large animated gradients under animated wide-radius blurs, redrawn
    /// every composed frame. Blur cost scales with radius and area, so a few of these saturate
    /// the GPU the way a busy game does, without touching the measuring process's render loop.
    /// </summary>
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
            rotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.3 + (0.2 * i)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                });
            blur.BeginAnimation(
                BlurEffect.RadiusProperty,
                new DoubleAnimation(30, 90, TimeSpan.FromSeconds(0.7 + (0.1 * i)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true,
                });
        }

        var window = new Window
        {
            Title = "SlideCadenceProbe GPU load",
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

    private static async System.Threading.Tasks.Task<Result> RunOne(Mechanism mechanism)
    {
        var padded = mechanism != Mechanism.TransformNoPadding && mechanism != Mechanism.WindowMove;
        var sampling = mechanism == Mechanism.TransformWithSampling;
        var travel = CardHeightDip + TravelPaddingDip;

        var slide = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(slide);

        var card = BuildCard();
        if (mechanism == Mechanism.TransformCached)
        {
            // The card is static for the whole slide, so caching it turns each frame from a re-raster of
            // text and two shadow effects into a transformed blit of one texture.
            card.CacheMode = new BitmapCache { RenderAtScale = 1.0, SnapsToDevicePixels = false };
        }

        var window = BuildWindow(mechanism, card, group, padded, travel, out var host);

        try
        {
            window.Show();
            window.UpdateLayout();

            // Let the window pay its first-paint cost before anything is timed, exactly as the plugin's
            // warm-frame wait does; otherwise every mechanism's first run measures window creation.
            await WaitFrames(3, 300);

            var ticks = new TickCounter();
            var accuracy = new SlideAccuracy();
            var dwmStartFrames = DwmFrameCount();
            var dwmClock = System.Diagnostics.Stopwatch.StartNew();
            var finished = new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

            if (mechanism == Mechanism.WindowMove)
            {
                var scale = RenderScale(window);
                var distancePx = (int)Math.Round(travel * scale);
                var restY = 120 + distancePx;
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

                EventHandler tick = null;
                tick = (s, e) =>
                {
                    if (!ticks.TryAdvance(e, out var elapsed))
                    {
                        return;
                    }

                    var t = Math.Min(1.0, elapsed / SlideDurationMs);
                    var y = (int)Math.Round((restY + distancePx) + ((restY - (restY + distancePx)) * ease.Ease(t)));
                    SetWindowPos(hwnd, IntPtr.Zero, 120, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    if (t >= 1.0)
                    {
                        CompositionTarget.Rendering -= tick;
                        finished.TrySetResult(true);
                    }
                };

                CompositionTarget.Rendering += tick;
                try
                {
                    await System.Threading.Tasks.Task.WhenAny(
                        finished.Task, System.Threading.Tasks.Task.Delay(SlideDurationMs + 2000));
                }
                finally
                {
                    CompositionTarget.Rendering -= tick;
                }
            }
            else
            {
                var animation = new DoubleAnimation
                {
                    From = travel,
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(SlideDurationMs)),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
                    FillBehavior = FillBehavior.HoldEnd,
                };
                Storyboard.SetTarget(animation, host);
                Storyboard.SetTargetProperty(
                    animation,
                    new PropertyPath(
                        "(0).(1)[1].(2)",
                        UIElement.RenderTransformProperty,
                        TransformGroup.ChildrenProperty,
                        TranslateTransform.YProperty));

                var storyboard = new Storyboard();
                storyboard.Children.Add(animation);

                var sampleIntervalMs = 1000d / RecordingFps;
                var dueTolerance = _displayPeriodMs / 2d;
                var nextDueMs = sampleIntervalMs;
                byte[] previous = null;
                var clockDriven = mechanism == Mechanism.TransformClock;
                var wall = new System.Diagnostics.Stopwatch();
                var ease = animation.EasingFunction;

                EventHandler tick = null;
                tick = (s, e) =>
                {
                    if (!ticks.TryAdvance(e, out var elapsed))
                    {
                        return;
                    }

                    var wallMs = wall.Elapsed.TotalMilliseconds;
                    if (clockDriven)
                    {
                        // The real clock decides where the card is: seek the (paused) storyboard to
                        // the wall time since Begin, and judge completion by that same time.
                        storyboard.SeekAlignedToLastTick(
                            host, TimeSpan.FromMilliseconds(Math.Min(wallMs, SlideDurationMs)),
                            TimeSeekOrigin.BeginTime);
                        elapsed = wallMs;
                    }

                    var progress = Math.Min(1d, wallMs / SlideDurationMs);
                    accuracy.Add(
                        (e as RenderingEventArgs)?.RenderingTime.TotalMilliseconds ?? wallMs,
                        wallMs,
                        slide.Y - (travel * (1d - ease.Ease(progress))));

                    if (sampling && elapsed >= nextDueMs - dueTolerance)
                    {
                        do
                        {
                            nextDueMs += sampleIntervalMs;
                        }
                        while (nextDueMs <= elapsed);

                        previous = SampleCard(card, previous);
                    }

                    if (elapsed >= SlideDurationMs)
                    {
                        CompositionTarget.Rendering -= tick;
                        finished.TrySetResult(true);
                    }
                };

                slide.Y = 0;
                CompositionTarget.Rendering += tick;
                storyboard.Begin(host, true);
                if (clockDriven)
                {
                    storyboard.Pause(host);
                }

                wall.Start();
                try
                {
                    await System.Threading.Tasks.Task.WhenAny(
                        finished.Task, System.Threading.Tasks.Task.Delay(SlideDurationMs + 2000));
                }
                finally
                {
                    CompositionTarget.Rendering -= tick;
                }
            }

            return new Result
            {
                Mechanism = mechanism,
                Frames = ticks.Frames,
                SpanMs = ticks.SpanMs,
                MedianMs = ticks.MedianIntervalMs,
                MaxGapMs = ticks.MaxIntervalMs,
                DwmHz = (DwmFrameCount() - dwmStartFrames) * 1000d / Math.Max(1d, dwmClock.Elapsed.TotalMilliseconds),
                SkewSdMs = accuracy.SkewSdMs,
                ErrMeanPx = accuracy.ErrMeanPx,
                ErrSdPx = accuracy.ErrSdPx,
            };
        }
        finally
        {
            try
            {
                window.Close();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// The probe window around the slide host: layered as shipped, or DWM-composited for
    /// <see cref="Mechanism.TransformDwm"/>.
    /// </summary>
    private static Window BuildWindow(
        Mechanism mechanism, FrameworkElement card, TransformGroup group, bool padded, double travel,
        out Grid host)
    {
        var dwm = mechanism == Mechanism.TransformDwm;
        card.Margin = padded ? new Thickness(0, 0, 0, travel) : new Thickness(0);

        host = new Grid
        {
            IsHitTestVisible = false,
            UseLayoutRounding = false,
            SnapsToDevicePixels = false,
            RenderTransform = group,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        host.Children.Add(card);

        var window = new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = !dwm,
            Background = Brushes.Transparent,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Left = 120,
            Top = 120,
            Opacity = 1,
            Content = host,
        };

        if (dwm)
        {
            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            };
        }

        return window;
    }

    /// <summary>
    /// Shows a magenta backdrop, puts the probe window over it, and reads the screen at a point in
    /// the window's transparent travel padding and at the card's centre. The padding must read as
    /// the backdrop and the card must not, or the window kind is not transparent where it matters.
    /// </summary>
    private static async System.Threading.Tasks.Task VerifyTransparencyOf(Mechanism mechanism)
    {
        var backdrop = new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            // Topmost, shown before the probe window: an unactivated plain window can open beneath
            // the console, and the padding would then read the console instead of the backdrop.
            Topmost = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 60,
            Top = 60,
            Width = CardWidthDip + 200,
            Height = (2 * CardHeightDip) + TravelPaddingDip + 200,
            Background = Brushes.Magenta,
        };

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(new TranslateTransform());
        var window = BuildWindow(mechanism, BuildCard(), group, true, CardHeightDip + TravelPaddingDip, out _);
        try
        {
            backdrop.Show();
            window.Show();
            window.UpdateLayout();
            await WaitFrames(10, 1000);
            await System.Threading.Tasks.Task.Delay(300);

            var topLeft = window.PointToScreen(new Point(0, 0));
            var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
            var cardCentre = window.PointToScreen(new Point(window.ActualWidth / 2, CardHeightDip / 2));
            var padding = new System.Drawing.Point(
                (int)((topLeft.X + bottomRight.X) / 2), (int)bottomRight.Y - 10);
            Console.WriteLine(
                "{0,-14} window {1:0}x{2:0} px  padding={3}  card={4}",
                mechanism,
                bottomRight.X - topLeft.X,
                bottomRight.Y - topLeft.Y,
                Describe(ReadScreen(padding)),
                Describe(ReadScreen(new System.Drawing.Point((int)cardCentre.X, (int)cardCentre.Y))));
        }
        finally
        {
            window.Close();
            backdrop.Close();
        }
    }

    private static System.Drawing.Color ReadScreen(System.Drawing.Point point)
    {
        using (var bitmap = new System.Drawing.Bitmap(1, 1))
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(point, System.Drawing.Point.Empty, new System.Drawing.Size(1, 1));
            return bitmap.GetPixel(0, 0);
        }
    }

    private static string Describe(System.Drawing.Color c)
    {
        var backdrop = c.R > 200 && c.G < 60 && c.B > 200;
        return string.Format(
            CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3}", c.R, c.G, c.B, backdrop ? " (backdrop)" : "");
    }

    /// <summary>
    /// What ToastOverlayTrackRecorder costs the render loop per sampled frame: rasterise the card
    /// through a VisualBrush, copy the pixels out, compare against the previous frame and XOR it.
    /// Returns the frame just taken, to be diffed against next time.
    /// </summary>
    private static byte[] SampleCard(FrameworkElement card, byte[] previous)
    {
        try
        {
            var w = card.ActualWidth > 0 ? card.ActualWidth : CardWidthDip;
            var h = card.ActualHeight > 0 ? card.ActualHeight : CardHeightDip;
            var pw = Math.Max(1, (int)Math.Ceiling(w));
            var ph = Math.Max(1, (int)Math.Ceiling(h));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var offset = VisualTreeHelper.GetOffset(card);
                dc.DrawRectangle(
                    new VisualBrush(card)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(offset.X, offset.Y, w, h),
                    },
                    null,
                    new Rect(0, 0, w, h));
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                pw, ph, 96.0, 96.0, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            var stride = pw * 4;
            var buffer = new byte[stride * ph];
            rtb.CopyPixels(buffer, stride, 0);

            if (previous != null && previous.Length == buffer.Length)
            {
                var same = true;
                for (var i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] != previous[i])
                    {
                        same = false;
                        break;
                    }
                }

                if (!same)
                {
                    var delta = new byte[buffer.Length];
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        delta[i] = (byte)(buffer[i] ^ previous[i]);
                    }

                    GC.KeepAlive(delta);
                }
            }

            return buffer;
        }
        catch
        {
            return previous;
        }
    }

    /// <summary>A card shaped like the real one in the ways that cost per frame: shadows and text.</summary>
    private static FrameworkElement BuildCard()
    {
        var text = new StackPanel { Margin = new Thickness(18, 12, 18, 12) };
        text.Children.Add(BuildLine("Achievement unlocked", 18, Brushes.White, 4, 0.8));
        text.Children.Add(BuildLine(
            "A reasonably long achievement description line", 13, Brushes.LightGray, 3, 0.7));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 64,
            Height = 64,
            Margin = new Thickness(12),
            Background = new LinearGradientBrush(Colors.SteelBlue, Colors.MidnightBlue, 45),
            CornerRadius = new CornerRadius(8),
        });
        row.Children.Add(text);

        return new Border
        {
            Width = CardWidthDip,
            MinHeight = CardHeightDip,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 80)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = CardGlowRadius, ShadowDepth = 0, Opacity = 0.9 },
            Child = row,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// One text line carrying the real template's shadow shape: a single effect on the text, or -
    /// with <see cref="NestedTextShadows"/> - an effect on a wrapper as well, which is what the
    /// template does to get a shadow denser than one feathered gaussian can produce and what costs
    /// a second full-width render intermediate per line.
    /// </summary>
    private static FrameworkElement BuildLine(
        string content, double fontSize, Brush foreground, double depth, double opacity)
    {
        var line = new TextBlock
        {
            Text = content,
            FontSize = fontSize,
            Foreground = foreground,
            Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = depth, Opacity = opacity },
        };

        if (!NestedTextShadows)
        {
            return line;
        }

        var wrapper = new Grid
        {
            Effect = new DropShadowEffect { BlurRadius = 2.5, ShadowDepth = depth, Opacity = opacity },
        };
        wrapper.Children.Add(line);
        return wrapper;
    }

    private static async System.Threading.Tasks.Task<int> WaitFrames(int frames, int timeoutMs)
    {
        var ticks = new TickCounter();
        var reached = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler tick = null;
        tick = (s, e) =>
        {
            if (ticks.TryAdvance(e, out _) && ticks.Frames >= frames)
            {
                reached.TrySetResult(true);
            }
        };

        CompositionTarget.Rendering += tick;
        try
        {
            await System.Threading.Tasks.Task.WhenAny(
                reached.Task, System.Threading.Tasks.Task.Delay(timeoutMs));
        }
        finally
        {
            CompositionTarget.Rendering -= tick;
        }

        return ticks.Frames;
    }

    private static double RenderScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        var m = source?.CompositionTarget?.TransformToDevice;
        return m.HasValue && m.Value.M11 > 0 ? m.Value.M11 : 1.0;
    }

    private static double ResolveDisplayPeriodMs()
    {
        try
        {
            var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) && devMode.dmDisplayFrequency > 1)
            {
                return 1000d / devMode.dmDisplayFrequency;
            }
        }
        catch
        {
        }

        return 1000d / 60d;
    }

    private static void Report(List<Result> results)
    {
        Console.WriteLine(
            "{0,-20} {1,7} {2,9} {3,9} {4,9} {5,9} {6,9} {7,8} {8,8} {9,8}",
            "mechanism", "frames", "medianMs", "sustained", "% of max", "maxGapMs", "dwmHz",
            "skewSd", "errMean", "errSd");

        foreach (Mechanism mechanism in Enum.GetValues(typeof(Mechanism)))
        {
            var runs = results.FindAll(r => r.Mechanism == mechanism);
            if (runs.Count == 0)
            {
                continue;
            }

            // Median across runs, so one scheduling hiccup does not decide the verdict.
            runs.Sort((a, b) => a.MedianMs.CompareTo(b.MedianMs));
            var mid = runs[runs.Count / 2];
            var sustained = mid.MedianMs > 0 ? 1000d / mid.MedianMs : 0d;

            Console.WriteLine(
                "{0,-20} {1,7} {2,9:0.00} {3,7:0.0}Hz {4,8:0}% {5,9:0.0} {6,7:0.0}Hz {7,6:0.00}ms {8,6:0.0}px {9,6:0.0}px",
                mechanism,
                mid.Frames,
                mid.MedianMs,
                sustained,
                100d * sustained / (1000d / _displayPeriodMs),
                mid.MaxGapMs,
                mid.DwmHz,
                mid.SkewSdMs,
                mid.ErrMeanPx,
                mid.ErrSdPx);
        }

        Console.WriteLine();
        Console.WriteLine("  sustained is the composition rate held during the motion; % of max is that");
        Console.WriteLine("  against the display's own rate. TransformNoPadding is not a usable mode -- it");
        Console.WriteLine("  clips the card -- and is here only to price the padded window's larger surface.");
    }

    /// <summary>Counts distinct composed frames by their own composition timestamp.</summary>
    private sealed class TickCounter
    {
        private readonly System.Diagnostics.Stopwatch _fallback = System.Diagnostics.Stopwatch.StartNew();
        private readonly List<double> _intervals = new List<double>();
        private bool _chosen;
        private bool _useRenderingTime;
        private double _firstMs;
        private double _lastMs = double.NegativeInfinity;

        public int Frames { get; private set; }

        public double SpanMs => Frames > 1 ? _lastMs - _firstMs : 0d;

        public double MaxIntervalMs { get; private set; }

        public double MedianIntervalMs
        {
            get
            {
                if (_intervals.Count == 0)
                {
                    return 0d;
                }

                var sorted = new List<double>(_intervals);
                sorted.Sort();
                return sorted[sorted.Count / 2];
            }
        }

        public bool TryAdvance(EventArgs e, out double elapsedMs)
        {
            elapsedMs = 0d;
            var renderingTime = (e as RenderingEventArgs)?.RenderingTime;
            if (!_chosen)
            {
                _useRenderingTime = renderingTime.HasValue;
                _chosen = true;
            }

            var nowMs = _useRenderingTime && renderingTime.HasValue
                ? renderingTime.Value.TotalMilliseconds
                : _fallback.Elapsed.TotalMilliseconds;
            if (nowMs <= _lastMs)
            {
                return false;
            }

            if (Frames == 0)
            {
                _firstMs = nowMs;
            }
            else
            {
                var interval = nowMs - _lastMs;
                _intervals.Add(interval);
                if (interval > MaxIntervalMs)
                {
                    MaxIntervalMs = interval;
                }
            }

            _lastMs = nowMs;
            Frames++;
            elapsedMs = nowMs - _firstMs;
            return true;
        }
    }
}

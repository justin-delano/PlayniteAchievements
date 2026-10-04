// Ground truth for SlideCadenceProbe: where the card actually was on screen, at the instant each
// frame was actually presented.
//
// The per-frame columns in SlideCadenceProbe compare the card against a clock the probe itself
// chose, so a slide driven by that same clock scores perfectly by construction. This instead reads
// the desktop through DXGI Desktop Duplication: every duplicated frame carries LastPresentTime, the
// QPC instant DWM presented it, on the same clock as Stopwatch. One pixel column through the card,
// over a magenta backdrop, gives the card's real top edge in that frame.
//
// Each slide's frames are fitted to the ideal curve with a free constant latency and position
// offset - a constant delay between building a frame and presenting it is invisible, so it must not
// count against a mechanism - and the residual is what the eye reads as uneven steps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;

internal static partial class SlideCadenceProbe
{
    private sealed class TruthResult
    {
        public Mechanism Mechanism;
        public int PresentedFrames;
        public int MovingFrames;
        public double LatencyMs;
        public double ResidualSdDip;
        public double ResidualMaxDip;
    }

    /// <summary>
    /// Runs each selected transform mechanism <paramref name="repeats"/> times under desktop
    /// duplication and prints the fitted residual per mechanism.
    /// </summary>
    private static async System.Threading.Tasks.Task RunTruth(int repeats)
    {
        var results = new List<TruthResult>();
        foreach (var mechanism in new[] { Mechanism.Transform, Mechanism.TransformClock })
        {
            if (Only != null && !Only.Contains(mechanism.ToString()))
            {
                continue;
            }

            for (var run = 0; run < repeats; run++)
            {
                var result = await RunTruthOne(mechanism);
                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        Console.WriteLine(
            "{0,-16} {1,9} {2,7} {3,10} {4,11} {5,11}",
            "mechanism", "presented", "moving", "latencyMs", "residualSd", "residualMax");
        foreach (var mechanism in new[] { Mechanism.Transform, Mechanism.TransformClock })
        {
            var runs = results.FindAll(r => r.Mechanism == mechanism);
            if (runs.Count == 0)
            {
                continue;
            }

            // Median run by residual, so one scheduling hiccup does not decide the verdict.
            runs.Sort((a, b) => a.ResidualSdDip.CompareTo(b.ResidualSdDip));
            var mid = runs[runs.Count / 2];
            Console.WriteLine(
                "{0,-16} {1,9} {2,7} {3,10:0.0} {4,9:0.00}px {5,9:0.00}px",
                mechanism, mid.PresentedFrames, mid.MovingFrames, mid.LatencyMs, mid.ResidualSdDip, mid.ResidualMaxDip);
            Console.WriteLine(
                "  all runs residualSd: {0}",
                string.Join(" ", runs.ConvertAll(r => r.ResidualSdDip.ToString("0.00", CultureInfo.InvariantCulture))));
        }

        Console.WriteLine();
        Console.WriteLine("  residualSd is the on-screen card position's scatter around the ideal curve at each");
        Console.WriteLine("  frame's real present time, after fitting a constant latency and offset (DIP).");
    }

    private static async System.Threading.Tasks.Task<TruthResult> RunTruthOne(Mechanism mechanism)
    {
        var travel = CardHeightDip + TravelPaddingDip;
        var slide = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(slide);

        var backdrop = BuildBackdrop();
        var window = BuildWindow(mechanism, BuildCard(), group, true, travel, out var host);
        DuplicationColumn column = null;
        try
        {
            backdrop.Show();
            window.Show();
            window.UpdateLayout();

            // Hold the start position while the window pays its first-paint cost, so frames
            // presented before the first tick show the card where the curve starts.
            slide.Y = travel;
            await WaitFrames(5, 500);

            var scale = RenderScale(window);
            var topLeft = window.PointToScreen(new Point(0, 0));
            var bottom = window.PointToScreen(new Point(0, window.ActualHeight));
            // Plain card background: right of the text, clear of the right-hand rounded corner.
            var columnX = (int)Math.Round(topLeft.X + ((CardWidthDip - 30) * scale));
            column = new DuplicationColumn(columnX, (int)Math.Round(topLeft.Y), (int)Math.Round(bottom.Y));

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

            var clockDriven = mechanism == Mechanism.TransformClock;
            var wall = new System.Diagnostics.Stopwatch();
            var finished = new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler tick = (s, e) =>
            {
                var wallMs = wall.Elapsed.TotalMilliseconds;
                if (clockDriven)
                {
                    storyboard.SeekAlignedToLastTick(
                        host, TimeSpan.FromMilliseconds(Math.Min(wallMs, SlideDurationMs)),
                        TimeSeekOrigin.BeginTime);
                }

                if (wallMs >= SlideDurationMs + 150)
                {
                    finished.TrySetResult(true);
                }
            };

            column.Start();
            await System.Threading.Tasks.Task.Delay(100);

            CompositionTarget.Rendering += tick;
            var startQpc = System.Diagnostics.Stopwatch.GetTimestamp();
            storyboard.Begin(host, true);
            if (clockDriven)
            {
                storyboard.Pause(host);
            }

            wall.Start();
            try
            {
                await System.Threading.Tasks.Task.WhenAny(
                    finished.Task, System.Threading.Tasks.Task.Delay(SlideDurationMs + 3000));
            }
            finally
            {
                CompositionTarget.Rendering -= tick;
            }

            var samples = column.Stop();
            return Fit(mechanism, samples, startQpc, topLeft.Y, scale, travel, animation.EasingFunction);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("truth run failed: " + ex.Message);
            return null;
        }
        finally
        {
            column?.Dispose();
            window.Close();
            backdrop.Close();
        }
    }

    private static Window BuildBackdrop()
    {
        return new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
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
    }

    /// <summary>
    /// Converts each presented frame's card edge to a slide offset in DIP and fits it against the
    /// curve with a free time shift and position offset. Only frames in flight count: the start
    /// hold and the overshoot (clipped by the window's top edge) say nothing about pacing.
    /// </summary>
    private static TruthResult Fit(
        Mechanism mechanism, List<ColumnSample> samples, long startQpc, double windowTopPx, double scale,
        double travel, IEasingFunction ease)
    {
        var frequency = (double)System.Diagnostics.Stopwatch.Frequency;
        if (Environment.GetEnvironmentVariable("SLIDE_TRUTH_DUMP") == "1")
        {
            Console.WriteLine("  {0}: windowTop={1:0} scale={2:0.00} travel={3:0}", mechanism, windowTopPx, scale, travel);
            foreach (var sample in samples)
            {
                Console.WriteLine(
                    "    t={0,8:0.00}ms edge={1}",
                    (sample.PresentQpc - startQpc) * 1000d / frequency, sample.EdgeY);
            }
        }

        var points = new List<(double TMs, double OffsetDip)>();
        foreach (var sample in samples)
        {
            if (sample.EdgeY < 0)
            {
                continue;
            }

            var offsetDip = (sample.EdgeY - windowTopPx) / scale;
            if (offsetDip <= 2 || offsetDip >= travel - 2)
            {
                continue;
            }

            points.Add(((sample.PresentQpc - startQpc) * 1000d / frequency, offsetDip));
        }

        var result = new TruthResult
        {
            Mechanism = mechanism,
            PresentedFrames = samples.Count,
            MovingFrames = points.Count,
            LatencyMs = double.NaN,
            ResidualSdDip = double.NaN,
            ResidualMaxDip = double.NaN,
        };
        if (points.Count < 3)
        {
            return result;
        }

        var bestSd = double.MaxValue;
        for (var shift = -40d; shift <= 120d; shift += 0.25)
        {
            var residuals = Residuals(points, shift, travel, ease, out var maxAbs);
            var sd = Rms(residuals);
            if (sd < bestSd)
            {
                bestSd = sd;
                result.LatencyMs = shift;
                result.ResidualSdDip = sd;
                result.ResidualMaxDip = maxAbs;
            }
        }

        return result;
    }

    private static List<double> Residuals(
        List<(double TMs, double OffsetDip)> points, double shiftMs, double travel, IEasingFunction ease,
        out double maxAbs)
    {
        var raw = new List<double>(points.Count);
        var mean = 0d;
        foreach (var p in points)
        {
            var t = Math.Max(0d, Math.Min(1d, (p.TMs - shiftMs) / SlideDurationMs));
            var r = p.OffsetDip - (travel * (1d - ease.Ease(t)));
            raw.Add(r);
            mean += r;
        }

        mean /= raw.Count;
        maxAbs = 0d;
        for (var i = 0; i < raw.Count; i++)
        {
            raw[i] -= mean;
            maxAbs = Math.Max(maxAbs, Math.Abs(raw[i]));
        }

        return raw;
    }

    private static double Rms(List<double> values)
    {
        var sum = 0d;
        foreach (var v in values)
        {
            sum += v * v;
        }

        return Math.Sqrt(sum / values.Count);
    }

    private struct ColumnSample
    {
        public long PresentQpc;
        public int EdgeY;
    }

    /// <summary>
    /// Desktop duplication of the output under one screen column, on its own thread. For every
    /// frame DWM presents, copies that column out and records the first card-coloured pixel from
    /// the top together with the frame's present time.
    /// </summary>
    private sealed class DuplicationColumn : IDisposable
    {
        private readonly int _x;
        private readonly int _top;
        private readonly int _bottom;
        private readonly List<ColumnSample> _samples = new List<ColumnSample>();
        private DXGI.Factory1 _factory;
        private DXGI.Adapter1 _adapter;
        private D3D11.Device _device;
        private DXGI.OutputDuplication _duplication;
        private D3D11.Texture2D _staging;
        private SharpDX.Mathematics.Interop.RawRectangle _outputBounds;
        private Thread _thread;
        private volatile bool _stop;
        private int _dumped;

        public DuplicationColumn(int x, int top, int bottom)
        {
            _x = x;
            _top = top;
            _bottom = bottom;
            Open();
        }

        private void Open()
        {
            _factory = new DXGI.Factory1();
            for (var a = 0; a < _factory.GetAdapterCount1(); a++)
            {
                var adapter = _factory.GetAdapter1(a);
                for (var o = 0; o < adapter.GetOutputCount(); o++)
                {
                    using (var output = adapter.GetOutput(o))
                    {
                        var bounds = output.Description.DesktopBounds;
                        if (_x < bounds.Left || _x >= bounds.Right || _top < bounds.Top || _top >= bounds.Bottom)
                        {
                            continue;
                        }

                        _adapter = adapter;
                        _outputBounds = bounds;
                        _device = new D3D11.Device(adapter);
                        using (var output1 = output.QueryInterface<DXGI.Output1>())
                        {
                            _duplication = output1.DuplicateOutput(_device);
                        }

                        _staging = new D3D11.Texture2D(_device, new D3D11.Texture2DDescription
                        {
                            Width = 1,
                            Height = _bottom - _top,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = DXGI.Format.B8G8R8A8_UNorm,
                            SampleDescription = new DXGI.SampleDescription(1, 0),
                            Usage = D3D11.ResourceUsage.Staging,
                            BindFlags = D3D11.BindFlags.None,
                            CpuAccessFlags = D3D11.CpuAccessFlags.Read,
                        });
                        return;
                    }
                }

                adapter.Dispose();
            }

            throw new InvalidOperationException("no DXGI output contains the probe window");
        }

        public void Start()
        {
            _thread = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        public List<ColumnSample> Stop()
        {
            _stop = true;
            _thread?.Join(2000);
            lock (_samples)
            {
                return new List<ColumnSample>(_samples);
            }
        }

        private void Pump()
        {
            var context = _device.ImmediateContext;
            var height = _bottom - _top;
            var region = new D3D11.ResourceRegion(
                _x - _outputBounds.Left, _top - _outputBounds.Top, 0,
                _x - _outputBounds.Left + 1, _bottom - _outputBounds.Top, 1);
            while (!_stop)
            {
                var hr = _duplication.TryAcquireNextFrame(50, out var info, out var resource);
                if (hr.Failure)
                {
                    continue;
                }

                try
                {
                    // Pointer-only updates carry no new desktop image.
                    if (info.LastPresentTime == 0)
                    {
                        continue;
                    }

                    using (var texture = resource.QueryInterface<D3D11.Texture2D>())
                    {
                        if (_dumped == 0 && Environment.GetEnvironmentVariable("SLIDE_TRUTH_DUMP") == "1")
                        {
                            Console.WriteLine("    duplicated format={0}", texture.Description.Format);
                        }

                        context.CopySubresourceRegion(texture, 0, region, _staging, 0);
                    }

                    var box = context.MapSubresource(_staging, 0, D3D11.MapMode.Read, D3D11.MapFlags.None);
                    var edge = -1;
                    try
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var bgra = SharpDX.Utilities.Read<int>(box.DataPointer + (y * box.RowPitch));
                            var b = bgra & 0xFF;
                            var g = (bgra >> 8) & 0xFF;
                            var r = (bgra >> 16) & 0xFF;
                            // The card is a dark grey (authored #18181C, presented brighter after
                            // colour management) with a lighter border. The magenta backdrop and the
                            // shadow over it keep green near zero, so a grey with green tracking red
                            // is the card's first row.
                            if (g > 30 && Math.Abs(r - g) < 20 && Math.Abs(b - g) < 30)
                            {
                                edge = _top + y;
                                break;
                            }
                        }
                    }
                    finally
                    {
                        if (_dumped++ < 3 && Environment.GetEnvironmentVariable("SLIDE_TRUTH_DUMP") == "1")
                        {
                            var rows = new List<string>();
                            for (var y = 0; y < height; y += 24)
                            {
                                rows.Add(SharpDX.Utilities.Read<int>(box.DataPointer + (y * box.RowPitch)).ToString("X8"));
                            }

                            Console.WriteLine("    column: " + string.Join(" ", rows));
                        }

                        context.UnmapSubresource(_staging, 0);
                    }

                    lock (_samples)
                    {
                        _samples.Add(new ColumnSample { PresentQpc = info.LastPresentTime, EdgeY = edge });
                    }
                }
                finally
                {
                    resource?.Dispose();
                    _duplication.ReleaseFrame();
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _thread?.Join(2000);
            _staging?.Dispose();
            _duplication?.Dispose();
            _device?.Dispose();
            _adapter?.Dispose();
            _factory?.Dispose();
        }
    }
}

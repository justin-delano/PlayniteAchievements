// Whether a card split across three stacked DWM windows stays in step while it slides.
//
// HoldProbe showed that splitting the card into glow, background and text windows is lossless at rest
// and removes the per-frame effect cost. During a slide all three move every frame, and each window's
// surface is presented to DWM separately, so a frame could show one window's new position beside
// another's old one. This reads every frame DWM presents through desktop duplication, with one marker
// block per window in its own screen column, and checks that all three block edges agree.
//
// Each window carries its real load: the back window the border glow with its pulse, the middle the
// animated background, the front the text lines with their nested shadows. One TranslateTransform is
// shared by all three slide hosts and set from the clock each frame, so the windows never disagree on
// the value, only possibly on when DWM shows it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;

internal static partial class SlideCadenceProbe
{
    private const double MarkerWidthDip = 24;

    private sealed class SplitResult
    {
        public int Presented;
        public int Moving;
        public int Misaligned;
        public int MaxSpreadPx;
    }

    private static async Task RunSplitTruth(int repeats)
    {
        var results = new List<SplitResult>();
        for (var run = 0; run < repeats; run++)
        {
            var result = await RunSplitTruthOne();
            if (result != null)
            {
                results.Add(result);
            }
        }

        Console.WriteLine("{0,4} {1,9} {2,7} {3,11} {4,12}", "run", "presented", "moving", "misaligned", "max spread");
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            Console.WriteLine("{0,4} {1,9} {2,7} {3,11} {4,10}px", i + 1, r.Presented, r.Moving, r.Misaligned, r.MaxSpreadPx);
        }

        Console.WriteLine();
        Console.WriteLine(
            "total: {0} moving frames, {1} misaligned, worst spread {2} px",
            results.Sum(r => r.Moving), results.Sum(r => r.Misaligned), results.Count == 0 ? 0 : results.Max(r => r.MaxSpreadPx));
        Console.WriteLine("  misaligned: a presented frame whose three window markers were more than 1 px apart.");
    }

    private static async Task<SplitResult> RunSplitTruthOne()
    {
        var travel = CardHeightDip + TravelPaddingDip;
        var slide = new TranslateTransform();
        var glowMargin = Math.Max(CardGlowRadius, MarkerWidthDip * 2);

        // Back: the glow, pulsing, plus a marker in the glow margin left of the card.
        var glow = new DropShadowEffect
        {
            BlurRadius = CardGlowRadius,
            ShadowDepth = 0,
            Color = Color.FromRgb(0xFF, 0xB0, 0x30),
            RenderingBias = RenderingBias.Performance,
        };
        var back = new Grid();
        back.Children.Add(new Border
        {
            Width = CardWidthDip,
            Height = CardHeightDip,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C)),
            Effect = glow,
        });
        back.Children.Add(Marker(Colors.Lime, -glowMargin + ((glowMargin - MarkerWidthDip) / 2)));

        // Middle: the animated background, plus a marker inside the card.
        var frames = new[] { SolidFrame(0x40, 0x20, 0x70), SolidFrame(0x20, 0x50, 0x80), SolidFrame(0x60, 0x30, 0x30) };
        var live = new WriteableBitmap(frames[0]);
        var middle = new Grid();
        middle.Children.Add(new Border
        {
            Width = CardWidthDip,
            Height = CardHeightDip,
            CornerRadius = new CornerRadius(8),
            Background = new ImageBrush(live),
        });
        middle.Children.Add(Marker(Colors.Cyan, CardWidthDip * 0.45));

        // Front: text with the nested shadow pair, plus a marker inside the card.
        var front = new Grid { Width = CardWidthDip, Height = CardHeightDip };
        var lines = new StackPanel { Margin = new Thickness(CardHeightDip * 0.6, CardHeightDip * 0.12, 20, 0) };
        foreach (var text in new[] { "Achievement unlocked", "A rather long achievement name here", "Description line" })
        {
            var outer = new Grid { Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1 } };
            outer.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = CardHeightDip * 0.13,
                Foreground = Brushes.White,
                Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.9 },
            });
            lines.Children.Add(outer);
        }

        front.Children.Add(lines);
        front.Children.Add(Marker(Colors.Yellow, CardWidthDip * 0.8));

        var backdrop = BuildBackdrop();
        backdrop.Width = CardWidthDip + (2 * glowMargin) + 200;
        backdrop.Height = (2 * CardHeightDip) + TravelPaddingDip + (2 * glowMargin) + 200;
        var windows = new[]
        {
            SplitWindow(back, slide, glowMargin, travel),
            SplitWindow(middle, slide, glowMargin, travel),
            SplitWindow(front, slide, glowMargin, travel),
        };

        DuplicationColumns columns = null;
        var running = true;
        AnimationClock pulse = null;
        try
        {
            backdrop.Show();
            foreach (var window in windows)
            {
                window.Show();
            }

            slide.Y = travel;
            pulse = new DoubleAnimation(0.35, 1, TimeSpan.FromSeconds(1.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }.CreateClock();
            glow.ApplyAnimationClock(DropShadowEffect.OpacityProperty, pulse);

            // The background advances on its own cadence, as GifPlayer does.
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var index = 0;
            var pixels = frames.Select(f =>
            {
                var buffer = new byte[f.PixelWidth * f.PixelHeight * 4];
                f.CopyPixels(buffer, f.PixelWidth * 4, 0);
                return buffer;
            }).ToArray();
            new Thread(() =>
            {
                while (running)
                {
                    Thread.Sleep(20);
                    dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                    {
                        index = (index + 1) % pixels.Length;
                        live.WritePixels(new Int32Rect(0, 0, live.PixelWidth, live.PixelHeight), pixels[index], live.PixelWidth * 4, 0);
                    }));
                }
            }) { IsBackground = true }.Start();

            await WaitFrames(10, 1000);

            var scale = RenderScale(windows[0]);
            var origin = windows[0].PointToScreen(new Point(0, 0));
            var bottom = windows[0].PointToScreen(new Point(0, windows[0].ActualHeight));
            Func<double, int> columnAt = xDip => (int)Math.Round(origin.X + ((glowMargin + xDip + (MarkerWidthDip / 2)) * scale));
            var xs = new[]
            {
                columnAt(-glowMargin + ((glowMargin - MarkerWidthDip) / 2)),
                columnAt(CardWidthDip * 0.45),
                columnAt(CardWidthDip * 0.8),
            };
            columns = new DuplicationColumns(xs, (int)Math.Round(origin.Y), (int)Math.Round(bottom.Y));

            var ease = CreateEase();
            var wall = new Stopwatch();
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler tick = (s, e) =>
            {
                var t = Math.Min(1d, wall.Elapsed.TotalMilliseconds / SlideDurationMs);
                slide.Y = travel * (1d - EaseAt(ease, t));
                if (wall.Elapsed.TotalMilliseconds >= SlideDurationMs + 150)
                {
                    finished.TrySetResult(true);
                }
            };

            columns.Start();
            await Task.Delay(100);
            CompositionTarget.Rendering += tick;
            wall.Start();
            try
            {
                await Task.WhenAny(finished.Task, Task.Delay(SlideDurationMs + 3000));
            }
            finally
            {
                CompositionTarget.Rendering -= tick;
            }

            var samples = columns.Stop();
            var startY = (int)Math.Round(origin.Y + (glowMargin + travel) * scale);
            var endY = (int)Math.Round(origin.Y + glowMargin * scale);
            var result = new SplitResult { Presented = samples.Count };
            foreach (var edges in samples)
            {
                if (edges.Any(e => e < 0))
                {
                    continue;
                }

                // Frames in flight only: the start hold and the rest say nothing about step.
                var moving = edges.Any(e => e < startY - 2 && e > endY + 2);
                if (!moving)
                {
                    continue;
                }

                result.Moving++;
                var spread = edges.Max() - edges.Min();
                result.MaxSpreadPx = Math.Max(result.MaxSpreadPx, spread);
                if (spread > 1)
                {
                    result.Misaligned++;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("split truth run failed: " + ex.Message);
            return null;
        }
        finally
        {
            running = false;
            pulse?.Controller?.Stop();
            columns?.Dispose();
            foreach (var window in windows)
            {
                window.Close();
            }

            backdrop.Close();
        }
    }

    private static FrameworkElement Marker(Color color, double xDip)
    {
        return new Border
        {
            Width = MarkerWidthDip,
            Height = CardHeightDip,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(xDip, 0, 0, 0),
            Background = new SolidColorBrush(color),
        };
    }

    private static BitmapSource SolidFrame(byte r, byte g, byte b)
    {
        var width = (int)CardWidthDip;
        var height = (int)CardHeightDip;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        frame.Freeze();
        return frame;
    }

    /// <summary>
    /// One layer's DWM window: the shipped toast geometry (card, glow margin, travel room below the
    /// card), with the layer content in a slide host that shares <paramref name="slide"/>.
    /// </summary>
    private static Window SplitWindow(FrameworkElement layer, TranslateTransform slide, double glowMargin, double travel)
    {
        var host = new Grid
        {
            Width = CardWidthDip,
            Height = CardHeightDip,
            Margin = new Thickness(glowMargin, glowMargin, glowMargin, glowMargin + travel),
            RenderTransform = slide,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        host.Children.Add(layer);

        var window = new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = false,
            Background = Brushes.Transparent,
            UseLayoutRounding = true,
            Left = 120,
            Top = 120,
            Content = host,
        };
        window.SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        };
        return window;
    }

    /// <summary>
    /// Desktop duplication under several screen columns. For every presented frame, records per
    /// column the first row of that column's marker colour: lime, cyan, yellow, left to right.
    /// </summary>
    private sealed class DuplicationColumns : IDisposable
    {
        private readonly int[] _xs;
        private readonly int _top;
        private readonly int _bottom;
        private readonly List<int[]> _samples = new List<int[]>();
        private DXGI.Factory1 _factory;
        private DXGI.Adapter1 _adapter;
        private D3D11.Device _device;
        private DXGI.OutputDuplication _duplication;
        private D3D11.Texture2D _staging;
        private SharpDX.Mathematics.Interop.RawRectangle _outputBounds;
        private Thread _thread;
        private volatile bool _stop;

        public DuplicationColumns(int[] xs, int top, int bottom)
        {
            _xs = xs;
            _top = top;
            _bottom = bottom;
            _factory = new DXGI.Factory1();
            for (var a = 0; a < _factory.GetAdapterCount1() && _duplication == null; a++)
            {
                var adapter = _factory.GetAdapter1(a);
                for (var o = 0; o < adapter.GetOutputCount(); o++)
                {
                    using (var output = adapter.GetOutput(o))
                    {
                        var bounds = output.Description.DesktopBounds;
                        if (xs[0] < bounds.Left || xs[xs.Length - 1] >= bounds.Right || top < bounds.Top || top >= bounds.Bottom)
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
                            Width = xs[xs.Length - 1] - xs[0] + 1,
                            Height = bottom - top,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = DXGI.Format.B8G8R8A8_UNorm,
                            SampleDescription = new DXGI.SampleDescription(1, 0),
                            Usage = D3D11.ResourceUsage.Staging,
                            BindFlags = D3D11.BindFlags.None,
                            CpuAccessFlags = D3D11.CpuAccessFlags.Read,
                        });
                        break;
                    }
                }

                if (_duplication == null)
                {
                    adapter.Dispose();
                }
            }

            if (_duplication == null)
            {
                throw new InvalidOperationException("no DXGI output contains the probe windows");
            }
        }

        public void Start()
        {
            _thread = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        public List<int[]> Stop()
        {
            _stop = true;
            _thread?.Join(2000);
            lock (_samples)
            {
                return new List<int[]>(_samples);
            }
        }

        private static bool Matches(int column, int bgra)
        {
            var b = bgra & 0xFF;
            var g = (bgra >> 8) & 0xFF;
            var r = (bgra >> 16) & 0xFF;
            switch (column)
            {
                case 0: return g > 180 && r < 90 && b < 90;
                case 1: return g > 180 && b > 180 && r < 90;
                default: return r > 180 && g > 180 && b < 90;
            }
        }

        private void Pump()
        {
            var context = _device.ImmediateContext;
            var height = _bottom - _top;
            var left = _xs[0];
            var region = new D3D11.ResourceRegion(
                left - _outputBounds.Left, _top - _outputBounds.Top, 0,
                _xs[_xs.Length - 1] - _outputBounds.Left + 1, _bottom - _outputBounds.Top, 1);
            while (!_stop)
            {
                var hr = _duplication.TryAcquireNextFrame(50, out var info, out var resource);
                if (hr.Failure)
                {
                    continue;
                }

                try
                {
                    if (info.LastPresentTime == 0)
                    {
                        continue;
                    }

                    using (var texture = resource.QueryInterface<D3D11.Texture2D>())
                    {
                        context.CopySubresourceRegion(texture, 0, region, _staging, 0);
                    }

                    var box = context.MapSubresource(_staging, 0, D3D11.MapMode.Read, D3D11.MapFlags.None);
                    var edges = new int[_xs.Length];
                    try
                    {
                        for (var c = 0; c < _xs.Length; c++)
                        {
                            edges[c] = -1;
                            for (var y = 0; y < height; y++)
                            {
                                var bgra = SharpDX.Utilities.Read<int>(box.DataPointer + (y * box.RowPitch) + ((_xs[c] - left) * 4));
                                if (Matches(c, bgra))
                                {
                                    edges[c] = _top + y;
                                    break;
                                }
                            }
                        }
                    }
                    finally
                    {
                        context.UnmapSubresource(_staging, 0);
                    }

                    lock (_samples)
                    {
                        _samples.Add(edges);
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

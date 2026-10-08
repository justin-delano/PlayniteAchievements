// Records every frame DWM presents for one screen region, so what a real notification looks like on
// screen can be inspected frame by frame - a flicker that lasts one frame cannot be seen any other way.
//
//   ScreenFrameRecorder.exe [--seconds 12] [--out <dir>] [--region x,y,w,h] [--max 1200]
//
// Desktop duplication of the output containing the region; every presented frame whose region
// pixels differ from the last saved one is written as a PNG named by its index and its present
// time in ms since recording started, with DXGI's accumulated-frame count (above 1 means presents
// were missed between captures). The region defaults to the bottom-right quarter of the
// primary display, where notifications appear by default. Coordinates are physical pixels.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;

internal static class ScreenFrameRecorder
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private static int Main(string[] args)
    {
        // Physical pixels throughout, on any display scale.
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        var seconds = 12d;
        var outDir = Path.Combine(Path.GetDirectoryName(typeof(ScreenFrameRecorder).Assembly.Location), "screen_frames");
        int[] region = null;
        var max = 1200;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds": seconds = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--out": outDir = args[++i]; break;
                case "--max": max = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--region":
                    region = Array.ConvertAll(args[++i].Split(','), s => int.Parse(s, CultureInfo.InvariantCulture));
                    break;
            }
        }

        if (Directory.Exists(outDir))
        {
            foreach (var old in Directory.GetFiles(outDir, "f*.png"))
            {
                File.Delete(old);
            }
        }

        Directory.CreateDirectory(outDir);

        using (var factory = new DXGI.Factory1())
        using (var adapter = factory.GetAdapter1(0))
        {
            DXGI.Output output = null;
            for (var o = 0; o < adapter.GetOutputCount(); o++)
            {
                var candidate = adapter.GetOutput(o);
                var b = candidate.Description.DesktopBounds;
                var inside = region == null
                    ? b.Left == 0 && b.Top == 0
                    : region[0] >= b.Left && region[0] < b.Right && region[1] >= b.Top && region[1] < b.Bottom;
                if (inside && output == null)
                {
                    output = candidate;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            if (output == null)
            {
                Console.WriteLine("no output contains the region");
                return 1;
            }

            var bounds = output.Description.DesktopBounds;
            if (region == null)
            {
                var w = (bounds.Right - bounds.Left) / 2;
                var h = (bounds.Bottom - bounds.Top) / 2;
                region = new[] { bounds.Left + w, bounds.Top + h, w, h };
            }

            Console.WriteLine("recording {0},{1} {2}x{3} for {4}s into {5}", region[0], region[1], region[2], region[3], seconds, outDir);
            using (var device = new D3D11.Device(adapter))
            using (var output1 = output.QueryInterface<DXGI.Output1>())
            using (var duplication = output1.DuplicateOutput(device))
            using (var staging = new D3D11.Texture2D(device, new D3D11.Texture2DDescription
            {
                Width = region[2],
                Height = region[3],
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new DXGI.SampleDescription(1, 0),
                Usage = D3D11.ResourceUsage.Staging,
                CpuAccessFlags = D3D11.CpuAccessFlags.Read,
            }))
            {
                var queue = new BlockingCollection<Tuple<string, byte[]>>(256);
                var writer = new Thread(() => Write(queue, region[2], region[3])) { IsBackground = true };
                writer.Start();

                var source = new D3D11.ResourceRegion(
                    region[0] - bounds.Left, region[1] - bounds.Top, 0,
                    region[0] - bounds.Left + region[2], region[1] - bounds.Top + region[3], 1);
                var context = device.ImmediateContext;
                var clock = Stopwatch.StartNew();
                var startQpc = Stopwatch.GetTimestamp();
                byte[] previous = null;
                var saved = 0;
                var presented = 0;
                while (clock.Elapsed.TotalSeconds < seconds && saved < max)
                {
                    SharpDX.DXGI.Resource resource;
                    DXGI.OutputDuplicateFrameInformation info;
                    var hr = duplication.TryAcquireNextFrame(50, out info, out resource);
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

                        presented++;
                        using (var texture = resource.QueryInterface<D3D11.Texture2D>())
                        {
                            context.CopySubresourceRegion(texture, 0, source, staging, 0);
                        }

                        var box = context.MapSubresource(staging, 0, D3D11.MapMode.Read, D3D11.MapFlags.None);
                        var pixels = new byte[region[2] * region[3] * 4];
                        try
                        {
                            for (var y = 0; y < region[3]; y++)
                            {
                                Marshal.Copy(box.DataPointer + (y * box.RowPitch), pixels, y * region[2] * 4, region[2] * 4);
                            }
                        }
                        finally
                        {
                            context.UnmapSubresource(staging, 0);
                        }

                        if (previous != null && Same(previous, pixels))
                        {
                            continue;
                        }

                        previous = pixels;
                        var ms = (info.LastPresentTime - startQpc) * 1000d / Stopwatch.Frequency;
                        queue.Add(Tuple.Create(
                            Path.Combine(outDir, string.Format(CultureInfo.InvariantCulture, "f{0:0000}_{1:000000.0}ms_acc{2}.png", saved, ms, info.AccumulatedFrames)),
                            pixels));
                        saved++;
                    }
                    finally
                    {
                        resource?.Dispose();
                        duplication.ReleaseFrame();
                    }
                }

                queue.CompleteAdding();
                writer.Join();
                Console.WriteLine("{0} presented frames, {1} distinct saved", presented, saved);
            }

            output.Dispose();
        }

        return 0;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
            {
                return false;
            }
        }

        return true;
    }

    private static void Write(BlockingCollection<Tuple<string, byte[]>> queue, int width, int height)
    {
        foreach (var item in queue.GetConsumingEnumerable())
        {
            using (var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                var data = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                Marshal.Copy(item.Item2, 0, data.Scan0, item.Item2.Length);
                bitmap.UnlockBits(data);
                bitmap.Save(item.Item1, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Images.Webm;
using PlayniteAchievements.Tests.TestInfrastructure;
using PlayniteAchievements.Views.Helpers.Gif;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PlayniteAchievements.Services.Images.Tests
{
    /// <summary>
    /// Decodes real VP8 and VP9 files through the Windows decoders. The fixtures are generated
    /// with known content: the alpha files are opaque (200, 40, 40) on the left half and fully
    /// transparent on the right; the odd file is a solid (40, 120, 200) 33x25 frame; the ramp is
    /// 10-bit gray rising one 8-bit level every 4 pixels from 16 to 80. Inconclusive on a machine
    /// without the decoders.
    /// </summary>
    [TestClass]
    public class WebmDecodeTests
    {
        [TestMethod]
        public void Vp9Alpha_DecodesColorAndCutsOutTheTransparentHalf()
        {
            AssertAlphaFixture("vp9-alpha.webm", WebmCodec.Vp9);
        }

        [TestMethod]
        public void Vp8Alpha_DecodesColorAndCutsOutTheTransparentHalf()
        {
            AssertAlphaFixture("vp8-alpha.webm", WebmCodec.Vp8);
        }

        [TestMethod]
        public void OddSize_IsCroppedFromThePaddedDecoderOutput()
        {
            var image = Load("vp9-odd.webm", WebmCodec.Vp9);
            Assert.AreEqual(33, image.Width);
            Assert.AreEqual(25, image.Height);

            using (var decoder = new WebmFrameDecoder(image))
            {
                decoder.Decode(0);
                Assert.AreEqual(33 * 25, decoder.Pixels.Length);
                AssertColor(decoder.Pixels[0], 255, 40, 120, 200, "top-left");

                // Rows past the first are read at the padded stride; a wrong stride would shear
                // this pixel into another color. The last chroma cell itself is skipped: the
                // encoder's own output is off there at odd sizes (ffmpeg decodes it the same).
                AssertColor(decoder.Pixels[22 * image.Width + 30], 255, 40, 120, 200, "near bottom-right");
                Assert.AreEqual(255, (decoder.Pixels[image.Width * image.Height - 1] >> 24) & 0xFF, "last pixel alpha");
            }
        }

        [TestMethod]
        public void LoopRestart_ReproducesTheSameFrame()
        {
            var image = Load("vp9-alpha.webm", WebmCodec.Vp9);
            using (var decoder = new WebmFrameDecoder(image))
            {
                decoder.Decode(1);
                var first = (int[])decoder.Pixels.Clone();
                decoder.Decode(2);
                decoder.Decode(1);
                CollectionAssert.AreEqual(first, decoder.Pixels);
            }
        }

        [TestMethod]
        public void TenBitRamp_KeepsInBetweenShadesThroughDithering()
        {
            var image = Load("vp9-10bit-ramp.webm", WebmCodec.Vp9);
            using (var decoder = new WebmFrameDecoder(image))
            {
                decoder.Decode(0);

                // Average each column over the rows: dithering turns the two sub-byte bits into a
                // mix of neighboring levels, so the averages climb in fractional steps where a
                // plain 8-bit conversion would stair-step four pixels at a time.
                var means = Enumerable.Range(0, image.Width)
                    .Select(x => Enumerable.Range(0, image.Height).Average(y => decoder.Pixels[y * image.Width + x] & 0xFF))
                    .ToArray();
                var fractional = means.Count(mean => Math.Abs(mean - Math.Round(mean)) > 0.1);

                Assert.AreEqual(16 + 0.0, means[8], 2.5);
                Assert.AreEqual(16 + 60.0, means[240], 2.5);
                Assert.IsTrue(fractional > image.Width / 4, $"Only {fractional} of {image.Width} columns fell between levels.");
            }
        }

        [TestMethod]
        public void Still_IsTheFirstFrameScaledToTheDecodeWidth()
        {
            RequireDecoder(WebmCodec.Vp9);
            var full = WebmStill.TryDecode(FixturePath("vp9-alpha.webm"), 0);
            var scaled = WebmStill.TryDecode(FixturePath("vp9-alpha.webm"), 32);

            Assert.IsNotNull(full);
            Assert.IsTrue(full.IsFrozen);
            Assert.AreEqual(64, full.PixelWidth);
            Assert.AreEqual(32, scaled.PixelWidth);
            Assert.AreEqual(24, scaled.PixelHeight);
            Assert.IsNull(WebmStill.TryDecode(FixturePath("missing.webm"), 0));
        }

        [TestMethod]
        [DoNotParallelize]
        public void Player_PlaysWebmThroughTheSharedBitmap()
        {
            RequireDecoder(WebmCodec.Vp9);
            var bytes = File.ReadAllBytes(FixturePath("vp9-alpha.webm"));
            RunOnDispatcher(async dispatcher =>
            {
                var player = await GifPlayer.CreateAsync(bytes, dispatcher);
                try
                {
                    Assert.AreEqual(3, player.FrameCount);
                    Assert.AreEqual(64, player.Bitmap.PixelWidth);

                    var pixel = new int[1];
                    player.Bitmap.CopyPixels(new System.Windows.Int32Rect(8, 8, 1, 1), pixel, 4, 0);
                    AssertColor(pixel[0], 255, 200, 40, 40, "left");
                    player.Bitmap.CopyPixels(new System.Windows.Int32Rect(56, 8, 1, 1), pixel, 4, 0);
                    Assert.AreEqual(0, (pixel[0] >> 24) & 0xFF, "The transparent half rendered opaque.");

                    // 100 ms frames: a viewer for half a second must advance playback.
                    var presents = 0;
                    player.Bitmap.Changed += (sender, e) => presents++;
                    player.SetViewerActive(true);
                    await Task.Delay(500);
                    player.SetViewerActive(false);
                    Assert.IsTrue(presents >= 2, $"Presented {presents} frames in 500 ms.");
                }
                finally
                {
                    player.Dispose();
                }
            });
        }

        private static void AssertAlphaFixture(string name, WebmCodec codec)
        {
            var image = Load(name, codec);
            Assert.AreEqual(3, image.Frames.Length);
            Assert.IsTrue(image.Frames.All(frame => frame.HasAlpha));
            Assert.AreEqual(100, image.Frames[0].DelayMs);

            using (var decoder = new WebmFrameDecoder(image))
            {
                for (var frame = 0; frame < image.Frames.Length; frame++)
                {
                    decoder.Decode(frame);
                    AssertColor(decoder.Pixels[8 * image.Width + 8], 255, 200, 40, 40, $"frame {frame} left");
                    Assert.AreEqual(0, (decoder.Pixels[8 * image.Width + 56] >> 24) & 0xFF, $"frame {frame} right alpha");
                }
            }
        }

        private static void AssertColor(int pixel, int alpha, int red, int green, int blue, string where)
        {
            // VP8/VP9 are lossy and YUV rounds; a few levels either way is the same color.
            const int tolerance = 6;
            Assert.AreEqual(alpha, (pixel >> 24) & 0xFF, tolerance, $"{where} alpha");
            Assert.AreEqual(red, (pixel >> 16) & 0xFF, tolerance, $"{where} red");
            Assert.AreEqual(green, (pixel >> 8) & 0xFF, tolerance, $"{where} green");
            Assert.AreEqual(blue, pixel & 0xFF, tolerance, $"{where} blue");
        }

        private static WebmImage Load(string name, WebmCodec codec)
        {
            RequireDecoder(codec);
            var image = WebmContainer.Parse(File.ReadAllBytes(FixturePath(name)));
            Assert.AreEqual(codec, image.Codec);
            return image;
        }

        private static void RequireDecoder(WebmCodec codec)
        {
            if (!WebmVideoDecoder.IsAvailable(codec))
            {
                Assert.Inconclusive($"Windows has no {codec} decoder on this machine.");
            }
        }

        private static string FixturePath(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", "Webm", name);
        }

        private static void RunOnDispatcher(Func<Dispatcher, Task> body)
        {
            LocalizationAssemblyInitializer.RunOnSta(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var task = body(dispatcher);
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            });
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Tests.TestInfrastructure;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Views.Helpers.Gif;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class NativeGifAnimationTests
    {
        [TestMethod]
        public async Task PayloadCache_SharesCompressedBytesOnlyWhileSourcesAreActive()
        {
            var path = CreateTempGifPayload(new byte[] { 1, 2, 3, 4 });
            try
            {
                var first = await NativeGifPayloadCache.AcquireAsync(path, CancellationToken.None);
                var second = await NativeGifPayloadCache.AcquireAsync(path, CancellationToken.None);
                try
                {
                    Assert.AreSame(first.PayloadReference, second.PayloadReference);
                    Assert.AreEqual(1, NativeGifPayloadCache.ActiveEntryCount);

                    first.Dispose();
                    Assert.AreEqual(1, NativeGifPayloadCache.ActiveEntryCount);
                    second.Dispose();
                    Assert.AreEqual(0, NativeGifPayloadCache.ActiveEntryCount);
                }
                finally
                {
                    first.Dispose();
                    second.Dispose();
                }
            }
            finally
            {
                DeleteTempPayload(path);
            }
        }

        [TestMethod]
        public async Task PayloadCache_ReplacedFileGetsANewPayloadWhileOldVisualIsAlive()
        {
            var path = CreateTempGifPayload(new byte[] { 1, 2, 3, 4 });
            NativeGifPayloadCache.Lease first = null;
            NativeGifPayloadCache.Lease replacement = null;
            try
            {
                first = await NativeGifPayloadCache.AcquireAsync(path, CancellationToken.None);
                File.WriteAllBytes(path, new byte[] { 9, 8, 7, 6, 5 });
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));

                replacement = await NativeGifPayloadCache.AcquireAsync(path, CancellationToken.None);

                Assert.AreNotSame(first.PayloadReference, replacement.PayloadReference);
                Assert.AreEqual(2, NativeGifPayloadCache.ActiveEntryCount);
            }
            finally
            {
                first?.Dispose();
                replacement?.Dispose();
                DeleteTempPayload(path);
            }

            Assert.AreEqual(0, NativeGifPayloadCache.ActiveEntryCount);
        }

        [TestMethod]
        public void GrayscaleView_TracksMutablePixelsAndPreservesAlpha()
        {
            LocalizationAssemblyInitializer.RunOnSta(() =>
            {
                var source = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                WritePixel(source, blue: 255, green: 0, red: 0, alpha: 128);
                var view = NativeGifAnimation.CreateGrayscaleView(source);

                var first = RenderPixel(view);
                Assert.AreEqual(first[0], first[1]);
                Assert.AreEqual(first[1], first[2]);
                Assert.IsTrue(first[3] >= 126 && first[3] <= 129, $"Alpha was {first[3]}.");

                WritePixel(source, blue: 255, green: 255, red: 255, alpha: 255);
                var second = RenderPixel(view);
                Assert.IsTrue(second[0] > first[0]);
                Assert.AreEqual(second[0], second[1]);
                Assert.AreEqual(second[1], second[2]);
                Assert.AreEqual(255, second[3]);
            });
        }

        [TestMethod]
        public void PlayerCache_SharesOnePlayerPerFileAndDisposesItWithTheLastLease()
        {
            var path = GifFixture.WriteTempGif(GifFixture.BuildSparseGif(64, 32, 3));
            try
            {
                RunOnDispatcher(async dispatcher =>
                {
                    var first = await GifPlayerCache.AcquireAsync(path, dispatcher, CancellationToken.None);
                    var second = await GifPlayerCache.AcquireAsync(path, dispatcher, CancellationToken.None);

                    Assert.AreSame(first.Player, second.Player);
                    Assert.AreEqual(64, first.Player.Bitmap.PixelWidth);
                    Assert.AreEqual(32, first.Player.Bitmap.PixelHeight);
                    Assert.AreEqual(1, GifPlayerCache.ActiveEntryCount);

                    first.Dispose();
                    Assert.AreEqual(1, GifPlayerCache.ActiveEntryCount);
                    second.Dispose();
                    Assert.AreEqual(0, GifPlayerCache.ActiveEntryCount);
                    Assert.AreEqual(0, NativeGifPayloadCache.ActiveEntryCount);
                });
            }
            finally
            {
                DeleteTempPayload(path);
            }
        }

        [TestMethod]
        public void PlayerCache_UnreadableFileFailsWithoutLeavingAnEntry()
        {
            var path = CreateTempGifPayload(new byte[] { 1, 2, 3, 4 });
            try
            {
                RunOnDispatcher(async dispatcher =>
                {
                    await AssertEx.ThrowsAsync<InvalidDataException>(
                        () => GifPlayerCache.AcquireAsync(path, dispatcher, CancellationToken.None));
                    Assert.AreEqual(0, GifPlayerCache.ActiveEntryCount);
                    Assert.AreEqual(0, NativeGifPayloadCache.ActiveEntryCount);
                });
            }
            finally
            {
                DeleteTempPayload(path);
            }
        }

        [TestMethod]
        [DoNotParallelize]
        public void Player_AdvancesOnlyWhileAViewerIsActive()
        {
            var bytes = GifFixture.BuildSparseGif(8, 8, 4);
            RunOnDispatcher(async dispatcher =>
            {
                var player = await GifPlayer.CreateAsync(bytes, dispatcher);
                var presents = 0;
                player.Bitmap.Changed += (sender, e) => presents++;

                await Task.Delay(300);
                Assert.AreEqual(0, presents, "A player without viewers presented frames.");

                player.SetViewerActive(true);
                await Task.Delay(1000);
                player.SetViewerActive(false);
                var whilePlaying = presents;

                // 40 ms frames for one second is 25; allow for a loaded test machine.
                Assert.IsTrue(whilePlaying >= 15 && whilePlaying <= 27, $"Presented {whilePlaying} frames in 1 s.");

                await Task.Delay(300);
                Assert.IsTrue(presents <= whilePlaying + 1, "The player kept presenting after its viewer left.");
                player.Dispose();
            });
        }

        [TestMethod]
        [DoNotParallelize]
        public void FrameClock_DeadlinesDoNotDrift()
        {
            // 50 frames at 20 ms, each deadline derived from the previous one, as the player does.
            const int frames = 50;
            var interval = GifFrameClock.FromMilliseconds(20);
            var done = new ManualResetEventSlim();
            var start = GifFrameClock.Now;
            var deadline = start;
            var fired = 0;
            var early = false;

            void Tick()
            {
                if (GifFrameClock.Now < deadline)
                {
                    early = true;
                }

                if (++fired == frames)
                {
                    done.Set();
                    return;
                }

                deadline += interval;
                GifFrameClock.Schedule(deadline, Tick);
            }

            deadline += interval;
            GifFrameClock.Schedule(deadline, Tick);
            Assert.IsTrue(done.Wait(5000), "The clock stopped firing.");

            var elapsedMs = (GifFrameClock.Now - start) * 1000.0 / Stopwatch.Frequency;
            Assert.IsFalse(early, "A callback ran before its deadline.");
            Assert.IsTrue(elapsedMs >= 1000 && elapsedMs < 1060, $"50 frames of 20 ms took {elapsedMs:F1} ms.");
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

        private static class AssertEx
        {
            internal static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
            {
                try
                {
                    await action();
                }
                catch (TException)
                {
                    return;
                }

                Assert.Fail($"Expected {typeof(TException).Name}.");
            }
        }

        private static string CreateTempGifPayload(byte[] bytes) => GifFixture.WriteTempGif(bytes);

        private static void DeleteTempPayload(string path) => GifFixture.DeleteTempPayload(path);

        private static void WritePixel(WriteableBitmap bitmap, byte blue, byte green, byte red, byte alpha)
        {
            bitmap.WritePixels(
                new Int32Rect(0, 0, 1, 1),
                new[] { blue, green, red, alpha },
                4,
                0);
        }

        private static byte[] RenderPixel(ImageSource source)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawImage(source, new Rect(0, 0, 1, 1));
            }

            var rendered = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            var pixel = new byte[4];
            rendered.CopyPixels(pixel, 4, 0);
            return pixel;
        }
    }
}

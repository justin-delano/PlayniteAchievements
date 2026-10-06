using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Tests.TestInfrastructure;
using PlayniteAchievements.Views.Helpers.Gif;
using System;
using System.Linq;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class GifCanvasTests
    {
        private const int Red = unchecked((int)0xFFFF0000);
        private const int Green = unchecked((int)0xFF00FF00);
        private const int Blue = unchecked((int)0xFF0000FF);
        private static readonly int[] Palette = { 0x000000, 0xFF0000, 0x00FF00, 0x0000FF };

        [TestMethod]
        public void Parse_KeepsReportedDimensionsAndEveryFrame()
        {
            // The user-reported 1727x289, 315-frame background, as 1x1 frames on the full canvas.
            var image = GifImage.Parse(GifFixture.BuildSparseGif(1727, 289, 315));

            Assert.AreEqual(1727, image.Width);
            Assert.AreEqual(289, image.Height);
            Assert.AreEqual(315, image.Frames.Length);
            Assert.IsTrue(image.Frames.All(frame => frame.DelayMs == 40));
        }

        [TestMethod]
        public void Parse_HonorsShortDelaysAndDefaultsOnlyZero()
        {
            var bytes = GifFixture.BuildGif(
                1,
                1,
                Palette,
                Solid(1, 1, 1, delay: 0),
                Solid(1, 1, 1, delay: 1),
                Solid(1, 1, 1, delay: 2));

            var delays = GifImage.Parse(bytes).Frames.Select(frame => frame.DelayMs).ToArray();

            CollectionAssert.AreEqual(new[] { 100, 10, 20 }, delays);
        }

        [TestMethod]
        public void Render_DrawsOpaquePixelsAndLeavesTransparentOnesUntouched()
        {
            var bytes = GifFixture.BuildGif(
                2,
                1,
                Palette,
                Solid(2, 1, 1),
                new GifFixture.Frame
                {
                    Width = 2,
                    Height = 1,
                    Indices = new byte[] { 0, 2 },
                    TransparentIndex = 0
                });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);
            canvas.Render(1);

            CollectionAssert.AreEqual(new[] { Red, Green }, canvas.Pixels);
        }

        [TestMethod]
        public void Render_RestoreBackgroundClearsOnlyThePreviousFrameRect()
        {
            var bytes = GifFixture.BuildGif(
                2,
                1,
                Palette,
                Solid(2, 1, 1),
                new GifFixture.Frame { Left = 1, Width = 1, Height = 1, Indices = new byte[] { 2 }, Disposal = 2 },
                new GifFixture.Frame { Width = 1, Height = 1, Indices = new byte[] { 3 } });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);
            canvas.Render(1);
            CollectionAssert.AreEqual(new[] { Red, Green }, canvas.Pixels);

            canvas.Render(2);
            CollectionAssert.AreEqual(new[] { Blue, 0 }, canvas.Pixels);
            Assert.AreEqual(new System.Windows.Int32Rect(0, 0, 2, 1), canvas.DirtyRect);
        }

        [TestMethod]
        public void Render_RestorePreviousPutsBackTheCoveredPixels()
        {
            var bytes = GifFixture.BuildGif(
                2,
                1,
                Palette,
                Solid(2, 1, 1),
                new GifFixture.Frame { Width = 1, Height = 1, Indices = new byte[] { 2 }, Disposal = 3 },
                new GifFixture.Frame { Left = 1, Width = 1, Height = 1, Indices = new byte[] { 3 } });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);
            canvas.Render(1);
            CollectionAssert.AreEqual(new[] { Green, Red }, canvas.Pixels);

            canvas.Render(2);
            CollectionAssert.AreEqual(new[] { Red, Blue }, canvas.Pixels);
        }

        [TestMethod]
        public void Render_PlacesInterlacedRowsInDisplayOrder()
        {
            var indices = Enumerable.Range(0, 9).Select(row => (byte)(row % 3 + 1)).ToArray();
            var bytes = GifFixture.BuildGif(
                1,
                9,
                Palette,
                new GifFixture.Frame { Width = 1, Height = 9, Indices = indices, Interlaced = true });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);

            var expected = indices.Select(index => index == 1 ? Red : index == 2 ? Green : Blue).ToArray();
            CollectionAssert.AreEqual(expected, canvas.Pixels);
        }

        [TestMethod]
        public void Render_LoopRestartBeginsFromAClearedCanvas()
        {
            var bytes = GifFixture.BuildGif(
                2,
                1,
                Palette,
                new GifFixture.Frame { Width = 1, Height = 1, Indices = new byte[] { 1 } },
                new GifFixture.Frame { Left = 1, Width = 1, Height = 1, Indices = new byte[] { 2 } });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);
            canvas.Render(1);
            canvas.Render(0);

            CollectionAssert.AreEqual(new[] { Red, 0 }, canvas.Pixels);
        }

        [TestMethod]
        public void Render_TruncatedFrameDrawsTheDecodedPixelsOnly()
        {
            var bytes = GifFixture.BuildGif(
                4,
                1,
                Palette,
                new GifFixture.Frame { Width = 4, Height = 1, Indices = new byte[] { 1, 2, 3, 1 }, EncodedPixels = 2 });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);

            CollectionAssert.AreEqual(new[] { Red, Green, 0, 0 }, canvas.Pixels);
        }

        [TestMethod]
        public void Render_ClipsFramesThatExtendPastTheCanvas()
        {
            var bytes = GifFixture.BuildGif(
                2,
                2,
                Palette,
                new GifFixture.Frame { Left = 1, Top = 1, Width = 2, Height = 2, Indices = new byte[] { 1, 2, 3, 3 } });
            var canvas = new GifCanvas(GifImage.Parse(bytes));

            canvas.Render(0);

            CollectionAssert.AreEqual(new[] { 0, 0, 0, Red }, canvas.Pixels);
            Assert.AreEqual(new System.Windows.Int32Rect(0, 0, 2, 2), canvas.DirtyRect);
        }

        [TestMethod]
        [DoNotParallelize]
        public void Render_SteadyStatePlaybackAllocatesNothing()
        {
            const int width = 512;
            const int height = 256;
            var frames = Enumerable.Range(0, 8)
                .Select(i => new GifFixture.Frame
                {
                    Width = width,
                    Height = height,
                    Indices = Enumerable.Range(0, width * height).Select(p => (byte)((p + i) % 4)).ToArray(),
                    Disposal = i % 2 == 0 ? 3 : 2,
                    Interlaced = i % 3 == 0
                })
                .ToArray();
            var canvas = new GifCanvas(GifImage.Parse(GifFixture.BuildGif(width, height, Palette, frames)));
            for (var i = 0; i < frames.Length; i++)
            {
                canvas.Render(i);
            }

            AppDomain.MonitoringIsEnabled = true;
            var before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            for (var loop = 0; loop < 10; loop++)
            {
                for (var i = 0; i < frames.Length; i++)
                {
                    canvas.Render(i);
                }
            }

            var allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before;

            // 80 full-canvas frames; one per-frame index buffer alone would be 128 KB each. The
            // bound leaves room for unrelated allocations elsewhere in the process.
            Assert.IsTrue(allocated < 256 * 1024, $"Allocated {allocated} bytes over 80 frames.");
        }

        private static GifFixture.Frame Solid(int width, int height, byte index, int delay = 4)
        {
            return new GifFixture.Frame
            {
                Width = width,
                Height = height,
                Indices = Enumerable.Repeat(index, width * height).ToArray(),
                DelayCentiseconds = delay
            };
        }
    }
}

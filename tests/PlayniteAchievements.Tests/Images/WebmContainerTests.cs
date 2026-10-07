using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Images.Webm;
using System;
using System.IO;

namespace PlayniteAchievements.Services.Images.Tests
{
    /// <summary>
    /// Covers the WebM container reader against hand-built files, so every structural case is
    /// exact: frame order, durations, where alpha comes from, and which blocks are ignored.
    /// Frame payloads are placeholder bytes; nothing here decodes video.
    /// </summary>
    [TestClass]
    public class WebmContainerTests
    {
        private static readonly byte[] ColorA = { 0x11, 0x12, 0x13 };
        private static readonly byte[] ColorB = { 0x21, 0x22 };
        private static readonly byte[] ColorC = { 0x31 };
        private static readonly byte[] AlphaA = { 0xA1, 0xA2 };
        private static readonly byte[] AlphaB = { 0xB1 };

        [TestMethod]
        public void Parse_ReadsTrackSizeCodecAndFramesInOrder()
        {
            var bytes = Build("V_VP9", AlphaFrames(), unknownSizeCluster: false, audioTrackFirst: false);

            var image = WebmContainer.Parse(bytes);

            Assert.AreEqual(WebmCodec.Vp9, image.Codec);
            Assert.AreEqual(64, image.Width);
            Assert.AreEqual(48, image.Height);
            Assert.AreEqual(3, image.Frames.Length);
            CollectionAssert.AreEqual(ColorA, Slice(bytes, image.Frames[0].ColorOffset, image.Frames[0].ColorLength));
            CollectionAssert.AreEqual(ColorB, Slice(bytes, image.Frames[1].ColorOffset, image.Frames[1].ColorLength));
            CollectionAssert.AreEqual(ColorC, Slice(bytes, image.Frames[2].ColorOffset, image.Frames[2].ColorLength));
        }

        [TestMethod]
        public void Parse_TakesAlphaFromBlockAdditionalOfTheSameGroup()
        {
            var bytes = Build("V_VP8", AlphaFrames(), unknownSizeCluster: false, audioTrackFirst: false);

            var image = WebmContainer.Parse(bytes);

            Assert.AreEqual(WebmCodec.Vp8, image.Codec);
            CollectionAssert.AreEqual(AlphaA, Slice(bytes, image.Frames[0].AlphaOffset, image.Frames[0].AlphaLength));
            CollectionAssert.AreEqual(AlphaB, Slice(bytes, image.Frames[1].AlphaOffset, image.Frames[1].AlphaLength));

            // A frame stored as a SimpleBlock has no alpha, rather than inheriting the previous one.
            Assert.IsFalse(image.Frames[2].HasAlpha);
        }

        [TestMethod]
        public void Parse_DelaysComeFromTimecodeDifferencesAndTheLastRepeatsThePrevious()
        {
            var image = WebmContainer.Parse(Build("V_VP9", AlphaFrames(), unknownSizeCluster: false, audioTrackFirst: false));

            Assert.AreEqual(40, image.Frames[0].DelayMs);
            Assert.AreEqual(60, image.Frames[1].DelayMs);
            Assert.AreEqual(60, image.Frames[2].DelayMs);
        }

        [TestMethod]
        public void Parse_ScalesTimecodesByTheSegmentTimecodeScale()
        {
            // Timecodes in units of 2 ms: the 0, 40, 100 timecodes become 0, 80, 200 ms.
            var bytes = WebmTestWriter.Build("V_VP9", 64, 48, AlphaFrames(), false, false, timecodeScale: 2000000);

            var image = WebmContainer.Parse(bytes);

            Assert.AreEqual(80, image.Frames[0].DelayMs);
            Assert.AreEqual(120, image.Frames[1].DelayMs);
        }

        [TestMethod]
        public void Parse_ClampsZeroDelaysToAMinimum()
        {
            var frames = new[]
            {
                new WebmTestFrame { Color = ColorA, Timecode = 0 },
                new WebmTestFrame { Color = ColorB, Timecode = 0 }
            };

            var image = WebmContainer.Parse(WebmTestWriter.Build("V_VP9", 8, 8, frames, false, false, 1000000));

            Assert.IsTrue(image.Frames[0].DelayMs > 0);
        }

        [TestMethod]
        public void Parse_HandlesUnknownSizeClustersAsStreamingMuxersWriteThem()
        {
            var image = WebmContainer.Parse(Build("V_VP9", AlphaFrames(), unknownSizeCluster: true, audioTrackFirst: false));

            Assert.AreEqual(3, image.Frames.Length);
            Assert.IsTrue(image.Frames[0].HasAlpha);
        }

        [TestMethod]
        public void Parse_IgnoresBlocksOfOtherTracks()
        {
            var bytes = Build("V_VP9", AlphaFrames(), unknownSizeCluster: false, audioTrackFirst: true);

            var image = WebmContainer.Parse(bytes);

            Assert.AreEqual(3, image.Frames.Length);
            CollectionAssert.AreEqual(ColorA, Slice(bytes, image.Frames[0].ColorOffset, image.Frames[0].ColorLength));
        }

        [TestMethod]
        public void Parse_RejectsFilesThatAreNotPlayableWebm()
        {
            Assert.ThrowsException<InvalidDataException>(() => WebmContainer.Parse(new byte[] { 0x47, 0x49, 0x46, 0x38 }));
            Assert.ThrowsException<InvalidDataException>(() => WebmContainer.Parse(Build("V_AV1", AlphaFrames(), false, false)));
            Assert.ThrowsException<InvalidDataException>(() =>
                WebmContainer.Parse(WebmTestWriter.Build("V_VP9", 64, 48, new WebmTestFrame[0], false, false, 1000000)));
        }

        [TestMethod]
        public void Parse_KeepsFramesReadBeforeATruncation()
        {
            var bytes = Build("V_VP9", AlphaFrames(), unknownSizeCluster: true, audioTrackFirst: false);
            var image = WebmContainer.Parse(bytes);
            var cut = new byte[image.Frames[2].ColorOffset - 1];
            Array.Copy(bytes, cut, cut.Length);

            Assert.AreEqual(2, WebmContainer.Parse(cut).Frames.Length);
        }

        [TestMethod]
        public void IsAnimated_AndTryReadSize_ReadTheFileOnDisk()
        {
            var directory = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "WebmContainerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var animated = Path.Combine(directory, "animated.webm");
                var still = Path.Combine(directory, "still.webm");
                File.WriteAllBytes(animated, Build("V_VP9", AlphaFrames(), false, false));
                File.WriteAllBytes(still, WebmTestWriter.Build(
                    "V_VP9", 64, 48, new[] { new WebmTestFrame { Color = ColorA } }, false, false, 1000000));

                Assert.IsTrue(WebmContainer.IsAnimated(animated));
                Assert.IsFalse(WebmContainer.IsAnimated(still));
                Assert.IsFalse(WebmContainer.IsAnimated(Path.Combine(directory, "missing.webm")));
                Assert.IsTrue(WebmContainer.TryReadSize(still, out var width, out var height));
                Assert.AreEqual(64, width);
                Assert.AreEqual(48, height);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static WebmTestFrame[] AlphaFrames()
        {
            return new[]
            {
                new WebmTestFrame { Color = ColorA, Alpha = AlphaA, Timecode = 0 },
                new WebmTestFrame { Color = ColorB, Alpha = AlphaB, Timecode = 40 },
                new WebmTestFrame { Color = ColorC, Timecode = 100 }
            };
        }

        private static byte[] Build(string codecId, WebmTestFrame[] frames, bool unknownSizeCluster, bool audioTrackFirst)
        {
            return WebmTestWriter.Build(codecId, 64, 48, frames, unknownSizeCluster, audioTrackFirst, 1000000);
        }

        private static byte[] Slice(byte[] bytes, int offset, int length)
        {
            var slice = new byte[length];
            Array.Copy(bytes, offset, slice, 0, length);
            return slice;
        }
    }
}

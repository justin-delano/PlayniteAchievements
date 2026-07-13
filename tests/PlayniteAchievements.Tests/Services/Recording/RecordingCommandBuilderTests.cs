using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Recording;

namespace PlayniteAchievements.Services.Tests.Recording
{
    [TestClass]
    public class RecordingCommandBuilderTests
    {
        private static RecordingCommandBuilder.CaptureOptions DefaultOptions()
        {
            return new RecordingCommandBuilder.CaptureOptions
            {
                Backend = RecordingCaptureBackend.Gdigrab,
                Fps = 30,
                MonitorX = -1920,
                MonitorY = 120,
                MonitorWidth = 1920,
                MonitorHeight = 1080,
                MonitorIndex = 1,
                Resolution = RecordingResolution.Native,
                EncoderArguments = "-c:v libx264 -preset ultrafast -crf 23",
                SegmentSeconds = 5,
                BufferDirectory = @"C:\Data\RecordingBuffer\20260101-120000"
            };
        }

        [TestMethod]
        public void Capture_Gdigrab_IncludesMonitorOffsetsAndSize()
        {
            var args = RecordingCommandBuilder.BuildCaptureArguments(DefaultOptions());

            StringAssert.Contains(args, "-f gdigrab");
            StringAssert.Contains(args, "-framerate 30");
            StringAssert.Contains(args, "-offset_x -1920");
            StringAssert.Contains(args, "-offset_y 120");
            StringAssert.Contains(args, "-video_size 1920x1080");
            StringAssert.Contains(args, "-draw_mouse 1 -i desktop");
        }

        [TestMethod]
        public void Capture_OddMonitorDimensions_RoundDownToEven()
        {
            var options = DefaultOptions();
            options.MonitorWidth = 1921;
            options.MonitorHeight = 1081;

            var args = RecordingCommandBuilder.BuildCaptureArguments(options);

            StringAssert.Contains(args, "-video_size 1920x1080");
        }

        [TestMethod]
        public void Capture_SegmentsWithForcedKeyframesAndStrftimeNames()
        {
            var args = RecordingCommandBuilder.BuildCaptureArguments(DefaultOptions());

            StringAssert.Contains(args, "-pix_fmt yuv420p");
            StringAssert.Contains(args, "-g 30 -keyint_min 30");
            StringAssert.Contains(args, "-force_key_frames \"expr:gte(t,n_forced*1)\"");
            StringAssert.Contains(args, "-f segment -segment_time 5 -reset_timestamps 1 -strftime 1");
            StringAssert.Contains(args, @"C:\Data\RecordingBuffer\20260101-120000\seg_%Y%m%d-%H%M%S.ts");
        }

        [TestMethod]
        public void Capture_NativeResolution_HasNoScaleFilter()
        {
            var args = RecordingCommandBuilder.BuildCaptureArguments(DefaultOptions());

            Assert.IsFalse(args.Contains("-vf"), args);
        }

        [TestMethod]
        public void Capture_FixedResolutions_UseEvenWidthScaleFilter()
        {
            var options = DefaultOptions();

            options.Resolution = RecordingResolution.P1080;
            StringAssert.Contains(RecordingCommandBuilder.BuildCaptureArguments(options), "-vf scale=-2:1080");

            options.Resolution = RecordingResolution.P720;
            StringAssert.Contains(RecordingCommandBuilder.BuildCaptureArguments(options), "-vf scale=-2:720");
        }

        [TestMethod]
        public void Capture_Ddagrab_UsesMonitorIndexScopedFilter()
        {
            var options = DefaultOptions();
            options.Backend = RecordingCaptureBackend.Ddagrab;

            var args = RecordingCommandBuilder.BuildCaptureArguments(options);

            StringAssert.Contains(args, "ddagrab=output_idx=1:framerate=30");
            Assert.IsFalse(args.Contains("gdigrab"), args);
        }

        [TestMethod]
        public void Encoder_Auto_PrefersNvencThenQsvThenAmfThenX264()
        {
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(
                    RecordingEncoder.Auto, new[] { "libx264", "h264_amf", "h264_qsv", "h264_nvenc" }),
                "h264_nvenc");
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(
                    RecordingEncoder.Auto, new[] { "libx264", "h264_amf", "h264_qsv" }),
                "h264_qsv");
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(
                    RecordingEncoder.Auto, new[] { "libx264", "h264_amf" }),
                "h264_amf");
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(
                    RecordingEncoder.Auto, new[] { "libx264" }),
                "libx264");
        }

        [TestMethod]
        public void Encoder_Auto_WithoutProbeFallsBackToX264()
        {
            var args = RecordingCommandBuilder.BuildEncoderArguments(RecordingEncoder.Auto, null);

            StringAssert.Contains(args, "libx264");
            StringAssert.Contains(args, "-preset ultrafast -crf 23");
        }

        [TestMethod]
        public void Encoder_ExplicitChoice_IsHonoredWithoutProbe()
        {
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(RecordingEncoder.Nvenc, null),
                "h264_nvenc -preset p4 -rc vbr -cq 23 -b:v 0");
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(RecordingEncoder.Qsv, null),
                "h264_qsv -global_quality 23");
            StringAssert.Contains(
                RecordingCommandBuilder.BuildEncoderArguments(RecordingEncoder.Amf, null),
                "h264_amf -quality speed -rc cqp");
        }

        [TestMethod]
        public void Trim_StreamCopy_SeeksConcatAndFaststarts()
        {
            var args = RecordingCommandBuilder.BuildTrimArguments(
                @"C:\buf\list.txt", 2.5, 17.25, @"D:\out\clip.mp4");

            StringAssert.Contains(args, "-f concat -safe 0 -ss 2.5 -i \"C:\\buf\\list.txt\"");
            StringAssert.Contains(args, "-t 17.25 -c copy -movflags +faststart \"D:\\out\\clip.mp4\"");
            Assert.IsFalse(args.Contains("libx264"), args);
        }

        [TestMethod]
        public void Trim_Reencode_UsesVeryfastCrf20()
        {
            var args = RecordingCommandBuilder.BuildTrimArguments(
                @"C:\buf\list.txt", 0, 15, @"D:\out\clip.mp4", reencode: true);

            StringAssert.Contains(args, "-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p");
            Assert.IsFalse(args.Contains("-c copy"), args);
        }

        [TestMethod]
        public void Trim_OffsetsAreInvariantCulture()
        {
            var args = RecordingCommandBuilder.BuildTrimArguments(
                @"C:\buf\list.txt", 1.125, 9.5, @"D:\out\clip.mp4");

            StringAssert.Contains(args, "-ss 1.125");
            StringAssert.Contains(args, "-t 9.5");
        }

        [TestMethod]
        public void ConcatList_QuotesAndEscapesApostrophes()
        {
            var content = RecordingCommandBuilder.BuildConcatListContent(new[]
            {
                @"C:\buf\seg_20260101-120000.ts",
                @"C:\buf\it's here\seg_20260101-120005.ts"
            });

            StringAssert.Contains(content, "file 'C:\\buf\\seg_20260101-120000.ts'\n");
            StringAssert.Contains(content, "file 'C:\\buf\\it'\\''s here\\seg_20260101-120005.ts'\n");
        }

        [TestMethod]
        public void ConcatList_SkipsBlankEntries()
        {
            var content = RecordingCommandBuilder.BuildConcatListContent(new[] { null, "", @"C:\a.ts" });

            Assert.AreEqual("file 'C:\\a.ts'\n", content);
        }

        [TestMethod]
        public void SmokeTest_IsOneSecondTinyGrabToNullMuxer()
        {
            var args = RecordingCommandBuilder.BuildSmokeTestArguments(10, 20);

            StringAssert.Contains(args, "-f gdigrab");
            StringAssert.Contains(args, "-offset_x 10 -offset_y 20");
            StringAssert.Contains(args, "-video_size 64x64");
            StringAssert.Contains(args, "-t 1 -f null -");
        }
    }
}

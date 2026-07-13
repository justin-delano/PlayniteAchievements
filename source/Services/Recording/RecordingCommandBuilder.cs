using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Recording
{
    /// <summary>
    /// Pure builders for every ffmpeg command line the recording feature runs: the rolling
    /// segment capture, the concat/trim clip export, validation probes, and the concat list
    /// file content. No process or filesystem access — fully unit-testable.
    /// </summary>
    internal static class RecordingCommandBuilder
    {
        /// <summary>Segment filename pattern written by the capture command (strftime, local time).</summary>
        public const string SegmentFilePrefix = "seg_";

        public const string SegmentFileExtension = ".ts";

        private const string SegmentStrftimePattern = "seg_%Y%m%d-%H%M%S.ts";

        public sealed class CaptureOptions
        {
            public RecordingCaptureBackend Backend { get; set; }

            public int Fps { get; set; }

            /// <summary>Monitor bounds in physical pixels (virtual-desktop coordinates for gdigrab).</summary>
            public int MonitorX { get; set; }

            public int MonitorY { get; set; }

            public int MonitorWidth { get; set; }

            public int MonitorHeight { get; set; }

            /// <summary>Zero-based output index for ddagrab (best-effort mapping from the monitor).</summary>
            public int MonitorIndex { get; set; }

            public RecordingResolution Resolution { get; set; }

            /// <summary>Encoder argument fragment from <see cref="BuildEncoderArguments"/>.</summary>
            public string EncoderArguments { get; set; }

            public int SegmentSeconds { get; set; }

            public string BufferDirectory { get; set; }
        }

        /// <summary>
        /// The rolling capture command: grabs the monitor, encodes with 1s forced keyframes (so
        /// stream-copy trims snap at most 1s early), and writes strftime-named MPEG-TS segments —
        /// a killed ffmpeg leaves every fully written segment decodable.
        /// </summary>
        public static string BuildCaptureArguments(CaptureOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var fps = Math.Max(1, options.Fps);
            // yuv420p requires even dimensions; round the capture size down to even so a native
            // capture of an odd-sized monitor region doesn't fail. The scale filter (-2) handles
            // evenness itself for the fixed resolutions.
            var width = Math.Max(2, options.MonitorWidth & ~1);
            var height = Math.Max(2, options.MonitorHeight & ~1);

            var builder = new StringBuilder();
            builder.Append("-hide_banner -loglevel warning -y");

            if (options.Backend == RecordingCaptureBackend.Ddagrab)
            {
                builder.Append(Invariant(
                    " -f lavfi -i \"ddagrab=output_idx={0}:framerate={1},hwdownload,format=bgra\"",
                    Math.Max(0, options.MonitorIndex),
                    fps));
            }
            else
            {
                builder.Append(Invariant(
                    " -f gdigrab -framerate {0} -offset_x {1} -offset_y {2} -video_size {3}x{4} -draw_mouse 1 -i desktop",
                    fps,
                    options.MonitorX,
                    options.MonitorY,
                    width,
                    height));
            }

            var scale = ScaleFilter(options.Resolution);
            if (scale != null)
            {
                builder.Append(" -vf ").Append(scale);
            }

            if (!string.IsNullOrWhiteSpace(options.EncoderArguments))
            {
                builder.Append(' ').Append(options.EncoderArguments.Trim());
            }

            builder.Append(" -pix_fmt yuv420p");
            builder.Append(Invariant(" -g {0} -keyint_min {0}", fps));
            builder.Append(" -force_key_frames \"expr:gte(t,n_forced*1)\"");
            builder.Append(Invariant(" -f segment -segment_time {0} -reset_timestamps 1 -strftime 1", Math.Max(1, options.SegmentSeconds)));
            builder.Append(" \"").Append(TrimTrailingSeparators(options.BufferDirectory)).Append('\\').Append(SegmentStrftimePattern).Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// Encoder argument fragment for the capture command. Auto prefers hardware encoders
        /// (NVENC &gt; QSV &gt; AMF) that appear in the probed encoder set, falling back to
        /// libx264; explicit choices are honored as-is.
        /// </summary>
        public static string BuildEncoderArguments(RecordingEncoder encoder, IReadOnlyCollection<string> availableEncoders)
        {
            switch (encoder)
            {
                case RecordingEncoder.Nvenc:
                    return NvencArguments;
                case RecordingEncoder.Qsv:
                    return QsvArguments;
                case RecordingEncoder.Amf:
                    return AmfArguments;
                case RecordingEncoder.X264:
                    return X264Arguments;
                case RecordingEncoder.Auto:
                default:
                    if (Contains(availableEncoders, "h264_nvenc"))
                    {
                        return NvencArguments;
                    }

                    if (Contains(availableEncoders, "h264_qsv"))
                    {
                        return QsvArguments;
                    }

                    if (Contains(availableEncoders, "h264_amf"))
                    {
                        return AmfArguments;
                    }

                    return X264Arguments;
            }
        }

        private const string X264Arguments = "-c:v libx264 -preset ultrafast -crf 23";
        private const string NvencArguments = "-c:v h264_nvenc -preset p4 -rc vbr -cq 23 -b:v 0";
        private const string QsvArguments = "-c:v h264_qsv -global_quality 23";
        private const string AmfArguments = "-c:v h264_amf -quality speed -rc cqp";

        /// <summary>
        /// The clip export command: concat demuxer over the selected segments, seek/trim, and an
        /// mp4 with faststart. The default stream-copy is near-free while a game runs (trim start
        /// snaps at most 1s early thanks to the forced keyframes); the re-encode variant is the
        /// retry path when the copy produces a broken or empty file.
        /// </summary>
        public static string BuildTrimArguments(
            string concatListPath,
            double startOffsetSeconds,
            double durationSeconds,
            string outputPath,
            bool reencode = false)
        {
            var codec = reencode
                ? "-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p"
                : "-c copy";
            return Invariant(
                "-hide_banner -loglevel warning -y -f concat -safe 0 -ss {0} -i \"{1}\" -t {2} {3} -movflags +faststart \"{4}\"",
                Seconds(startOffsetSeconds),
                concatListPath,
                Seconds(durationSeconds),
                codec,
                outputPath);
        }

        /// <summary>
        /// Content of the concat demuxer list file: one absolute path per line, single-quoted
        /// with embedded quotes escaped per the demuxer's quoting rules.
        /// </summary>
        public static string BuildConcatListContent(IEnumerable<string> segmentPaths)
        {
            var builder = new StringBuilder();
            foreach (var path in segmentPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                builder.Append("file '").Append(path.Replace("'", "'\\''")).Append("'\n");
            }

            return builder.ToString();
        }

        /// <summary>
        /// A 1-second, tiny gdigrab capture to the null muxer — verifies the ffmpeg build can
        /// actually grab the screen, without writing anything.
        /// </summary>
        public static string BuildSmokeTestArguments(int offsetX = 0, int offsetY = 0)
        {
            return Invariant(
                "-hide_banner -loglevel warning -f gdigrab -framerate 10 -offset_x {0} -offset_y {1} -video_size 64x64 -i desktop -t 1 -f null -",
                offsetX,
                offsetY);
        }

        public const string VersionProbeArguments = "-hide_banner -version";

        public const string EncodersProbeArguments = "-hide_banner -encoders";

        private static string ScaleFilter(RecordingResolution resolution)
        {
            switch (resolution)
            {
                case RecordingResolution.P1080:
                    return "scale=-2:1080";
                case RecordingResolution.P720:
                    return "scale=-2:720";
                default:
                    return null;
            }
        }

        private static bool Contains(IReadOnlyCollection<string> set, string encoder)
        {
            return set != null && set.Any(e => string.Equals(e?.Trim(), encoder, StringComparison.OrdinalIgnoreCase));
        }

        private static string Seconds(double value)
        {
            return Math.Max(0, value).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string TrimTrailingSeparators(string directory)
        {
            return (directory ?? string.Empty).TrimEnd('\\', '/');
        }

        private static string Invariant(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}

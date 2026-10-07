using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PlayniteAchievements.Services.Images.Tests
{
    /// <summary>One frame for <see cref="WebmTestWriter"/>: bitstream bytes and a cluster-relative timecode.</summary>
    internal sealed class WebmTestFrame
    {
        public byte[] Color;
        public byte[] Alpha;
        public short Timecode;
    }

    /// <summary>
    /// Writes minimal WebM files: an EBML header, an unknown-size Segment, one Info, one Tracks and
    /// one Cluster. Frames without alpha become SimpleBlocks; frames with alpha become BlockGroups
    /// carrying BlockAdditional ID 1, which is how muxers store a VP8/VP9 alpha stream.
    /// </summary>
    /// <remarks>Kept to C# 5 so it also runs under Windows PowerShell's Add-Type.</remarks>
    internal static class WebmTestWriter
    {
        public static byte[] Build(
            string codecId,
            int width,
            int height,
            IList<WebmTestFrame> frames,
            bool unknownSizeCluster,
            bool audioTrackFirst,
            long timecodeScale)
        {
            using (var output = new MemoryStream())
            {
                Element(output, 0x1A45DFA3, Concat(Text(0x4282, "webm"), Uint(0x4287, 4)));

                var tracks = new List<byte>();
                var videoTrackNumber = 1;
                if (audioTrackFirst)
                {
                    tracks.AddRange(Master(0xAE, Concat(Uint(0xD7, 1), Uint(0x83, 2), Text(0x86, "A_OPUS"))));
                    videoTrackNumber = 2;
                }

                tracks.AddRange(Master(0xAE, Concat(
                    Uint(0xD7, videoTrackNumber),
                    Uint(0x83, 1),
                    Text(0x86, codecId),
                    Master(0xE0, Concat(Uint(0xB0, width), Uint(0xBA, height), Uint(0x53C0, 1))))));

                var cluster = new List<byte>(Uint(0xE7, 0));
                for (var i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    if (audioTrackFirst)
                    {
                        // An audio block between video frames must not be read as a frame.
                        cluster.AddRange(Element(0xA3, Block(1, frame.Timecode, 0x80, new byte[] { 0xFC })));
                    }

                    if (frame.Alpha == null)
                    {
                        cluster.AddRange(Element(0xA3, Block(videoTrackNumber, frame.Timecode, (byte)(i == 0 ? 0x80 : 0x00), frame.Color)));
                    }
                    else
                    {
                        cluster.AddRange(Master(0xA0, Concat(
                            Element(0xA1, Block(videoTrackNumber, frame.Timecode, 0x00, frame.Color)),
                            Master(0x75A1, Master(0xA6, Concat(Uint(0xEE, 1), Element(0xA5, frame.Alpha)))))));
                    }
                }

                var segment = Concat(
                    Master(0x1549A966, Uint(0x2AD7B1, timecodeScale)),
                    Master(0x1654AE6B, tracks.ToArray()),
                    unknownSizeCluster ? UnknownSize(0x1F43B675, cluster.ToArray()) : Master(0x1F43B675, cluster.ToArray()));

                var segmentBytes = UnknownSize(0x18538067, segment);
                output.Write(segmentBytes, 0, segmentBytes.Length);
                return output.ToArray();
            }
        }

        private static byte[] Block(int track, short timecode, byte flags, byte[] data)
        {
            var bytes = new List<byte> { (byte)(0x80 | track), (byte)(timecode >> 8), (byte)timecode, flags };
            bytes.AddRange(data);
            return bytes.ToArray();
        }

        private static byte[] Master(uint id, byte[] children)
        {
            return Element(id, children);
        }

        private static byte[] UnknownSize(uint id, byte[] children)
        {
            var bytes = new List<byte>(Id(id));
            bytes.AddRange(new byte[] { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
            bytes.AddRange(children);
            return bytes.ToArray();
        }

        private static byte[] Uint(uint id, long value)
        {
            var bytes = new byte[8];
            for (var i = 0; i < 8; i++)
            {
                bytes[7 - i] = (byte)(value >> (8 * i));
            }

            return Element(id, bytes);
        }

        private static byte[] Text(uint id, string value)
        {
            return Element(id, Encoding.ASCII.GetBytes(value));
        }

        private static void Element(Stream output, uint id, byte[] data)
        {
            var bytes = Element(id, data);
            output.Write(bytes, 0, bytes.Length);
        }

        private static byte[] Element(uint id, byte[] data)
        {
            var bytes = new List<byte>(Id(id));
            var size = (long)data.Length;
            bytes.Add(0x01);
            for (var i = 6; i >= 0; i--)
            {
                bytes.Add((byte)(size >> (8 * i)));
            }

            bytes.AddRange(data);
            return bytes.ToArray();
        }

        private static byte[] Id(uint id)
        {
            if (id > 0xFFFFFF)
            {
                return new[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            }

            if (id > 0xFFFF)
            {
                return new[] { (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            }

            return id > 0xFF ? new[] { (byte)(id >> 8), (byte)id } : new[] { (byte)id };
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var bytes = new List<byte>();
            foreach (var part in parts)
            {
                bytes.AddRange(part);
            }

            return bytes.ToArray();
        }
    }
}

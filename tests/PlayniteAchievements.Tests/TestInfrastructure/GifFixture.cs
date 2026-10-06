using System;
using System.IO;
using System.Text;

namespace PlayniteAchievements.Tests.TestInfrastructure
{
    /// <summary>
    /// Builds real GIF bytes for tests. Every encoded frame is a single pixel while the logical
    /// canvas is whatever size is asked for, so a fixture can carry hundreds of frames at a large
    /// declared resolution without allocating anything close to that many full-canvas bitmaps.
    /// </summary>
    internal static class GifFixture
    {
        internal static byte[] BuildSparseGif(int width, int height, int frameCount)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
                writer.Write((ushort)width);
                writer.Write((ushort)height);
                writer.Write((byte)0x80); // global two-color table
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write(new byte[] { 0, 0, 0, 255, 255, 255 });

                // Loop forever.
                writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
                writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                writer.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

                for (var i = 0; i < frameCount; i++)
                {
                    // Graphic control extension: 40ms delay.
                    writer.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x04, 0x00, 0x00, 0x00 });
                    // One-pixel image at (0,0) on the large logical canvas.
                    writer.Write((byte)0x2C);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)1);
                    writer.Write((byte)0);
                    // LZW: clear, color index 1, end.
                    writer.Write(new byte[] { 0x02, 0x02, 0x4C, 0x01, 0x00 });
                }

                writer.Write((byte)0x3B);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>One encoded frame for <see cref="BuildGif"/>. Indices are in display row order.</summary>
        internal sealed class Frame
        {
            internal int Left;
            internal int Top;
            internal int Width;
            internal int Height;
            internal byte[] Indices;
            internal int Disposal;
            internal int TransparentIndex = -1;
            internal int DelayCentiseconds = 4;
            internal bool Interlaced;

            /// <summary>Encode only this many pixels and end the data early, as a truncated file would.</summary>
            internal int? EncodedPixels;
        }

        /// <summary>
        /// Builds a GIF with a 256-entry global table from <paramref name="rgbPalette"/>, encoding
        /// every pixel as a literal LZW code with a clear code before the table would widen. Not
        /// compact, but any decoder must read it, and the test controls every pixel exactly.
        /// </summary>
        internal static byte[] BuildGif(int width, int height, int[] rgbPalette, params Frame[] frames)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
                writer.Write((ushort)width);
                writer.Write((ushort)height);
                writer.Write((byte)0xF7); // global table of 256 entries
                writer.Write((byte)0);
                writer.Write((byte)0);
                for (var i = 0; i < 256; i++)
                {
                    var rgb = i < rgbPalette.Length ? rgbPalette[i] : 0;
                    writer.Write((byte)(rgb >> 16));
                    writer.Write((byte)(rgb >> 8));
                    writer.Write((byte)rgb);
                }

                foreach (var frame in frames)
                {
                    var flags = (byte)((frame.Disposal << 2) | (frame.TransparentIndex >= 0 ? 1 : 0));
                    writer.Write(new byte[] { 0x21, 0xF9, 0x04, flags });
                    writer.Write((ushort)frame.DelayCentiseconds);
                    writer.Write((byte)Math.Max(0, frame.TransparentIndex));
                    writer.Write((byte)0);

                    writer.Write((byte)0x2C);
                    writer.Write((ushort)frame.Left);
                    writer.Write((ushort)frame.Top);
                    writer.Write((ushort)frame.Width);
                    writer.Write((ushort)frame.Height);
                    writer.Write((byte)(frame.Interlaced ? 0x40 : 0));
                    writer.Write((byte)8);
                    WriteSubBlocks(writer, EncodeLiteralLzw(StreamOrder(frame), frame.EncodedPixels));
                }

                writer.Write((byte)0x3B);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] StreamOrder(Frame frame)
        {
            if (!frame.Interlaced)
            {
                return frame.Indices;
            }

            var ordered = new byte[frame.Indices.Length];
            var position = 0;
            foreach (var pass in new[] { new[] { 0, 8 }, new[] { 4, 8 }, new[] { 2, 4 }, new[] { 1, 2 } })
            {
                for (var row = pass[0]; row < frame.Height; row += pass[1])
                {
                    Array.Copy(frame.Indices, row * frame.Width, ordered, position, frame.Width);
                    position += frame.Width;
                }
            }

            return ordered;
        }

        private static byte[] EncodeLiteralLzw(byte[] indices, int? encodedPixels)
        {
            const int clear = 256;
            const int end = 257;
            const int codeSize = 9;
            var count = Math.Min(indices.Length, encodedPixels ?? indices.Length);
            var output = new MemoryStream();
            var buffer = 0;
            var bits = 0;

            void Emit(int code)
            {
                buffer |= code << bits;
                bits += codeSize;
                while (bits >= 8)
                {
                    output.WriteByte((byte)buffer);
                    buffer >>= 8;
                    bits -= 8;
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (i % 250 == 0)
                {
                    Emit(clear);
                }

                Emit(indices[i]);
            }

            if (count == indices.Length)
            {
                Emit(end);
            }

            if (bits > 0)
            {
                output.WriteByte((byte)buffer);
            }

            return output.ToArray();
        }

        private static void WriteSubBlocks(BinaryWriter writer, byte[] data)
        {
            for (var offset = 0; offset < data.Length; offset += 255)
            {
                var length = Math.Min(255, data.Length - offset);
                writer.Write((byte)length);
                writer.Write(data, offset, length);
            }

            writer.Write((byte)0);
        }

        /// <summary>
        /// Writes bytes to a fresh temp directory as animation.gif and returns the full path.
        /// Pair with <see cref="DeleteTempPayload"/>.
        /// </summary>
        internal static string WriteTempGif(byte[] bytes)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAchievementsTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "animation.gif");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        internal static void DeleteTempPayload(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}

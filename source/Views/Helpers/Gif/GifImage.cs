using System;
using System.Collections.Generic;
using System.IO;

namespace PlayniteAchievements.Views.Helpers.Gif
{
    internal enum GifDisposal
    {
        None = 0,
        Keep = 1,
        RestoreBackground = 2,
        RestorePrevious = 3
    }

    /// <summary>
    /// One frame's placement, palette and timing, plus the payload offset of its LZW data. The
    /// pixels stay compressed in the payload until <see cref="GifCanvas"/> draws the frame.
    /// </summary>
    internal sealed class GifFrame
    {
        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;
        internal bool Interlaced;

        /// <summary>256 Bgra32 entries; indices past the file's table size read as transparent.</summary>
        internal int[] Palette;

        /// <summary>The palette index left undrawn, or -1.</summary>
        internal int TransparentIndex = -1;

        internal GifDisposal Disposal;
        internal int DelayMs;
        internal int LzwMinimumCodeSize;

        /// <summary>Offset of the first data sub-block's length byte.</summary>
        internal int DataOffset;
    }

    /// <summary>
    /// The parsed structure of a GIF: canvas size and per-frame metadata over the original payload
    /// bytes. Parsing walks the block structure only; no frame is decompressed here.
    /// </summary>
    internal sealed class GifImage
    {
        /// <summary>Canvases above this pixel count are refused rather than allocated.</summary>
        internal const long MaxCanvasPixels = 8192L * 8192L;

        /// <summary>Frame delay used when the file specifies 0, matching XamlAnimatedGif.</summary>
        internal const int DefaultDelayMs = 100;

        private GifImage(byte[] payload, int width, int height, GifFrame[] frames)
        {
            Payload = payload;
            Width = width;
            Height = height;
            Frames = frames;

            var max = 0;
            foreach (var frame in frames)
            {
                max = Math.Max(max, frame.Width * frame.Height);
            }

            MaxFramePixels = max;
        }

        internal byte[] Payload { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal GifFrame[] Frames { get; }

        internal int MaxFramePixels { get; }

        internal static GifImage Parse(byte[] payload)
        {
            if (payload == null || payload.Length < 13 ||
                payload[0] != (byte)'G' || payload[1] != (byte)'I' || payload[2] != (byte)'F')
            {
                throw new InvalidDataException("Not a GIF file.");
            }

            var reader = new Reader(payload, 6);
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            var screenFlags = reader.ReadByte();
            reader.Skip(2); // background color index, pixel aspect ratio

            var globalPalette = (screenFlags & 0x80) != 0
                ? reader.ReadPalette(1 << ((screenFlags & 0x07) + 1))
                : null;

            var frames = new List<GifFrame>();
            var delayMs = DefaultDelayMs;
            var transparentIndex = -1;
            var disposal = GifDisposal.None;

            try
            {
                while (reader.HasMore)
                {
                    var introducer = reader.ReadByte();
                    if (introducer == 0x3B)
                    {
                        break;
                    }

                    if (introducer == 0x21)
                    {
                        var label = reader.ReadByte();
                        if (label == 0xF9 && reader.Peek() >= 4)
                        {
                            var blockSize = reader.ReadByte();
                            var flags = reader.ReadByte();
                            var delay = reader.ReadUInt16() * 10;
                            var transparent = reader.ReadByte();
                            delayMs = delay > 0 ? delay : DefaultDelayMs;
                            transparentIndex = (flags & 0x01) != 0 ? transparent : -1;
                            var method = (flags >> 2) & 0x07;
                            disposal = method <= 3 ? (GifDisposal)method : GifDisposal.None;
                            reader.Skip(blockSize - 4);
                        }

                        reader.SkipSubBlocks();
                        continue;
                    }

                    if (introducer != 0x2C)
                    {
                        // Trailing garbage after decodable frames is tolerated, as browsers do.
                        if (frames.Count > 0)
                        {
                            break;
                        }

                        throw new InvalidDataException("Unexpected GIF block.");
                    }

                    var frame = new GifFrame
                    {
                        Left = reader.ReadUInt16(),
                        Top = reader.ReadUInt16(),
                        Width = reader.ReadUInt16(),
                        Height = reader.ReadUInt16()
                    };

                    var imageFlags = reader.ReadByte();
                    frame.Interlaced = (imageFlags & 0x40) != 0;
                    frame.Palette = (imageFlags & 0x80) != 0
                        ? reader.ReadPalette(1 << ((imageFlags & 0x07) + 1))
                        : globalPalette ?? new int[256];
                    frame.LzwMinimumCodeSize = reader.ReadByte();
                    frame.DataOffset = reader.Position;
                    frame.DelayMs = delayMs;
                    frame.TransparentIndex = transparentIndex;
                    frame.Disposal = disposal;
                    frames.Add(frame);

                    delayMs = DefaultDelayMs;
                    transparentIndex = -1;
                    disposal = GifDisposal.None;

                    // A file truncated inside this frame's data still keeps the frame; the LZW
                    // decoder draws whatever pixels arrived.
                    reader.SkipSubBlocks();
                }
            }
            catch (EndOfStreamException) when (frames.Count > 0)
            {
            }

            if (frames.Count == 0)
            {
                throw new InvalidDataException("The GIF has no frames.");
            }

            if (width == 0 || height == 0)
            {
                foreach (var frame in frames)
                {
                    width = Math.Max(width, frame.Left + frame.Width);
                    height = Math.Max(height, frame.Top + frame.Height);
                }
            }

            if (width <= 0 || height <= 0 || (long)width * height > MaxCanvasPixels)
            {
                throw new InvalidDataException($"Unsupported GIF canvas {width}x{height}.");
            }

            return new GifImage(payload, width, height, frames.ToArray());
        }

        private struct Reader
        {
            private readonly byte[] _bytes;
            private int _position;

            internal Reader(byte[] bytes, int position)
            {
                _bytes = bytes;
                _position = position;
            }

            internal int Position => _position;

            internal bool HasMore => _position < _bytes.Length;

            internal int Peek()
            {
                Require(1);
                return _bytes[_position];
            }

            internal byte ReadByte()
            {
                Require(1);
                return _bytes[_position++];
            }

            internal int ReadUInt16()
            {
                Require(2);
                var value = _bytes[_position] | (_bytes[_position + 1] << 8);
                _position += 2;
                return value;
            }

            internal void Skip(int count)
            {
                Require(count);
                _position += count;
            }

            internal int[] ReadPalette(int count)
            {
                Require(count * 3);
                var palette = new int[256];
                for (var i = 0; i < count; i++)
                {
                    var r = _bytes[_position++];
                    var g = _bytes[_position++];
                    var b = _bytes[_position++];
                    palette[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }

                return palette;
            }

            internal void SkipSubBlocks()
            {
                while (true)
                {
                    var length = ReadByte();
                    if (length == 0)
                    {
                        return;
                    }

                    if (_position + length > _bytes.Length)
                    {
                        _position = _bytes.Length;
                        throw new EndOfStreamException();
                    }

                    _position += length;
                }
            }

            private void Require(int count)
            {
                if (_position + count > _bytes.Length)
                {
                    throw new EndOfStreamException();
                }
            }
        }
    }
}

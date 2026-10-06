using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PlayniteAchievements.Services.Images.Webm
{
    internal enum WebmCodec
    {
        Vp8,
        Vp9
    }

    /// <summary>One video frame: where its color and optional alpha bitstreams sit in the payload.</summary>
    internal sealed class WebmFrame
    {
        internal int ColorOffset;
        internal int ColorLength;
        internal int AlphaOffset;
        internal int AlphaLength;
        internal int DelayMs;

        internal bool HasAlpha => AlphaLength > 0;
    }

    /// <summary>The first video track of a WebM file, with every frame in decode order.</summary>
    internal sealed class WebmImage
    {
        internal WebmImage(byte[] payload, WebmCodec codec, int width, int height, WebmFrame[] frames)
        {
            Payload = payload;
            Codec = codec;
            Width = width;
            Height = height;
            Frames = frames;
        }

        /// <summary>The whole file; frames reference it by offset rather than copying.</summary>
        internal byte[] Payload { get; }

        internal WebmCodec Codec { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal WebmFrame[] Frames { get; }
    }

    /// <summary>
    /// Reads the parts of a WebM (Matroska) file an image player needs: the first VP8 or VP9 video
    /// track, its frames in order with their display durations, and the alpha bitstream each frame
    /// carries in BlockAdditional ID 1. Windows decoders ignore that alpha stream, which is why the
    /// container is read here instead of through a Media Foundation source.
    /// </summary>
    /// <remarks>
    /// The walk is flat: master elements are entered in place and every other element is skipped
    /// by its size. That makes unknown-size Segment and Cluster elements, which streaming muxers
    /// write, need no special handling.
    /// </remarks>
    internal static class WebmContainer
    {
        /// <summary>Largest frame accepted, matching the budget the animated WebP path uses.</summary>
        internal const long MaxPixels = 32L * 1024 * 1024;

        internal const int DefaultDelayMs = 100;
        private const int MinDelayMs = 10;

        private const uint EbmlId = 0x1A45DFA3;
        private const uint SegmentId = 0x18538067;
        private const uint InfoId = 0x1549A966;
        private const uint TimecodeScaleId = 0x2AD7B1;
        private const uint TracksId = 0x1654AE6B;
        private const uint TrackEntryId = 0xAE;
        private const uint TrackNumberId = 0xD7;
        private const uint TrackTypeId = 0x83;
        private const uint CodecIdId = 0x86;
        private const uint DefaultDurationId = 0x23E383;
        private const uint VideoId = 0xE0;
        private const uint PixelWidthId = 0xB0;
        private const uint PixelHeightId = 0xBA;
        private const uint ClusterId = 0x1F43B675;
        private const uint TimecodeId = 0xE7;
        private const uint SimpleBlockId = 0xA3;
        private const uint BlockGroupId = 0xA0;
        private const uint BlockId = 0xA1;
        private const uint BlockAdditionsId = 0x75A1;
        private const uint BlockMoreId = 0xA6;
        private const uint BlockAddIdId = 0xEE;
        private const uint BlockAdditionalId = 0xA5;

        private const int VideoTrackType = 1;
        private const int AlphaBlockAddId = 1;

        private static readonly ConcurrentDictionary<string, bool> AnimatedByFile =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<uint> Masters = new HashSet<uint>
        {
            SegmentId, InfoId, TracksId, TrackEntryId, VideoId, ClusterId, BlockGroupId, BlockAdditionsId, BlockMoreId
        };

        private sealed class Track
        {
            internal long Number = -1;
            internal long Type;
            internal string CodecId;
            internal long DefaultDurationNs;
            internal int Width;
            internal int Height;
        }

        private sealed class Block
        {
            internal WebmFrame Frame;
            internal long Timecode;
        }

        /// <summary>True when the bytes start with the EBML magic every WebM file begins with.</summary>
        internal static bool HasSignature(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 4 &&
                   bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3;
        }

        /// <summary>Parses the whole file. Throws <see cref="InvalidDataException"/> when it is not a playable WebM.</summary>
        internal static WebmImage Parse(byte[] payload)
        {
            return Read(payload, stopAfterFrames: int.MaxValue);
        }

        /// <summary>
        /// True when the file holds more than one frame. Layout passes ask this repeatedly, so the
        /// answer is kept per file version (path, length, last write) instead of re-reading the file.
        /// </summary>
        internal static bool IsAnimated(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return false;
                }

                var key = string.Concat(info.FullName, "\u001f", info.Length.ToString(), "\u001f", info.LastWriteTimeUtc.Ticks.ToString());
                return AnimatedByFile.GetOrAdd(key, _ => CountFrames(info.FullName, limit: 2) > 1);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The video track's pixel size; false when the file is not a readable WebM.</summary>
        internal static bool TryReadSize(string path, out int width, out int height)
        {
            try
            {
                var image = Read(File.ReadAllBytes(path), stopAfterFrames: 1);
                width = image.Width;
                height = image.Height;
                return true;
            }
            catch
            {
                width = 0;
                height = 0;
                return false;
            }
        }

        /// <summary>
        /// Counts frames, stopping once <paramref name="limit"/> are seen; 0 when the file is not a
        /// readable WebM.
        /// </summary>
        internal static int CountFrames(string path, int limit)
        {
            try
            {
                return Read(File.ReadAllBytes(path), limit).Frames.Length;
            }
            catch
            {
                return 0;
            }
        }

        private static WebmImage Read(byte[] payload, int stopAfterFrames)
        {
            if (!HasSignature(payload))
            {
                throw new InvalidDataException("Not a WebM file.");
            }

            var tracks = new List<Track>();
            Track current = null;
            Track video = null;
            long timecodeScaleNs = 1000000;
            long clusterTimecode = 0;
            long blockAddId = AlphaBlockAddId;
            var blocks = new List<Block>();
            Block lastBlock = null;

            var position = 0;
            while (position < payload.Length && blocks.Count < stopAfterFrames)
            {
                var id = ReadId(payload, ref position);
                var size = ReadSize(payload, ref position, out var unknownSize);

                if (Masters.Contains(id))
                {
                    if (id == TrackEntryId)
                    {
                        current = new Track();
                        tracks.Add(current);
                    }
                    else if (id == BlockMoreId)
                    {
                        blockAddId = AlphaBlockAddId;
                    }
                    else if (id == BlockGroupId)
                    {
                        lastBlock = null;
                    }

                    continue;
                }

                if (unknownSize || size > payload.Length - position)
                {
                    // Only a truncated file or a corrupt size lands here; keep what was read.
                    break;
                }

                var start = position;
                var length = (int)size;
                position += length;

                switch (id)
                {
                    case EbmlId:
                        break;
                    case TimecodeScaleId:
                        timecodeScaleNs = ReadUnsigned(payload, start, length);
                        break;
                    case TrackNumberId when current != null:
                        current.Number = ReadUnsigned(payload, start, length);
                        break;
                    case TrackTypeId when current != null:
                        current.Type = ReadUnsigned(payload, start, length);
                        break;
                    case CodecIdId when current != null:
                        current.CodecId = Encoding.ASCII.GetString(payload, start, length).TrimEnd('\0');
                        break;
                    case DefaultDurationId when current != null:
                        current.DefaultDurationNs = ReadUnsigned(payload, start, length);
                        break;
                    case PixelWidthId when current != null:
                        current.Width = (int)ReadUnsigned(payload, start, length);
                        break;
                    case PixelHeightId when current != null:
                        current.Height = (int)ReadUnsigned(payload, start, length);
                        break;
                    case TimecodeId:
                        clusterTimecode = ReadUnsigned(payload, start, length);
                        break;
                    case BlockAddIdId:
                        blockAddId = ReadUnsigned(payload, start, length);
                        break;
                    case BlockAdditionalId:
                        if (blockAddId == AlphaBlockAddId && lastBlock != null)
                        {
                            lastBlock.Frame.AlphaOffset = start;
                            lastBlock.Frame.AlphaLength = length;
                        }

                        break;
                    case SimpleBlockId:
                    case BlockId:
                        video = video ?? SelectVideoTrack(tracks);
                        lastBlock = ReadBlock(payload, start, length, video, clusterTimecode);
                        if (lastBlock != null)
                        {
                            blocks.Add(lastBlock);
                        }

                        break;
                }
            }

            video = video ?? SelectVideoTrack(tracks);
            if (blocks.Count == 0)
            {
                throw new InvalidDataException("The WebM file has no video frames.");
            }

            var frames = new WebmFrame[blocks.Count];
            var fallbackDelay = video.DefaultDurationNs > 0
                ? ClampDelay(video.DefaultDurationNs / 1000000.0)
                : DefaultDelayMs;
            for (var i = 0; i < blocks.Count; i++)
            {
                var frame = blocks[i].Frame;
                frame.DelayMs = i + 1 < blocks.Count
                    ? ClampDelay((blocks[i + 1].Timecode - blocks[i].Timecode) * (double)timecodeScaleNs / 1000000.0)
                    : i > 0 ? frames[i - 1].DelayMs : fallbackDelay;
                frames[i] = frame;
            }

            return new WebmImage(payload, ParseCodec(video.CodecId), video.Width, video.Height, frames);
        }

        private static Track SelectVideoTrack(List<Track> tracks)
        {
            foreach (var track in tracks)
            {
                if (track.Type == VideoTrackType &&
                    track.Number >= 0 &&
                    (track.CodecId == "V_VP8" || track.CodecId == "V_VP9"))
                {
                    if (track.Width <= 0 || track.Height <= 0 || (long)track.Width * track.Height > MaxPixels)
                    {
                        throw new InvalidDataException($"Unsupported WebM frame size {track.Width}x{track.Height}.");
                    }

                    return track;
                }
            }

            throw new InvalidDataException("The WebM file has no VP8 or VP9 video track.");
        }

        private static WebmCodec ParseCodec(string codecId)
        {
            return codecId == "V_VP8" ? WebmCodec.Vp8 : WebmCodec.Vp9;
        }

        private static Block ReadBlock(byte[] payload, int start, int length, Track video, long clusterTimecode)
        {
            var end = start + length;
            var position = start;
            var track = ReadSize(payload, ref position, out _);
            if (track != video.Number)
            {
                return null;
            }

            if (end - position < 3)
            {
                throw new InvalidDataException("Truncated WebM block.");
            }

            var relative = (short)((payload[position] << 8) | payload[position + 1]);
            var flags = payload[position + 2];
            position += 3;

            // Lacing packs several frames into one block. Video muxers do not use it.
            if ((flags & 0x06) != 0)
            {
                throw new InvalidDataException("Laced WebM video blocks are not supported.");
            }

            return new Block
            {
                Timecode = clusterTimecode + relative,
                Frame = new WebmFrame
                {
                    ColorOffset = position,
                    ColorLength = end - position
                }
            };
        }

        private static int ClampDelay(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || milliseconds < MinDelayMs)
            {
                return MinDelayMs;
            }

            return milliseconds > int.MaxValue ? int.MaxValue : (int)Math.Round(milliseconds);
        }

        /// <summary>An element ID keeps its length marker bits.</summary>
        private static uint ReadId(byte[] bytes, ref int position)
        {
            var length = VintLength(bytes, position);
            if (length > 4)
            {
                throw new InvalidDataException("Invalid EBML element ID.");
            }

            uint value = 0;
            for (var i = 0; i < length; i++)
            {
                value = (value << 8) | bytes[position + i];
            }

            position += length;
            return value;
        }

        /// <summary>A size or track number drops its marker bit; all value bits set means unknown.</summary>
        private static long ReadSize(byte[] bytes, ref int position, out bool unknown)
        {
            var length = VintLength(bytes, position);
            var mask = 0xFF >> length;
            long value = bytes[position] & mask;
            var allOnes = value == mask;
            for (var i = 1; i < length; i++)
            {
                var b = bytes[position + i];
                value = (value << 8) | b;
                allOnes &= b == 0xFF;
            }

            position += length;
            unknown = allOnes;
            return value;
        }

        private static int VintLength(byte[] bytes, int position)
        {
            if (position >= bytes.Length)
            {
                throw new InvalidDataException("Truncated WebM element.");
            }

            var first = bytes[position];
            var length = 1;
            for (var mask = 0x80; length <= 8 && (first & mask) == 0; mask >>= 1)
            {
                length++;
            }

            if (length > 8 || position + length > bytes.Length)
            {
                throw new InvalidDataException("Invalid EBML variable-length integer.");
            }

            return length;
        }

        private static long ReadUnsigned(byte[] bytes, int start, int length)
        {
            long value = 0;
            for (var i = 0; i < length && i < 8; i++)
            {
                value = (value << 8) | bytes[start + i];
            }

            return value;
        }
    }
}

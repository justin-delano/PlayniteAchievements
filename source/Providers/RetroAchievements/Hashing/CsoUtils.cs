using System;
using System.IO;
using System.IO.Compression;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing
{
    internal static class CsoUtils
    {
        private const uint CisoMagic = 0x4F534943; // "CISO" in little-endian
        private const int CisoHeaderSize = 24;
        private const int DefaultBlockSize = 2048;

        public static bool IsCsoPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            return ext != null &&
                   (ext.Equals(".cso", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".ciso", StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryReadHeader(string filePath, out CsoHeader header)
        {
            header = default;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return TryReadHeader(fs, out header);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadHeader(Stream stream, out CsoHeader header)
        {
            header = default;

            try
            {
                var buffer = new byte[CisoHeaderSize];
                var bytesRead = stream.Read(buffer, 0, CisoHeaderSize);
                if (bytesRead < CisoHeaderSize) return false;

                var magic = BitConverter.ToUInt32(buffer, 0);
                if (magic != CisoMagic) return false;

                header = new CsoHeader
                {
                    Magic = magic,
                    HeaderSize = BitConverter.ToUInt32(buffer, 4),
                    UncompressedSize = BitConverter.ToUInt64(buffer, 8),
                    BlockSize = BitConverter.ToUInt32(buffer, 16),
                    Version = buffer[20]
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static ArchiveUtils.TempFile DecompressToTempFile(string csoPath, Action<ulong, ulong> progressCallback = null)
        {
            if (string.IsNullOrWhiteSpace(csoPath))
                throw new ArgumentNullException(nameof(csoPath));

            if (!TryReadHeader(csoPath, out var header))
                throw new InvalidOperationException("Invalid CSO file header.");

            var blockCount = (long)((header.UncompressedSize + header.BlockSize - 1) / header.BlockSize);
            var indexSize = (blockCount + 1) * 4;
            var indexOffset = CisoHeaderSize;

            var outPath = Path.Combine(Path.GetTempPath(), $"PlayniteAchievements_cso_{Guid.NewGuid():N}.iso");

            using (var inStream = new FileStream(csoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                inStream.Position = indexOffset;

                var blockIndex = new uint[blockCount + 1];
                var indexBuffer = new byte[indexSize];
                var indexBytesRead = inStream.Read(indexBuffer, 0, (int)indexSize);

                if (indexBytesRead < indexSize)
                    throw new InvalidOperationException("Failed to read CSO block index.");

                for (var i = 0; i < blockCount + 1; i++)
                {
                    blockIndex[i] = BitConverter.ToUInt32(indexBuffer, i * 4);
                }

                var decompressBuffer = new byte[header.BlockSize * 2];
                var remaining = header.UncompressedSize;

                for (uint blockIndex_i = 0; blockIndex_i < blockCount; blockIndex_i++)
                {
                    var offset = blockIndex[blockIndex_i];
                    var nextOffset = blockIndex[blockIndex_i + 1];
                    var isCompressed = (offset & 0x80000000) == 0;

                    offset &= 0x7FFFFFFF;
                    var compressedSize = nextOffset - offset;

                    inStream.Position = indexOffset + offset;

                    var bytesToWrite = (ulong)Math.Min(header.BlockSize, remaining);

                    if (isCompressed)
                    {
                        var compressedBuffer = new byte[compressedSize];
                        var bytesRead = inStream.Read(compressedBuffer, 0, (int)compressedSize);

                        using (var ms = new MemoryStream(compressedBuffer, 0, bytesRead))
                        using (var deflateStream = new DeflateStream(ms, CompressionMode.Decompress))
                        {
                            var decompressedBytes = deflateStream.Read(decompressBuffer, 0, decompressBuffer.Length);
                            outStream.Write(decompressBuffer, 0, (int)bytesToWrite);
                        }
                    }
                    else
                    {
                        var buffer = new byte[compressedSize];
                        var bytesRead = inStream.Read(buffer, 0, (int)compressedSize);
                        outStream.Write(buffer, 0, (int)bytesToWrite);
                    }

                    remaining -= bytesToWrite;

                    if (progressCallback != null && blockIndex_i % 100 == 0)
                    {
                        progressCallback(
                            header.UncompressedSize - remaining,
                            header.UncompressedSize
                        );
                    }
                }

                if (progressCallback != null)
                {
                    progressCallback(header.UncompressedSize, header.UncompressedSize);
                }
            }

            return new ArchiveUtils.TempFile(outPath);
        }

        public struct CsoHeader
        {
            public uint Magic;
            public uint HeaderSize;
            public ulong UncompressedSize;
            public uint BlockSize;
            public byte Version;
        }
    }
}

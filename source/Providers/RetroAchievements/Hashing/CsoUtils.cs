using System;
using System.Collections.Generic;
using System.IO;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing
{
    internal static class CsoUtils
    {
        private const uint CisoMagic = 0x4F534943; // "CISO" in little-endian
        private const int CisoHeaderSize = 24;
        private const uint CisoNotCompressedMask = 0x80000000;
        private const uint CisoOffsetMask = 0x7FFFFFFF;
        private const byte CsoV2 = 2;

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
                    Version = buffer[20],
                    IndexShift = buffer[21]
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

            ValidateHeader(header);

            var outPath = Path.Combine(Path.GetTempPath(), $"PlayniteAchievements_cso_{Guid.NewGuid():N}.iso");
            var outFileCreated = false;

            try
            {
                using (var inStream = new FileStream(csoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    outFileCreated = true;

                    var blockCount = GetBlockCount(header);
                    var indexEntryCount = checked(blockCount + 1);
                    var indexSize = checked((long)indexEntryCount * sizeof(uint));

                    uint[] blockIndex = null;
                    long indexOffset = 0;
                    long offsetBase = 0;
                    Exception lastIndexException = null;

                    foreach (var candidateIndexOffset in GetIndexOffsetCandidates(header, inStream.Length, indexSize))
                    {
                        try
                        {
                            inStream.Position = candidateIndexOffset;
                            var candidateIndex = ReadBlockIndex(inStream, indexEntryCount);
                            var candidateOffsetBase = ResolveOffsetBase(candidateIndex, header, candidateIndexOffset, indexSize, inStream.Length);

                            blockIndex = candidateIndex;
                            indexOffset = candidateIndexOffset;
                            offsetBase = candidateOffsetBase;
                            break;
                        }
                        catch (Exception ex) when (ex is InvalidDataException || ex is EndOfStreamException)
                        {
                            lastIndexException = ex;
                        }
                    }

                    if (blockIndex == null)
                    {
                        throw new InvalidDataException("Failed to parse CSO block index.", lastIndexException);
                    }

                    var decompressBuffer = new byte[header.BlockSize];
                    var directCopyBuffer = new byte[header.BlockSize];
                    var remaining = header.UncompressedSize;

                    for (var i = 0; i < blockCount; i++)
                    {
                        var rawEntry = blockIndex[i];
                        var rawNextEntry = blockIndex[i + 1];
                        var blockOffset = DecodeEntryOffset(rawEntry, header.IndexShift, offsetBase);
                        var nextBlockOffset = DecodeEntryOffset(rawNextEntry, header.IndexShift, offsetBase);

                        if (nextBlockOffset < blockOffset)
                        {
                            throw new InvalidDataException($"Invalid CSO block index ordering at block {i}.");
                        }

                        var storedSize = nextBlockOffset - blockOffset;
                        if (storedSize <= 0)
                        {
                            throw new InvalidDataException($"Invalid CSO block size at block {i}: {storedSize}.");
                        }

                        if (blockOffset < 0 || nextBlockOffset > inStream.Length)
                        {
                            throw new InvalidDataException($"CSO block {i} points outside file bounds.");
                        }

                        inStream.Position = blockOffset;

                        var bytesToWrite = (int)Math.Min((ulong)header.BlockSize, remaining);
                        var isCompressed = IsCompressedBlock(header.Version, rawEntry, storedSize, header.BlockSize);

                        if (isCompressed)
                        {
                            if (header.Version == CsoV2 && (rawEntry & CisoNotCompressedMask) != 0)
                            {
                                throw new InvalidDataException($"Unsupported CSO v2 LZ4-compressed block at index {i}.");
                            }

                            if (storedSize > int.MaxValue)
                            {
                                throw new InvalidDataException($"Compressed CSO block {i} is too large to process: {storedSize} bytes.");
                            }

                            var compressedBuffer = new byte[storedSize];
                            ReadExactly(inStream, compressedBuffer, 0, compressedBuffer.Length);
                            DecompressRawDeflateBlock(compressedBuffer, bytesToWrite, decompressBuffer);
                            outStream.Write(decompressBuffer, 0, bytesToWrite);
                        }
                        else
                        {
                            if (storedSize < bytesToWrite)
                            {
                                throw new InvalidDataException($"Uncompressed CSO block {i} is truncated ({storedSize} < {bytesToWrite}).");
                            }

                            ReadExactly(inStream, directCopyBuffer, 0, bytesToWrite);
                            outStream.Write(directCopyBuffer, 0, bytesToWrite);
                        }

                        remaining -= (ulong)bytesToWrite;

                        if (progressCallback != null && i % 100 == 0)
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
            }
            catch
            {
                if (outFileCreated)
                {
                    TryDeleteFile(outPath);
                }

                throw;
            }

            return new ArchiveUtils.TempFile(outPath);
        }

        private static void ValidateHeader(CsoHeader header)
        {
            if (header.BlockSize == 0)
            {
                throw new InvalidDataException("Invalid CSO header: block size is 0.");
            }

            if (header.BlockSize > int.MaxValue)
            {
                throw new InvalidDataException($"Unsupported CSO block size: {header.BlockSize}.");
            }

            if (header.IndexShift > 31)
            {
                throw new InvalidDataException($"Invalid CSO index shift: {header.IndexShift}.");
            }

            if (header.UncompressedSize > long.MaxValue)
            {
                throw new InvalidDataException($"CSO file is too large to process: {header.UncompressedSize} bytes.");
            }
        }

        private static int GetBlockCount(CsoHeader header)
        {
            if (header.UncompressedSize == 0)
            {
                return 0;
            }

            var blockCount = (header.UncompressedSize + header.BlockSize - 1) / header.BlockSize;
            if (blockCount > int.MaxValue - 1)
            {
                throw new InvalidDataException($"CSO has too many blocks to process: {blockCount}.");
            }

            return (int)blockCount;
        }

        private static IEnumerable<long> GetIndexOffsetCandidates(CsoHeader header, long fileLength, long indexSize)
        {
            var defaultOffset = (long)CisoHeaderSize;
            var declaredOffset = header.HeaderSize >= CisoHeaderSize ? (long)header.HeaderSize : defaultOffset;

            // CSO v1 header_size is often unreliable in the wild; prefer canonical 24-byte header.
            if (defaultOffset + indexSize <= fileLength)
            {
                yield return defaultOffset;
            }

            if (declaredOffset != defaultOffset && declaredOffset + indexSize <= fileLength)
            {
                yield return declaredOffset;
            }
        }

        private static uint[] ReadBlockIndex(Stream stream, int indexEntryCount)
        {
            var result = new uint[indexEntryCount];
            var buffer = new byte[sizeof(uint)];

            for (var i = 0; i < indexEntryCount; i++)
            {
                ReadExactly(stream, buffer, 0, buffer.Length);
                result[i] = BitConverter.ToUInt32(buffer, 0);
            }

            return result;
        }

        private static long ResolveOffsetBase(uint[] blockIndex, CsoHeader header, long indexOffset, long indexSize, long fileLength)
        {
            if (blockIndex == null || blockIndex.Length == 0)
            {
                return 0;
            }

            var firstOffset = DecodeEntryOffset(blockIndex[0], header.IndexShift, 0);
            var minDataStart = indexOffset + indexSize;

            // Most CSO files store absolute file offsets.
            if (firstOffset >= minDataStart && firstOffset < fileLength)
            {
                return 0;
            }

            // Fallback for non-standard files that store offsets relative to index start.
            var relativeFirstOffset = firstOffset + indexOffset;
            if (relativeFirstOffset >= minDataStart && relativeFirstOffset < fileLength)
            {
                return indexOffset;
            }

            throw new InvalidDataException("Unable to determine CSO data offset base from block index.");
        }

        private static long DecodeEntryOffset(uint indexEntry, byte indexShift, long offsetBase)
        {
            var rawOffset = (long)(indexEntry & CisoOffsetMask);
            var shiftedOffset = rawOffset << indexShift;
            return shiftedOffset + offsetBase;
        }

        private static bool IsCompressedBlock(byte version, uint indexEntry, long storedSize, uint blockSize)
        {
            if (version == CsoV2)
            {
                // In CSO v2, blocks with stored size >= block size must be treated as uncompressed.
                return storedSize < blockSize;
            }

            return (indexEntry & CisoNotCompressedMask) == 0;
        }

        private static void DecompressRawDeflateBlock(byte[] compressedBuffer, int outputLength, byte[] outputBuffer)
        {
            if (outputLength > outputBuffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(outputLength));
            }

            using (var input = new MemoryStream(compressedBuffer, 0, compressedBuffer.Length, writable: false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress, CompressionLevel.Default, null))
            {
                var total = 0;
                while (total < outputLength)
                {
                    var read = deflate.Read(outputBuffer, total, outputLength - total);
                    if (read <= 0)
                    {
                        throw new InvalidDataException($"Deflate block ended early. Expected {outputLength} bytes, got {total}.");
                    }

                    total += read;
                }
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"Unexpected end of stream (wanted {count} bytes, read {totalRead} bytes).");
                }

                totalRead += read;
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }

        public struct CsoHeader
        {
            public uint Magic;
            public uint HeaderSize;
            public ulong UncompressedSize;
            public uint BlockSize;
            public byte Version;
            public byte IndexShift;
        }
    }
}

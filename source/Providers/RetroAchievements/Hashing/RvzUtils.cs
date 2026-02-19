using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing
{
    /// <summary>
    /// Provides decompression support for RVZ files (Dolphin's Wii/GameCube disc format).
    /// Based on Dolphin emulator's WIABlob implementation.
    /// </summary>
    internal static class RvzUtils
    {
        #region Constants

        private const uint RvzMagic = 0x015A5652; // "RVZ\x1" in little-endian
        private const uint RvzVersion = 0x01000000;
        private const uint RvzVersionReadCompatible = 0x00030000;
        private const int Sha1DigestSize = 20;
        private const int WiaHeader1Size = 0x48;
        private const int WiaHeader2Size = 0xDC;
        private const int PartitionEntrySize = 0x30;
        private const int RawDataEntrySize = 0x18;
        private const int RvzGroupEntrySize = 0x0C;

        // Wii disc constants
        private const int WiiBlockTotalSize = 0x8000;    // 32 KiB
        private const int WiiBlockDataSize = 0x7C00;     // 31 KiB
        private const int WiiBlockHeaderSize = 0x0400;   // 1 KiB
        private const int WiiBlocksPerGroup = 64;
        private const int WiiGroupTotalSize = WiiBlocksPerGroup * WiiBlockTotalSize; // 2 MiB
        private const int WiiGroupDataSize = WiiBlocksPerGroup * WiiBlockDataSize;

        #endregion

        #region Enums

        private enum RvzCompressionType : uint
        {
            None = 0,
            Purge = 1,
            Bzip2 = 2,
            LZMA = 3,
            LZMA2 = 4,
            Zstd = 5
        }

        private enum DiscType : uint
        {
            Unknown = 0,
            GameCube = 1,
            Wii = 2
        }

        #endregion

        #region Structures

        private class WiaHeader1
        {
            public uint Magic;
            public uint Version;
            public uint VersionCompatible;
            public uint Header2Size;
            public byte[] Header2Hash; // 20 bytes
            public ulong IsoFileSize;
            public ulong WiaFileSize;
            public byte[] Header1Hash; // 20 bytes

            public WiaHeader1()
            {
                Header2Hash = new byte[Sha1DigestSize];
                Header1Hash = new byte[Sha1DigestSize];
            }
        }

        private class WiaHeader2
        {
            public uint DiscType;
            public uint CompressionType;
            public int CompressionLevel;
            public uint ChunkSize;
            public byte[] DiscHeader; // 128 bytes
            public uint NumberOfPartitionEntries;
            public uint PartitionEntrySize;
            public ulong PartitionEntriesOffset;
            public byte[] PartitionEntriesHash; // 20 bytes
            public uint NumberOfRawDataEntries;
            public ulong RawDataEntriesOffset;
            public uint RawDataEntriesSize;
            public uint NumberOfGroupEntries;
            public ulong GroupEntriesOffset;
            public uint GroupEntriesSize;
            public byte CompressorDataSize;
            public byte[] CompressorData; // 7 bytes

            public WiaHeader2()
            {
                DiscHeader = new byte[0x80];
                PartitionEntriesHash = new byte[Sha1DigestSize];
                CompressorData = new byte[7];
            }
        }

        private struct PartitionDataEntry
        {
            public uint FirstSector;
            public uint NumberOfSectors;
            public uint GroupIndex;
            public uint NumberOfGroups;
        }

        private class PartitionEntry
        {
            public byte[] PartitionKey; // 16 bytes
            public PartitionDataEntry[] DataEntries; // 2 entries

            public PartitionEntry()
            {
                PartitionKey = new byte[16];
                DataEntries = new PartitionDataEntry[2];
            }
        }

        private struct RawDataEntry
        {
            public ulong DataOffset;
            public ulong DataSize;
            public uint GroupIndex;
            public uint NumberOfGroups;
        }

        private struct RvzGroupEntry
        {
            public uint DataOffset;
            public uint DataSize;
            public uint RvzPackedSize;
        }

        #endregion

        #region Public API

        public static bool IsRvzPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            return ext != null && ext.Equals(".rvz", StringComparison.OrdinalIgnoreCase);
        }

        public static ArchiveUtils.TempFile DecompressToTempFile(string rvzPath, Action<ulong, ulong> progressCallback = null)
        {
            if (string.IsNullOrWhiteSpace(rvzPath))
                throw new ArgumentNullException(nameof(rvzPath));

            if (!File.Exists(rvzPath))
                throw new FileNotFoundException("RVZ file not found.", rvzPath);

            var outPath = Path.Combine(Path.GetTempPath(), $"PlayniteAchievements_rvz_{Guid.NewGuid():N}.iso");
            var outFileCreated = false;

            try
            {
                using (var inStream = new FileStream(rvzPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // Read and validate headers
                    if (!TryReadHeaders(inStream, out var header1, out var header2))
                    {
                        throw new InvalidDataException("Invalid RVZ file headers.");
                    }

                    // Validate file size
                    if (header1.WiaFileSize != (ulong)inStream.Length)
                    {
                        throw new InvalidDataException("RVZ file size mismatch.");
                    }

                    var isoSize = header1.IsoFileSize;
                    if (isoSize > long.MaxValue)
                    {
                        throw new InvalidDataException($"RVZ decompressed size too large: {isoSize} bytes.");
                    }

                    using (var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        outFileCreated = true;

                        // Read partition entries
                        var partitionEntries = ReadPartitionEntries(inStream, header2);

                        // Read raw data entries (may be compressed)
                        var rawDataEntries = ReadRawDataEntries(inStream, header2);

                        // Read group entries (may be compressed)
                        var groupEntries = ReadGroupEntries(inStream, header2);

                        // Write disc header from header2
                        WriteDiscHeader(outStream, header2);

                        // Decompress all data
                        DecompressAllData(
                            inStream,
                            outStream,
                            header2,
                            partitionEntries,
                            rawDataEntries,
                            groupEntries,
                            isoSize,
                            progressCallback);
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

        #endregion

        #region Header Reading

        private static bool TryReadHeaders(Stream stream, out WiaHeader1 header1, out WiaHeader2 header2)
        {
            header1 = null;
            header2 = null;

            try
            {
                // Read header 1
                var header1Bytes = new byte[WiaHeader1Size];
                if (stream.Read(header1Bytes, 0, WiaHeader1Size) != WiaHeader1Size)
                    return false;

                header1 = new WiaHeader1
                {
                    Magic = BitConverter.ToUInt32(header1Bytes, 0),
                    Version = BitConverter.ToUInt32(header1Bytes, 4),
                    VersionCompatible = BitConverter.ToUInt32(header1Bytes, 8),
                    Header2Size = BitConverter.ToUInt32(header1Bytes, 12),
                    IsoFileSize = BitConverter.ToUInt64(header1Bytes, 32),
                    WiaFileSize = BitConverter.ToUInt64(header1Bytes, 40)
                };

                Buffer.BlockCopy(header1Bytes, 16, header1.Header2Hash, 0, Sha1DigestSize);
                Buffer.BlockCopy(header1Bytes, 48, header1.Header1Hash, 0, Sha1DigestSize);

                // Validate magic
                if (header1.Magic != RvzMagic)
                    return false;

                // Validate version
                var version = SwapBytes(header1.Version);
                var versionCompatible = SwapBytes(header1.VersionCompatible);
                if (RvzVersion < versionCompatible || RvzVersionReadCompatible > version)
                    return false;

                // Validate header 1 hash
                var actualHash1 = ComputeSha1(header1Bytes, 0, WiaHeader1Size - Sha1DigestSize);
                if (!actualHash1.SequenceEqual(header1.Header1Hash))
                    return false;

                // Read header 2
                var header2Size = (int)SwapBytes(header1.Header2Size);
                if (header2Size < WiaHeader2Size - 7) // Minimum size without compressor_data
                    return false;

                var header2Bytes = new byte[header2Size];
                if (stream.Read(header2Bytes, 0, header2Size) != header2Size)
                    return false;

                // Validate header 2 hash
                var actualHash2 = ComputeSha1(header2Bytes);
                if (!actualHash2.SequenceEqual(header1.Header2Hash))
                    return false;

                header2 = new WiaHeader2
                {
                    DiscType = BitConverter.ToUInt32(header2Bytes, 0),
                    CompressionType = BitConverter.ToUInt32(header2Bytes, 4),
                    CompressionLevel = BitConverter.ToInt32(header2Bytes, 8),
                    ChunkSize = BitConverter.ToUInt32(header2Bytes, 12),
                    NumberOfPartitionEntries = BitConverter.ToUInt32(header2Bytes, 140),
                    PartitionEntrySize = BitConverter.ToUInt32(header2Bytes, 144),
                    PartitionEntriesOffset = BitConverter.ToUInt64(header2Bytes, 148),
                    NumberOfRawDataEntries = BitConverter.ToUInt32(header2Bytes, 172),
                    RawDataEntriesOffset = BitConverter.ToUInt64(header2Bytes, 176),
                    RawDataEntriesSize = BitConverter.ToUInt32(header2Bytes, 184),
                    NumberOfGroupEntries = BitConverter.ToUInt32(header2Bytes, 188),
                    GroupEntriesOffset = BitConverter.ToUInt64(header2Bytes, 192),
                    GroupEntriesSize = BitConverter.ToUInt32(header2Bytes, 200),
                    CompressorDataSize = header2Bytes[204]
                };

                Buffer.BlockCopy(header2Bytes, 16, header2.DiscHeader, 0, 0x80);
                Buffer.BlockCopy(header2Bytes, 160, header2.PartitionEntriesHash, 0, Sha1DigestSize);
                Buffer.BlockCopy(header2Bytes, 205, header2.CompressorData, 0, 7);

                // Validate compression type
                var compressionType = (RvzCompressionType)SwapBytes(header2.CompressionType);
                if (compressionType > RvzCompressionType.Zstd)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteDiscHeader(Stream outStream, WiaHeader2 header2)
        {
            outStream.Write(header2.DiscHeader, 0, header2.DiscHeader.Length);
        }

        #endregion

        #region Entry Reading

        private static List<PartitionEntry> ReadPartitionEntries(Stream stream, WiaHeader2 header2)
        {
            var entries = new List<PartitionEntry>();
            var offset = (long)SwapBytes(header2.PartitionEntriesOffset);
            var count = (int)SwapBytes(header2.NumberOfPartitionEntries);
            var entrySize = (int)SwapBytes(header2.PartitionEntrySize);

            if (count == 0 || offset == 0)
                return entries;

            stream.Position = offset;
            var buffer = new byte[count * entrySize];
            stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < count; i++)
            {
                var entry = new PartitionEntry();
                var entryOffset = i * entrySize;

                // Read partition key (16 bytes)
                Buffer.BlockCopy(buffer, entryOffset, entry.PartitionKey, 0, 16);

                // Read data entries (2 x 16 bytes)
                for (int j = 0; j < 2; j++)
                {
                    var dataEntryOffset = entryOffset + 16 + j * 16;
                    entry.DataEntries[j] = new PartitionDataEntry
                    {
                        FirstSector = SwapBytes(BitConverter.ToUInt32(buffer, dataEntryOffset)),
                        NumberOfSectors = SwapBytes(BitConverter.ToUInt32(buffer, dataEntryOffset + 4)),
                        GroupIndex = SwapBytes(BitConverter.ToUInt32(buffer, dataEntryOffset + 8)),
                        NumberOfGroups = SwapBytes(BitConverter.ToUInt32(buffer, dataEntryOffset + 12))
                    };
                }

                entries.Add(entry);
            }

            return entries;
        }

        private static List<RawDataEntry> ReadRawDataEntries(Stream stream, WiaHeader2 header2)
        {
            var entries = new List<RawDataEntry>();
            var compressionType = (RvzCompressionType)SwapBytes(header2.CompressionType);
            var offset = (long)SwapBytes(header2.RawDataEntriesOffset);
            var compressedSize = (int)SwapBytes(header2.RawDataEntriesSize);
            var count = (int)SwapBytes(header2.NumberOfRawDataEntries);

            if (count == 0 || offset == 0)
                return entries;

            stream.Position = offset;

            byte[] decompressedData;
            if (compressionType == RvzCompressionType.None)
            {
                decompressedData = new byte[compressedSize];
                stream.Read(decompressedData, 0, compressedSize);
            }
            else
            {
                var compressedData = new byte[compressedSize];
                stream.Read(compressedData, 0, compressedSize);
                decompressedData = DecompressData(compressedData, count * RawDataEntrySize, compressionType);
            }

            for (int i = 0; i < count; i++)
            {
                var entryOffset = i * RawDataEntrySize;
                entries.Add(new RawDataEntry
                {
                    DataOffset = SwapBytes(BitConverter.ToUInt64(decompressedData, entryOffset)),
                    DataSize = SwapBytes(BitConverter.ToUInt64(decompressedData, entryOffset + 8)),
                    GroupIndex = SwapBytes(BitConverter.ToUInt32(decompressedData, entryOffset + 16)),
                    NumberOfGroups = SwapBytes(BitConverter.ToUInt32(decompressedData, entryOffset + 20))
                });
            }

            return entries;
        }

        private static List<RvzGroupEntry> ReadGroupEntries(Stream stream, WiaHeader2 header2)
        {
            var entries = new List<RvzGroupEntry>();
            var compressionType = (RvzCompressionType)SwapBytes(header2.CompressionType);
            var offset = (long)SwapBytes(header2.GroupEntriesOffset);
            var compressedSize = (int)SwapBytes(header2.GroupEntriesSize);
            var count = (int)SwapBytes(header2.NumberOfGroupEntries);

            if (count == 0 || offset == 0)
                return entries;

            stream.Position = offset;

            byte[] decompressedData;
            if (compressionType == RvzCompressionType.None)
            {
                decompressedData = new byte[compressedSize];
                stream.Read(decompressedData, 0, compressedSize);
            }
            else
            {
                var compressedData = new byte[compressedSize];
                stream.Read(compressedData, 0, compressedSize);
                decompressedData = DecompressData(compressedData, count * RvzGroupEntrySize, compressionType);
            }

            for (int i = 0; i < count; i++)
            {
                var entryOffset = i * RvzGroupEntrySize;
                entries.Add(new RvzGroupEntry
                {
                    DataOffset = SwapBytes(BitConverter.ToUInt32(decompressedData, entryOffset)),
                    DataSize = SwapBytes(BitConverter.ToUInt32(decompressedData, entryOffset + 4)),
                    RvzPackedSize = SwapBytes(BitConverter.ToUInt32(decompressedData, entryOffset + 8))
                });
            }

            return entries;
        }

        #endregion

        #region Decompression

        private static void DecompressAllData(
            Stream inStream,
            Stream outStream,
            WiaHeader2 header2,
            List<PartitionEntry> partitionEntries,
            List<RawDataEntry> rawDataEntries,
            List<RvzGroupEntry> groupEntries,
            ulong isoSize,
            Action<ulong, ulong> progressCallback)
        {
            var chunkSize = SwapBytes(header2.ChunkSize);
            var compressionType = (RvzCompressionType)SwapBytes(header2.CompressionType);
            var bytesWritten = (ulong)outStream.Position;

            // Process each data range in order
            var sortedRanges = GetSortedDataRanges(partitionEntries, rawDataEntries);

            foreach (var range in sortedRanges)
            {
                if (range.IsPartition)
                {
                    // Handle partition data
                    var partition = partitionEntries[range.PartitionIndex];
                    var partData = partition.DataEntries[range.PartitionDataIndex];

                    DecompressPartitionData(
                        inStream,
                        outStream,
                        groupEntries,
                        partData,
                        compressionType,
                        chunkSize,
                        ref bytesWritten);
                }
                else
                {
                    // Handle raw data
                    var rawData = rawDataEntries[range.RawDataIndex];

                    DecompressRawData(
                        inStream,
                        outStream,
                        groupEntries,
                        rawData,
                        compressionType,
                        chunkSize,
                        ref bytesWritten);
                }

                progressCallback?.Invoke(bytesWritten, isoSize);
            }
        }

        private static List<(ulong Offset, ulong Size, bool IsPartition, int PartitionIndex, int PartitionDataIndex, int RawDataIndex)>
            GetSortedDataRanges(List<PartitionEntry> partitionEntries, List<RawDataEntry> rawDataEntries)
        {
            var ranges = new List<(ulong, ulong, bool, int, int, int)>();

            // Add partition ranges
            for (int i = 0; i < partitionEntries.Count; i++)
            {
                var partition = partitionEntries[i];
                for (int j = 0; j < partition.DataEntries.Length; j++)
                {
                    var dataEntry = partition.DataEntries[j];
                    if (dataEntry.NumberOfSectors == 0) continue;

                    var offset = (ulong)dataEntry.FirstSector * WiiBlockTotalSize;
                    var size = (ulong)dataEntry.NumberOfSectors * WiiBlockTotalSize;
                    ranges.Add((offset, size, true, i, j, -1));
                }
            }

            // Add raw data ranges
            for (int i = 0; i < rawDataEntries.Count; i++)
            {
                var rawData = rawDataEntries[i];
                if (rawData.DataSize == 0) continue;

                ranges.Add((rawData.DataOffset, rawData.DataSize, false, -1, -1, i));
            }

            return ranges.OrderBy(r => r.Item1).ToList();
        }

        private static void DecompressPartitionData(
            Stream inStream,
            Stream outStream,
            List<RvzGroupEntry> groupEntries,
            PartitionDataEntry partData,
            RvzCompressionType headerCompressionType,
            uint chunkSize,
            ref ulong bytesWritten)
        {
            var groupIndex = partData.GroupIndex;
            var numberOfGroups = partData.NumberOfGroups;
            var dataOffset = (ulong)partData.FirstSector * WiiBlockTotalSize;
            var dataSize = (ulong)partData.NumberOfSectors * WiiBlockTotalSize;

            var groupOffsetInData = 0UL;

            for (uint i = 0; i < numberOfGroups && groupOffsetInData < dataSize; i++)
            {
                var totalGroupIndex = groupIndex + i;
                if (totalGroupIndex >= groupEntries.Count)
                    break;

                var group = groupEntries[(int)totalGroupIndex];
                var currentChunkSize = Math.Min((ulong)chunkSize, dataSize - groupOffsetInData);

                var groupDataSize = group.DataSize & 0x7FFFFFFF;
                var isGroupCompressed = (group.DataSize & 0x80000000) == 0;

                var compressionType = isGroupCompressed ? headerCompressionType : RvzCompressionType.None;
                var rvzPackedSize = group.RvzPackedSize;

                if (groupDataSize == 0)
                {
                    // Zero-filled group
                    var zeroBuffer = new byte[currentChunkSize];
                    outStream.Write(zeroBuffer, 0, (int)currentChunkSize);
                }
                else
                {
                    var groupOffsetInFile = (ulong)group.DataOffset << 2;

                    // Read and decompress group data
                    var groupData = ReadAndDecompressGroup(
                        inStream,
                        groupOffsetInFile,
                        groupDataSize,
                        currentChunkSize,
                        compressionType,
                        rvzPackedSize,
                        dataOffset + groupOffsetInData);

                    outStream.Write(groupData, 0, (int)currentChunkSize);
                }

                groupOffsetInData += currentChunkSize;
                bytesWritten += currentChunkSize;
            }
        }

        private static void DecompressRawData(
            Stream inStream,
            Stream outStream,
            List<RvzGroupEntry> groupEntries,
            RawDataEntry rawData,
            RvzCompressionType headerCompressionType,
            uint chunkSize,
            ref ulong bytesWritten)
        {
            var groupIndex = rawData.GroupIndex;
            var numberOfGroups = rawData.NumberOfGroups;
            var dataOffset = rawData.DataOffset;
            var dataSize = rawData.DataSize;

            var groupOffsetInData = 0UL;

            for (uint i = 0; i < numberOfGroups && groupOffsetInData < dataSize; i++)
            {
                var totalGroupIndex = groupIndex + i;
                if (totalGroupIndex >= groupEntries.Count)
                    break;

                var group = groupEntries[(int)totalGroupIndex];
                var currentChunkSize = Math.Min((ulong)chunkSize, dataSize - groupOffsetInData);

                var groupDataSize = group.DataSize & 0x7FFFFFFF;
                var isGroupCompressed = (group.DataSize & 0x80000000) == 0;

                var compressionType = isGroupCompressed ? headerCompressionType : RvzCompressionType.None;
                var rvzPackedSize = group.RvzPackedSize;

                if (groupDataSize == 0)
                {
                    // Zero-filled group
                    var zeroBuffer = new byte[currentChunkSize];
                    outStream.Write(zeroBuffer, 0, (int)currentChunkSize);
                }
                else
                {
                    var groupOffsetInFile = (ulong)group.DataOffset << 2;

                    var groupData = ReadAndDecompressGroup(
                        inStream,
                        groupOffsetInFile,
                        groupDataSize,
                        currentChunkSize,
                        compressionType,
                        rvzPackedSize,
                        dataOffset + groupOffsetInData);

                    outStream.Write(groupData, 0, (int)currentChunkSize);
                }

                groupOffsetInData += currentChunkSize;
                bytesWritten += currentChunkSize;
            }
        }

        private static byte[] ReadAndDecompressGroup(
            Stream stream,
            ulong offsetInFile,
            uint compressedSize,
            ulong decompressedSize,
            RvzCompressionType compressionType,
            uint rvzPackedSize,
            ulong dataOffset)
        {
            stream.Position = (long)offsetInFile;

            var compressedData = new byte[compressedSize];
            stream.Read(compressedData, 0, (int)compressedSize);

            byte[] decompressed;

            if (compressionType == RvzCompressionType.None)
            {
                decompressed = compressedData;
            }
            else
            {
                decompressed = DecompressData(compressedData, (int)decompressedSize, compressionType);
            }

            // Handle RVZ packing
            if (rvzPackedSize > 0)
            {
                decompressed = RvzUnpack(decompressed, (int)rvzPackedSize, (int)decompressedSize, dataOffset);
            }

            return decompressed;
        }

        private static byte[] DecompressData(byte[] compressedData, int decompressedSize, RvzCompressionType compressionType)
        {
            switch (compressionType)
            {
                case RvzCompressionType.None:
                    return compressedData;

                case RvzCompressionType.Zstd:
                    return DecompressZstd(compressedData, decompressedSize);

                case RvzCompressionType.Purge:
                    return DecompressPurge(compressedData, decompressedSize);

                // LZMA/LZMA2/Bzip2 would require additional libraries
                default:
                    throw new NotSupportedException(
                        $"RVZ compression type {compressionType} is not supported. " +
                        $"Supported types: None, Purge, Zstd (if ZstdSharp.Port is installed).");
            }
        }

        private static byte[] DecompressZstd(byte[] compressedData, int decompressedSize)
        {
            // Try to use ZstdSharp via reflection to avoid hard dependency
            try
            {
                var assembly = Assembly.Load("ZstdSharp");
                if (assembly != null)
                {
                    var decompressorType = assembly.GetType("ZstdSharp.Decompressor");
                    if (decompressorType != null)
                    {
                        var decompressor = Activator.CreateInstance(decompressorType);
                        var unwrapMethod = decompressorType.GetMethod("Unwrap", new[] { typeof(byte[]), typeof(int) });
                        if (unwrapMethod != null)
                        {
                            var result = unwrapMethod.Invoke(decompressor, new object[] { compressedData, decompressedSize });
                            if (result is Array arr)
                            {
                                var bytes = new byte[arr.Length];
                                Array.Copy(arr, bytes, bytes.Length);
                                return bytes;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ZstdSharp not available
            }

            throw new NotSupportedException(
                "Zstd compression requires ZstdSharp.Port package to be installed. " +
                "Run: nuget install ZstdSharp.Port -Version 0.8.2");
        }

        private static byte[] DecompressPurge(byte[] compressedData, int decompressedSize)
        {
            // Purge compression stores data with zero-run-length encoding
            var output = new byte[decompressedSize];
            var outPos = 0;
            var inPos = 0;

            while (inPos < compressedData.Length && outPos < decompressedSize)
            {
                var runLength = compressedData[inPos++];

                if (runLength == 0)
                {
                    // Zero run - expand to 256 zeros
                    var count = Math.Min(256, decompressedSize - outPos);
                    outPos += count;
                }
                else if (runLength <= 128)
                {
                    // Non-zero run - copy runLength bytes
                    var count = Math.Min(runLength, decompressedSize - outPos);
                    if (inPos + count <= compressedData.Length)
                    {
                        Array.Copy(compressedData, inPos, output, outPos, count);
                    }
                    inPos += runLength;
                    outPos += count;
                }
                else
                {
                    // Repeated byte - copy (256 - runLength) times
                    var count = Math.Min(256 - runLength, decompressedSize - outPos);
                    if (inPos < compressedData.Length)
                    {
                        var repeatByte = compressedData[inPos++];
                        for (int i = 0; i < count; i++)
                        {
                            output[outPos++] = repeatByte;
                        }
                    }
                }
            }

            return output;
        }

        #endregion

        #region RVZ Packing

        private static byte[] RvzUnpack(byte[] packedData, int packedSize, int unpackedSize, ulong dataOffset)
        {
            var output = new byte[unpackedSize];
            var inPos = 0;
            var outPos = 0;

            while (inPos < packedSize && outPos < unpackedSize)
            {
                if (inPos + 4 > packedData.Length)
                    break;

                var chunkHeader = SwapBytes(BitConverter.ToUInt32(packedData, inPos));
                inPos += 4;

                var isJunk = (chunkHeader & 0x80000000) != 0;
                var chunkSize = (int)(chunkHeader & 0x7FFFFFFF);

                if (isJunk)
                {
                    // Junk data - reconstruct using Lagged Fibonacci Generator
                    var seed = new byte[16];
                    if (inPos + 16 <= packedData.Length)
                    {
                        Array.Copy(packedData, inPos, seed, 0, 16);
                    }
                    inPos += 16;

                    var chunkOffset = dataOffset + (ulong)outPos;
                    var junkData = LaggedFibonacciGenerator.Generate(seed, chunkSize, chunkOffset);
                    var copySize = Math.Min(chunkSize, unpackedSize - outPos);
                    Array.Copy(junkData, 0, output, outPos, copySize);
                    outPos += copySize;
                }
                else
                {
                    // Non-junk data - copy directly
                    var copySize = Math.Min(chunkSize, unpackedSize - outPos);
                    if (inPos + copySize <= packedData.Length)
                    {
                        Array.Copy(packedData, inPos, output, outPos, copySize);
                    }
                    inPos += chunkSize;
                    outPos += copySize;
                }
            }

            return output;
        }

        #endregion

        #region Lagged Fibonacci Generator

        private static class LaggedFibonacciGenerator
        {
            public static byte[] Generate(byte[] seed, int size, ulong offset)
            {
                var output = new byte[size];

                if (seed == null || seed.Length < 16)
                    return output;

                // Parse seed into 4 32-bit integers
                var state = new uint[4];
                for (int i = 0; i < 4; i++)
                {
                    state[i] = BitConverter.ToUInt32(seed, i * 4);
                }

                // Calculate initial offset in the generator's sequence
                // The LFG skips based on the offset in 32KB blocks
                var skipCount = (int)((offset / 0x8000) % 968);

                // Skip ahead in the sequence
                for (int i = 0; i < skipCount; i++)
                {
                    AdvanceState(state);
                }

                // Generate output bytes
                for (int i = 0; i < size; i++)
                {
                    output[i] = (byte)(GenerateByte(state) ^ seed[i % 16]);
                }

                return output;
            }

            private static void AdvanceState(uint[] state)
            {
                // Advance the LFG state using the characteristic polynomial
                var feedback = state[0] ^ state[1] ^ state[2] ^ state[3];

                state[0] = state[1];
                state[1] = state[2];
                state[2] = state[3];
                state[3] = feedback;

                // Apply Galois field reduction
                if ((feedback & 1) != 0)
                {
                    state[3] ^= 0xA3000000;
                }
                if ((feedback & 2) != 0)
                {
                    state[3] ^= 0x1C000000;
                }
            }

            private static byte GenerateByte(uint[] state)
            {
                // Generate a pseudo-random byte from the state
                var result = (byte)((state[0] ^ state[1] ^ state[2] ^ state[3]) & 0xFF);
                AdvanceState(state);
                return result;
            }
        }

        #endregion

        #region Utility Methods

        private static uint SwapBytes(uint value)
        {
            return ((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0xFF000000) >> 24);
        }

        private static int SwapBytes(int value)
        {
            return (int)SwapBytes((uint)value);
        }

        private static ulong SwapBytes(ulong value)
        {
            return ((value & 0x00000000000000FFUL) << 56) |
                   ((value & 0x000000000000FF00UL) << 40) |
                   ((value & 0x0000000000FF0000UL) << 24) |
                   ((value & 0x00000000FF000000UL) << 8) |
                   ((value & 0x000000FF00000000UL) >> 8) |
                   ((value & 0x0000FF0000000000UL) >> 24) |
                   ((value & 0x00FF000000000000UL) >> 40) |
                   ((value & 0xFF00000000000000UL) >> 56);
        }

        private static byte[] ComputeSha1(byte[] data, int offset, int count)
        {
            using (var sha1 = SHA1.Create())
            {
                return sha1.ComputeHash(data, offset, count);
            }
        }

        private static byte[] ComputeSha1(byte[] data)
        {
            return ComputeSha1(data, 0, data.Length);
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

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

        #endregion
    }
}

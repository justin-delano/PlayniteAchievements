using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing.Hashers
{
    internal sealed class NdsCustomHasher : IRaHasher
    {
        public string Name => "Nintendo DS (header + arm9 + arm7 + icon/title MD5)";

        public async Task<IReadOnlyList<string>> ComputeHashesAsync(string filePath, CancellationToken cancel)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var md5 = MD5.Create())
            {
                var header = new byte[512];
                long baseOffset = 0;

                if (await HashUtils.ReadExactlyAsync(stream, header, 0, header.Length, cancel).ConfigureAwait(false) != 512)
                {
                    return Array.Empty<string>();
                }

                // SuperCard header detection: ignore first 512 bytes and re-read header.
                if (header[0] == 0x2E && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0xEA &&
                    header[0xB0] == 0x44 && header[0xB1] == 0x46 && header[0xB2] == 0x96 && header[0xB3] == 0x00)
                {
                    baseOffset = 512;
                    stream.Seek(baseOffset, SeekOrigin.Begin);
                    if (await HashUtils.ReadExactlyAsync(stream, header, 0, header.Length, cancel).ConfigureAwait(false) != 512)
                    {
                        return Array.Empty<string>();
                    }
                }

                var arm9Offset = HashUtils.ReadUInt32LE(header, 0x20);
                var arm9Size = HashUtils.ReadUInt32LE(header, 0x2C);
                var arm7Offset = HashUtils.ReadUInt32LE(header, 0x30);
                var arm7Size = HashUtils.ReadUInt32LE(header, 0x3C);
                var iconOffset = HashUtils.ReadUInt32LE(header, 0x68);

                if (arm9Size + arm7Size > 16u * 1024u * 1024u)
                {
                    return Array.Empty<string>();
                }

                // Hash header (first 0x160 bytes), then arm9, arm7, then 0xA00 bytes icon/title (0-padded if short).
                md5.TransformBlock(header, 0, 0x160, null, 0);

                var workSize = (int)Math.Max(0xA00u, Math.Max(arm9Size, arm7Size));
                var buffer = new byte[workSize];

                if (arm9Size > 0)
                {
                    stream.Seek(baseOffset + arm9Offset, SeekOrigin.Begin);
                    var read = await HashUtils.ReadExactlyAsync(stream, buffer, 0, (int)arm9Size, cancel).ConfigureAwait(false);
                    if (read != (int)arm9Size) return Array.Empty<string>();
                    md5.TransformBlock(buffer, 0, (int)arm9Size, null, 0);
                }

                if (arm7Size > 0)
                {
                    stream.Seek(baseOffset + arm7Offset, SeekOrigin.Begin);
                    var read = await HashUtils.ReadExactlyAsync(stream, buffer, 0, (int)arm7Size, cancel).ConfigureAwait(false);
                    if (read != (int)arm7Size) return Array.Empty<string>();
                    md5.TransformBlock(buffer, 0, (int)arm7Size, null, 0);
                }

                stream.Seek(baseOffset + iconOffset, SeekOrigin.Begin);
                var iconRead = await HashUtils.ReadExactlyAsync(stream, buffer, 0, 0xA00, cancel).ConfigureAwait(false);
                if (iconRead < 0xA00)
                {
                    Array.Clear(buffer, iconRead, 0xA00 - iconRead);
                }
                md5.TransformBlock(buffer, 0, 0xA00, null, 0);

                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return new[] { HashUtils.ToHexLower(md5.Hash) };
            }
        }
    }
}


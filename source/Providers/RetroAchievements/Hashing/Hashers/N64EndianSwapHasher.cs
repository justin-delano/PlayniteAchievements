using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing.Hashers
{
    internal sealed class N64EndianSwapHasher : IRaHasher
    {
        public string Name => "Nintendo 64 (endian normalized MD5)";

        public async Task<IReadOnlyList<string>> ComputeHashesAsync(string filePath, CancellationToken cancel)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var first = new byte[1];
                var read = await stream.ReadAsync(first, 0, 1, cancel).ConfigureAwait(false);
                if (read != 1)
                {
                    return Array.Empty<string>();
                }

                var b0 = first[0];
                var swap16 = false;
                var swap32 = false;

                if (b0 == 0x80)
                {
                    // z64 - big endian
                }
                else if (b0 == 0x37)
                {
                    // v64 - byteswapped (16-bit)
                    swap16 = true;
                }
                else if (b0 == 0x40)
                {
                    // n64 - little endian (32-bit words)
                    swap32 = true;
                }
                else if (b0 == 0xE8 || b0 == 0x22)
                {
                    // ndd format - don't byteswap
                }
                else
                {
                    return Array.Empty<string>();
                }

                stream.Seek(0, SeekOrigin.Begin);

                using (var md5 = MD5.Create())
                {
                    var remaining = Math.Min(stream.Length, (long)HashUtils.MaxHashBytes);
                    var buffer = new byte[64 * 1024];

                    while (remaining > 0)
                    {
                        cancel.ThrowIfCancellationRequested();

                        var toRead = (int)Math.Min(buffer.Length, remaining);
                        var n = await stream.ReadAsync(buffer, 0, toRead, cancel).ConfigureAwait(false);
                        if (n <= 0) break;

                        if (swap16) HashUtils.ByteSwap16(buffer, n);
                        else if (swap32) HashUtils.ByteSwap32(buffer, n);

                        md5.TransformBlock(buffer, 0, n, null, 0);
                        remaining -= n;
                    }

                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return new[] { HashUtils.ToHexLower(md5.Hash) };
                }
            }
        }
    }
}


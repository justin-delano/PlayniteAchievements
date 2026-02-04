using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing.Hashers
{
    internal sealed class HeaderMagicSkipHasher : IRaHasher
    {
        private readonly IReadOnlyList<byte[]> _magicPrefixes;
        private readonly int _skipBytes;

        public HeaderMagicSkipHasher(IReadOnlyList<byte[]> magicPrefixes, int skipBytes)
        {
            _magicPrefixes = magicPrefixes ?? throw new ArgumentNullException(nameof(magicPrefixes));
            _skipBytes = Math.Max(0, skipBytes);
        }

        public string Name => $"MD5 (magic header skip {_skipBytes} bytes)";

        public async Task<IReadOnlyList<string>> ComputeHashesAsync(string filePath, CancellationToken cancel)
        {
            if (_magicPrefixes.Count == 0)
            {
                throw new InvalidOperationException("No magic prefixes configured.");
            }

            var maxMagicLen = _magicPrefixes.Max(m => m?.Length ?? 0);
            if (maxMagicLen <= 0)
            {
                throw new InvalidOperationException("Invalid magic prefix configuration.");
            }

            var header = new byte[maxMagicLen];
            long offset = 0;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var read = await HashUtils.ReadExactlyAsync(stream, header, 0, header.Length, cancel).ConfigureAwait(false);
                if (read > 0)
                {
                    foreach (var magic in _magicPrefixes)
                    {
                        if (magic == null || magic.Length == 0 || read < magic.Length) continue;

                        var matches = true;
                        for (var i = 0; i < magic.Length; i++)
                        {
                            if (header[i] != magic[i])
                            {
                                matches = false;
                                break;
                            }
                        }

                        if (matches)
                        {
                            offset = _skipBytes;
                            break;
                        }
                    }
                }
            }

            var maxBytes = Math.Max(0, (long)HashUtils.MaxHashBytes - offset);
            var hash = await HashUtils
                .ComputeMd5HexFromFileAsync(filePath, startOffset: offset, maxBytes: maxBytes, cancel)
                .ConfigureAwait(false);

            return new[] { hash };
        }
    }
}


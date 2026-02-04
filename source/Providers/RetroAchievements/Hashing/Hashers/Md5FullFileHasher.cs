using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing.Hashers
{
    internal sealed class Md5FullFileHasher : IRaHasher
    {
        public string Name => "MD5 (whole file)";

        public async Task<IReadOnlyList<string>> ComputeHashesAsync(string filePath, CancellationToken cancel)
        {
            var hash = await HashUtils
                .ComputeMd5HexFromFileAsync(filePath, startOffset: 0, maxBytes: HashUtils.MaxHashBytes, cancel)
                .ConfigureAwait(false);

            return new[] { hash };
        }
    }
}


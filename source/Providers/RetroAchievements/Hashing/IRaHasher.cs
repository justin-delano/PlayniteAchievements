using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing
{
    internal interface IRaHasher
    {
        string Name { get; }

        Task<IReadOnlyList<string>> ComputeHashesAsync(string filePath, CancellationToken cancel);
    }
}


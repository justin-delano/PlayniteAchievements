using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing
{
    /// <summary>
    /// Base class for disc image hashers that provides common error handling and validation.
    /// </summary>
    internal abstract class DiscBasedHasher : IRaHasher
    {
        /// <summary>
        /// Logger for recording hash failures and warnings.
        /// </summary>
        protected readonly ILogger Logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscBasedHasher"/> class.
        /// </summary>
        /// <param name="logger">Logger for recording hash failures and warnings.</param>
        protected DiscBasedHasher(ILogger logger)
        {
            Logger = logger;
        }

        /// <summary>
        /// Gets the name of this hasher.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Computes hashes for the specified disc image file.
        /// </summary>
        /// <param name="filePath">Path to the disc image file.</param>
        /// <param name="cancel">Cancellation token for async operation.</param>
        /// <returns>
        /// A list of hash strings, or an empty list if the file does not exist
        /// or an error occurs during hashing.
        /// </returns>
        public async Task<IReadOnlyList<string>> ComputeHashesAsync(string filePath, CancellationToken cancel)
        {
            if (!File.Exists(filePath))
            {
                return Array.Empty<string>();
            }

            try
            {
                return await ComputeHashesInternalAsync(filePath, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.Warn(ex, $"[RA] {Name}: Failed to hash disc image: {filePath}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Performs the actual hash computation for the disc image.
        /// </summary>
        /// <param name="filePath">Path to the disc image file.</param>
        /// <param name="cancel">Cancellation token for async operation.</param>
        /// <returns>A list of hash strings.</returns>
        /// <remarks>
        /// Implementations do not need to handle file existence checks or top-level exception handling,
        /// as these are managed by the base class.
        /// </remarks>
        protected abstract Task<IReadOnlyList<string>> ComputeHashesInternalAsync(string filePath, CancellationToken cancel);
    }
}

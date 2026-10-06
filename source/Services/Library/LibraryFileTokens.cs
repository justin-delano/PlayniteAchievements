using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// Managed files as projection values: <c>{"file":"sha256:..."}</c> by content, so the same
    /// file copied to a new managed path is the same value. File hashes are cached by path,
    /// length and write time.
    /// </summary>
    public static class LibraryFileTokens
    {
        public const string FileProperty = "file";

        private const string HashPrefix = "sha256:";
        private const string MissingPrefix = "missing:";

        private static readonly ConcurrentDictionary<string, CachedHash> HashCache =
            new ConcurrentDictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);

        private sealed class CachedHash
        {
            public CachedHash(long length, DateTime writeUtc, string hash)
            {
                Length = length;
                WriteUtc = writeUtc;
                Hash = hash;
            }

            public long Length { get; }

            public DateTime WriteUtc { get; }

            public string Hash { get; }
        }

        /// <summary>The token of a file, or null for a blank path.</summary>
        public static JObject FileValue(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : new JObject { [FileProperty] = FileToken(path) };
        }

        /// <summary>
        /// The content hash of a file as <c>sha256:&lt;hex&gt;</c>, or <c>missing:&lt;path&gt;</c>
        /// when it cannot be read.
        /// </summary>
        public static string FileToken(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return MissingPrefix + path.Trim().ToLowerInvariant();
                }

                if (HashCache.TryGetValue(info.FullName, out var cached)
                    && cached.Length == info.Length
                    && cached.WriteUtc == info.LastWriteTimeUtc)
                {
                    return cached.Hash;
                }

                var hash = HashPrefix + LibraryStore.HashFile(info.FullName);
                HashCache[info.FullName] = new CachedHash(info.Length, info.LastWriteTimeUtc, hash);
                return hash;
            }
            catch (Exception)
            {
                return MissingPrefix + path.Trim().ToLowerInvariant();
            }
        }

        /// <summary>The content hash of a text as <c>sha256:&lt;hex&gt;</c> over its UTF-8 bytes, or null for a blank text.</summary>
        public static string TextToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using (var sha = SHA256.Create())
            {
                return HashPrefix + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
    }
}

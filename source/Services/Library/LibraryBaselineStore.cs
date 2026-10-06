using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>A baseline written by an apply: its file name and the hash of the projection it holds.</summary>
    public sealed class LibraryBaseline
    {
        public LibraryBaseline(string file, string hash)
        {
            File = file;
            Hash = hash;
        }

        /// <summary>The file name inside the baselines folder, as a link stores it.</summary>
        public string File { get; }

        public string Hash { get; }
    }

    /// <summary>
    /// The baselines links merge against, in <c>UserData\library\baselines</c>: one new file per
    /// apply, named by a fresh GUID and never overwritten, so a link that a settings Cancel
    /// restores still finds the baseline it was written with. Files no link references are
    /// removed by <see cref="Sweep"/>.
    /// </summary>
    public sealed class LibraryBaselineStore
    {
        public const string DirectoryName = "baselines";
        private const string Extension = ".json";

        private readonly string _directory;
        private readonly Action<Exception, string> _warn;

        /// <param name="libraryDirectory">The library folder (<c>UserData\library</c>).</param>
        /// <param name="warn">Optional sink for read and write failures.</param>
        public LibraryBaselineStore(string libraryDirectory, Action<Exception, string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(libraryDirectory))
            {
                throw new ArgumentException("The library folder is required.", nameof(libraryDirectory));
            }

            _directory = Path.Combine(libraryDirectory, DirectoryName);
            _warn = warn;
        }

        public string Directory => _directory;

        /// <summary>
        /// Writes <paramref name="projection"/> to a new baseline file. Throws when it cannot be
        /// written: an apply without its baseline could not be merged later.
        /// </summary>
        public LibraryBaseline Write(JToken projection)
        {
            if (projection == null)
            {
                throw new ArgumentNullException(nameof(projection));
            }

            var text = projection.ToString(Formatting.None);
            System.IO.Directory.CreateDirectory(_directory);
            var name = Guid.NewGuid().ToString("N") + Extension;
            using (var stream = new FileStream(Path.Combine(_directory, name), FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
            }

            return new LibraryBaseline(name, HashProjection(projection));
        }

        /// <summary>The projection a baseline holds, or null when the file is missing or unreadable.</summary>
        public JToken Read(string baselineFile)
        {
            var path = Resolve(baselineFile);
            if (path == null || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return JToken.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, $"Failed reading the library baseline {baselineFile}.");
                return null;
            }
        }

        /// <summary>The absolute path of a baseline: a file name resolves inside the baselines folder, a rooted path stays as is.</summary>
        public string Resolve(string baselineFile)
        {
            if (string.IsNullOrWhiteSpace(baselineFile))
            {
                return null;
            }

            return Path.IsPathRooted(baselineFile) ? baselineFile : Path.Combine(_directory, baselineFile);
        }

        /// <summary>
        /// Deletes every baseline file in the folder that <paramref name="referenced"/> does not
        /// name. The caller passes the baselines of every link that can still come back: the live
        /// settings, the settings edit snapshot when a session is open, and the per-game links.
        /// Returns how many files were deleted.
        /// </summary>
        public int Sweep(IEnumerable<string> referenced)
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return 0;
            }

            var keep = new HashSet<string>(
                (referenced ?? Enumerable.Empty<string>())
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .Select(file => Path.GetFullPath(Resolve(file))),
                StringComparer.OrdinalIgnoreCase);

            var deleted = 0;
            foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*" + Extension).ToList())
            {
                if (keep.Contains(Path.GetFullPath(path)))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                    deleted++;
                }
                catch (Exception ex)
                {
                    _warn?.Invoke(ex, $"Failed removing the unused library baseline {path}.");
                }
            }

            return deleted;
        }

        /// <summary>Lowercase hex SHA-256 of a projection's compact JSON text.</summary>
        public static string HashProjection(JToken projection)
        {
            var text = projection?.ToString(Formatting.None) ?? "null";
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// The zip plumbing every portable package format shares (.pa, .pastyle, .pashowcase,
    /// .pasounds, .pabundle): entry-name normalization, traversal checks, size and entry caps, and
    /// manifest reads. Packages may come from other users, so a reader never trusts an entry name
    /// or a declared length. Writers build beside the destination and swap in, so a failed write
    /// leaves any existing file intact.
    /// </summary>
    internal static class PortablePackage
    {
        /// <summary>Upper bound on a package's declared uncompressed size.</summary>
        public const long MaxPackageBytes = 256L * 1024 * 1024;

        /// <summary>Upper bound on one entry's declared uncompressed size.</summary>
        public const long MaxEntryBytes = 64L * 1024 * 1024;

        public const int MaxEntryCount = 4096;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// True when the file starts with the zip magic ("PK"). The package extensions are bare
        /// (zip inside, like Playnite's .pext), so the extension alone does not prove the container.
        /// </summary>
        public static bool IsZipContent(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    return stream.ReadByte() == 0x50 && stream.ReadByte() == 0x4B;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Forward slashes, no leading slash, trimmed; null for a blank name.</summary>
        public static string NormalizeEntryName(string value)
        {
            var normalized = (value ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        /// <summary>
        /// True when <paramref name="normalizedEntryName"/> is exactly <c>folder/&lt;file&gt;</c>:
        /// one level under the folder, no traversal, and a file name Windows accepts.
        /// </summary>
        public static bool IsFlatEntryUnder(string normalizedEntryName, string folder)
        {
            if (string.IsNullOrWhiteSpace(normalizedEntryName) || string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            var prefix = folder.TrimEnd('/') + "/";
            if (!normalizedEntryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = normalizedEntryName.Substring(prefix.Length);
            return IsSafeFileName(fileName);
        }

        /// <summary>A bare file name: no separators, no traversal, no reserved characters.</summary>
        public static bool IsSafeFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   fileName.IndexOf('/') < 0 &&
                   fileName.IndexOf('\\') < 0 &&
                   !fileName.Contains("..") &&
                   fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public static void EnsureFlatEntryUnderOrThrow(string entryName, string folder, string whatItIs)
        {
            if (!IsFlatEntryUnder(NormalizeEntryName(entryName), folder))
            {
                throw new InvalidOperationException($"Invalid bundled {whatItIs} path '{entryName}'.");
            }
        }

        /// <summary>
        /// Opens a package for reading, rejecting anything that is not a zip before
        /// System.IO.Compression can surface its raw "End of Central Directory" error.
        /// </summary>
        public static ZipArchive OpenRead(string path, string notPackageMessage)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("Package file not found.", path);
            }

            if (!IsZipContent(path))
            {
                throw new InvalidOperationException(notPackageMessage);
            }

            return ZipFile.OpenRead(path);
        }

        /// <summary>
        /// Every file entry keyed by normalized name (the first wins on a case-insensitive clash),
        /// after checking the archive against the entry and byte caps. Directory entries are skipped.
        /// </summary>
        public static IReadOnlyDictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive archive)
        {
            if (archive == null)
            {
                throw new ArgumentNullException(nameof(archive));
            }

            if (archive.Entries.Count > MaxEntryCount)
            {
                throw new InvalidOperationException(
                    $"The package has too many entries ({archive.Entries.Count}; the limit is {MaxEntryCount}).");
            }

            var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var name = NormalizeEntryName(entry.FullName);
                if (name == null || string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                if (entry.Length > MaxEntryBytes)
                {
                    throw new InvalidOperationException(
                        $"The package entry '{name}' is too large ({entry.Length} bytes; the limit is {MaxEntryBytes}).");
                }

                total += entry.Length;
                if (total > MaxPackageBytes)
                {
                    throw new InvalidOperationException(
                        $"The package is too large to import (over {MaxPackageBytes} bytes uncompressed).");
                }

                if (!result.ContainsKey(name))
                {
                    result[name] = entry;
                }
            }

            return result;
        }

        public static string ReadText(ZipArchiveEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true))
            {
                return ReadBounded(reader, entry.Length);
            }
        }

        public static T ReadJson<T>(ZipArchiveEntry entry) where T : class
        {
            var text = ReadText(entry);
            return text == null ? null : JsonConvert.DeserializeObject<T>(text);
        }

        /// <summary>
        /// Reads only the <c>Kind</c> discriminator from a JSON manifest, so a caller can decide
        /// which store owns the package without deserializing a foreign shape.
        /// </summary>
        public static string ReadKind(ZipArchiveEntry manifestEntry)
        {
            var text = ReadText(manifestEntry);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeAnonymousType(text, new { Kind = (string)null })?.Kind;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Copies an entry to <paramref name="destinationPath"/>, stopping at the entry cap even
        /// when the central directory under-reports the size.
        /// </summary>
        public static void ExtractToFile(ZipArchiveEntry entry, string destinationPath)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var source = entry.Open())
            using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    copied += read;
                    if (copied > MaxEntryBytes)
                    {
                        throw new InvalidOperationException(
                            $"The package entry '{entry.FullName}' is larger than its declared size.");
                    }

                    destination.Write(buffer, 0, read);
                }
            }
        }

        /// <summary>
        /// A fresh temp folder under the plugin's temp root for one import; the caller deletes it
        /// with <see cref="TryDeleteDirectory"/> once the files are in managed storage.
        /// </summary>
        public static string CreateScratchDirectory(string purpose)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAchievements",
                purpose,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A locked temp file is left for the OS temp cleanup; nothing depends on it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Writes a package next to <paramref name="destinationPath"/> and swaps it in when
        /// <paramref name="fill"/> completes without throwing.
        /// </summary>
        public static void Write(string destinationPath, Action<ZipArchive> fill)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Destination path is required.", nameof(destinationPath));
            }

            if (fill == null)
            {
                throw new ArgumentNullException(nameof(fill));
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = destinationPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            try
            {
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    fill(archive);
                }

                File.Copy(tempPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        public static void AddText(ZipArchive archive, string entryName, string text)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(text ?? string.Empty);
            }
        }

        public static void AddJson(ZipArchive archive, string entryName, object value, JsonSerializerSettings settings)
        {
            AddText(archive, entryName, JsonConvert.SerializeObject(value, settings));
        }

        public static void AddFile(ZipArchive archive, string entryName, string sourcePath)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var source = File.OpenRead(sourcePath))
            using (var destination = entry.Open())
            {
                source.CopyTo(destination);
            }
        }

        /// <summary>Adds files in name order so two exports of the same content are byte-identical.</summary>
        public static void AddFiles(ZipArchive archive, IEnumerable<KeyValuePair<string, string>> entryNameToSourcePath)
        {
            foreach (var pair in (entryNameToSourcePath ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(pair.Value) || !File.Exists(pair.Value))
                {
                    throw new InvalidOperationException($"Missing bundled file: {pair.Value ?? pair.Key}");
                }

                AddFile(archive, pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// Strips whichever of <paramref name="recognizedSuffixes"/> (longest first) the path ends
        /// with and appends <paramref name="extension"/>, so a dialog-chosen name always lands on
        /// the canonical extension.
        /// </summary>
        public static string NormalizeExportPath(string path, string extension, params string[] recognizedSuffixes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var value = path.Trim();
            foreach (var suffix in recognizedSuffixes.OrderByDescending(suffix => suffix.Length))
            {
                // Longer than the suffix, so a file named after nothing but an extension keeps a
                // usable stem rather than collapsing to an empty string.
                if (value.Length > suffix.Length &&
                    value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - suffix.Length);
                    break;
                }
            }

            return value + extension;
        }

        private static string ReadBounded(TextReader reader, long declaredLength)
        {
            // Text entries are small manifests and templates; a declared length past the entry
            // cap was already rejected by IndexEntries, so a bounded read is belt and braces.
            var builder = new StringBuilder((int)Math.Min(Math.Max(declaredLength, 0), 1 << 20));
            var buffer = new char[8192];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                builder.Append(buffer, 0, read);
                if (builder.Length > MaxEntryBytes)
                {
                    throw new InvalidOperationException("A package text entry is larger than its declared size.");
                }
            }

            return builder.ToString();
        }
    }
}

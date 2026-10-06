using System;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The preset folder of one library kind as the update service writes to it: names map to
    /// files, a name already taken gets a free variant, and saving copies a package in.
    /// </summary>
    public interface ILibraryPackageFolder
    {
        /// <summary>The file of the preset named <paramref name="name"/>, or null when there is none.</summary>
        string Find(string name);

        /// <summary>The name, or a variant of it no preset has yet.</summary>
        string UniqueName(string name);

        /// <summary>Copies <paramref name="packagePath"/> in as the preset <paramref name="name"/>, replacing one of that name, and returns its file.</summary>
        string Save(string name, string packagePath);

        /// <summary>The preset name of a file in the folder.</summary>
        string NameOf(string path);
    }

    /// <summary>A plain folder of package files named <c>&lt;name&gt;&lt;extension&gt;</c>, for kinds without a preset store.</summary>
    public sealed class DirectoryPackageFolder : ILibraryPackageFolder
    {
        private const int MaxNameLength = 64;

        private readonly string _directory;
        private readonly string _extension;

        public DirectoryPackageFolder(string directory, string extension)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A folder is required.", nameof(directory));
            }

            if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException("Extension must start with a dot.", nameof(extension));
            }

            _directory = directory;
            _extension = extension;
        }

        public string Find(string name)
        {
            var sanitized = Sanitize(name);
            var path = Path.Combine(_directory, sanitized + _extension);
            return sanitized.Length > 0 && File.Exists(path) ? path : null;
        }

        public string UniqueName(string name)
        {
            var sanitized = Sanitize(name);
            if (sanitized.Length == 0)
            {
                sanitized = "Preset";
            }

            if (Find(sanitized) == null)
            {
                return sanitized;
            }

            for (var n = 2; n < 1000; n++)
            {
                var suffix = " (" + n + ")";
                var stem = sanitized.Length + suffix.Length > MaxNameLength
                    ? sanitized.Substring(0, MaxNameLength - suffix.Length).TrimEnd()
                    : sanitized;
                if (Find(stem + suffix) == null)
                {
                    return stem + suffix;
                }
            }

            throw new InvalidOperationException("Too many presets share this name.");
        }

        public string Save(string name, string packagePath)
        {
            var sanitized = Sanitize(name);
            if (sanitized.Length == 0)
            {
                throw new ArgumentException("Preset name is invalid.", nameof(name));
            }

            Directory.CreateDirectory(_directory);
            var destination = Path.Combine(_directory, sanitized + _extension);
            File.Copy(packagePath, destination, overwrite: true);
            return destination;
        }

        public string NameOf(string path)
        {
            var fileName = Path.GetFileName(path) ?? string.Empty;
            return fileName.EndsWith(_extension, StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - _extension.Length)
                : Path.GetFileNameWithoutExtension(fileName);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return cleaned.Length > MaxNameLength ? cleaned.Substring(0, MaxNameLength).Trim() : cleaned;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>One saved preset: its display name and the package file that holds it.</summary>
    public sealed class PackagePresetInfo
    {
        public PackagePresetInfo(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
        }

        public string Name { get; }

        public string FilePath { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// A folder of portable packages used as named presets, one file per preset. The package
    /// format itself is the owning portable store's; this class only names, lists, copies and
    /// deletes files, so a preset is always also a valid package for the regular import and
    /// export flow. Color sets and unlock sound packs use it; notification styles keep their
    /// own per-surface store.
    /// </summary>
    public sealed class PackagePresetStore
    {
        public const int MaxPresetCount = 50;
        public const int MaxNameLength = 64;

        private readonly string _directory;
        private readonly string _extension;
        private readonly Action<string> _validate;
        private readonly Func<string, bool> _countsTowardCap;

        /// <param name="pluginUserDataPath">The plugin's user data folder.</param>
        /// <param name="folderName">The folder under it that holds this store's presets.</param>
        /// <param name="extension">The package extension, including the dot.</param>
        /// <param name="validate">Throws when a package is not valid for this store; runs before
        /// a file is copied in so a bad file never becomes a preset.</param>
        /// <param name="countsTowardCap">Whether a preset file counts toward
        /// <see cref="MaxPresetCount"/>; null counts every file. Workshop items do not count.</param>
        public PackagePresetStore(
            string pluginUserDataPath,
            string folderName,
            string extension,
            Action<string> validate,
            Func<string, bool> countsTowardCap = null)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new ArgumentException("Folder name is required.", nameof(folderName));
            }

            if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException("Extension must start with a dot.", nameof(extension));
            }

            _directory = Path.Combine(pluginUserDataPath, folderName);
            _extension = extension;
            _validate = validate ?? (_ => { });
            _countsTowardCap = countsTowardCap;
        }

        public string DirectoryPath => _directory;

        public IReadOnlyList<PackagePresetInfo> List()
        {
            if (!Directory.Exists(_directory))
            {
                return Array.Empty<PackagePresetInfo>();
            }

            return Directory.EnumerateFiles(_directory)
                .Where(path => path.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
                .Select(path => new PackagePresetInfo(Path.GetFileNameWithoutExtension(path), path))
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>How many presets count toward <see cref="MaxPresetCount"/>.</summary>
        public int Count() => _countsTowardCap == null
            ? List().Count
            : List().Count(preset => _countsTowardCap(preset.FilePath));

        public bool Exists(string name)
        {
            var sanitized = SanitizeName(name);
            return !string.IsNullOrEmpty(sanitized) && File.Exists(PathFor(sanitized));
        }

        public PackagePresetInfo Find(string name)
        {
            var sanitized = SanitizeName(name);
            return string.IsNullOrEmpty(sanitized) || !File.Exists(PathFor(sanitized))
                ? null
                : new PackagePresetInfo(sanitized, PathFor(sanitized));
        }

        /// <summary>
        /// Trims the name, strips characters that cannot appear in a file name, and caps the
        /// length at <see cref="MaxNameLength"/>. Returns an empty string when nothing valid
        /// remains; callers treat that as an invalid name.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim();
            if (cleaned.Length > MaxNameLength)
            {
                cleaned = cleaned.Substring(0, MaxNameLength).Trim();
            }

            return cleaned;
        }

        /// <summary>
        /// The sanitized name, or when a preset already has it, the name with " (2)", " (3)" and
        /// so on appended, so a file import never silently replaces a saved preset.
        /// </summary>
        public string UniqueName(string name)
        {
            var sanitized = SanitizeName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "Preset";
            }

            if (!File.Exists(PathFor(sanitized)))
            {
                return sanitized;
            }

            for (var n = 2; n < 1000; n++)
            {
                var suffix = " (" + n + ")";
                var stem = sanitized.Length + suffix.Length > MaxNameLength
                    ? sanitized.Substring(0, MaxNameLength - suffix.Length).TrimEnd()
                    : sanitized;
                var candidate = stem + suffix;
                if (!File.Exists(PathFor(candidate)))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Too many presets share this name.");
        }

        /// <summary>
        /// Saves <paramref name="packagePath"/> as the preset <paramref name="name"/>, replacing
        /// a preset of the same name, once the owning format has validated it.
        /// </summary>
        public PackagePresetInfo SaveFrom(string name, string packagePath)
        {
            var sanitized = RequireName(name);
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new FileNotFoundException("Package not found.", packagePath);
            }

            _validate(packagePath);
            var destination = PathFor(sanitized);
            EnsureRoom(destination);
            Directory.CreateDirectory(_directory);
            File.Copy(packagePath, destination, overwrite: true);
            return new PackagePresetInfo(sanitized, destination);
        }

        /// <summary>
        /// Saves a preset that <paramref name="write"/> produces at the destination path it is
        /// given; used when the current settings are exported straight into the store.
        /// </summary>
        public PackagePresetInfo Save(string name, Action<string> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            var sanitized = RequireName(name);
            var destination = PathFor(sanitized);
            EnsureRoom(destination);
            Directory.CreateDirectory(_directory);
            write(destination);
            return new PackagePresetInfo(sanitized, destination);
        }

        public void Delete(PackagePresetInfo preset)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            if (File.Exists(preset.FilePath))
            {
                File.Delete(preset.FilePath);
            }
        }

        private static string RequireName(string name)
        {
            var sanitized = SanitizeName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                throw new ArgumentException("Preset name is invalid.", nameof(name));
            }

            return sanitized;
        }

        private void EnsureRoom(string destination)
        {
            if (!File.Exists(destination) && Count() >= MaxPresetCount)
            {
                throw new InvalidOperationException($"You can save up to {MaxPresetCount} presets.");
            }
        }

        private string PathFor(string sanitizedName) => Path.Combine(_directory, sanitizedName + _extension);
    }
}

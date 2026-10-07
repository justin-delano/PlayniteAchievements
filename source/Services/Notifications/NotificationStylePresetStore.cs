using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayniteAchievements.Services.Library;

namespace PlayniteAchievements.Services.Notifications
{
    /// <summary>
    /// A named notification appearance preset on disk. The file is a standard surface package
    /// (<c>.panotif</c> or <c>.paframe</c>); the surface it captures is encoded by the folder it
    /// lives in and the display name is the file name minus the package extension.
    /// </summary>
    public sealed class NotificationStylePresetInfo
    {
        public NotificationStylePresetInfo(string name, string filePath, bool isFrame)
        {
            Name = name;
            FilePath = filePath;
            IsFrame = isFrame;
        }

        public string Name { get; }

        public string FilePath { get; }

        public bool IsFrame { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Stores named per-surface appearance presets as self-contained surface packages
    /// (<c>.panotif</c> under <c>library\notifications</c>, <c>.paframe</c> under
    /// <c>library\frames</c>) in the plugin's user data folder: the notification and frame part of
    /// the library. Each preset carries one surface's style plus its bundled images and optional
    /// custom template; packages are checked by <see cref="NotificationStylePortableStore"/>, so
    /// a preset file is also a valid style package for the regular import/export flow. Presets
    /// saved by earlier versions as <c>.pastyle</c> are renamed to the surface extension the
    /// first time they are listed.
    /// </summary>
    public sealed class NotificationStylePresetStore
    {
        public const int MaxNameLength = 64;

        private readonly NotificationStylePortableStore _portableStore;
        private readonly string _pluginUserDataPath;

        public NotificationStylePresetStore(
            NotificationStylePortableStore portableStore,
            string pluginUserDataPath)
        {
            _portableStore = portableStore ?? throw new ArgumentNullException(nameof(portableStore));
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            _pluginUserDataPath = pluginUserDataPath;
        }

        public IReadOnlyList<NotificationStylePresetInfo> ListPresets(bool isFrame)
        {
            var directory = GetSurfaceDirectory(isFrame);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<NotificationStylePresetInfo>();
            }

            MigrateLegacyExtensions(directory, isFrame);
            return Directory.EnumerateFiles(directory)
                .Where(NotificationStylePortableStore.IsPackagePath)
                .Select(path => new NotificationStylePresetInfo(GetPresetName(path), path, isFrame))
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
        /// Saves a .panotif or .paframe file as the preset <paramref name="name"/>: a look saved
        /// from the settings (exported to a package first), a Workshop install, or a file import.
        /// The package must carry the surface's style; a preset of the same name is replaced.
        /// </summary>
        public NotificationStylePresetInfo SavePresetFromPackage(bool isFrame, string name, string packagePath)
        {
            var sanitized = SanitizeName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                throw new ArgumentException("Preset name is invalid.", nameof(name));
            }

            var contents = _portableStore.InspectPackage(packagePath);
            if (isFrame ? !contents.HasFrameStyle : !contents.HasToastStyle)
            {
                throw new InvalidOperationException(isFrame
                    ? "This package does not contain a frame style."
                    : "This package does not contain a notification style.");
            }

            var destination = GetPresetPath(isFrame, sanitized);
            Directory.CreateDirectory(GetSurfaceDirectory(isFrame));
            File.Copy(packagePath, destination, overwrite: true);
            return new NotificationStylePresetInfo(sanitized, destination, isFrame);
        }

        /// <summary>
        /// The sanitized name, or when a preset of that surface already has it, the name with
        /// " (2)", " (3)" and so on appended, so a file import never replaces a saved preset.
        /// </summary>
        public string UniqueName(bool isFrame, string name)
        {
            var sanitized = SanitizeName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "Preset";
            }

            if (!File.Exists(GetPresetPath(isFrame, sanitized)))
            {
                return sanitized;
            }

            for (var n = 2; n < 1000; n++)
            {
                var suffix = " (" + n + ")";
                var stem = sanitized.Length + suffix.Length > MaxNameLength
                    ? sanitized.Substring(0, MaxNameLength - suffix.Length).TrimEnd()
                    : sanitized;
                if (!File.Exists(GetPresetPath(isFrame, stem + suffix)))
                {
                    return stem + suffix;
                }
            }

            throw new InvalidOperationException("Too many presets share this name.");
        }

        private string GetSurfaceDirectory(bool isFrame)
        {
            return LibraryStore.PresetDirectory(_pluginUserDataPath, isFrame ? LibraryItemKind.Frame : LibraryItemKind.Toast);
        }

        private string GetPresetPath(bool isFrame, string sanitizedName)
        {
            return Path.Combine(
                GetSurfaceDirectory(isFrame),
                sanitizedName + NotificationStylePortableStore.SurfaceExtension(isFrame));
        }

        /// <summary>
        /// Renames presets written with the retired <c>.pastyle</c> (or <c>.pastyle.zip</c>)
        /// extension to the surface's own extension. The folder already says which surface a
        /// preset is, so the rename changes nothing about how it is read; it only makes the
        /// surface extension the one spelling on disk. A name clash leaves the old file alone.
        /// </summary>
        private static void MigrateLegacyExtensions(string directory, bool isFrame)
        {
            var target = NotificationStylePortableStore.SurfaceExtension(isFrame);
            foreach (var path in Directory.EnumerateFiles(directory).ToList())
            {
                var fileName = Path.GetFileName(path);
                if (fileName == null ||
                    (!fileName.EndsWith(NotificationStylePortableStore.LegacyPackageFileExtension, StringComparison.OrdinalIgnoreCase) &&
                     !fileName.EndsWith(NotificationStylePortableStore.LegacyPackageFileExtension + ".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var renamed = Path.Combine(directory, NotificationStylePortableStore.StripRecognizedSuffix(fileName) + target);
                if (File.Exists(renamed))
                {
                    continue;
                }

                try
                {
                    File.Move(path, renamed);
                }
                catch (IOException)
                {
                    // Locked or otherwise unmovable: listed under its old name, still importable.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string GetPresetName(string filePath)
        {
            // Strips whichever suffix the file actually carries: presets saved before the
            // extensions went bare are named "<name>.pastyle.zip", and blindly removing
            // PackageFileExtension.Length characters left a partial stem that no longer round
            // tripped through GetPresetPath.
            return NotificationStylePortableStore.StripRecognizedSuffix(
                Path.GetFileName(filePath) ?? string.Empty);
        }
    }
}

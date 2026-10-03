using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    /// (<c>.panotif</c> under <c>notification_style_presets\toast</c>, <c>.paframe</c> under
    /// <c>...\frame</c>) in the plugin's user data folder. Each preset carries one surface's
    /// style plus its bundled images and optional custom template; packaging and image
    /// re-materialization are delegated to <see cref="NotificationStylePortableStore"/>, so a
    /// preset file is also a valid style package for the regular import/export flow. Presets
    /// saved by earlier versions as <c>.pastyle</c> are renamed to the surface extension the
    /// first time they are listed.
    /// </summary>
    public sealed class NotificationStylePresetStore
    {
        public const int MaxPresetCount = 50;
        public const int MaxNameLength = 64;

        private const string PresetsFolderName = "notification_style_presets";
        private const string ToastFolderName = "toast";
        private const string FrameFolderName = "frame";

        private readonly NotificationStylePortableStore _portableStore;
        private readonly string _presetsRoot;

        public NotificationStylePresetStore(
            NotificationStylePortableStore portableStore,
            string pluginUserDataPath)
        {
            _portableStore = portableStore ?? throw new ArgumentNullException(nameof(portableStore));
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            _presetsRoot = Path.Combine(pluginUserDataPath, PresetsFolderName);
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

        public bool PresetExists(bool isFrame, string name)
        {
            var sanitized = SanitizeName(name);
            return !string.IsNullOrEmpty(sanitized) && File.Exists(GetPresetPath(isFrame, sanitized));
        }

        public int CountPresets(bool isFrame)
        {
            return ListPresets(isFrame).Count;
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
        /// Saves the given surface of <paramref name="currentStyle"/> as a named preset,
        /// overwriting any preset with the same name. Only the saved surface travels, with its
        /// own badge images and header texts riding along inside the surface style; a toast
        /// preset additionally carries the toast-only background image. The other surface is
        /// left at factory defaults in the package and is ignored on apply.
        /// </summary>
        public void SavePreset(
            bool isFrame,
            string name,
            NotificationStyleSettings currentStyle,
            string templateXamlOrNull)
        {
            if (currentStyle == null)
            {
                throw new ArgumentNullException(nameof(currentStyle));
            }

            var sanitized = SanitizeName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                throw new ArgumentException("Preset name is invalid.", nameof(name));
            }

            _portableStore.ExportSurfacePackage(
                isFrame,
                currentStyle,
                GetPresetPath(isFrame, sanitized),
                templateXamlOrNull);
        }

        /// <summary>
        /// Saves an existing .panotif or .paframe file as the preset <paramref name="name"/>, for
        /// files that arrive from the Workshop or a file import. The package must carry the
        /// surface's style; a preset of the same name is replaced.
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
            if (!File.Exists(destination) && CountPresets(isFrame) >= MaxPresetCount)
            {
                throw new InvalidOperationException($"You can save up to {MaxPresetCount} presets.");
            }

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

        /// <summary>
        /// Loads a preset's style, re-materializing any bundled images into managed storage
        /// for <paramref name="targetOwner"/>. The caller merges only the preset's surface
        /// into the target style.
        /// </summary>
        public Task<NotificationStyleSettings> LoadPresetStyleAsync(
            NotificationStylePresetInfo preset,
            NotificationImageOwner targetOwner,
            CancellationToken cancel)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            return _portableStore.ImportAsync(preset.FilePath, targetOwner, cancel);
        }

        /// <summary>
        /// Reads the preset's embedded template XAML for its surface, or null when the preset
        /// was saved without a custom template.
        /// </summary>
        public string ReadPresetTemplateXaml(NotificationStylePresetInfo preset)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            return _portableStore.ReadTemplateXaml(preset.FilePath, preset.IsFrame);
        }

        public void DeletePreset(NotificationStylePresetInfo preset)
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

        private string GetSurfaceDirectory(bool isFrame)
        {
            return Path.Combine(_presetsRoot, isFrame ? FrameFolderName : ToastFolderName);
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

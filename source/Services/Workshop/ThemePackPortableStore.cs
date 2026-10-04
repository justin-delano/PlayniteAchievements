using Newtonsoft.Json;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>The four parts a theme bundle can carry; a bundle holds any subset.</summary>
    [Flags]
    public enum ThemePackParts
    {
        None = 0,
        Colors = 1,
        Sounds = 2,
        Toast = 4,
        Frame = 8,
        All = Colors | Sounds | Toast | Frame
    }

    /// <summary>
    /// A theme bundle's manifest. It only names the parts; each part is an embedded package of
    /// its own standalone format under <c>parts/</c> (<c>.pacolors</c>, <c>.pasounds</c>,
    /// <c>.panotif</c>, <c>.paframe</c>), so a bundle is read with the same code as the
    /// standalone files and never grows a second schema for them.
    /// </summary>
    public sealed class ThemePackFile
    {
        public const string ThemeKind = "PlayniteAchievements.Theme";

        public string Kind { get; set; }

        public int Version { get; set; }

        /// <summary>Part names (<see cref="ThemePackParts"/> members) the bundle carries.</summary>
        public List<string> Parts { get; set; } = new List<string>();
    }

    /// <summary>
    /// Exports and imports a <c>.patheme</c> bundle: the global look as any subset of colors,
    /// unlock sounds, notification style and screenshot-frame style. Parts are embedded, not
    /// referenced, so a bundle installs offline and cannot break when another item changes. The
    /// importer applies only the parts the caller selects and leaves the rest of the settings as
    /// they are.
    /// </summary>
    public sealed class ThemePackPortableStore
    {
        public const string PackageFileExtension = ".patheme";
        public const string ManifestEntryName = "theme.json";
        public const string PartsFolderName = "parts";
        public const string ColorsEntryName = PartsFolderName + "/colors" + ColorPackPortableStore.PackageFileExtension;
        public const string SoundsEntryName = PartsFolderName + "/sounds" + UnlockSoundPortableStore.PackageFileExtension;
        public const string ToastEntryName = PartsFolderName + "/toast" + NotificationStylePortableStore.ToastPackageFileExtension;
        public const string FrameEntryName = PartsFolderName + "/frame" + NotificationStylePortableStore.FramePackageFileExtension;
        public const int CurrentVersion = 1;

        private const string NotPackageMessage =
            "This file is not a theme bundle. Import the original .patheme file, not a file extracted from it.";

        private static readonly string[] RecognizedFileSuffixes =
        {
            PackageFileExtension + ".zip",
            PackageFileExtension,
            ".zip"
        };

        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly NotificationStylePortableStore _styleStore;
        private readonly UnlockSoundPortableStore _soundStore;
        private readonly ColorPackPortableStore _colorStore;
        private readonly ILogger _logger;

        public ThemePackPortableStore(
            NotificationStylePortableStore styleStore,
            UnlockSoundPortableStore soundStore,
            ColorPackPortableStore colorStore,
            ILogger logger = null)
        {
            _styleStore = styleStore ?? throw new ArgumentNullException(nameof(styleStore));
            _soundStore = soundStore ?? throw new ArgumentNullException(nameof(soundStore));
            _colorStore = colorStore ?? throw new ArgumentNullException(nameof(colorStore));
            _logger = logger;
        }

        public static bool IsPackagePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   (path.EndsWith(PackageFileExtension, StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(PackageFileExtension + ".zip", StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeExportPath(string path)
        {
            return PortablePackage.NormalizeExportPath(path, PackageFileExtension, RecognizedFileSuffixes);
        }

        public static string BuildFileDialogFilter()
        {
            return $"Playnite Achievements Theme (*{PackageFileExtension})|*{PackageFileExtension};*{PackageFileExtension}.zip";
        }

        /// <summary>
        /// Writes the selected parts of the global look, each as its standalone package embedded
        /// under <c>parts/</c>. Sounds come from <paramref name="resolvedSounds"/> (what each tier
        /// actually plays); the toast and frame come from <paramref name="persisted"/>'s global
        /// style with the optional installed custom templates. A selected part with nothing to
        /// carry (no custom sounds) is dropped from the manifest rather than failing the export.
        /// </summary>
        public void Export(
            string destinationPath,
            ThemePackParts parts,
            PersistedSettings persisted,
            IEnumerable<ResolvedUnlockSound> resolvedSounds = null,
            string toastTemplateXaml = null,
            string frameTemplateXaml = null)
        {
            if (persisted == null)
            {
                throw new ArgumentNullException(nameof(persisted));
            }

            if (!IsPackagePath(destinationPath))
            {
                throw new InvalidOperationException($"Destination path must end with {PackageFileExtension}.");
            }

            if (parts == ThemePackParts.None)
            {
                throw new InvalidOperationException("Select at least one part to export.");
            }

            var scratch = PortablePackage.CreateScratchDirectory("ThemeExport");
            try
            {
                var files = new Dictionary<ThemePackParts, string>();

                if (parts.HasFlag(ThemePackParts.Colors))
                {
                    var colorsPath = Path.Combine(scratch, "colors" + ColorPackPortableStore.PackageFileExtension);
                    _colorStore.Export(persisted, colorsPath);
                    files[ThemePackParts.Colors] = colorsPath;
                }

                if (parts.HasFlag(ThemePackParts.Sounds) && resolvedSounds != null)
                {
                    var soundsPath = Path.Combine(scratch, "sounds" + UnlockSoundPortableStore.PackageFileExtension);
                    try
                    {
                        _soundStore.Export(resolvedSounds, soundsPath);
                        files[ThemePackParts.Sounds] = soundsPath;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Nothing custom to carry: the bundle simply has no sounds part.
                        _logger?.Debug(ex, "Theme export skipped the sounds part.");
                    }
                }

                var style = persisted.NotificationStyle ?? NotificationStyleSettings.CreateDefault();
                if (parts.HasFlag(ThemePackParts.Toast))
                {
                    var toastPath = Path.Combine(scratch, "toast" + NotificationStylePortableStore.ToastPackageFileExtension);
                    _styleStore.ExportSurfacePackage(isFrame: false, style, toastPath, toastTemplateXaml);
                    files[ThemePackParts.Toast] = toastPath;
                }

                if (parts.HasFlag(ThemePackParts.Frame))
                {
                    var framePath = Path.Combine(scratch, "frame" + NotificationStylePortableStore.FramePackageFileExtension);
                    _styleStore.ExportSurfacePackage(isFrame: true, style, framePath, frameTemplateXaml);
                    files[ThemePackParts.Frame] = framePath;
                }

                if (files.Count == 0)
                {
                    throw new InvalidOperationException("None of the selected parts has anything to export.");
                }

                ExportParts(destinationPath, files);
            }
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
            }
        }

        /// <summary>
        /// Writes a bundle from ready-made standalone packages, one per part: colors as a
        /// .pacolors, sounds as a .pasounds, toast and frame as .panotif and .paframe. Each file
        /// is checked to be a valid package of its kind before it is embedded, so a preset file
        /// can be handed in directly.
        /// </summary>
        public void ExportParts(string destinationPath, IReadOnlyDictionary<ThemePackParts, string> partFiles)
        {
            if (!IsPackagePath(destinationPath))
            {
                throw new InvalidOperationException($"Destination path must end with {PackageFileExtension}.");
            }

            if (partFiles == null || partFiles.Count == 0)
            {
                throw new InvalidOperationException("Select at least one part to export.");
            }

            var manifest = new ThemePackFile
            {
                Kind = ThemePackFile.ThemeKind,
                Version = CurrentVersion
            };
            var embedded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in new[] { ThemePackParts.Colors, ThemePackParts.Sounds, ThemePackParts.Toast, ThemePackParts.Frame })
            {
                if (!partFiles.TryGetValue(part, out var file) || string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                if (!File.Exists(file))
                {
                    throw new FileNotFoundException($"The {part} part file is missing.", file);
                }

                ValidatePart(part, file);
                embedded[EntryNameFor(part)] = file;
                manifest.Parts.Add(part.ToString());
            }

            if (manifest.Parts.Count == 0)
            {
                throw new InvalidOperationException("None of the selected parts has anything to export.");
            }

            PortablePackage.Write(destinationPath, archive =>
            {
                PortablePackage.AddJson(archive, ManifestEntryName, manifest, WriteSettings);
                PortablePackage.AddFiles(archive, embedded);
            });
        }

        private void ValidatePart(ThemePackParts part, string file)
        {
            switch (part)
            {
                case ThemePackParts.Colors:
                    _colorStore.Read(file);
                    break;
                case ThemePackParts.Sounds:
                    _soundStore.Inspect(file);
                    break;
                case ThemePackParts.Toast:
                case ThemePackParts.Frame:
                {
                    var contents = _styleStore.InspectPackage(file);
                    var ok = part == ThemePackParts.Frame ? contents.HasFrameStyle : contents.HasToastStyle;
                    if (!ok)
                    {
                        throw new InvalidOperationException($"The {part} part file does not carry that style.");
                    }

                    break;
                }
            }
        }

        /// <summary>Reads the manifest and reports which parts the bundle carries.</summary>
        public ThemePackParts Inspect(string sourcePath)
        {
            using (var archive = PortablePackage.OpenRead(sourcePath, NotPackageMessage))
            {
                var entries = PortablePackage.IndexEntries(archive);
                var manifest = ReadManifestOrThrow(entries);
                return ResolveParts(manifest, entries);
            }
        }

        /// <summary>
        /// Applies the selected parts to <paramref name="persisted"/>: colors are validated and
        /// set, sounds are copied into managed storage, and each notification surface is imported
        /// into global image storage and merged onto the global style. Embedded templates are
        /// handed to <paramref name="installTemplate"/> (surface, xaml) when supplied. The caller
        /// persists the settings and refreshes application resources afterwards. Returns the
        /// parts that were applied.
        /// </summary>
        /// <summary>
        /// Writes the selected parts that the bundle carries into <paramref name="directory"/>
        /// as their standalone packages (colors.pacolors, sounds.pasounds, toast.panotif,
        /// frame.paframe) and returns the path of each. Callers that save presets rather than
        /// apply the theme use this; <see cref="ImportAsync"/> builds on it.
        /// </summary>
        public IReadOnlyDictionary<ThemePackParts, string> ExtractParts(string sourcePath, ThemePackParts selected, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory is required.", nameof(directory));
            }

            var extracted = new Dictionary<ThemePackParts, string>();
            using (var archive = PortablePackage.OpenRead(sourcePath, NotPackageMessage))
            {
                var entries = PortablePackage.IndexEntries(archive);
                var manifest = ReadManifestOrThrow(entries);
                var available = ResolveParts(manifest, entries);

                foreach (var part in new[] { ThemePackParts.Colors, ThemePackParts.Sounds, ThemePackParts.Toast, ThemePackParts.Frame })
                {
                    if (selected.HasFlag(part) && available.HasFlag(part))
                    {
                        var entryName = EntryNameFor(part);
                        var target = Path.Combine(directory, entryName.Substring(PartsFolderName.Length + 1));
                        PortablePackage.ExtractToFile(entries[entryName], target);
                        extracted[part] = target;
                    }
                }
            }

            return extracted;
        }

        public async Task<ThemePackParts> ImportAsync(
            string sourcePath,
            ThemePackParts selected,
            PersistedSettings persisted,
            Action<bool, string> installTemplate,
            CancellationToken cancel)
        {
            if (persisted == null)
            {
                throw new ArgumentNullException(nameof(persisted));
            }

            var scratch = PortablePackage.CreateScratchDirectory("ThemeImport");
            try
            {
                var extracted = ExtractParts(sourcePath, selected, scratch);

                var applied = ThemePackParts.None;

                if (extracted.TryGetValue(ThemePackParts.Colors, out var colorsPath))
                {
                    _colorStore.Import(colorsPath, persisted);
                    applied |= ThemePackParts.Colors;
                }

                if (extracted.TryGetValue(ThemePackParts.Sounds, out var soundsPath))
                {
                    var sounds = persisted.UnlockSounds ?? UnlockSoundSettings.CreateDefault();
                    _soundStore.Import(soundsPath, sounds);
                    persisted.UnlockSounds = sounds;
                    _soundStore.PruneUnreferenced(sounds);
                    applied |= ThemePackParts.Sounds;
                }

                foreach (var part in new[] { ThemePackParts.Toast, ThemePackParts.Frame })
                {
                    if (!extracted.TryGetValue(part, out var packagePath))
                    {
                        continue;
                    }

                    var isFrame = part == ThemePackParts.Frame;
                    var imported = await _styleStore
                        .ImportAsync(packagePath, NotificationImageOwner.Global, cancel)
                        .ConfigureAwait(false);
                    var merged = (persisted.NotificationStyle ?? NotificationStyleSettings.CreateDefault()).Clone();
                    NotificationStylePortableStore.ApplyPackSurfaces(merged, imported, isFrame);
                    persisted.NotificationStyle = merged;

                    var xaml = _styleStore.ReadTemplateXaml(packagePath, isFrame);
                    if (!string.IsNullOrWhiteSpace(xaml))
                    {
                        installTemplate?.Invoke(isFrame, xaml);
                    }

                    applied |= part;
                }

                return applied;
            }
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
            }
        }

        private static ThemePackFile ReadManifestOrThrow(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
            {
                throw new InvalidOperationException("The package does not contain a theme manifest.");
            }

            ThemePackFile manifest;
            try
            {
                manifest = PortablePackage.ReadJson<ThemePackFile>(manifestEntry);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("This theme file is damaged and could not be read.", ex);
            }

            if (manifest == null || !string.Equals(manifest.Kind, ThemePackFile.ThemeKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("This file is not a Playnite Achievements theme.");
            }

            if (manifest.Version > CurrentVersion)
            {
                throw new InvalidOperationException(
                    "This theme was exported by a newer version of Playnite Achievements. Update the extension to import it.");
            }

            return manifest;
        }

        /// <summary>
        /// The parts the manifest declares that the archive actually backs. A declared part with
        /// no payload is an error, so a tampered bundle fails rather than silently shrinking.
        /// </summary>
        private static ThemePackParts ResolveParts(ThemePackFile manifest, IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            var parts = ThemePackParts.None;
            foreach (var name in manifest.Parts ?? new List<string>())
            {
                if (!Enum.TryParse(name, ignoreCase: true, out ThemePackParts part) ||
                    part == ThemePackParts.None || part == ThemePackParts.All)
                {
                    throw new InvalidOperationException($"The theme names an unknown part '{name}'.");
                }

                if (!entries.ContainsKey(EntryNameFor(part)))
                {
                    throw new InvalidOperationException($"The theme is missing its '{EntryNameFor(part)}' part.");
                }

                parts |= part;
            }

            return parts;
        }

        private static string EntryNameFor(ThemePackParts part)
        {
            switch (part)
            {
                case ThemePackParts.Colors: return ColorsEntryName;
                case ThemePackParts.Sounds: return SoundsEntryName;
                case ThemePackParts.Toast: return ToastEntryName;
                case ThemePackParts.Frame: return FrameEntryName;
                default: throw new ArgumentOutOfRangeException(nameof(part), part, "No archive entry backs this part.");
            }
        }
    }
}

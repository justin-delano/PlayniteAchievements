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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>The colors part: rarity, provider and resource overrides, as the settings hold them.</summary>
    public sealed class ThemePackColors
    {
        public RarityColorSettings RarityColors { get; set; }

        public Dictionary<string, string> ProviderColorOverrides { get; set; }

        public Dictionary<string, ResourceOverrideSetting> ResourceOverrides { get; set; }
    }

    /// <summary>
    /// A theme bundle's manifest. The sounds, toast and frame parts are not described here; each
    /// is an embedded package of its own format under <c>parts/</c>, so a bundle is read with the
    /// same code as the standalone files and never grows a second schema for them.
    /// </summary>
    public sealed class ThemePackFile
    {
        public const string ThemeKind = "PlayniteAchievements.Theme";

        public string Kind { get; set; }

        public int Version { get; set; }

        /// <summary>Part names (<see cref="ThemePackParts"/> members) the bundle carries.</summary>
        public List<string> Parts { get; set; } = new List<string>();

        public ThemePackColors Colors { get; set; }
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

        private static readonly Regex HexColorPattern = new Regex(
            @"^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly NotificationStylePortableStore _styleStore;
        private readonly UnlockSoundPortableStore _soundStore;
        private readonly ILogger _logger;

        public ThemePackPortableStore(
            NotificationStylePortableStore styleStore,
            UnlockSoundPortableStore soundStore,
            ILogger logger = null)
        {
            _styleStore = styleStore ?? throw new ArgumentNullException(nameof(styleStore));
            _soundStore = soundStore ?? throw new ArgumentNullException(nameof(soundStore));
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
        /// Writes the selected parts of the global look. Sounds come from
        /// <paramref name="resolvedSounds"/> (what each tier actually plays); the toast and frame
        /// come from <paramref name="persisted"/>'s global style with the optional installed custom
        /// templates. A selected part with nothing to carry (no custom sounds) is dropped from the
        /// manifest rather than failing the export.
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

            var manifest = new ThemePackFile
            {
                Kind = ThemePackFile.ThemeKind,
                Version = CurrentVersion
            };

            var scratch = PortablePackage.CreateScratchDirectory("ThemeExport");
            try
            {
                var embedded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (parts.HasFlag(ThemePackParts.Colors))
                {
                    manifest.Colors = new ThemePackColors
                    {
                        RarityColors = persisted.RarityColors?.Clone() ?? RarityColorSettings.CreateDefault(),
                        ProviderColorOverrides = new Dictionary<string, string>(
                            persisted.ProviderColorOverrides ?? new Dictionary<string, string>(),
                            StringComparer.OrdinalIgnoreCase),
                        ResourceOverrides = (persisted.ResourceOverrides ?? new Dictionary<string, ResourceOverrideSetting>())
                            .Where(pair => pair.Value != null)
                            .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase)
                    };
                    manifest.Parts.Add(ThemePackParts.Colors.ToString());
                }

                if (parts.HasFlag(ThemePackParts.Sounds) && resolvedSounds != null)
                {
                    var soundsPath = Path.Combine(scratch, "sounds" + UnlockSoundPortableStore.PackageFileExtension);
                    try
                    {
                        _soundStore.Export(resolvedSounds, soundsPath);
                        embedded[SoundsEntryName] = soundsPath;
                        manifest.Parts.Add(ThemePackParts.Sounds.ToString());
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
                    embedded[ToastEntryName] = toastPath;
                    manifest.Parts.Add(ThemePackParts.Toast.ToString());
                }

                if (parts.HasFlag(ThemePackParts.Frame))
                {
                    var framePath = Path.Combine(scratch, "frame" + NotificationStylePortableStore.FramePackageFileExtension);
                    _styleStore.ExportSurfacePackage(isFrame: true, style, framePath, frameTemplateXaml);
                    embedded[FrameEntryName] = framePath;
                    manifest.Parts.Add(ThemePackParts.Frame.ToString());
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
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
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
                ThemePackFile manifest;
                ThemePackParts available;
                var extracted = new Dictionary<ThemePackParts, string>();
                using (var archive = PortablePackage.OpenRead(sourcePath, NotPackageMessage))
                {
                    var entries = PortablePackage.IndexEntries(archive);
                    manifest = ReadManifestOrThrow(entries);
                    available = ResolveParts(manifest, entries);

                    foreach (var part in new[] { ThemePackParts.Sounds, ThemePackParts.Toast, ThemePackParts.Frame })
                    {
                        if (selected.HasFlag(part) && available.HasFlag(part))
                        {
                            var entryName = EntryNameFor(part);
                            var target = Path.Combine(scratch, entryName.Substring(PartsFolderName.Length + 1));
                            PortablePackage.ExtractToFile(entries[entryName], target);
                            extracted[part] = target;
                        }
                    }
                }

                var applied = ThemePackParts.None;

                if (selected.HasFlag(ThemePackParts.Colors) && available.HasFlag(ThemePackParts.Colors))
                {
                    ApplyColors(manifest.Colors, persisted);
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

        /// <summary>
        /// Validates and applies the colors part. Rarity and provider colors must be hex; resource
        /// overrides are kept only for keys the resolver knows, with values that parse for the
        /// key's kind. A bad value anywhere rejects the part, since a half-applied palette is
        /// harder to undo than a refused one.
        /// </summary>
        public static void ApplyColors(ThemePackColors colors, PersistedSettings persisted)
        {
            if (colors == null)
            {
                throw new InvalidOperationException("The theme bundle has no colors part.");
            }

            var rarity = colors.RarityColors?.Clone() ?? RarityColorSettings.CreateDefault();
            foreach (var value in new[]
            {
                rarity.Common, rarity.Uncommon, rarity.Rare, rarity.UltraRare,
                rarity.CompletedStart, rarity.CompletedEnd,
                rarity.TrophyBronze, rarity.TrophySilver, rarity.TrophyGold, rarity.TrophyPlatinum
            })
            {
                EnsureHexColorOrThrow(value, "rarity color");
            }

            var providerColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in colors.ProviderColorOverrides ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                EnsureHexColorOrThrow(pair.Value, $"provider color for '{pair.Key}'");
                providerColors[pair.Key.Trim()] = pair.Value.Trim();
            }

            var kinds = PlayAchResourceService.ResourceDescriptors
                .ToDictionary(descriptor => descriptor.ResourceKey, descriptor => descriptor.ValueKind, StringComparer.OrdinalIgnoreCase);
            var overrides = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in colors.ResourceOverrides ?? new Dictionary<string, ResourceOverrideSetting>())
            {
                if (pair.Value == null || !kinds.TryGetValue(pair.Key ?? string.Empty, out var kind))
                {
                    continue;
                }

                var setting = pair.Value.Clone();
                if (setting.Mode == ResourceOverrideMode.Custom)
                {
                    EnsureResourceValueOrThrow(kind, setting.CustomValue, pair.Key);
                }

                overrides[pair.Key] = setting;
            }

            persisted.RarityColors = rarity;
            persisted.ProviderColorOverrides = providerColors;
            persisted.ResourceOverrides = overrides;
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

                if (part == ThemePackParts.Colors)
                {
                    if (manifest.Colors == null)
                    {
                        throw new InvalidOperationException("The theme declares a colors part but carries none.");
                    }
                }
                else if (!entries.ContainsKey(EntryNameFor(part)))
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
                case ThemePackParts.Sounds: return SoundsEntryName;
                case ThemePackParts.Toast: return ToastEntryName;
                case ThemePackParts.Frame: return FrameEntryName;
                default: throw new ArgumentOutOfRangeException(nameof(part), part, "No archive entry backs this part.");
            }
        }

        private static void EnsureHexColorOrThrow(string value, string whatItIs)
        {
            if (string.IsNullOrWhiteSpace(value) || !HexColorPattern.IsMatch(value.Trim()))
            {
                throw new InvalidOperationException($"The theme's {whatItIs} '{value}' is not a #RRGGBB or #AARRGGBB color.");
            }
        }

        private static void EnsureResourceValueOrThrow(ResourceOverrideValueKind kind, string value, string key)
        {
            switch (kind)
            {
                case ResourceOverrideValueKind.Brush:
                    EnsureHexColorOrThrow(value, $"resource color for '{key}'");
                    return;
                case ResourceOverrideValueKind.FontSize:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ||
                        size <= 0 || size > 200 || double.IsNaN(size) || double.IsInfinity(size))
                    {
                        throw new InvalidOperationException($"The theme's font size for '{key}' is not valid.");
                    }

                    return;
                case ResourceOverrideValueKind.FontFamily:
                    if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
                    {
                        throw new InvalidOperationException($"The theme's font family for '{key}' is not valid.");
                    }

                    return;
                default:
                    throw new InvalidOperationException($"The theme's resource override '{key}' has an unknown kind.");
            }
        }
    }
}

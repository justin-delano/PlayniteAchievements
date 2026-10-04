using Newtonsoft.Json;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// A portable color set: rarity, provider and resource overrides, as the settings hold them.
    /// </summary>
    public sealed class ColorPackFile
    {
        public const string ColorsKind = "PlayniteAchievements.Colors";

        public string Kind { get; set; }

        public int Version { get; set; }

        public RarityColorSettings RarityColors { get; set; }

        public Dictionary<string, string> ProviderColorOverrides { get; set; }

        public Dictionary<string, ResourceOverrideSetting> ResourceOverrides { get; set; }
    }

    /// <summary>
    /// Exports and imports the plugin's colors as a <c>.pacolors</c> zip package: the rarity,
    /// completed and trophy colors, the per-provider colors, and the resource overrides. Values
    /// are validated strictly on import (hex colors, known resource keys, parsable font sizes),
    /// since a bad value anywhere would leave a half-applied palette.
    /// </summary>
    public sealed class ColorPackPortableStore
    {
        public const string PackageFileExtension = ".pacolors";
        public const string ManifestEntryName = "colors.json";
        public const int CurrentVersion = 1;

        private const string NotPackageMessage =
            "This file is not a color pack. Import the original .pacolors file, not a file extracted from it.";

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
            return $"Playnite Achievements Colors (*{PackageFileExtension})|*{PackageFileExtension};*{PackageFileExtension}.zip";
        }

        /// <summary>The settings' current colors as a package manifest.</summary>
        public static ColorPackFile Capture(PersistedSettings persisted)
        {
            if (persisted == null)
            {
                throw new ArgumentNullException(nameof(persisted));
            }

            return new ColorPackFile
            {
                Kind = ColorPackFile.ColorsKind,
                Version = CurrentVersion,
                RarityColors = persisted.RarityColors?.Clone() ?? RarityColorSettings.CreateDefault(),
                ProviderColorOverrides = new Dictionary<string, string>(
                    persisted.ProviderColorOverrides ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
                ResourceOverrides = (persisted.ResourceOverrides ?? new Dictionary<string, ResourceOverrideSetting>())
                    .Where(pair => pair.Value != null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            };
        }

        public void Export(PersistedSettings persisted, string destinationPath)
        {
            if (!IsPackagePath(destinationPath))
            {
                throw new InvalidOperationException($"Destination path must end with {PackageFileExtension}.");
            }

            var manifest = Capture(persisted);
            PortablePackage.Write(destinationPath, archive =>
                PortablePackage.AddJson(archive, ManifestEntryName, manifest, WriteSettings));
        }

        /// <summary>Reads and validates the package without applying it.</summary>
        public ColorPackFile Read(string sourcePath)
        {
            using (var archive = PortablePackage.OpenRead(sourcePath, NotPackageMessage))
            {
                var entries = PortablePackage.IndexEntries(archive);
                var manifest = ReadManifestOrThrow(entries);
                Validate(manifest);
                return manifest;
            }
        }

        /// <summary>Validates the package and writes its colors into <paramref name="persisted"/>.</summary>
        public void Import(string sourcePath, PersistedSettings persisted)
        {
            if (persisted == null)
            {
                throw new ArgumentNullException(nameof(persisted));
            }

            Apply(Read(sourcePath), persisted);
        }

        /// <summary>
        /// Validates and applies a manifest. Rarity and provider colors must be hex; resource
        /// overrides are kept only for keys the resolver knows, with values that parse for the
        /// key's kind.
        /// </summary>
        public static void Apply(ColorPackFile colors, PersistedSettings persisted)
        {
            if (colors == null)
            {
                throw new InvalidOperationException("The color pack is empty.");
            }

            if (persisted == null)
            {
                throw new ArgumentNullException(nameof(persisted));
            }

            var validated = Validate(colors);
            persisted.RarityColors = validated.RarityColors;
            persisted.ProviderColorOverrides = validated.ProviderColorOverrides;
            persisted.ResourceOverrides = validated.ResourceOverrides;
        }

        /// <summary>Returns a normalized copy, or throws on the first invalid value.</summary>
        public static ColorPackFile Validate(ColorPackFile colors)
        {
            if (colors == null)
            {
                throw new InvalidOperationException("The color pack is empty.");
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

            return new ColorPackFile
            {
                Kind = ColorPackFile.ColorsKind,
                Version = colors.Version == 0 ? CurrentVersion : colors.Version,
                RarityColors = rarity,
                ProviderColorOverrides = providerColors,
                ResourceOverrides = overrides
            };
        }

        private static ColorPackFile ReadManifestOrThrow(IReadOnlyDictionary<string, System.IO.Compression.ZipArchiveEntry> entries)
        {
            if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
            {
                throw new InvalidOperationException("The package does not contain a colors manifest.");
            }

            ColorPackFile manifest;
            try
            {
                manifest = PortablePackage.ReadJson<ColorPackFile>(manifestEntry);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("This color pack file is damaged and could not be read.", ex);
            }

            if (manifest == null || !string.Equals(manifest.Kind, ColorPackFile.ColorsKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("This file is not a Playnite Achievements color pack.");
            }

            if (manifest.Version > CurrentVersion)
            {
                throw new InvalidOperationException(
                    "This color pack was exported by a newer version of Playnite Achievements. Update the extension to import it.");
            }

            return manifest;
        }

        private static void EnsureHexColorOrThrow(string value, string whatItIs)
        {
            if (string.IsNullOrWhiteSpace(value) || !HexColorPattern.IsMatch(value.Trim()))
            {
                throw new InvalidOperationException($"The {whatItIs} '{value}' is not a #RRGGBB or #AARRGGBB color.");
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
                        throw new InvalidOperationException($"The font size for '{key}' is not valid.");
                    }

                    return;
                case ResourceOverrideValueKind.FontFamily:
                    if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
                    {
                        throw new InvalidOperationException($"The font family for '{key}' is not valid.");
                    }

                    return;
                default:
                    throw new InvalidOperationException($"The resource override '{key}' has an unknown kind.");
            }
        }
    }
}

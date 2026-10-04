using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>Where one theme part comes from: the live settings, or a saved preset file.</summary>
    public sealed class BundlePartSource
    {
        public BundlePartSource(BundleParts part, string label, string presetPath)
        {
            Part = part;
            Label = label;
            PresetPath = presetPath;
        }

        public BundleParts Part { get; }

        public string Label { get; }

        /// <summary>The preset package to embed, or null for the current settings.</summary>
        public string PresetPath { get; }

        public bool IsCurrent => PresetPath == null;

        public override string ToString() => Label;
    }

    /// <summary>One part of a theme being composed: included or not, and from which source.</summary>
    public sealed class BundlePartChoice
    {
        public BundlePartChoice(BundleParts part, bool included, BundlePartSource source)
        {
            Part = part;
            Included = included;
            Source = source;
        }

        public BundleParts Part { get; }

        public bool Included { get; }

        public BundlePartSource Source { get; }
    }

    /// <summary>
    /// Lists what each theme part can be built from and turns a set of choices into the
    /// standalone package files a bundle embeds: a preset is used as it is (a preset file is
    /// already a valid package), the current settings are exported fresh. Shared by theme export
    /// to file and Share to Workshop, so both show the same composer.
    /// </summary>
    public sealed class BundleComposer
    {
        private static readonly BundleParts[] AllParts =
        {
            BundleParts.Colors, BundleParts.Sounds, BundleParts.Toast, BundleParts.Frame
        };

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        public BundleComposer(PlayniteAchievementsPlugin plugin, ILogger logger = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
        }

        public static IReadOnlyList<BundleParts> Parts => AllParts;

        /// <summary>The user-facing label of a part, the same words the part picker uses.</summary>
        public static string LabelFor(BundleParts part)
        {
            switch (part)
            {
                case BundleParts.Colors: return ResourceProvider.GetString("LOCPlayAch_Settings_Display_Colors");
                case BundleParts.Sounds: return ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Sounds");
                case BundleParts.Toast: return ResourceProvider.GetString("LOCPlayAch_Settings_Style_ToastTab");
                case BundleParts.Frame: return ResourceProvider.GetString("LOCPlayAch_Settings_FrameHeader");
                default: return part.ToString();
            }
        }

        /// <summary>
        /// The sources offered for a part: the current settings first (when they have anything
        /// to carry), then every saved preset of that kind by name.
        /// </summary>
        public IReadOnlyList<BundlePartSource> SourcesFor(BundleParts part)
        {
            var sources = new List<BundlePartSource>();
            if (HasCurrent(part))
            {
                sources.Add(new BundlePartSource(part, ResourceProvider.GetString("LOCPlayAch_Workshop_CurrentSettings"), null));
            }

            try
            {
                switch (part)
                {
                    case BundleParts.Colors:
                        sources.AddRange(_plugin.ColorPresetStore.List().Select(p => new BundlePartSource(part, p.Name, p.FilePath)));
                        break;
                    case BundleParts.Sounds:
                        sources.AddRange(_plugin.UnlockSoundPresetStore.List().Select(p => new BundlePartSource(part, p.Name, p.FilePath)));
                        break;
                    case BundleParts.Toast:
                    case BundleParts.Frame:
                        sources.AddRange(_plugin.NotificationStylePresetStore
                            .ListPresets(isFrame: part == BundleParts.Frame)
                            .Select(p => new BundlePartSource(part, p.Name, p.FilePath)));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed listing presets for theme part {part}.");
            }

            return sources;
        }

        /// <summary>
        /// Whether the live settings have something to export for a part. Colors and the two
        /// styles always do; sounds only when some tier plays a custom or theme file.
        /// </summary>
        public bool HasCurrent(BundleParts part)
        {
            if (part != BundleParts.Sounds)
            {
                return true;
            }

            var resolved = _plugin.UnlockSounds?.Resolver?.ResolveAll();
            return resolved != null && resolved.Any(s => s.Source == UnlockSoundSource.Custom || s.Source == UnlockSoundSource.Theme);
        }

        /// <summary>
        /// Materializes the included choices as standalone packages: preset files are returned as
        /// they are, current settings are exported into <paramref name="directory"/>. The caller
        /// owns the directory and deletes it once the bundle has been written or shared.
        /// </summary>
        public IReadOnlyDictionary<BundleParts, string> BuildPartFiles(IEnumerable<BundlePartChoice> choices, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory is required.", nameof(directory));
            }

            var persisted = _plugin.Settings?.Persisted ?? throw new InvalidOperationException("Settings are not available.");
            Directory.CreateDirectory(directory);
            var files = new Dictionary<BundleParts, string>();

            foreach (var choice in choices ?? Enumerable.Empty<BundlePartChoice>())
            {
                if (choice == null || !choice.Included || choice.Source == null)
                {
                    continue;
                }

                if (!choice.Source.IsCurrent)
                {
                    if (!File.Exists(choice.Source.PresetPath))
                    {
                        throw new FileNotFoundException("The preset file is missing.", choice.Source.PresetPath);
                    }

                    files[choice.Part] = choice.Source.PresetPath;
                    continue;
                }

                files[choice.Part] = ExportCurrent(choice.Part, persisted, directory);
            }

            return files;
        }

        private string ExportCurrent(BundleParts part, PersistedSettings persisted, string directory)
        {
            switch (part)
            {
                case BundleParts.Colors:
                {
                    var path = Path.Combine(directory, "colors" + ColorPackPortableStore.PackageFileExtension);
                    _plugin.ColorPackPortableStore.Export(persisted, path);
                    return path;
                }

                case BundleParts.Sounds:
                {
                    var path = Path.Combine(directory, "sounds" + UnlockSoundPortableStore.PackageFileExtension);
                    _plugin.UnlockSoundPortableStore.Export(_plugin.UnlockSounds?.Resolver?.ResolveAll(), path);
                    return path;
                }

                case BundleParts.Toast:
                case BundleParts.Frame:
                {
                    var isFrame = part == BundleParts.Frame;
                    var path = Path.Combine(
                        directory,
                        (isFrame ? "frame" : "toast") + NotificationStylePortableStore.SurfaceExtension(isFrame));
                    var resolver = new AchievementToastTemplateResolver(
                        _plugin.PlayniteApi,
                        _logger,
                        customTemplatesDirectory: AchievementToastTemplateResolver.GetCustomTemplatesDirectory(_plugin.GetPluginUserDataPath()));
                    _plugin.NotificationStylePortableStore.ExportSurfacePackage(
                        isFrame,
                        persisted.NotificationStyle ?? NotificationStyleSettings.CreateDefault(),
                        path,
                        resolver.ReadCustomTemplateXaml(isFrame, providerKey: null, gameId: Guid.Empty));
                    return path;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(part));
            }
        }
    }
}

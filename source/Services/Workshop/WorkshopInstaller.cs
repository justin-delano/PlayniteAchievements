using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Refresh;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Services.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using Newtonsoft.Json;

namespace PlayniteAchievements.Services.Workshop
{
    public sealed class WorkshopInstallRequest
    {
        public WorkshopItem Item { get; set; }

        /// <summary>The downloaded, checksum-verified package.</summary>
        public string PackagePath { get; set; }

        /// <summary>For bundles, which parts to apply.</summary>
        public BundleParts Parts { get; set; } = BundleParts.All;

        /// <summary>For per-game data, the library game to install onto.</summary>
        public Guid? TargetGameId { get; set; }

        /// <summary>For per-game data, how the package meets the custom data the game already has.</summary>
        public WorkshopGameDataInstallMode GameDataMode { get; set; } = WorkshopGameDataInstallMode.KeepEditsSinceInstall;

        /// <summary>
        /// For an update or a reinstall of looks, how the new package meets the targets that
        /// follow the item; null for a first install, which only adds the item to the library.
        /// </summary>
        public Library.LibraryApplyMode? FollowerMode { get; set; }
    }

    /// <summary>How installed per-game data meets the custom data a game already has.</summary>
    public enum WorkshopGameDataInstallMode
    {
        /// <summary>
        /// Update: changes made since the earlier install of this item are kept, and everything
        /// else follows the package.
        /// </summary>
        KeepEditsSinceInstall,

        /// <summary>The game's custom data is replaced by the package.</summary>
        Replace,

        /// <summary>
        /// Everything the game already has is kept, and the package only fills in what is not
        /// set; existing icon files are kept wherever the merged data still uses them.
        /// </summary>
        MergeKeepingExisting
    }

    public sealed class WorkshopInstallResult
    {
        public Guid? GameId { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>The library names the install wrote, one per saved part; empty for game data.</summary>
        public List<string> PresetNames { get; } = new List<string>();

        /// <summary>The library items the install wrote or recorded.</summary>
        public List<string> LibraryItemIds { get; } = new List<string>();

        /// <summary>For game data, the baseline snapshot written for the next update to merge against.</summary>
        public string BaselineFile { get; set; }

        /// <summary>Targets that now hold the new version.</summary>
        public int UpdatedTargets { get; set; }

        /// <summary>Values kept from the user's edits across those targets.</summary>
        public int KeptEdits { get; set; }

        /// <summary>Targets of kinds that take the new version when it is applied again.</summary>
        public int PendingTargets { get; set; }
    }

    /// <summary>
    /// Installs a downloaded Workshop package into the library. Looks (color sets, notification
    /// styles, frames, sound packs and bundle parts) become library items under the item's name
    /// and are applied from the owning card, so a first install never changes the current look;
    /// an update or reinstall also brings the targets that follow the item along. Showcase pages
    /// and game data are also applied on install, and their targets are linked. Runs on the UI
    /// thread: the showcase refresh needs it.
    /// </summary>
    public sealed class WorkshopInstaller
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly WorkshopInstalledRegistry _registry;
        private readonly WorkshopBaselineStore _baselines;
        private readonly ILogger _logger;

        public WorkshopInstaller(
            PlayniteAchievementsPlugin plugin,
            WorkshopInstalledRegistry registry,
            ILogger logger = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger;
            _baselines = new WorkshopBaselineStore(Path.Combine(_registry.Directory, "baselines"), logger);
        }

        /// <summary>The baselines game-data updates merge against; read by the Workshop preview.</summary>
        internal WorkshopBaselineStore Baselines => _baselines;

        public async Task<WorkshopInstallResult> InstallAsync(WorkshopInstallRequest request, CancellationToken cancel)
        {
            if (request?.Item == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var persisted = _plugin.Settings?.Persisted
                            ?? throw new InvalidOperationException("Settings are not available.");
            var result = new WorkshopInstallResult();

            switch (request.Item.Kind)
            {
                case WorkshopItemKind.Colors:
                    InstallColors(request, persisted, result);
                    break;
                case WorkshopItemKind.NotificationStyle:
                case WorkshopItemKind.ScreenshotFrame:
                    await InstallNotificationStyleAsync(request, persisted, result, cancel).ConfigureAwait(true);
                    break;
                case WorkshopItemKind.UnlockSounds:
                    InstallSounds(request, persisted, result);
                    break;
                case WorkshopItemKind.Bundle:
                    await InstallBundleAsync(request, persisted, result, cancel).ConfigureAwait(true);
                    break;
                case WorkshopItemKind.ShowcasePage:
                    InstallShowcasePage(request, persisted, result);
                    break;
                case WorkshopItemKind.GameCustomData:
                    InstallGameData(request, result);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Workshop item kind '{request.Item.Kind}'.");
            }

            if (request.FollowerMode is Library.LibraryApplyMode mode && IsLook(request.Item.Kind))
            {
                var updates = _plugin.LibraryUpdateService;
                foreach (var id in result.LibraryItemIds)
                {
                    var report = updates.MergeIntoTargets(id, mode);
                    result.UpdatedTargets += report.UpdatedTargets.Count;
                    result.KeptEdits += report.KeptEdits;
                    result.PendingTargets += report.PendingTargets.Count;
                }
            }

            return result;
        }

        private static bool IsLook(WorkshopItemKind kind)
        {
            return kind == WorkshopItemKind.Colors
                   || kind == WorkshopItemKind.UnlockSounds
                   || kind == WorkshopItemKind.NotificationStyle
                   || kind == WorkshopItemKind.ScreenshotFrame
                   || kind == WorkshopItemKind.Bundle;
        }

        /// <summary>Which bundle parts a Workshop item offers, as the installer's flags.</summary>
        public static BundleParts BundlePartsOf(WorkshopItem item)
        {
            var parts = BundleParts.None;
            foreach (var name in item?.PartNames ?? Array.Empty<string>())
            {
                if (Enum.TryParse(name, ignoreCase: true, out BundleParts part))
                {
                    parts |= part;
                }
            }

            return parts;
        }

        // ---- notification style / frame ------------------------------------------------------

        private const string ColorsPart = "colors";
        private const string SoundsPart = "sounds";
        private const string ToastPart = "toast";
        private const string FramePart = "frame";

        private Task InstallNotificationStyleAsync(
            WorkshopInstallRequest request,
            PersistedSettings persisted,
            WorkshopInstallResult result,
            CancellationToken cancel)
        {
            var contents = _plugin.NotificationStylePortableStore.InspectPackage(request.PackagePath);
            var saved = false;
            foreach (var isFrame in new[] { false, true })
            {
                if (isFrame ? !contents.HasFrameStyle : !contents.HasToastStyle)
                {
                    continue;
                }

                SaveStylePart(request, result, isFrame, request.PackagePath);
                saved = true;
            }

            if (!saved)
            {
                throw new InvalidOperationException("This package does not contain a notification or frame style.");
            }

            return Task.CompletedTask;
        }

        // ---- colors --------------------------------------------------------------------------

        private void InstallColors(WorkshopInstallRequest request, PersistedSettings persisted, WorkshopInstallResult result)
        {
            SaveColorsPart(request, result, request.PackagePath);
        }

        // ---- sounds --------------------------------------------------------------------------

        private void InstallSounds(WorkshopInstallRequest request, PersistedSettings persisted, WorkshopInstallResult result)
        {
            SaveSoundsPart(request, result, request.PackagePath);
        }

        // ---- bundle --------------------------------------------------------------------------

        private Task InstallBundleAsync(
            WorkshopInstallRequest request,
            PersistedSettings persisted,
            WorkshopInstallResult result,
            CancellationToken cancel)
        {
            var store = _plugin.BundlePortableStore;
            var available = store.Inspect(request.PackagePath);
            var parts = request.Parts & available;
            if (parts == BundleParts.None)
            {
                throw new InvalidOperationException("None of the selected bundle parts is in this package.");
            }

            // Each part becomes a library item of its own kind under the bundle's name, so a
            // bundle can be picked up piece by piece from the cards and never overwrites anything.
            var scratch = PortablePackage.CreateScratchDirectory("WorkshopBundle");
            try
            {
                var extracted = store.ExtractParts(request.PackagePath, parts, scratch);
                if (extracted.TryGetValue(BundleParts.Colors, out var colorsPath))
                {
                    SaveColorsPart(request, result, colorsPath);
                }

                if (extracted.TryGetValue(BundleParts.Sounds, out var soundsPath))
                {
                    SaveSoundsPart(request, result, soundsPath);
                }

                if (extracted.TryGetValue(BundleParts.Toast, out var toastPath))
                {
                    SaveStylePart(request, result, isFrame: false, toastPath);
                }

                if (extracted.TryGetValue(BundleParts.Frame, out var framePath))
                {
                    SaveStylePart(request, result, isFrame: true, framePath);
                }
            }
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
            }

            return Task.CompletedTask;
        }

        // ---- library parts -------------------------------------------------------------------

        private void SaveColorsPart(WorkshopInstallRequest request, WorkshopInstallResult result, string packagePath)
        {
            WritePart(request, result, ColorsPart, Library.LibraryItemKind.Colors, packagePath,
                new Library.PackagePresetFolder(_plugin.ColorPresetStore));
        }

        private void SaveSoundsPart(WorkshopInstallRequest request, WorkshopInstallResult result, string packagePath)
        {
            WritePart(request, result, SoundsPart, Library.LibraryItemKind.Sounds, packagePath,
                new Library.PackagePresetFolder(_plugin.UnlockSoundPresetStore));
        }

        private void SaveStylePart(WorkshopInstallRequest request, WorkshopInstallResult result, bool isFrame, string packagePath)
        {
            WritePart(
                request,
                result,
                isFrame ? FramePart : ToastPart,
                isFrame ? Library.LibraryItemKind.Frame : Library.LibraryItemKind.Toast,
                packagePath,
                new Library.NotificationStylePresetFolder(_plugin.NotificationStylePresetStore, isFrame));
        }

        /// <summary>
        /// Writes one part as its library item. An earlier copy the user edited in place stays as
        /// a local item beside the new version, and the result says so.
        /// </summary>
        private void WritePart(
            WorkshopInstallRequest request,
            WorkshopInstallResult result,
            string part,
            Library.LibraryItemKind kind,
            string packagePath,
            Library.ILibraryPackageFolder folder)
        {
            var item = request.Item;
            var write = _plugin.LibraryUpdateService.WriteWorkshopPart(
                WorkshopLibraryItem(item, kind, Library.LibraryMigration.LibraryIdFor(item.Id, item.Kind, part), part),
                packagePath,
                folder,
                RecordedPartHash(item.Id, part));
            result.PresetNames.Add(write.WrittenName);
            result.LibraryItemIds.Add(write.Item.Id);
            if (write.KeptLocalCopyName != null)
            {
                result.Warnings.Add(string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Workshop_UpdateKeptPreset"),
                    write.KeptLocalCopyName,
                    write.WrittenName));
            }
        }

        private static Library.LibraryItem WorkshopLibraryItem(WorkshopItem item, Library.LibraryItemKind kind, string libraryId, string part)
        {
            return new Library.LibraryItem
            {
                Id = libraryId,
                Kind = kind,
                Name = item.Name,
                Origin = Library.LibraryItemOrigin.Workshop,
                WorkshopItemId = item.Id,
                Part = part,
                Version = item.Version,
                Author = item.Author
            };
        }

        /// <summary>
        /// The hash an install made before the library recorded it wrote for a part, from
        /// installed.json, so an item migrated from there can still tell an edited file.
        /// </summary>
        private string RecordedPartHash(string workshopItemId, string part)
        {
            var recorded = _registry.Find(workshopItemId)?.ContentHash;
            return Library.LibraryMigration.ParsePartHashes(recorded)
                .Where(pair => string.Equals(pair.Key, part, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }

        private static string HashFile(string path) => WorkshopBaselineStore.HashFile(path);

        // ---- showcase page -------------------------------------------------------------------

        /// <summary>
        /// Adds the page package to the library and, unless a page that follows the item is still
        /// there, creates the page and links it. A page that follows the item takes a new version
        /// when it is applied again.
        /// </summary>
        private void InstallShowcasePage(WorkshopInstallRequest request, PersistedSettings persisted, WorkshopInstallResult result)
        {
            var portable = ShowcasePagePortableStore.Read(request.PackagePath);
            try
            {
                var libraryId = Library.LibraryItem.WorkshopId(request.Item.Id);
                var write = _plugin.LibraryUpdateService.WriteWorkshopPart(
                    WorkshopLibraryItem(request.Item, Library.LibraryItemKind.ShowcasePage, libraryId, part: null),
                    request.PackagePath,
                    new Library.DirectoryPackageFolder(
                        Path.Combine(_plugin.GetPluginUserDataPath(), Library.LibraryStore.ShowcaseFolderName),
                        ShowcasePagePortableStore.PackageFileExtension));
                result.LibraryItemIds.Add(write.Item.Id);

                var layout = persisted.Showcase;
                var followingPages = persisted.LibraryLinks
                    .Where(pair => string.Equals(pair.Value?.LibraryItemId, libraryId, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .Where(key => layout?.Pages?.Any(page => !string.IsNullOrWhiteSpace(page?.PageId)
                        && string.Equals(Library.LibraryTargetKeys.Showcase(page.PageId), key, StringComparison.OrdinalIgnoreCase)) == true)
                    .ToList();
                if (followingPages.Count > 0)
                {
                    result.PendingTargets += followingPages.Count;
                    return;
                }

                var page = ShowcasePagePortableStore.ApplyPortable(
                    layout,
                    persisted.GridOptions,
                    portable,
                    insertAfterPageId: null,
                    storeImage: extracted => _plugin.ShowcaseImageStore?.Import(extracted));

                // The same post-edit sequence the showcase editor runs after an import.
                ShowcaseLayoutService.Normalize(layout);
                ShowcaseLayoutService.PruneOrphanedWidgets(layout);
                ShowcaseGridSurfaces.PruneOrphaned(persisted.GridOptions, layout);
                if (!string.IsNullOrWhiteSpace(page?.PageId))
                {
                    persisted.SetLibraryLink(Library.LibraryTargetKeys.Showcase(page.PageId), new LibraryLink
                    {
                        LibraryItemId = libraryId,
                        AppliedVersion = write.Item.Version,
                        AppliedUtc = DateTime.UtcNow
                    });
                }

                _plugin.PersistSettingsForUi();
                _plugin.ShowcaseImageStore?.Prune(layout);
                ShowcaseConfigurationEvents.RaiseChanged();
            }
            finally
            {
                ShowcasePagePortableStore.DeleteExtractedImages(portable);
            }
        }

        // ---- per-game data -------------------------------------------------------------------

        private void InstallGameData(WorkshopInstallRequest request, WorkshopInstallResult result)
        {
            var gameId = request.TargetGameId
                         ?? throw new InvalidOperationException("Choose which game to install this data onto.");
            var store = _plugin.GameCustomDataStore
                        ?? throw new InvalidOperationException("Game custom data store is not available.");

            GameCustomDataFile previous = store.TryLoad(gameId, out var loaded) ? loaded : null;
            var isCustomAchievementsPackage = store.IsCustomAchievementsPackage(request.PackagePath);

            // An update onto data the user has edited since the last install: the baseline is what
            // that install left behind, so the merge below can tell their edits from the rest, and
            // icons they swapped in place are set aside before the import rewrites the slots.
            // A merge onto data the user had before this item treats all of it as theirs: an empty
            // baseline makes every value they set count as an edit, and every icon file on disk is
            // set aside. A replace keeps nothing.
            var libraryId = Library.LibraryItem.WorkshopId(request.Item.Id);
            var baselineFile = GameDataBaselineFile(request.Item.Id, gameId);
            var iconDirectory = _plugin.ManagedCustomIconService?.GetGameCustomIconDirectory(gameId.ToString("D"));
            GameCustomDataFile baseline = null;
            EditedIconSet editedIcons = null;
            if (!isCustomAchievementsPackage && previous != null)
            {
                switch (request.GameDataMode)
                {
                    case WorkshopGameDataInstallMode.MergeKeepingExisting:
                        baseline = new GameCustomDataFile();
                        editedIcons = SnapshotEditedIcons(iconDirectory, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                        break;
                    case WorkshopGameDataInstallMode.KeepEditsSinceInstall:
                        baseline = _baselines.Load(baselineFile);
                        editedIcons = baseline != null
                            ? SnapshotEditedIcons(iconDirectory, _baselines.LoadIconHashes(baselineFile))
                            : null;
                        break;
                }
            }

            try
            {
                if (isCustomAchievementsPackage)
                {
                    var parsed = store.ImportCustomAchievementsPackage(gameId, request.PackagePath);
                    if (parsed == null || parsed.HasErrors)
                    {
                        throw new InvalidOperationException(
                            string.Join(Environment.NewLine, parsed?.Errors?.Take(8) ?? Enumerable.Empty<string>()));
                    }

                    var overrides = _plugin.AchievementOverridesService
                                    ?? throw new InvalidOperationException("Achievement overrides service is not available.");
                    overrides.MergeCustomAchievements(gameId, parsed.Definitions, out _, out _);
                }
                else
                {
                    var imported = store.ImportReplacePortable(gameId, request.PackagePath);
                    if (imported?.ImportedData == null)
                    {
                        throw new InvalidOperationException("The package contained no custom data.");
                    }

                    if (imported.HasIgnoredPackageImages)
                    {
                        result.Warnings.Add(string.Format(
                            ResourceProvider.GetString("LOCPlayAch_ManageAchievements_Overrides_ImportIgnoredPackageImages"),
                            imported.IgnoredPackageImageCount));
                    }
                }

                var incoming = store.TryLoad(gameId, out var after) ? after : null;
                var current = incoming;
                if (baseline != null && incoming != null)
                {
                    var merged = GameCustomDataThreeWayMerge.Merge(baseline, previous, incoming, out var keptEdits);
                    if (keptEdits > 0)
                    {
                        store.Save(gameId, merged);
                        current = merged;
                    }

                    var keptIcons = RestoreEditedIcons(editedIcons, iconDirectory, current);
                    if (keptEdits + keptIcons > 0)
                    {
                        result.Warnings.Add(string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_UpdateKeptEdits"), keptEdits + keptIcons));
                    }
                }

                if (!isCustomAchievementsPackage && incoming != null)
                {
                    result.BaselineFile = _baselines.Write(request.Item.Id, gameId, incoming, iconDirectory);
                }
                else
                {
                    result.BaselineFile = baselineFile;
                }

                // The game follows the item: its link carries the baseline the next update merges against.
                var stored = _plugin.LibraryUpdateService.RecordWorkshopItem(
                    WorkshopLibraryItem(request.Item, Library.LibraryItemKind.GameData, libraryId, part: null));
                result.LibraryItemIds.Add(stored.Id);
                _plugin.GameLinkStore.Set(Library.LibraryTargetKeys.GameData(gameId), new LibraryLink
                {
                    LibraryItemId = libraryId,
                    AppliedVersion = request.Item.Version,
                    BaselineFile = result.BaselineFile,
                    AppliedUtc = DateTime.UtcNow
                });

                var effects = CustomDataTransition.Analyze(previous, current);

                _plugin.CacheManager?.NotifyCacheInvalidated(new[] { gameId });
                if (_plugin.Settings?.SelectedGame?.Id == gameId)
                {
                    _plugin.ThemeUpdateService?.RequestUpdate(gameId, forceRefresh: true);
                }

                if (effects.RequiresRefresh)
                {
                    _ = _plugin.RefreshEntryPoint?.ExecuteAsync(
                        new RefreshRequest
                        {
                            Mode = RefreshModeType.Single,
                            SingleGameId = gameId,
                            SurfaceUserNotices = true,
                            Options = new RefreshOptions
                            {
                                Subjects = RefreshSubjects.CurrentUser,
                                Scope = RefreshGameScope.SelectedGame,
                                PlayniteGameIds = new[] { gameId },
                                RespectUserExclusions = false,
                                ForceBypassExclusionsForExplicitIncludes = true,
                                ForceIconRefresh = effects.ForceIconRefresh
                            }
                        },
                        RefreshExecutionPolicy.ProgressWindow(gameId));
                }
            }
            finally
            {
                if (editedIcons != null)
                {
                    PortablePackage.TryDeleteDirectory(editedIcons.Directory);
                }
            }

            result.GameId = gameId;
        }

        /// <summary>
        /// The baseline a game-data update onto <paramref name="gameId"/> merges against: the one
        /// the game's link to the item carries, or for an install made before the library, the
        /// one installed.json recorded. Null when there is none.
        /// </summary>
        internal string GameDataBaselineFile(string workshopItemId, Guid gameId)
        {
            var link = _plugin.GameLinkStore.Get(Library.LibraryTargetKeys.GameData(gameId));
            if (link != null && string.Equals(link.LibraryItemId, Library.LibraryItem.WorkshopId(workshopItemId), StringComparison.OrdinalIgnoreCase))
            {
                return link.BaselineFile;
            }

            return _registry.Find(workshopItemId, gameId)?.BaselineFile;
        }

        // ---- update baselines ----------------------------------------------------------------

        /// <summary>Icons the user replaced since the baseline, copied aside before an import rewrites their slots.</summary>
        private sealed class EditedIconSet
        {
            public string Directory { get; set; }
            public List<string> RelativePaths { get; } = new List<string>();
        }

        /// <summary>
        /// Copies aside every icon file whose content differs from <paramref name="baselineHashes"/>
        /// (all of them, for an empty map), so the import cannot lose them.
        /// </summary>
        private EditedIconSet SnapshotEditedIcons(string iconDirectory, IReadOnlyDictionary<string, string> baselineHashes)
        {
            if (string.IsNullOrEmpty(iconDirectory) || !Directory.Exists(iconDirectory) || baselineHashes == null)
            {
                return null;
            }

            var set = new EditedIconSet { Directory = PortablePackage.CreateScratchDirectory("WorkshopIcons") };
            foreach (var pair in WorkshopBaselineStore.HashIcons(iconDirectory))
            {
                if (baselineHashes.TryGetValue(pair.Key, out var recorded) && string.Equals(recorded, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Path.Combine(set.Directory, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(iconDirectory, pair.Key), target, overwrite: true);
                set.RelativePaths.Add(pair.Key);
            }

            return set;
        }

        /// <summary>
        /// Puts the user's swapped icon files back wherever the merged data still points at them,
        /// and returns how many were restored.
        /// </summary>
        private static int RestoreEditedIcons(EditedIconSet edited, string iconDirectory, GameCustomDataFile merged)
        {
            if (edited == null || edited.RelativePaths.Count == 0 || string.IsNullOrEmpty(iconDirectory) || merged == null)
            {
                return 0;
            }

            var json = JsonConvert.SerializeObject(merged);
            var restored = 0;
            foreach (var relative in edited.RelativePaths)
            {
                var fileName = Path.GetFileName(relative);
                if (string.IsNullOrEmpty(fileName) || json.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var source = Path.Combine(edited.Directory, relative);
                var target = Path.Combine(iconDirectory, relative);
                if (!File.Exists(source))
                {
                    continue;
                }

                if (File.Exists(target) && string.Equals(HashFile(source), HashFile(target), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, overwrite: true);
                restored++;
            }

            return restored;
        }

        // ---- shared --------------------------------------------------------------------------

        private AchievementToastTemplateResolver CreateTemplateResolver()
        {
            return new AchievementToastTemplateResolver(
                _plugin.PlayniteApi,
                _logger,
                customTemplatesDirectory: AchievementToastTemplateResolver.GetCustomTemplatesDirectory(
                    _plugin.GetPluginUserDataPath()));
        }
    }
}

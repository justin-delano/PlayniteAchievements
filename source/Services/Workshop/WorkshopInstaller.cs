using Playnite.SDK;
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

namespace PlayniteAchievements.Services.Workshop
{
    public sealed class WorkshopInstallRequest
    {
        public WorkshopItem Item { get; set; }

        /// <summary>The downloaded, checksum-verified package.</summary>
        public string PackagePath { get; set; }

        /// <summary>For themes, which parts to apply.</summary>
        public ThemePackParts ThemeParts { get; set; } = ThemePackParts.All;

        /// <summary>For per-game data, the library game to install onto.</summary>
        public Guid? TargetGameId { get; set; }
    }

    public sealed class WorkshopInstallResult
    {
        public string UndoSnapshotId { get; set; }

        public Guid? GameId { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Applies a downloaded Workshop package through the same import code the settings pages
    /// and the Manage Achievements window use, after snapshotting what it will replace so the
    /// install can be reverted. Runs on the UI thread: template validation and the showcase
    /// refresh need it.
    /// </summary>
    public sealed class WorkshopInstaller
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly WorkshopInstalledRegistry _registry;
        private readonly WorkshopUndoStore _undo;
        private readonly ILogger _logger;

        public WorkshopInstaller(
            PlayniteAchievementsPlugin plugin,
            WorkshopInstalledRegistry registry,
            WorkshopUndoStore undo,
            ILogger logger = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _undo = undo ?? throw new ArgumentNullException(nameof(undo));
            _logger = logger;
        }

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
                case WorkshopItemKind.Theme:
                    await InstallThemeAsync(request, persisted, result, cancel).ConfigureAwait(true);
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

            _registry.Record(request.Item, result.GameId);
            return result;
        }

        /// <summary>Restores a snapshot taken before an install and re-applies the live resources.</summary>
        public void Revert(string snapshotId)
        {
            var persisted = _plugin.Settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            var restored = _undo.Restore(snapshotId, persisted);
            if (restored == WorkshopSettingsSlices.None)
            {
                return;
            }

            _plugin.PersistSettingsForUi();
            AfterSettingsChanged(restored, persisted);
            _undo.Delete(snapshotId);
        }

        /// <summary>Which theme parts a Workshop item offers, as the installer's flags.</summary>
        public static ThemePackParts ThemePartsOf(WorkshopItem item)
        {
            var parts = ThemePackParts.None;
            foreach (var name in item?.ThemeParts ?? Array.Empty<string>())
            {
                if (Enum.TryParse(name, ignoreCase: true, out ThemePackParts part))
                {
                    parts |= part;
                }
            }

            return parts;
        }

        // ---- notification style / frame ------------------------------------------------------

        private async Task InstallNotificationStyleAsync(
            WorkshopInstallRequest request,
            PersistedSettings persisted,
            WorkshopInstallResult result,
            CancellationToken cancel)
        {
            var store = _plugin.NotificationStylePortableStore;
            var contents = store.InspectPackage(request.PackagePath);

            result.UndoSnapshotId = _undo.Snapshot(persisted, WorkshopSettingsSlices.NotificationStyle, request.Item.Id, request.Item.Name);

            var imported = await store.ImportAsync(request.PackagePath, NotificationImageOwner.Global, cancel).ConfigureAwait(true);
            var merged = (persisted.NotificationStyle ?? NotificationStyleSettings.CreateDefault()).Clone();
            if (contents.HasToastStyle)
            {
                NotificationStylePortableStore.ApplyPackSurfaces(merged, imported, isFrame: false);
            }

            if (contents.HasFrameStyle)
            {
                NotificationStylePortableStore.ApplyPackSurfaces(merged, imported, isFrame: true);
            }

            persisted.NotificationStyle = merged;

            var resolver = CreateTemplateResolver();
            foreach (var isFrame in new[] { false, true })
            {
                var xaml = store.ReadTemplateXaml(request.PackagePath, isFrame);
                if (string.IsNullOrWhiteSpace(xaml))
                {
                    continue;
                }

                try
                {
                    resolver.SaveCustomTemplate(isFrame, xaml, providerKey: null, gameId: Guid.Empty);
                }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, $"Workshop item {request.Item.Id}: the {(isFrame ? "frame" : "toast")} template was rejected.");
                    result.Warnings.Add(ex.Message);
                }
            }

            _plugin.PersistSettingsForUi();
            _plugin.NotificationImageStore?.PruneOrphans(persisted, _plugin.GameCustomDataStore?.LoadAll());
        }

        // ---- colors --------------------------------------------------------------------------

        private void InstallColors(WorkshopInstallRequest request, PersistedSettings persisted, WorkshopInstallResult result)
        {
            result.UndoSnapshotId = _undo.Snapshot(persisted, WorkshopSettingsSlices.Colors, request.Item.Id, request.Item.Name);
            _plugin.ColorPackPortableStore.Import(request.PackagePath, persisted);
            _plugin.PersistSettingsForUi();
            AfterSettingsChanged(WorkshopSettingsSlices.Colors, persisted);
        }

        // ---- sounds --------------------------------------------------------------------------

        private void InstallSounds(WorkshopInstallRequest request, PersistedSettings persisted, WorkshopInstallResult result)
        {
            result.UndoSnapshotId = _undo.Snapshot(persisted, WorkshopSettingsSlices.Sounds, request.Item.Id, request.Item.Name);

            var sounds = persisted.UnlockSounds ?? UnlockSoundSettings.CreateDefault();
            _plugin.UnlockSoundPortableStore.Import(request.PackagePath, sounds);
            persisted.UnlockSounds = sounds;
            _plugin.UnlockSoundPortableStore.PruneUnreferenced(sounds);

            _plugin.PersistSettingsForUi();
            AfterSettingsChanged(WorkshopSettingsSlices.Sounds, persisted);
        }

        // ---- theme ---------------------------------------------------------------------------

        private async Task InstallThemeAsync(
            WorkshopInstallRequest request,
            PersistedSettings persisted,
            WorkshopInstallResult result,
            CancellationToken cancel)
        {
            var store = _plugin.ThemePackPortableStore;
            var available = store.Inspect(request.PackagePath);
            var parts = request.ThemeParts & available;
            if (parts == ThemePackParts.None)
            {
                throw new InvalidOperationException("None of the selected theme parts is in this package.");
            }

            var slices = WorkshopSettingsSlices.None;
            if (parts.HasFlag(ThemePackParts.Colors)) slices |= WorkshopSettingsSlices.Colors;
            if (parts.HasFlag(ThemePackParts.Sounds)) slices |= WorkshopSettingsSlices.Sounds;
            if (parts.HasFlag(ThemePackParts.Toast) || parts.HasFlag(ThemePackParts.Frame)) slices |= WorkshopSettingsSlices.NotificationStyle;
            result.UndoSnapshotId = _undo.Snapshot(persisted, slices, request.Item.Id, request.Item.Name);

            var resolver = CreateTemplateResolver();
            var applied = await store.ImportAsync(
                request.PackagePath,
                parts,
                persisted,
                (isFrame, xaml) =>
                {
                    try
                    {
                        resolver.SaveCustomTemplate(isFrame, xaml, providerKey: null, gameId: Guid.Empty);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn(ex, $"Workshop theme {request.Item.Id}: the {(isFrame ? "frame" : "toast")} template was rejected.");
                        result.Warnings.Add(ex.Message);
                    }
                },
                cancel).ConfigureAwait(true);

            _plugin.PersistSettingsForUi();
            AfterSettingsChanged(slices, persisted);
            if (applied.HasFlag(ThemePackParts.Toast) || applied.HasFlag(ThemePackParts.Frame))
            {
                _plugin.NotificationImageStore?.PruneOrphans(persisted, _plugin.GameCustomDataStore?.LoadAll());
            }
        }

        // ---- showcase page -------------------------------------------------------------------

        private void InstallShowcasePage(WorkshopInstallRequest request, PersistedSettings persisted, WorkshopInstallResult result)
        {
            var portable = ShowcasePagePortableStore.Read(request.PackagePath);
            try
            {
                result.UndoSnapshotId = _undo.Snapshot(persisted, WorkshopSettingsSlices.Showcase, request.Item.Id, request.Item.Name);

                var layout = persisted.Showcase;
                ShowcasePagePortableStore.ApplyPortable(
                    layout,
                    persisted.GridOptions,
                    portable,
                    insertAfterPageId: null,
                    storeImage: extracted => _plugin.ShowcaseImageStore?.Import(extracted));

                // The same post-edit sequence the showcase editor runs after an import.
                ShowcaseLayoutService.Normalize(layout);
                ShowcaseLayoutService.PruneOrphanedWidgets(layout);
                ShowcaseGridSurfaces.PruneOrphaned(persisted.GridOptions, layout);
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

            if (store.IsCustomAchievementsPackage(request.PackagePath))
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

            var current = store.TryLoad(gameId, out var after) ? after : null;
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

            result.GameId = gameId;
        }

        // ---- shared --------------------------------------------------------------------------

        /// <summary>Pushes changed settings slices into the live application resources and services.</summary>
        private void AfterSettingsChanged(WorkshopSettingsSlices slices, PersistedSettings persisted)
        {
            if (slices.HasFlag(WorkshopSettingsSlices.Colors))
            {
                var resources = Application.Current?.Resources;
                if (resources != null)
                {
                    PlayAchResourceService.Apply(resources, persisted.ResourceOverrides, persisted);
                    RarityAppearanceHelper.ApplyBadgeApplicationResources(persisted);
                }
            }

            if (slices.HasFlag(WorkshopSettingsSlices.Sounds))
            {
                try
                {
                    _plugin.UnlockSounds?.ApplySettings();
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "Re-applying unlock sounds after a Workshop change failed.");
                }
            }

            if (slices.HasFlag(WorkshopSettingsSlices.Showcase))
            {
                ShowcaseConfigurationEvents.RaiseChanged();
            }
        }

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

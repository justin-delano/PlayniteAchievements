using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
// WinForms dialog: the WPF Microsoft.Win32 picker renders legacy-style on .NET Framework.
using DialogResult = System.Windows.Forms.DialogResult;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Settings.Notifications
{
    /// <summary>
    /// Notification settings: the Behavior page. What fires (the four enable switches), how it
    /// fires (duration, delay, concurrency, screen corner), and what accompanies it (controller
    /// vibration and the per-tier unlock sounds). Capture settings live in
    /// <see cref="NotificationCapturesSection"/>, per-provider overrides in
    /// <see cref="NotificationPlatformsSection"/>, and styling in
    /// <see cref="General.NotificationAppearanceSection"/>.
    /// </summary>
    public partial class NotificationBehaviorSection : UserControl, IDisposable
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PersistedSettingsSubscription _persistedSubscription;
        private readonly UnlockSoundSettingsViewModel _unlockSoundsViewModel;
        private readonly ILogger _logger;

        public NotificationBehaviorSection()
        {
            InitializeComponent();
            RefreshSoundPackPresetOptions();
        }

        internal NotificationBehaviorSection(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger)
            : this()
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;

            _persistedSubscription = new PersistedSettingsSubscription(
                _settings,
                OnPersistedPropertyChanged);

            // DataContext island: the per-tier sound rows carry their own view model.
            _unlockSoundsViewModel = new UnlockSoundSettingsViewModel(settings, plugin.UnlockSounds, logger);
            UnlockSoundRows.DataContext = _unlockSoundsViewModel;
        }

        private void UnlockSoundBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = UnlockSoundResolver.BuildOpenFileDialogFilter(),
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                row.CustomPath = dialog.FileName;
            }
        }

        private void UnlockSoundClear_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row)
            {
                row.CustomPath = null;
            }
        }

        private void UnlockSoundTest_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            if ((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row)
            {
                _unlockSoundsViewModel?.Test(row);
            }
        }

        /// <summary>
        /// Writes what each tier currently plays, minus the bundled defaults, to a .pasounds file.
        /// </summary>
        private void UnlockSoundPackExport_Click(object sender, RoutedEventArgs e)
        {
            // Only the user's own and theme files travel, so all-default tiers have nothing to share.
            var resolved = _plugin?.UnlockSounds?.Resolver?.ResolveAll();
            var hasOwnSounds = resolved != null
                && resolved.Any(s => s.Source == UnlockSoundSource.Custom || s.Source == UnlockSoundSource.Theme);
            WorkshopMenus.OpenExport(
                sender as Button,
                () => UnlockSoundPackExportFile_Click(sender, e),
                () => _plugin?.OpenWorkshopShare(WorkshopItemKind.UnlockSounds, Window.GetWindow(this)),
                workshopEnabled: hasOwnSounds);
        }

        private void UnlockSoundPackExportFile_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var store = _plugin?.UnlockSoundPortableStore;
            var resolver = _plugin?.UnlockSounds?.Resolver;
            if (store == null || resolver == null)
            {
                return;
            }

            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = UnlockSoundPortableStore.BuildFileDialogFilter(),
                    AddExtension = true,
                    DefaultExt = UnlockSoundPortableStore.PackageFileExtension,
                    FileName = "unlock-sounds" + UnlockSoundPortableStore.PackageFileExtension
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                store.Export(resolver.ResolveAll(), UnlockSoundPortableStore.NormalizeExportPath(dialog.FileName));
                ShowMessage(L("LOCPlayAch_Status_Succeeded"), MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed exporting unlock sound pack.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Copies a .pasounds pack into managed storage and points the carried tiers at it; tiers
        /// the pack does not carry keep their current sound.
        /// </summary>
        private void UnlockSoundPackImport_Click(object sender, RoutedEventArgs e)
        {
            WorkshopMenus.OpenImport(
                sender as Button,
                () => UnlockSoundPackImportFile_Click(sender, e),
                () => _plugin?.OpenWorkshopWindow(focusKind: WorkshopItemKind.UnlockSounds));
        }

        /// <summary>
        /// Adds a .pasounds file to the saved sound packs, named after the file. Applying it to
        /// the tiers is the job of the pack list, so the current sounds do not change here.
        /// </summary>
        private void UnlockSoundPackImportFile_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var presets = _plugin?.UnlockSoundPresetStore;
            if (presets == null)
            {
                return;
            }

            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = UnlockSoundPortableStore.BuildFileDialogFilter(),
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                var saved = presets.SaveFrom(presets.UniqueName(PackageStem(dialog.FileName)), dialog.FileName);
                RefreshSoundPackPresetOptions(saved.Name);
                ShowMessage(string.Format(L("LOCPlayAch_Workshop_SavedAsPreset"), saved.Name), MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed importing unlock sound pack.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        /// <summary>The file name without its package extension, including a trailing .zip.</summary>
        private static string PackageStem(string path)
        {
            var name = System.IO.Path.GetFileName(path) ?? string.Empty;
            foreach (var suffix in new[] { ".zip", UnlockSoundPortableStore.PackageFileExtension })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                }
            }

            return name;
        }

        // ---- saved sound packs ---------------------------------------------------------------

        private Services.Workshop.PackagePresetInfo SelectedSoundPackPreset =>
            SoundPackPresetSelector?.SelectedItem as Services.Workshop.PackagePresetInfo;

        private void RefreshSoundPackPresetOptions(string selectName = null)
        {
            var store = _plugin?.UnlockSoundPresetStore;
            if (SoundPackPresetSelector == null || store == null)
            {
                return;
            }

            var items = new System.Collections.Generic.List<object> { L("LOCPlayAch_Common_None") };
            try
            {
                items.AddRange(store.List());
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed listing sound pack presets.");
            }

            SoundPackPresetSelector.ItemsSource = items;
            SoundPackPresetSelector.SelectedItem = string.IsNullOrWhiteSpace(selectName)
                ? items[0]
                : items.OfType<Services.Workshop.PackagePresetInfo>().FirstOrDefault(preset =>
                      string.Equals(preset.Name, selectName, StringComparison.OrdinalIgnoreCase)) ?? items[0];
            RefreshSoundPackPresetButtons();
        }

        private void RefreshSoundPackPresetButtons()
        {
            if (ApplySoundPackPresetButton == null || DeleteSoundPackPresetButton == null)
            {
                return;
            }

            ApplySoundPackPresetButton.IsEnabled = DeleteSoundPackPresetButton.IsEnabled = SelectedSoundPackPreset != null;
        }

        private void SoundPackPresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshSoundPackPresetButtons();
        }

        /// <summary>Presets saved elsewhere (a Workshop install, another window) show up when the list opens.</summary>
        private void SoundPackPresetSelector_DropDownOpened(object sender, EventArgs e)
        {
            RefreshSoundPackPresetOptions(SelectedSoundPackPreset?.Name);
        }

        /// <summary>Copies the selected pack onto the tiers: its files into managed storage, the rest left as they are.</summary>
        private void ApplySoundPackPreset_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var preset = SelectedSoundPackPreset;
            var store = _plugin?.UnlockSoundPortableStore;
            var sounds = _settings?.Persisted?.UnlockSounds;
            if (preset == null || store == null || sounds == null)
            {
                return;
            }

            try
            {
                store.Import(preset.FilePath, sounds);
                store.PruneUnreferenced(sounds);
                _unlockSoundsViewModel?.Refresh();
                _unlockSoundsViewModel?.ScheduleApply();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed applying sound pack preset.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        /// <summary>Saves the current own and theme sounds as a named pack, replacing one of the same name after confirmation.</summary>
        private void SaveSoundPackPreset_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var presets = _plugin?.UnlockSoundPresetStore;
            var store = _plugin?.UnlockSoundPortableStore;
            var resolver = _plugin?.UnlockSounds?.Resolver;
            if (presets == null || store == null || resolver == null)
            {
                return;
            }

            try
            {
                var resolved = resolver.ResolveAll();
                if (!PresetNamePrompt.TryAsk(_plugin, SelectedSoundPackPreset?.Name, Services.Workshop.PackagePresetStore.SanitizeName, Services.Workshop.PackagePresetStore.MaxNameLength, out var name))
                {
                    return;
                }

                var exists = presets.Exists(name);
                if (!exists && presets.Count() >= Services.Workshop.PackagePresetStore.MaxPresetCount)
                {
                    ShowMessage(string.Format(L("LOCPlayAch_Presets_MaxReached"), Services.Workshop.PackagePresetStore.MaxPresetCount), MessageBoxImage.Warning);
                    return;
                }

                if (exists && !Confirm(string.Format(L("LOCPlayAch_Presets_OverwriteConfirm"), name)))
                {
                    return;
                }

                var saved = presets.Save(name, path => store.Export(resolved, path));
                RefreshSoundPackPresetOptions(saved.Name);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed saving sound pack preset.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        private void DeleteSoundPackPreset_Click(object sender, RoutedEventArgs e)
        {
            var preset = SelectedSoundPackPreset;
            var presets = _plugin?.UnlockSoundPresetStore;
            if (preset == null || presets == null)
            {
                return;
            }

            if (!Confirm(string.Format(L("LOCPlayAch_Presets_DeleteConfirm"), preset.Name)))
            {
                return;
            }

            try
            {
                presets.Delete(preset);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed deleting sound pack preset.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }

            RefreshSoundPackPresetOptions();
        }

        private bool Confirm(string message)
        {
            return _plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                       message,
                       L("LOCPlayAch_Title_PluginName"),
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private void ShowMessage(string message, MessageBoxImage image)
        {
            _plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                message,
                L("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                image);
        }

        private static string L(string key)
        {
            return ResourceProvider.GetString(key);
        }

        /// <summary>
        /// Plays what a theme ships for this tier. One candidate is not worth a menu, so the button
        /// plays it outright; two means the desktop and the fullscreen theme ship different files
        /// for the tier and the listener has to pick, which is the case this exists for. Either can
        /// be heard without restarting Playnite into the other mode.
        /// </summary>
        private void UnlockSoundTheme_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            if (!(sender is Button button) || !(button.DataContext is UnlockSoundRowItem row))
            {
                return;
            }

            if (row.ThemeCandidates.Count == 0)
            {
                return;
            }

            if (row.ThemeCandidates.Count == 1)
            {
                _unlockSoundsViewModel?.TestFile(row.ThemeCandidates[0].Path);
                return;
            }

            var menu = button.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();
            foreach (var candidate in row.ThemeCandidates)
            {
                var path = candidate.Path;
                var item = new MenuItem { Header = candidate.Label };
                item.Click += (s, args) => _unlockSoundsViewModel?.TestFile(path);
                menu.Items.Add(item);
            }

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>
        /// Pulses the controllers at the currently configured strength and duration so the settings
        /// can be felt without unlocking an achievement.
        /// </summary>
        private void TestVibration_Click(object sender, RoutedEventArgs e)
        {
            // Nothing here takes focus away from the button the way the buttons that open a window
            // do, so it would keep the theme's focused look until something else was clicked.
            Keyboard.ClearFocus();

            var persisted = _settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            try
            {
                ControllerVibrationService.Pulse(
                    persisted.ControllerVibrationStrengthPercent,
                    persisted.ControllerVibrationDurationMs,
                    _logger);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Test controller vibration failed.");
            }
        }

        private void OnPersistedPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e?.PropertyName)
            {
                case null:
                case "":
                case nameof(PersistedSettings.UnlockSounds):
                    // The persisted instance was replaced (Cancel) or the slot object swapped.
                    _unlockSoundsViewModel?.Refresh();
                    break;
                case nameof(PersistedSettings.AllowThemeUnlockSounds):
                    // Changes which file each tier resolves to, so the rows are restated and the
                    // host's preloaded set is rebuilt.
                    _unlockSoundsViewModel?.Refresh();
                    _unlockSoundsViewModel?.ScheduleApply();
                    break;
                case nameof(PersistedSettings.UnlockSoundVolumePercent):
                case nameof(PersistedSettings.EnableUnlockSounds):
                    _unlockSoundsViewModel?.ScheduleApply();
                    break;
            }

            // The tier badges are drawn from the rarity appearance settings, so they have to be
            // rebuilt when those change while this page is open.
            if (!string.IsNullOrEmpty(e?.PropertyName) &&
                RarityAppearanceHelper.IsAppearanceSettingPropertyName(e.PropertyName))
            {
                _unlockSoundsViewModel?.Refresh();
            }
        }

        public void Dispose()
        {
            _persistedSubscription?.Dispose();
            _unlockSoundsViewModel?.Dispose();
        }
    }
}

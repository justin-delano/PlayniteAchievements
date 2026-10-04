using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DialogResult = System.Windows.Forms.DialogResult;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Views.Dialogs;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Settings.Workshop
{
    /// <summary>
    /// Workshop settings: the Themes page. Composes a .patheme bundle part by part (current
    /// settings or a saved preset for each), writes it to a file or shares it to the Workshop,
    /// and adds the parts of a bundle file to their preset lists.
    /// </summary>
    public partial class WorkshopThemesSection : UserControl
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        public WorkshopThemesSection()
        {
            InitializeComponent();
        }

        internal WorkshopThemesSection(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger)
            : this()
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
        }

        /// <summary>
        /// Composer first, so the user sees what the bundle will hold, then the save dialog. The
        /// chosen parts are materialized into a scratch folder and zipped from there.
        /// </summary>
        private void ExportTheme_Click(object sender, RoutedEventArgs e)
        {
            WorkshopMenus.OpenExport(
                sender as Button,
                () => ExportThemeFile_Click(sender, e),
                () => ShareTheme_Click(sender, e));
        }

        private void ExportThemeFile_Click(object sender, RoutedEventArgs e)
        {
            var store = _plugin?.ThemePackPortableStore;
            if (store == null)
            {
                return;
            }

            try
            {
                var choices = Compose(ResourceProvider.GetString("LOCPlayAch_Workshop_ExportTheme"));
                if (choices == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = ThemePackPortableStore.BuildFileDialogFilter(),
                    AddExtension = true,
                    DefaultExt = ThemePackPortableStore.PackageFileExtension,
                    FileName = "theme" + ThemePackPortableStore.PackageFileExtension
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                var scratch = PortablePackage.CreateScratchDirectory("ThemeCompose");
                try
                {
                    var parts = new ThemeComposer(_plugin, _logger).BuildPartFiles(choices, scratch);
                    store.ExportParts(ThemePackPortableStore.NormalizeExportPath(dialog.FileName), parts);
                }
                finally
                {
                    PortablePackage.TryDeleteDirectory(scratch);
                }

                ShowMessage(ResourceProvider.GetString("LOCPlayAch_Status_Succeeded"), MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed exporting theme bundle.");
                ShowMessage(string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Adds the chosen parts of a .patheme bundle to their preset lists under the file name:
        /// the color set, the sound pack, and the notification and frame styles. Nothing is
        /// applied until picked from the owning card.
        /// </summary>
        private void ImportTheme_Click(object sender, RoutedEventArgs e)
        {
            var store = _plugin?.ThemePackPortableStore;
            if (store == null)
            {
                return;
            }

            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = ThemePackPortableStore.BuildFileDialogFilter(),
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                var available = store.Inspect(dialog.FileName);
                var parts = PickThemeParts(available, System.IO.Path.GetFileName(dialog.FileName));
                if (parts == ThemePackParts.None)
                {
                    return;
                }

                var stem = PackageStem(dialog.FileName);
                var names = new List<string>();
                var scratch = PortablePackage.CreateScratchDirectory("ThemeImport");
                try
                {
                    var extracted = store.ExtractParts(dialog.FileName, parts, scratch);
                    if (extracted.TryGetValue(ThemePackParts.Colors, out var colorsPath))
                    {
                        var colors = _plugin.ColorPresetStore;
                        names.Add(colors.SaveFrom(colors.UniqueName(stem), colorsPath).Name);
                    }

                    if (extracted.TryGetValue(ThemePackParts.Sounds, out var soundsPath))
                    {
                        var sounds = _plugin.UnlockSoundPresetStore;
                        names.Add(sounds.SaveFrom(sounds.UniqueName(stem), soundsPath).Name);
                    }

                    var styles = _plugin.NotificationStylePresetStore;
                    if (extracted.TryGetValue(ThemePackParts.Toast, out var toastPath))
                    {
                        names.Add(styles.SavePresetFromPackage(false, styles.UniqueName(false, stem), toastPath).Name);
                    }

                    if (extracted.TryGetValue(ThemePackParts.Frame, out var framePath))
                    {
                        names.Add(styles.SavePresetFromPackage(true, styles.UniqueName(true, stem), framePath).Name);
                    }
                }
                finally
                {
                    PortablePackage.TryDeleteDirectory(scratch);
                }

                ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_SavedAsPreset"), string.Join(", ", names.Distinct())),
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed importing theme bundle.");
                ShowMessage(string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Same composer as Export, then the share dialog with the composed parts. The scratch
        /// folder outlives the modal share dialog and is removed when it closes.
        /// </summary>
        private void ShareTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_plugin == null)
            {
                return;
            }

            try
            {
                var choices = Compose(ResourceProvider.GetString("LOCPlayAch_Workshop_Share"));
                if (choices == null)
                {
                    return;
                }

                var scratch = PortablePackage.CreateScratchDirectory("ThemeCompose");
                try
                {
                    var parts = new ThemeComposer(_plugin, _logger).BuildPartFiles(choices, scratch);
                    _plugin.OpenWorkshopShare(WorkshopItemKind.Theme, Window.GetWindow(this), themePartFiles: parts);
                }
                finally
                {
                    PortablePackage.TryDeleteDirectory(scratch);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed composing theme for sharing.");
                ShowMessage(string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        private IReadOnlyList<ThemePartChoice> Compose(string title)
        {
            return ThemeComposerDialog.Show(
                new ThemeComposer(_plugin, _logger),
                title,
                ResourceProvider.GetString("LOCPlayAch_Workshop_ThemeComposerHint"),
                Window.GetWindow(this) ?? _plugin.PlayniteApi?.Dialogs?.GetCurrentAppWindow());
        }

        /// <summary>
        /// Offers the four theme parts of an imported bundle as a checklist, with parts outside
        /// <paramref name="available"/> shown disabled, and returns the chosen set.
        /// </summary>
        private ThemePackParts PickThemeParts(ThemePackParts available, string hint)
        {
            var items = ThemeComposer.Parts
                .Select(part => new PartPickerItem(part, ThemeComposer.LabelFor(part), isEnabled: available.HasFlag(part)))
                .ToList();

            var selected = PartPickerDialog.Show(
                ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Theme"),
                hint,
                items,
                Window.GetWindow(this) ?? _plugin.PlayniteApi?.Dialogs?.GetCurrentAppWindow());

            return selected == null
                ? ThemePackParts.None
                : selected.OfType<ThemePackParts>().Aggregate(ThemePackParts.None, (acc, part) => acc | part);
        }

        /// <summary>The file name without its package extension, including a trailing .zip.</summary>
        private static string PackageStem(string path)
        {
            var name = System.IO.Path.GetFileName(path) ?? string.Empty;
            foreach (var suffix in new[] { ".zip", ThemePackPortableStore.PackageFileExtension })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                }
            }

            return name;
        }

        private void ShowMessage(string message, MessageBoxImage image)
        {
            _plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                message,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                image);
        }
    }
}

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
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Views.Dialogs;

namespace PlayniteAchievements.Views.Settings.Workshop
{
    /// <summary>
    /// Workshop settings: the Themes page. Exports the global look as a .patheme bundle, adds
    /// the parts of a bundle file to their preset lists, or shares the look to the Workshop.
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
        /// Writes the global look as a .patheme bundle. The user picks which parts travel; the
        /// notification parts carry the installed global custom templates when there are any.
        /// </summary>
        private void ExportTheme_Click(object sender, RoutedEventArgs e)
        {
            var persisted = _settings?.Persisted;
            var store = _plugin?.ThemePackPortableStore;
            if (persisted == null || store == null)
            {
                return;
            }

            try
            {
                var parts = PickThemeParts(ThemePackParts.All, ResourceProvider.GetString("LOCPlayAch_Workshop_ExportTheme"));
                if (parts == ThemePackParts.None)
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

                var resolver = CreateTemplateResolver();
                store.Export(
                    ThemePackPortableStore.NormalizeExportPath(dialog.FileName),
                    parts,
                    persisted,
                    _plugin.UnlockSounds?.Resolver?.ResolveAll(),
                    resolver.ReadCustomTemplateXaml(isFrame: false, providerKey: null, gameId: Guid.Empty),
                    resolver.ReadCustomTemplateXaml(isFrame: true, providerKey: null, gameId: Guid.Empty));
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

        private void ShareTheme_Click(object sender, RoutedEventArgs e)
        {
            _plugin?.OpenWorkshopShare(WorkshopItemKind.Theme, Window.GetWindow(this));
        }

        /// <summary>
        /// Offers the four theme parts as a checklist titled as the theme row, with
        /// <paramref name="hint"/> above the list and parts outside <paramref name="available"/>
        /// shown disabled, and returns the chosen set.
        /// </summary>
        private ThemePackParts PickThemeParts(ThemePackParts available, string hint)
        {
            var items = new[]
            {
                new PartPickerItem(ThemePackParts.Colors, ResourceProvider.GetString("LOCPlayAch_Settings_Display_Colors"),
                    isEnabled: available.HasFlag(ThemePackParts.Colors)),
                new PartPickerItem(ThemePackParts.Sounds, ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Sounds"),
                    isEnabled: available.HasFlag(ThemePackParts.Sounds)),
                new PartPickerItem(ThemePackParts.Toast, ResourceProvider.GetString("LOCPlayAch_Settings_Style_ToastTab"),
                    isEnabled: available.HasFlag(ThemePackParts.Toast)),
                new PartPickerItem(ThemePackParts.Frame, ResourceProvider.GetString("LOCPlayAch_Settings_FrameHeader"),
                    isEnabled: available.HasFlag(ThemePackParts.Frame))
            };

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

        private AchievementToastTemplateResolver CreateTemplateResolver()
        {
            return new AchievementToastTemplateResolver(
                _plugin.PlayniteApi,
                _logger,
                customTemplatesDirectory: AchievementToastTemplateResolver.GetCustomTemplatesDirectory(
                    _plugin.GetPluginUserDataPath()));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DialogResult = System.Windows.Forms.DialogResult;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;
using Playnite.SDK;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Settings.Workshop
{
    /// <summary>
    /// Workshop settings: the Presets page. One list over every saved preset (notification
    /// styles, frames, color sets, sound packs), each a package file in its store, with Export
    /// and Delete. Applying stays on the card that owns the kind.
    /// </summary>
    public partial class WorkshopPresetsSection : UserControl
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        /// <summary>One listed preset: where it came from and how to remove it.</summary>
        private sealed class PresetRow
        {
            public WorkshopItemKind Kind { get; set; }
            public string KindLabel { get; set; }
            public string Name { get; set; }
            public string FilePath { get; set; }
            public Action Delete { get; set; }
        }

        public WorkshopPresetsSection()
        {
            InitializeComponent();
        }

        internal WorkshopPresetsSection(PlayniteAchievementsPlugin plugin, ILogger logger)
            : this()
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            Refresh();
        }

        private PresetRow Selected => PresetList?.SelectedItem as PresetRow;

        private void Refresh()
        {
            var rows = new List<PresetRow>();
            try
            {
                var styles = _plugin.NotificationStylePresetStore;
                foreach (var isFrame in new[] { false, true })
                {
                    var label = ResourceProvider.GetString(isFrame
                        ? "LOCPlayAch_Workshop_Kind_ScreenshotFrame"
                        : "LOCPlayAch_Workshop_Kind_NotificationStyle");
                    foreach (var preset in styles.ListPresets(isFrame))
                    {
                        var captured = preset;
                        rows.Add(new PresetRow
                        {
                            Kind = isFrame ? WorkshopItemKind.ScreenshotFrame : WorkshopItemKind.NotificationStyle,
                            KindLabel = label,
                            Name = preset.Name,
                            FilePath = preset.FilePath,
                            Delete = () => styles.DeletePreset(captured)
                        });
                    }
                }

                AddPackagePresets(rows, _plugin.ColorPresetStore, WorkshopItemKind.Colors, "LOCPlayAch_Workshop_Kind_Colors");
                AddPackagePresets(rows, _plugin.UnlockSoundPresetStore, WorkshopItemKind.UnlockSounds, "LOCPlayAch_Workshop_Kind_UnlockSounds");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed listing presets.");
            }

            var selectedPath = Selected?.FilePath;
            PresetList.ItemsSource = rows
                .OrderBy(row => row.KindLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            PresetList.SelectedItem = selectedPath == null
                ? null
                : rows.FirstOrDefault(row => string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtons();
        }

        private static void AddPackagePresets(List<PresetRow> rows, PackagePresetStore store, WorkshopItemKind kind, string kindKey)
        {
            if (store == null)
            {
                return;
            }

            var label = ResourceProvider.GetString(kindKey);
            foreach (var preset in store.List())
            {
                var captured = preset;
                rows.Add(new PresetRow
                {
                    Kind = kind,
                    KindLabel = label,
                    Name = preset.Name,
                    FilePath = preset.FilePath,
                    Delete = () => store.Delete(captured)
                });
            }
        }

        private void UpdateButtons()
        {
            ExportButton.IsEnabled = DeleteButton.IsEnabled = Selected != null;
        }

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        /// <summary>Export is the same two-way menu as everywhere else: to a file, or shared to the Workshop.</summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (Selected == null)
            {
                return;
            }

            WorkshopMenus.OpenExport(
                sender as Button,
                () => ExportFile_Click(sender, e),
                ShareToWorkshop);
        }

        /// <summary>Copies the preset package file wherever the user chooses; it is already a valid package.</summary>
        private void ExportFile_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected;
            if (row == null || !File.Exists(row.FilePath))
            {
                return;
            }

            try
            {
                var extension = Path.GetExtension(row.FilePath) ?? string.Empty;
                var dialog = new SaveFileDialog
                {
                    Filter = "*" + extension + "|*" + extension,
                    AddExtension = true,
                    DefaultExt = extension.TrimStart('.'),
                    FileName = Path.GetFileName(row.FilePath)
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                var destination = dialog.FileName;
                if (!destination.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    destination += extension;
                }

                File.Copy(row.FilePath, destination, overwrite: true);
                ShowMessage(ResourceProvider.GetString("LOCPlayAch_Status_Succeeded"), MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed exporting preset.");
                ShowMessage(string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
        }

        /// <summary>Shares the preset file as it is; the share dialog previews that preset, not the live look.</summary>
        private void ShareToWorkshop()
        {
            var row = Selected;
            if (row == null || !File.Exists(row.FilePath))
            {
                return;
            }

            _plugin.OpenWorkshopShare(row.Kind, Window.GetWindow(this), packagePath: row.FilePath, defaultName: row.Name);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected;
            if (row == null)
            {
                return;
            }

            var confirmed = _plugin.PlayniteApi?.Dialogs?.ShowMessage(
                                string.Format(ResourceProvider.GetString("LOCPlayAch_Presets_DeleteConfirm"), row.Name),
                                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (!confirmed)
            {
                return;
            }

            try
            {
                row.Delete();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed deleting preset.");
                ShowMessage(string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }

            Refresh();
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

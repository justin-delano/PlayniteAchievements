using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Views.Dialogs;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// The one "name this preset" dialog, shared by the notification, color set and sound pack
    /// preset rows and the Library page's Rename. Returns false when the user cancels or leaves
    /// nothing valid after <paramref name="sanitize"/> has run. <paramref name="title"/> replaces
    /// the Save Preset window title.
    /// </summary>
    internal static class PresetNamePrompt
    {
        public static bool TryAsk(
            PlayniteAchievementsPlugin plugin,
            string defaultName,
            System.Func<string, string> sanitize,
            int maxNameLength,
            out string presetName,
            string title = null)
        {
            presetName = null;

            var inputDialog = new TextInputDialog(
                ResourceProvider.GetString("LOCPlayAch_Presets_NameDialogHint"),
                defaultName ?? string.Empty);

            var window = PlayniteUiProvider.CreateExtensionWindow(
                title ?? ResourceProvider.GetString("LOCPlayAch_Presets_NameDialogTitle"),
                inputDialog,
                new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = false,
                    Width = 460,
                    Height = 200
                });

            WindowPlacementPersistenceService.Attach(window, "PresetName");

            try
            {
                if (window.Owner == null)
                {
                    window.Owner = plugin?.PlayniteApi?.Dialogs?.GetCurrentAppWindow();
                }
            }
            catch
            {
            }

            inputDialog.RequestClose += (s, e) => window.Close();
            window.ShowDialog();

            if (inputDialog.DialogResult != true)
            {
                return false;
            }

            var sanitized = sanitize(inputDialog.InputText);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Presets_NameInvalid"), maxNameLength),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            presetName = sanitized;
            return true;
        }
    }
}

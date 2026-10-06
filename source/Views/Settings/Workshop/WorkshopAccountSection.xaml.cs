using System;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Views.Settings.Workshop
{
    /// <summary>
    /// Workshop settings: the Account page. The sharer name, the submitter key that owns this
    /// install's submissions (copy it to carry the identity to another install, paste one to
    /// take an identity over), and the endpoint overrides for forks and local testing.
    /// </summary>
    public partial class WorkshopAccountSection : UserControl
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        public WorkshopAccountSection()
        {
            InitializeComponent();
        }

        internal WorkshopAccountSection(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger)
            : this()
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;

            try
            {
                var identity = _plugin.WorkshopIdentityStore;
                NameBox.Text = identity.DisplayName ?? string.Empty;
                KeyBox.Text = identity.GetOrCreateSubmitterKey();
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed reading the Workshop identity.");
            }
        }

        private void NameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                _plugin.WorkshopIdentityStore.DisplayName = NameBox.Text;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed saving the Workshop sharer name.");
            }
        }

        private void CopyKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(KeyBox.Text ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed copying the submitter key.");
            }
        }

        /// <summary>
        /// Replaces the key with the clipboard's, after a confirmation that names the cost:
        /// items submitted with the current key stop being updatable from here.
        /// </summary>
        private void PasteKey_Click(object sender, RoutedEventArgs e)
        {
            string clipboard;
            try
            {
                clipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed reading the clipboard.");
                return;
            }

            var identity = _plugin.WorkshopIdentityStore;
            if (!identity.IsValidSubmitterKey(clipboard))
            {
                ShowMessage(ResourceProvider.GetString("LOCPlayAch_Workshop_InvalidKey"), MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(clipboard.Trim(), KeyBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var confirmed = _plugin.PlayniteApi?.Dialogs?.ShowMessage(
                                ResourceProvider.GetString("LOCPlayAch_Workshop_ReplaceKeyConfirm"),
                                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!confirmed)
            {
                return;
            }

            try
            {
                identity.TrySetSubmitterKey(clipboard);
                KeyBox.Text = identity.GetOrCreateSubmitterKey();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed replacing the submitter key.");
                ShowMessage(string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
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

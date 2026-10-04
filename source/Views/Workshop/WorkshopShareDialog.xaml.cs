using Playnite.SDK;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
// WinForms dialog: the WPF Microsoft.Win32 picker renders legacy-style on .NET Framework.
using DialogResult = System.Windows.Forms.DialogResult;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace PlayniteAchievements.Views.Workshop
{
    /// <summary>
    /// Collects the submission form for one shareable thing and runs the share: package, upload,
    /// submit. Closes itself on success after showing where to follow the review.
    /// </summary>
    public partial class WorkshopShareDialog : UserControl
    {
        private sealed class ExistingOption
        {
            public string ItemId { get; set; }
            public string Label { get; set; }
        }

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;
        private readonly WorkshopShareCandidate _candidate;
        private readonly WorkshopShareService _share;
        private readonly WorkshopInstalledRegistry _registry;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private string _previewScratch;
        private bool _busy;

        public WorkshopShareDialog()
        {
            InitializeComponent();
        }

        internal WorkshopShareDialog(
            PlayniteAchievementsPlugin plugin,
            ILogger logger,
            WorkshopShareCandidate candidate,
            WorkshopShareService share,
            WorkshopInstalledRegistry registry)
            : this()
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
            _share = share ?? throw new ArgumentNullException(nameof(share));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            CandidateLabel.Text = candidate.Label;
            NameBox.Text = candidate.DefaultName ?? string.Empty;
            AuthorBox.Text = registry.DisplayName ?? string.Empty;

            // Where the plugin can draw the thing itself (toast, frame, theme), start with that
            // render; the user can still browse for a different image.
            _previewScratch = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "WorkshopPreview", Guid.NewGuid().ToString("N"));
            var rendered = new WorkshopPreviewRenderer(plugin, logger).TryRender(candidate.Kind, _previewScratch, candidate.PackagePath ?? (candidate.ThemePartFiles != null && candidate.ThemePartFiles.TryGetValue(ThemePackParts.Toast, out var toastPart) ? toastPart : null));
            if (rendered != null)
            {
                PreviewBox.Text = rendered;
            }

            // Earlier submissions of the same kind whose Workshop id is known can be updated.
            var options = registry.Submissions
                .Where(s => s.Kind == candidate.Kind && !string.IsNullOrWhiteSpace(s.ItemId))
                .GroupBy(s => s.ItemId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ExistingOption { ItemId = g.Key, Label = $"{g.First().Name} ({g.Key})" })
                .ToList();
            ExistingBox.ItemsSource = options;
        }

        public event EventHandler RequestClose;

        private void BrowsePreview_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                PreviewBox.Text = dialog.FileName;
            }
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            var name = NameBox.Text.Trim();
            var author = AuthorBox.Text.Trim();
            var description = DescriptionBox.Text.Trim();
            if (name.Length == 0 || author.Length == 0 || description.Length == 0)
            {
                StatusText.Text = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_Failed"),
                    ResourceProvider.GetString("LOCPlayAch_Column_Name") + ", " +
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Share_AuthorName") + ", " +
                    ResourceProvider.GetString("LOCPlayAch_Column_Description"));
                return;
            }

            if (RightsBox.IsChecked != true)
            {
                StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Rights");
                return;
            }

            var submission = new WorkshopSubmission
            {
                Kind = _candidate.Kind,
                Name = name,
                Author = author,
                Description = description,
                Tags = TagsBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(tag => tag.Trim().ToLowerInvariant())
                    .Where(tag => tag.Length > 0)
                    .Distinct()
                    .Take(10)
                    .ToList(),
                License = (LicenseBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "CC-BY-4.0",
                Readme = ReadmeBox.Text.Trim(),
                ExistingId = ResolveExistingId()
            };

            _busy = true;
            SubmitButton.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            try
            {
                var progress = new Progress<WorkshopShareProgress>(p =>
                {
                    switch (p.Phase)
                    {
                        case WorkshopSharePhase.Uploading:
                            Progress.IsIndeterminate = p.BytesTotal <= 0;
                            Progress.Value = p.BytesTotal > 0 ? Math.Min(1, (double)p.BytesSent / p.BytesTotal) : 0;
                            StatusText.Text = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Uploading"), name);
                            break;
                        case WorkshopSharePhase.Submitting:
                            Progress.IsIndeterminate = true;
                            StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Submit");
                            break;
                        default:
                            Progress.IsIndeterminate = true;
                            StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Workshop_Installing");
                            break;
                    }
                });

                var receipt = await _share.ShareAsync(
                    _candidate,
                    submission,
                    string.IsNullOrWhiteSpace(PreviewBox.Text) ? null : PreviewBox.Text,
                    progress,
                    _cancel.Token);

                Progress.Visibility = Visibility.Collapsed;
                var message = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Submitted"), receipt.IssueUrl);
                StatusText.Text = message;
                _plugin.PlayniteApi?.Dialogs?.ShowMessage(
                    message,
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Sharing {_candidate.Kind} to the Workshop failed.");
                Progress.Visibility = Visibility.Collapsed;
                StatusText.Text = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message);
            }
            finally
            {
                _busy = false;
                SubmitButton.IsEnabled = true;
            }
        }

        private string ResolveExistingId()
        {
            if (ExistingBox.SelectedItem is ExistingOption option)
            {
                return option.ItemId;
            }

            var typed = ExistingBox.Text?.Trim().Trim('`', '/');
            return string.IsNullOrWhiteSpace(typed) ? null : typed;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cancel.Cancel();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Removes the rendered preview scratch folder; called by the host when the window closes.</summary>
        public void Cleanup()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_previewScratch) && Directory.Exists(_previewScratch))
                {
                    Directory.Delete(_previewScratch, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed cleaning the Workshop preview scratch folder.");
            }
        }
    }
}

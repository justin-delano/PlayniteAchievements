using Playnite.SDK;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private bool _rendering;
        private bool _userPickedPreview;
        private bool _closed;

        // The package built for the preview image, uploaded as is on submit; null until built.
        private string _packagePath;

        // The preview model the image was rendered from; disposed in Cleanup.
        private WorkshopPreviewModel _model;

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

            // The standardized preview image is rendered once the dialog is up (OnLoaded); the
            // user can still browse for a different image.
            _previewScratch = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "WorkshopPreview", Guid.NewGuid().ToString("N"));
            Loaded += OnLoaded;

            // Earlier submissions of the same kind whose Workshop id is known can be updated.
            var options = registry.Submissions
                .Where(s => s.Kind == candidate.Kind && !string.IsNullOrWhiteSpace(s.ItemId))
                .GroupBy(s => s.ItemId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ExistingOption { ItemId = g.Key, Label = $"{g.First().Name} ({g.Key})" })
                .ToList();
            ExistingBox.ItemsSource = options;
            _ = LoadOwnedItemsAsync(options);
        }

        public event EventHandler RequestClose;

        /// <summary>The file filter of the preview image picker.</summary>
        private const string PreviewImageFilter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp";

        private static string RenderingPreviewText => ResourceProvider.GetString("LOCPlayAch_Workshop_Share_RenderingPreview");

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await RenderPreviewAsync();
        }

        /// <summary>
        /// Builds the package once (kept for the submit), reads it into a preview model and renders
        /// the standardized preview image into the preview box. Submit waits for it unless the
        /// user picks an image of their own; a failed render leaves the box empty.
        /// </summary>
        private async Task RenderPreviewAsync()
        {
            _rendering = true;
            StatusText.Text = RenderingPreviewText;
            UpdateSubmitEnabled();
            try
            {
                var candidate = _candidate;
                var share = _share;
                var cancel = _cancel.Token;
                var packageDirectory = Path.Combine(_previewScratch, "package");
                var packagePath = await Task.Run(() => share.BuildPackage(candidate, packageDirectory), cancel);
                if (_closed)
                {
                    return;
                }

                _packagePath = packagePath;
                var context = WorkshopPreviewContext.FromPlugin(_plugin);
                var model = await Task.Run(() => WorkshopPreviewModelBuilder.Build(candidate.Kind, packagePath, context), cancel);
                if (_closed)
                {
                    model.Dispose();
                    return;
                }

                _model = model;
                var rendered = await new WorkshopPreviewRasterizer(_plugin, _logger)
                    .RenderAsync(model, Path.Combine(_previewScratch, "render"), cancel);
                if (!_closed && !_userPickedPreview && !string.IsNullOrWhiteSpace(rendered))
                {
                    PreviewBox.Text = rendered;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!_closed)
                {
                    _logger?.Warn(ex, $"Rendering the Workshop preview image for {_candidate.Kind} failed.");
                }
            }
            finally
            {
                _rendering = false;
                if (!_busy && string.Equals(StatusText.Text, RenderingPreviewText, StringComparison.Ordinal))
                {
                    StatusText.Text = string.Empty;
                }

                UpdateSubmitEnabled();
            }
        }

        // Submit waits for the rendered image unless the user picked one; never while submitting.
        private void UpdateSubmitEnabled()
        {
            SubmitButton.IsEnabled = !_busy && (!_rendering || _userPickedPreview);
        }

        private void BrowsePreview_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = PreviewImageFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                PreviewBox.Text = dialog.FileName;
                _userPickedPreview = true;
                if (!_busy && string.Equals(StatusText.Text, RenderingPreviewText, StringComparison.Ordinal))
                {
                    StatusText.Text = string.Empty;
                }

                UpdateSubmitEnabled();
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
                    _cancel.Token,
                    _packagePath);

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
                UpdateSubmitEnabled();
            }
        }

        /// <summary>
        /// Adds the user's published items of this kind to the update list. The index marks
        /// every item with its owner's hash, so anything shared from this install is found even
        /// when the local record does not know its id (a first submission has none until it is
        /// published), and matching records are linked to their id for next time.
        /// </summary>
        private async Task LoadOwnedItemsAsync(List<ExistingOption> options)
        {
            try
            {
                var client = _plugin.WorkshopClient;
                if (client == null)
                {
                    return;
                }

                var index = await client.FetchIndexAsync(_cancel.Token);
                var owner = _registry.GetSubmitterHash();
                var owned = index.Items
                    .Where(item => item.Kind == _candidate.Kind && string.Equals(item.OwnerHash, owner, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (owned.Count == 0)
                {
                    return;
                }

                _registry.LinkSubmissions(owned);
                foreach (var item in owned)
                {
                    if (options.Any(option => string.Equals(option.ItemId, item.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    options.Add(new ExistingOption { ItemId = item.Id, Label = $"{item.Name} ({item.Id})" });
                }

                var selected = ExistingBox.SelectedItem;
                ExistingBox.ItemsSource = options.OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
                ExistingBox.SelectedItem = selected;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Could not list this user's published Workshop items.");
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

        /// <summary>
        /// Stops a render still in progress, disposes the preview model and removes the preview
        /// scratch folder (the built package and the rendered image); called by the host when the
        /// window closes.
        /// </summary>
        public void Cleanup()
        {
            _closed = true;
            _cancel.Cancel();
            _model?.Dispose();
            _model = null;
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

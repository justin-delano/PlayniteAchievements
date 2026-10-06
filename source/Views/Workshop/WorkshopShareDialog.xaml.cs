using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
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
using System.Windows.Media.Imaging;
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
        private readonly WorkshopIdentityStore _identity;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private string _previewScratch;
        private bool _busy;
        private bool _rendering;
        private bool _closed;

        // The standardized preview image rendered from the package; null until rendered or when
        // rendering failed, in which case the submission goes without one.
        private string _previewPath;

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
            WorkshopIdentityStore identity)
            : this()
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
            _share = share ?? throw new ArgumentNullException(nameof(share));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));

            CandidateLabel.Text = candidate.Label;
            NameBox.Text = candidate.DefaultName ?? string.Empty;
            AuthorBox.Text = identity.DisplayName ?? string.Empty;
            CoverBox.Text = DefaultCoverPath(plugin, candidate, logger) ?? string.Empty;

            // The standardized preview image is rendered once the dialog is up (OnLoaded) and
            // always submitted with the package; it is not chosen by hand.
            _previewScratch = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "WorkshopPreview", Guid.NewGuid().ToString("N"));
            Loaded += OnLoaded;

            // Earlier submissions of the same kind whose Workshop id is known can be updated.
            var options = identity.Submissions
                .Where(s => s.Kind == candidate.Kind && !string.IsNullOrWhiteSpace(s.ItemId))
                .GroupBy(s => s.ItemId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ExistingOption { ItemId = g.Key, Label = $"{g.First().Name} ({g.Key})" })
                .ToList();
            ExistingBox.ItemsSource = options;
            _ = LoadOwnedItemsAsync(options);
        }

        public event EventHandler RequestClose;

        /// <summary>The file filter of the cover image picker.</summary>
        private const string CoverImageFilter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp";

        /// <summary>The largest cover image the Workshop accepts.</summary>
        private const long MaxCoverBytes = 5L * 1024 * 1024;

        private static string RenderingPreviewText =>ResourceProvider.GetString("LOCPlayAch_Workshop_Share_RenderingPreview");

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await RenderPreviewAsync();
        }

        /// <summary>
        /// Builds the package once (kept for the submit), reads it into a preview model and renders
        /// the standardized preview image. Submit waits for it; a failed render submits without an
        /// image.
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
                var plugin = _plugin;
                var model = await Task.Run(() =>
                {
                    context.GameDataSource = BuildOwnGameSource(plugin, candidate, context);
                    return WorkshopPreviewModelBuilder.Build(candidate.Kind, packagePath, context);
                }, cancel);
                if (_closed)
                {
                    model.Dispose();
                    return;
                }

                _model = model;
                var rendered = await new WorkshopPreviewRasterizer(_plugin, _logger)
                    .RenderAsync(model, Path.Combine(_previewScratch, "render"), cancel);
                if (!_closed && !string.IsNullOrWhiteSpace(rendered))
                {
                    _previewPath = rendered;
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

        /// <summary>
        /// For game data, the sharer's own game as the source the published image is rendered
        /// from, so it shows the achievement grid; no baseline, and no personal progress. Null for
        /// other kinds or when the game has no cached provider achievements, which renders the
        /// package's own entries instead.
        /// </summary>
        private static GameCustomDataPreviewSource BuildOwnGameSource(
            PlayniteAchievementsPlugin plugin,
            WorkshopShareCandidate candidate,
            WorkshopPreviewContext context)
        {
            if (candidate.Kind != WorkshopItemKind.GameCustomData || candidate.GameId == null)
            {
                return null;
            }

            var gameId = candidate.GameId.Value;
            var dataService = plugin.AchievementDataService;
            var raw = dataService?.GetRawGameAchievementData(gameId);
            if (raw?.Achievements == null || raw.Achievements.Count == 0)
            {
                return null;
            }

            GameCustomDataFile current = null;
            context.GameCustomDataStore?.TryLoad(gameId, out current);
            return new GameCustomDataPreviewSource
            {
                GameId = gameId,
                GameName = candidate.DefaultName,
                RawData = raw,
                CurrentData = dataService.GetGameAchievementData(gameId),
                Current = current,
                Persisted = plugin.Settings?.Persisted,
                ManagedCustomIconService = plugin.ManagedCustomIconService,
                HidePersonalProgress = true
            };
        }

        // Submit waits for the rendered image; never while submitting.
        private void UpdateSubmitEnabled()
        {
            SubmitButton.IsEnabled = !_busy && !_rendering;
        }

        private void BrowseCover_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = CoverImageFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (new FileInfo(dialog.FileName).Length > MaxCoverBytes)
            {
                StatusText.Text = ResourceProvider.GetString("LOCPlayAch_Workshop_Share_CoverTooLarge");
                return;
            }

            CoverBox.Text = dialog.FileName;
            if (!_busy && string.Equals(StatusText.Text, ResourceProvider.GetString("LOCPlayAch_Workshop_Share_CoverTooLarge"), StringComparison.Ordinal))
            {
                StatusText.Text = string.Empty;
            }
        }

        /// <summary>
        /// For game data, the Playnite game's own artwork as a starting cover: its background
        /// image, else its cover image, when that is a local file within the size limit. Null
        /// for other kinds or when the game has no usable artwork.
        /// </summary>
        private static string DefaultCoverPath(PlayniteAchievementsPlugin plugin, WorkshopShareCandidate candidate, ILogger logger)
        {
            if (candidate.Kind != WorkshopItemKind.GameCustomData || candidate.GameId == null)
            {
                return null;
            }

            try
            {
                var database = plugin.PlayniteApi?.Database;
                var game = database?.Games?.Get(candidate.GameId.Value);
                if (game == null)
                {
                    return null;
                }

                foreach (var image in new[] { game.BackgroundImage, game.CoverImage })
                {
                    if (string.IsNullOrWhiteSpace(image) || image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = database.GetFullFilePath(image);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length <= MaxCoverBytes)
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Could not resolve the game's artwork as a Workshop cover image.");
            }

            return null;
        }

        private void ClearCover_Click(object sender, RoutedEventArgs e)
        {
            CoverBox.Text = string.Empty;
        }

        /// <summary>
        /// Shows the chosen cover under its picker. The file is read fully into memory so it is
        /// not held open; an image WPF cannot decode (WebP without a codec) shows nothing but is
        /// still submitted.
        /// </summary>
        private void CoverBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CoverImage == null || CoverImageFrame == null)
            {
                return;
            }

            var path = CoverBox.Text;
            BitmapImage image = null;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelHeight = 320;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Could not show the cover image {path}.");
                    image = null;
                }
            }

            CoverImage.Source = image;
            CoverImageFrame.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
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
                    _previewPath,
                    progress,
                    _cancel.Token,
                    _packagePath,
                    string.IsNullOrWhiteSpace(CoverBox.Text) ? null : CoverBox.Text);

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
                var owner = _identity.GetSubmitterHash();
                var owned = index.Items
                    .Where(item => item.Kind == _candidate.Kind && string.Equals(item.OwnerHash, owner, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (owned.Count == 0)
                {
                    return;
                }

                _identity.LinkSubmissions(owned);
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

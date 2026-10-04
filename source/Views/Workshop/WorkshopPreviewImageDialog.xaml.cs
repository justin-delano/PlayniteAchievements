using Playnite.SDK;
using PlayniteAchievements.Services.Logging;
using PlayniteAchievements.Views.Helpers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
// WinForms dialog: the WPF Microsoft.Win32 picker renders legacy-style on .NET Framework.
using DialogResult = System.Windows.Forms.DialogResult;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace PlayniteAchievements.Views.Workshop
{
    /// <summary>
    /// Confirms the preview image to submit for a Workshop item this install published: shows the
    /// rendered image, lets the user browse for a different one, and returns the chosen path on
    /// Submit or null on Cancel.
    /// </summary>
    public partial class WorkshopPreviewImageDialog : UserControl
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(WorkshopPreviewImageDialog));

        private string _imagePath;

        public WorkshopPreviewImageDialog()
        {
            InitializeComponent();
        }

        private WorkshopPreviewImageDialog(string itemName, string imagePath)
            : this()
        {
            NameText.Text = itemName ?? string.Empty;
            SetImage(imagePath);
        }

        /// <summary>The image the user chose to submit; null until Submit.</summary>
        public string ChosenPath { get; private set; }

        public event EventHandler RequestClose;

        /// <summary>
        /// Shows the dialog modally and returns the image path to submit, or null when cancelled.
        /// <paramref name="imagePath"/> may be null, leaving the user to browse for an image.
        /// </summary>
        public static string Show(string itemName, string imagePath, Window owner = null)
        {
            var dialog = new WorkshopPreviewImageDialog(itemName, imagePath);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                ResourceProvider.GetString("LOCPlayAch_Workshop_UpdatePreview"),
                dialog,
                new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = true,
                    Width = 960,
                    Height = 760
                });

            try
            {
                if (window.Owner == null)
                {
                    window.Owner = owner ?? API.Instance?.Dialogs?.GetCurrentAppWindow();
                }
            }
            catch (InvalidOperationException)
            {
            }

            dialog.RequestClose += (sender, args) => window.Close();
            window.ShowDialog();
            return dialog.ChosenPath;
        }

        // Decoded fully up front, so the file is not held open. A picked format WPF cannot decode
        // (WebP without a codec) shows no image but can still be submitted.
        private void SetImage(string path)
        {
            _imagePath = null;
            PreviewImage.Source = null;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _imagePath = path;
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    PreviewImage.Source = image;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Could not show the Workshop preview image {path}.");
                }
            }

            SubmitButton.IsEnabled = _imagePath != null;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = WorkshopShareDialog.PreviewImageFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SetImage(dialog.FileName);
            }
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_imagePath == null)
            {
                return;
            }

            ChosenPath = _imagePath;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ChosenPath = null;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}

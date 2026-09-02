using Microsoft.Win32;
using Playnite.SDK.Events;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PlayniteAchievements.Views
{
    public partial class ManageAchievementsCustomTab : UserControl, IFullscreenControllerNavigable
    {
        private static readonly Regex HttpUrlRegex = new Regex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] SupportedImageExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff"
        };

        public ManageAchievementsCustomTab(ManageAchievementsCustomViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        private ManageAchievementsCustomViewModel ViewModel => DataContext as ManageAchievementsCustomViewModel;

        public void RefreshData()
        {
            ViewModel?.RefreshData();
        }

        private void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || button.ContextMenu == null)
            {
                return;
            }

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }

        private void RarityMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is MenuItem menuItem) ||
                !(menuItem.DataContext is CustomAchievementSelectionOption option))
            {
                return;
            }

            var menu = ItemsControl.ItemsControlFromItemContainer(menuItem) as ContextMenu;
            if ((menu?.PlacementTarget as FrameworkElement)?.DataContext is CustomAchievementEditItem row)
            {
                row.RarityInput = option.DisplayName;
            }
        }

        private void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveRowAndVariant(sender as FrameworkElement, out var row, out var variant))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SetIconPath(row, variant, dialog.FileName);
        }

        private void ClearIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveRowAndVariant(sender as FrameworkElement, out var row, out var variant))
            {
                return;
            }

            SetIconPath(row, variant, null);
            e.Handled = true;
        }

        private void IconTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            var hasDropPayload = TryGetFirstImageFilePath(e.Data, out _) || TryGetFirstBrowserUrl(e.Data, out _);
            e.Effects = hasDropPayload ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void IconTextBox_Drop(object sender, DragEventArgs e)
        {
            if (!TryResolveRowAndVariant(sender as FrameworkElement, out var row, out var variant))
            {
                return;
            }

            try
            {
                if (TryGetFirstImageFilePath(e.Data, out var imagePath))
                {
                    SetIconPath(row, variant, imagePath);
                    e.Handled = true;
                    return;
                }

                if (TryGetFirstBrowserUrl(e.Data, out var url))
                {
                    SetIconPath(row, variant, url);
                    e.Handled = true;
                }
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var candidate = BuildCandidateText(textBox, e?.Text);
            e.Handled = !IsValidNumericCandidate(candidate, textBox?.Tag as string);
        }

        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var textBox = sender as TextBox;
            var pastedText = e.DataObject?.GetData(typeof(string)) as string;
            if (!IsValidNumericCandidate(BuildCandidateText(textBox, pastedText), textBox?.Tag as string))
            {
                e.CancelCommand();
            }
        }

        public bool HandleFullscreenControllerInput(ControllerInput input)
        {
            if (CustomAchievementsGrid?.IsKeyboardFocusWithin != true)
            {
                return false;
            }

            if (FullscreenControllerNavigationService.IsFocusWithinDataGridColumnHeader(CustomAchievementsGrid))
            {
                if (FullscreenControllerNavigationService.IsAcceptInput(input))
                {
                    return FullscreenControllerNavigationService.ActivateFocusedDataGridColumnHeader(CustomAchievementsGrid);
                }
            }

            return false;
        }

        public IList<UIElement> GetControllerElements()
        {
            var elements = new List<UIElement>
            {
                AddButton,
                DuplicateButton,
                DeleteButton,
                ImportFileButton,
                ExportButton,
                RevertButton,
                ClearButton,
                SaveButton,
                CustomAchievementsGrid
            };

            return elements
                .Where(element => element != null && element.IsVisible && element.IsEnabled)
                .ToList();
        }

        private static bool TryResolveRowAndVariant(
            FrameworkElement element,
            out CustomAchievementEditItem row,
            out AchievementIconVariant variant)
        {
            row = element?.DataContext as CustomAchievementEditItem;
            variant = AchievementIconVariant.Unlocked;
            if (row == null)
            {
                return false;
            }

            var variantToken = (element as ButtonBase)?.CommandParameter as string;
            if (string.IsNullOrWhiteSpace(variantToken))
            {
                variantToken = element?.Tag as string;
            }

            if (string.Equals((variantToken ?? string.Empty).Trim(), "Locked", StringComparison.OrdinalIgnoreCase))
            {
                variant = AchievementIconVariant.Locked;
            }

            return true;
        }

        private static void SetIconPath(
            CustomAchievementEditItem row,
            AchievementIconVariant variant,
            string value)
        {
            if (row == null)
            {
                return;
            }

            if (variant == AchievementIconVariant.Locked)
            {
                row.LockedIconPath = value;
            }
            else
            {
                row.UnlockedIconPath = value;
            }
        }

        private static string BuildCandidateText(TextBox textBox, string input)
        {
            var current = textBox?.Text ?? string.Empty;
            input ??= string.Empty;
            var start = Math.Max(0, textBox?.SelectionStart ?? current.Length);
            var length = Math.Max(0, textBox?.SelectionLength ?? 0);
            if (start > current.Length)
            {
                start = current.Length;
            }

            if (start + length > current.Length)
            {
                length = current.Length - start;
            }

            return current.Remove(start, length).Insert(start, input);
        }

        private static bool IsValidNumericCandidate(string value, string mode)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            if (string.Equals(mode, "Percent", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.Count(c => c == '.') > 1 ||
                    !double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    return false;
                }

                return percent >= 0 && percent <= 100;
            }

            return normalized.All(char.IsDigit);
        }

        private static bool TryGetFirstImageFilePath(IDataObject data, out string imagePath)
        {
            imagePath = null;
            if (data == null)
            {
                return false;
            }

            try
            {
                if (!data.GetDataPresent(DataFormats.FileDrop))
                {
                    return false;
                }

                var files = data.GetData(DataFormats.FileDrop) as string[];
                imagePath = files?.FirstOrDefault(IsSupportedImageFile);
                return !string.IsNullOrWhiteSpace(imagePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetFirstBrowserUrl(IDataObject data, out string url)
        {
            url = null;
            if (data == null)
            {
                return false;
            }

            try
            {
                var text = ReadDroppedText(data, DataFormats.UnicodeText) ??
                           ReadDroppedText(data, DataFormats.Text) ??
                           ReadDroppedText(data, DataFormats.Html);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                var match = HttpUrlRegex.Match(text);
                if (!match.Success)
                {
                    return false;
                }

                url = TrimTrailingUrlPunctuation(match.Value);
                return !string.IsNullOrWhiteSpace(url);
            }
            catch
            {
                return false;
            }
        }

        private static string ReadDroppedText(IDataObject data, string format)
        {
            if (data == null || string.IsNullOrWhiteSpace(format))
            {
                return null;
            }

            try
            {
                if (!data.GetDataPresent(format))
                {
                    return null;
                }

                return data.GetData(format) as string;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSupportedImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path) ?? string.Empty;
            if (!SupportedImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string TrimTrailingUrlPunctuation(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('.', ',', ';', ')', ']', '}');
        }
    }
}

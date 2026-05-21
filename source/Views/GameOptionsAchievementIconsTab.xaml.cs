using Microsoft.Win32;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Playnite.SDK.Events;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views
{
    public partial class GameOptionsAchievementIconsTab : UserControl, IFullscreenControllerNavigable
    {
        private const double SmoothMouseWheelDivisor = 3.0;
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

        private readonly GameOptionsAchievementIconsViewModel _viewModel;
        private ScrollViewer _achievementCardsScrollViewer;
        private int _controllerCardTargetIndex;

        public GameOptionsAchievementIconsTab(GameOptionsAchievementIconsViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            InitializeComponent();
            _viewModel.IconOverridesSaved += ViewModel_IconOverridesSaved;
        }

        public event EventHandler IconOverridesSaved;

        public void RefreshData()
        {
            _viewModel?.RefreshData();
        }

        public void Cleanup()
        {
            if (_viewModel != null)
            {
                _viewModel.IconOverridesSaved -= ViewModel_IconOverridesSaved;
                _viewModel.Cleanup();
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
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

            await _viewModel.ApplyLocalFileOverrideAsync(row, variant, dialog.FileName);
        }

        private void ClearOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveRowAndVariant(sender as FrameworkElement, out var row, out var variant))
            {
                return;
            }

            row.ClearOverride(variant);
            e.Handled = true;
        }

        private void AchievementCardsList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (IsInteractiveElementHit(source))
            {
                return;
            }

            var itemContainer = VisualTreeHelpers.FindVisualParent<ListBoxItem>(source);
            var row = itemContainer?.DataContext as AchievementIconOverrideItem;
            if (row?.CanReveal != true)
            {
                return;
            }

            row.ToggleReveal();
            e.Handled = true;
        }

        private void AchievementCardsList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            EnsureAchievementCardsScrollViewer();
            if (_achievementCardsScrollViewer == null || _achievementCardsScrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            var nextOffset = _achievementCardsScrollViewer.VerticalOffset - (e.Delta / SmoothMouseWheelDivisor);
            nextOffset = Math.Max(0, Math.Min(_achievementCardsScrollViewer.ScrollableHeight, nextOffset));
            _achievementCardsScrollViewer.ScrollToVerticalOffset(nextOffset);
            e.Handled = true;
        }

        private void OverrideTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            var hasDropPayload = TryGetFirstImageFilePath(e.Data, out _) || TryGetFirstBrowserUrl(e.Data, out _);
            e.Effects = hasDropPayload ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void OverrideTextBox_Drop(object sender, DragEventArgs e)
        {
            if (!TryResolveRowAndVariant(sender as FrameworkElement, out var row, out var variant))
            {
                return;
            }

            try
            {
                if (TryGetFirstImageFilePath(e.Data, out var imagePath))
                {
                    e.Handled = true;
                    await _viewModel.ApplyLocalFileOverrideAsync(row, variant, imagePath);
                    return;
                }

                if (TryGetFirstBrowserUrl(e.Data, out var url))
                {
                    e.Handled = true;
                    if (variant == AchievementIconVariant.Locked)
                    {
                        row.LockedOverrideValue = url;
                    }
                    else
                    {
                        row.UnlockedOverrideValue = url;
                    }
                }
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void ViewModel_IconOverridesSaved(object sender, EventArgs e)
        {
            IconOverridesSaved?.Invoke(this, EventArgs.Empty);
        }

        private void EnsureAchievementCardsScrollViewer()
        {
            if (_achievementCardsScrollViewer != null)
            {
                return;
            }

            _achievementCardsScrollViewer = VisualTreeHelpers.FindVisualChild<ScrollViewer>(AchievementCardsList);
        }

        private static bool TryResolveRowAndVariant(
            FrameworkElement element,
            out AchievementIconOverrideItem row,
            out AchievementIconVariant variant)
        {
            row = element?.DataContext as AchievementIconOverrideItem;
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

        private static bool IsInteractiveElementHit(DependencyObject source)
        {
            return source is ButtonBase ||
                   source is TextBoxBase ||
                   source is ScrollBar ||
                   VisualTreeHelpers.FindVisualParent<ButtonBase>(source) != null ||
                   VisualTreeHelpers.FindVisualParent<TextBoxBase>(source) != null ||
                   VisualTreeHelpers.FindVisualParent<ScrollBar>(source) != null;
        }

        public bool HandleFullscreenControllerInput(ControllerInput input)
        {
            if (AchievementCardsList?.IsKeyboardFocusWithin != true)
            {
                return false;
            }

            if (FullscreenControllerNavigationService.TryGetVerticalDelta(input, out var delta))
            {
                return MoveControllerSelection(delta);
            }

            if (FullscreenControllerNavigationService.TryGetHorizontalDelta(input, out var horizontalDelta))
            {
                return MoveControllerCardTarget(horizontalDelta);
            }

            if (!FullscreenControllerNavigationService.IsAcceptInput(input) ||
                IsInteractiveElementHit(Keyboard.FocusedElement as DependencyObject))
            {
                return false;
            }

            var item = GetFocusedControllerItem();
            if (item?.CanReveal != true)
            {
                return false;
            }

            item.ToggleReveal();
            return true;
        }

        private bool MoveControllerSelection(int delta)
        {
            if (AchievementCardsList?.Items == null || AchievementCardsList.Items.Count == 0)
            {
                return false;
            }

            _controllerCardTargetIndex = GetFocusedCardTargetIndex();

            var index = AchievementCardsList.SelectedIndex;
            if (index < 0)
            {
                index = delta < 0 ? AchievementCardsList.Items.Count : -1;
            }

            var nextIndex = Math.Max(0, Math.Min(AchievementCardsList.Items.Count - 1, index + delta));
            if (nextIndex == index)
            {
                return false;
            }

            AchievementCardsList.SelectedIndex = nextIndex;
            var item = AchievementCardsList.Items[nextIndex];
            AchievementCardsList.ScrollIntoView(item);
            AchievementCardsList.UpdateLayout();

            var container = AchievementCardsList.ItemContainerGenerator.ContainerFromIndex(nextIndex) as ListBoxItem;
            return FocusCardTarget(container, _controllerCardTargetIndex) ||
                   FullscreenControllerNavigationService.FocusElement((UIElement)container ?? AchievementCardsList);
        }

        private AchievementIconOverrideItem GetFocusedControllerItem()
        {
            var focusedItem = FullscreenControllerNavigationService.FindAncestor<ListBoxItem>(
                Keyboard.FocusedElement as DependencyObject);
            if (focusedItem?.DataContext is AchievementIconOverrideItem row)
            {
                return row;
            }

            return AchievementCardsList?.SelectedItem as AchievementIconOverrideItem;
        }

        public IList<UIElement> GetControllerElements()
        {
            return new UIElement[]
                {
                    RevertChangesButton,
                    ClearAllButton,
                    SaveButton,
                    OpenIconsFolderButton,
                    AchievementCardsList
                }
                .Where(element => element != null && element.IsVisible && element.IsEnabled)
                .ToList();
        }

        private bool MoveControllerCardTarget(int delta)
        {
            var container = GetFocusedControllerContainer();
            var targets = GetCardTargets(container);
            if (targets.Count == 0)
            {
                return false;
            }

            var currentIndex = GetFocusedCardTargetIndex(targets);
            if (currentIndex < 0)
            {
                currentIndex = Math.Max(0, Math.Min(targets.Count - 1, _controllerCardTargetIndex));
            }

            var nextIndex = Math.Max(0, Math.Min(targets.Count - 1, currentIndex + delta));
            _controllerCardTargetIndex = nextIndex;
            return FullscreenControllerNavigationService.FocusElement(targets[nextIndex]);
        }

        private bool FocusCardTarget(ListBoxItem container, int targetIndex)
        {
            var targets = GetCardTargets(container);
            if (targets.Count == 0)
            {
                return false;
            }

            var nextIndex = Math.Max(0, Math.Min(targets.Count - 1, targetIndex));
            _controllerCardTargetIndex = nextIndex;
            return FullscreenControllerNavigationService.FocusElement(targets[nextIndex]);
        }

        private int GetFocusedCardTargetIndex()
        {
            return GetFocusedCardTargetIndex(GetCardTargets(GetFocusedControllerContainer()));
        }

        private static int GetFocusedCardTargetIndex(IList<UIElement> targets)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (targets == null || focused == null)
            {
                return -1;
            }

            for (var i = 0; i < targets.Count; i++)
            {
                if (ReferenceEquals(targets[i], focused) ||
                    FullscreenControllerNavigationService.IsDescendantOf(focused, targets[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private ListBoxItem GetFocusedControllerContainer()
        {
            var focusedItem = FullscreenControllerNavigationService.FindAncestor<ListBoxItem>(
                Keyboard.FocusedElement as DependencyObject);
            if (focusedItem != null &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(focusedItem), AchievementCardsList))
            {
                return focusedItem;
            }

            var index = AchievementCardsList?.SelectedIndex ?? -1;
            if (index < 0 && AchievementCardsList?.Items.Count > 0)
            {
                index = 0;
                AchievementCardsList.SelectedIndex = index;
            }

            if (AchievementCardsList == null || index < 0)
            {
                return null;
            }

            AchievementCardsList.ScrollIntoView(AchievementCardsList.Items[index]);
            AchievementCardsList.UpdateLayout();
            return AchievementCardsList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
        }

        private static IList<UIElement> GetCardTargets(ListBoxItem container)
        {
            var targets = new List<UIElement>();
            if (container == null || !container.IsVisible || !container.IsEnabled)
            {
                return targets;
            }

            targets.Add(container);
            targets.AddRange(
                FullscreenControllerNavigationService.GetVisibleDescendantElements<UIElement>(container)
                    .Where(element => element is TextBoxBase || element is ButtonBase));

            return targets.Distinct().ToList();
        }
    }
}

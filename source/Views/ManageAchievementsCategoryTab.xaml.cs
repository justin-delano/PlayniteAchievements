using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK;
using Playnite.SDK.Events;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views
{
    public partial class ManageAchievementsCategoryTab : UserControl, IFullscreenControllerNavigable
    {
        private const string CategoryDragDataFormat = "PlayniteAchievements.ManageAchievementsCategoryRows";
        private const double AutoScrollEdgeThreshold = 64;
        private const double AutoScrollMinStep = 2;
        private const double AutoScrollVariableStep = 14;
        private const double AutoScrollMaxFactor = 3;
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

        private DataGridRow _pendingRightClickRow;
        private Point _categoryDragStartPoint;
        private bool _hasCategoryDragStartPoint;
        private ManageAchievementsCategoryMetadataItem _categoryDragAnchorItem;
        private ScrollViewer _categoryManagerScrollViewer;
        private readonly DispatcherTimer _categoryAutoScrollTimer;
        private bool _isCategoryDragging;
        private int _categoryDragItemCount;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        public ManageAchievementsCategoryTab(ManageAchievementsCategoryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _categoryAutoScrollTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _categoryAutoScrollTimer.Tick += CategoryAutoScrollTimer_Tick;
        }

        private ManageAchievementsCategoryViewModel ViewModel => DataContext as ManageAchievementsCategoryViewModel;

        internal void SelectManageCategoriesSubTab()
        {
            if (CategorySubTabs != null)
            {
                CategorySubTabs.SelectedIndex = 1;
            }
        }

        private void ApplyBulkButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyBulk();
        }

        private void ClearSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            ClearSelected();
        }

        private void TypeSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || TypeSelectionContextMenu == null || TypeSelectionButton == null)
            {
                return;
            }

            OpenCategoryTypeContextMenu(
                TypeSelectionButton,
                TypeSelectionContextMenu,
                ViewModel.TypeSelectionOptions);
        }

        private void FilterTypeSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || FilterTypeSelectionContextMenu == null || FilterTypeSelectionButton == null)
            {
                return;
            }

            OpenCategoryTypeContextMenu(
                FilterTypeSelectionButton,
                FilterTypeSelectionContextMenu,
                ViewModel.TypeFilterOptions);
        }

        private void CategoryLabelFilterSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null ||
                CategoryLabelFilterSelectionButton == null ||
                CategoryLabelFilterSelectionContextMenu == null)
            {
                return;
            }

            OpenMultiSelectFilterContextMenu(
                CategoryLabelFilterSelectionButton,
                CategoryLabelFilterSelectionContextMenu,
                ViewModel.CategoryLabelFilterOptions,
                option => ViewModel.IsCategoryLabelFilterSelected(option),
                (option, isSelected) => ViewModel.SetCategoryLabelFilterSelected(option, isSelected),
                AchievementCategoryTypeHelper.ToCategoryLabelDisplayText);
        }

        private void CategoryInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyBulk();
                e.Handled = true;
            }
        }

        private void ApplyBulk()
        {
            if (ViewModel == null)
            {
                return;
            }

            var selectedRows = ViewModel.GetAllSelectedRows();
            if (selectedRows.Count == 0)
            {
                return;
            }

            var applied = ViewModel.ApplyBulkToSelection(selectedRows, CategoryInputTextBox.Text);
            if (applied)
            {
                CategoryInputTextBox.Text = string.Empty;
                ViewModel.ResetBulkEditorInputs();
                ViewModel.ClearAllSelections();
            }
        }

        private void ClearSelected()
        {
            if (ViewModel == null)
            {
                return;
            }

            var selectedRows = ViewModel.GetAllSelectedRows();
            if (selectedRows.Count == 0)
            {
                return;
            }

            var cleared = ViewModel.ClearSelectionOverrides(selectedRows);
            if (cleared)
            {
                CategoryInputTextBox.Text = string.Empty;
                ViewModel.ResetBulkEditorInputs();
            }
        }

        private void CategoryRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyCategoryRenameOverride(sender as TextBox);
        }

        private void CategoryRenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            var textBox = sender as TextBox;
            ApplyCategoryRenameOverride(textBox);
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void ApplyCategoryRenameOverride(TextBox textBox)
        {
            if (ViewModel == null || !(textBox?.DataContext is ManageAchievementsCategoryMetadataItem row))
            {
                return;
            }

            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            ViewModel.ApplyCategoryRenameOverride(row);
        }

        private async void BrowseCategoryImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveCategoryImageRowAndKind(sender as FrameworkElement, out var row, out var kind))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                await ViewModel.ApplyCategoryLocalFileOverrideAsync(row, kind, dialog.FileName);
            }
        }

        private void ClearCategoryImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveCategoryImageRowAndKind(sender as FrameworkElement, out var row, out var kind))
            {
                return;
            }

            row.ClearOverride(kind);
            e.Handled = true;
        }

        private void CategoryImageTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            var hasDropPayload = TryGetFirstImageFilePath(e.Data, out _) || TryGetFirstBrowserUrl(e.Data, out _);
            e.Effects = hasDropPayload ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void CategoryImageTextBox_Drop(object sender, DragEventArgs e)
        {
            if (!TryResolveCategoryImageRowAndKind(sender as FrameworkElement, out var row, out var kind))
            {
                return;
            }

            try
            {
                if (TryGetFirstImageFilePath(e.Data, out var imagePath))
                {
                    e.Handled = true;
                    await ViewModel.ApplyCategoryLocalFileOverrideAsync(row, kind, imagePath);
                    return;
                }

                if (TryGetFirstBrowserUrl(e.Data, out var url))
                {
                    e.Handled = true;
                    row.SetOverrideValue(kind, url);
                }
            }
            catch
            {
                e.Handled = true;
            }
        }

        private static bool TryResolveCategoryImageRowAndKind(
            FrameworkElement element,
            out ManageAchievementsCategoryMetadataItem row,
            out CategoryImageKind kind)
        {
            row = element?.DataContext as ManageAchievementsCategoryMetadataItem;
            kind = CategoryImageKind.Icon;
            if (row == null)
            {
                return false;
            }

            var token = (element as ButtonBase)?.CommandParameter as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                token = element?.Tag as string;
            }

            if (string.Equals((token ?? string.Empty).Trim(), "Cover", StringComparison.OrdinalIgnoreCase))
            {
                kind = CategoryImageKind.Cover;
            }

            return true;
        }

        private void CategoryDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (source == null)
            {
                return;
            }

            if (source is CheckBox || VisualTreeHelpers.FindVisualParent<CheckBox>(source) != null)
            {
                return;
            }

            var row = VisualTreeHelpers.FindVisualParent<DataGridRow>(source);
            if (!(row?.DataContext is ManageAchievementsCategoryItem item))
            {
                return;
            }

            ViewModel?.ToggleReveal(item);

            // Selection is controlled by the checkbox column only.
            e.Handled = true;
        }

        private void CategoryManagerDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _hasCategoryDragStartPoint = false;
            _categoryDragAnchorItem = null;

            var row = VisualTreeHelpers.FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (!(row?.DataContext is ManageAchievementsCategoryMetadataItem clickedItem))
            {
                return;
            }

            if (!IsDragColumnHit(e.OriginalSource as DependencyObject))
            {
                return;
            }

            _categoryDragStartPoint = e.GetPosition(CategoryManagerDataGrid);
            _hasCategoryDragStartPoint = true;
            _categoryDragAnchorItem = clickedItem;

            var hasSelectionModifier = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            if (row.IsSelected &&
                CategoryManagerDataGrid.SelectedItems.Count > 1 &&
                !hasSelectionModifier)
            {
                e.Handled = true;
            }
        }

        private void CategoryManagerDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _hasCategoryDragStartPoint = false;
            _categoryDragAnchorItem = null;
        }

        private void CategoryManagerDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_hasCategoryDragStartPoint || e.LeftButton != MouseButtonState.Pressed || ViewModel == null)
            {
                return;
            }

            var currentPos = e.GetPosition(CategoryManagerDataGrid);
            var delta = currentPos - _categoryDragStartPoint;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var clickedItem = _categoryDragAnchorItem;
            if (clickedItem == null)
            {
                var row = VisualTreeHelpers.FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
                clickedItem = row?.DataContext as ManageAchievementsCategoryMetadataItem;
            }

            if (clickedItem == null)
            {
                return;
            }

            var selectedRows = CategoryManagerDataGrid.SelectedItems
                .OfType<ManageAchievementsCategoryMetadataItem>()
                .ToList();
            if (!selectedRows.Contains(clickedItem))
            {
                selectedRows = new List<ManageAchievementsCategoryMetadataItem> { clickedItem };
            }

            var sourceOrder = ViewModel.CategoryRows.ToList();
            var draggedLabels = selectedRows
                .OrderBy(item => sourceOrder.IndexOf(item))
                .Select(item => item.CategoryLabel)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToList();
            if (draggedLabels.Count == 0)
            {
                return;
            }

            var dragData = new DataObject(CategoryDragDataFormat, draggedLabels);
            _hasCategoryDragStartPoint = false;
            _categoryDragAnchorItem = null;
            _categoryDragItemCount = draggedLabels.Count;
            _isCategoryDragging = true;
            ShowCategoryDragCountPopup();
            StartCategoryAutoScroll();

            try
            {
                DragDrop.DoDragDrop(CategoryManagerDataGrid, dragData, DragDropEffects.Move);
            }
            finally
            {
                StopCategoryAutoScroll();
                _isCategoryDragging = false;
                _categoryDragItemCount = 0;
                HideCategoryDragCountPopup();
                HideCategoryDropIndicator();
            }
        }

        private void CategoryManagerDataGrid_DragOver(object sender, DragEventArgs e)
        {
            var hasValidDragData = e.Data.GetDataPresent(CategoryDragDataFormat);
            e.Effects = hasValidDragData ? DragDropEffects.Move : DragDropEffects.None;
            if (hasValidDragData)
            {
                EnsureCategoryManagerScrollViewer();
                UpdateCategoryDropIndicator(e);
            }
            else
            {
                HideCategoryDropIndicator();
            }

            e.Handled = true;
        }

        private void CategoryManagerDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            var pointerPosition = Mouse.GetPosition(CategoryManagerDataGrid);
            if (!IsPointWithinCategoryManagerGrid(pointerPosition))
            {
                HideCategoryDropIndicator();
            }
        }

        private void CategoryManagerDataGrid_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (!_isCategoryDragging)
            {
                return;
            }

            UpdateCategoryDragCountPopupPosition();
            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        private void CategoryManagerDataGrid_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (e.Action == DragAction.Continue)
            {
                ApplyCategoryAutoScrollFromCursor();
                return;
            }

            _isCategoryDragging = false;
            _categoryDragItemCount = 0;
            _hasCategoryDragStartPoint = false;
            _categoryDragAnchorItem = null;
            StopCategoryAutoScroll();
            HideCategoryDragCountPopup();
            HideCategoryDropIndicator();
        }

        private void CategoryManagerDataGrid_Drop(object sender, DragEventArgs e)
        {
            HideCategoryDropIndicator();
            if (ViewModel == null || !e.Data.GetDataPresent(CategoryDragDataFormat))
            {
                HideCategoryDragCountPopup();
                return;
            }

            var draggedLabels = (e.Data.GetData(CategoryDragDataFormat) as IEnumerable<string>)?.ToList();
            if (draggedLabels == null || draggedLabels.Count == 0)
            {
                HideCategoryDragCountPopup();
                return;
            }

            var row = VisualTreeHelpers.FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            var moved = false;
            if (row?.DataContext is ManageAchievementsCategoryMetadataItem targetItem)
            {
                var pos = e.GetPosition(row);
                var insertAfter = pos.Y > row.ActualHeight / 2.0;
                moved = ViewModel.MoveCategoryRowsByLabel(draggedLabels, targetItem.CategoryLabel, insertAfter);
            }
            else
            {
                moved = ViewModel.MoveCategoryRowsToEndByLabel(draggedLabels);
            }

            if (moved)
            {
                RestoreCategoryManagerSelectionByLabels(draggedLabels);
                _isCategoryDragging = false;
                _categoryDragItemCount = 0;
                StopCategoryAutoScroll();
                HideCategoryDragCountPopup();
                e.Handled = true;
            }
        }

        private void CategoryDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                e.Handled = true;
                _pendingRightClickRow = row;
            }
        }

        private void CategoryDataGridRow_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                e.Handled = true;
                var targetRow = _pendingRightClickRow ?? row;
                _pendingRightClickRow = null;
                OpenContextMenuForRow(targetRow);
            }
        }

        public bool HandleFullscreenControllerInput(ControllerInput input)
        {
            if (CategoryManagerDataGrid?.IsKeyboardFocusWithin == true)
            {
                if (FullscreenControllerNavigationService.IsFocusWithinDataGridColumnHeader(CategoryManagerDataGrid))
                {
                    if (FullscreenControllerNavigationService.IsAcceptInput(input))
                    {
                        return FullscreenControllerNavigationService.ActivateFocusedDataGridColumnHeader(CategoryManagerDataGrid);
                    }

                    return false;
                }
            }

            if (CategoryDataGrid?.IsKeyboardFocusWithin != true)
            {
                return false;
            }

            if (FullscreenControllerNavigationService.IsFocusWithinDataGridColumnHeader(CategoryDataGrid))
            {
                if (FullscreenControllerNavigationService.IsAcceptInput(input))
                {
                    return FullscreenControllerNavigationService.ActivateFocusedDataGridColumnHeader(CategoryDataGrid);
                }

                return false;
            }

            if (FullscreenControllerNavigationService.IsSecondaryClickInput(input))
            {
                return TryOpenSelectedRowContextMenu();
            }

            return false;
        }

        public IList<UIElement> GetControllerElements()
        {
            var elements = new List<UIElement>
            {
                CategorySubTabs,
                ResetCategoryButton,
                SearchTextBox,
                ClearSearchButton,
                FilterTypeSelectionButton,
                CategoryLabelFilterSelectionButton,
                SelectAllButton,
                DeselectAllButton,
                CategoryDataGrid,
                BulkEditExpander
            };

            if (BulkEditExpander?.IsExpanded == true)
            {
                elements.Add(TypeSelectionButton);
                elements.Add(ClearSelectedButton);
                elements.Add(CategoryInputTextBox);
                elements.Add(ApplyBulkButton);
            }

            if (CategorySubTabs?.SelectedIndex == 1)
            {
                elements.Add(RevertCategoryImagesButton);
                elements.Add(ClearCategoryImagesButton);
                elements.Add(SaveCategoryImagesButton);
                elements.Add(ResetCategoryMetadataButton);
                elements.Add(OpenCategoryImagesFolderButton);
                elements.Add(CategoryManagerDataGrid);
            }

            return elements
                .Where(IsControllerElementAvailable)
                .ToList();
        }

        private static bool IsControllerElementAvailable(UIElement element)
        {
            if (element == null || !element.IsVisible || !element.IsEnabled)
            {
                return false;
            }

            if (element is Button button &&
                ReferenceEquals(button.Style, button.TryFindResource("ClearSearchButtonStyle")))
            {
                return !string.IsNullOrEmpty(button.Tag as string);
            }

            return true;
        }

        private bool TryActivateSelectedRow()
        {
            if (FullscreenControllerNavigationService.FindAncestor<ButtonBase>(
                    Keyboard.FocusedElement as DependencyObject) != null)
            {
                return false;
            }

            var item = CategoryDataGrid?.SelectedItem as ManageAchievementsCategoryItem
                       ?? CategoryDataGrid?.CurrentItem as ManageAchievementsCategoryItem;
            if (item == null || !item.CanReveal)
            {
                return false;
            }

            ViewModel?.ToggleReveal(item);
            return true;
        }

        private bool TryOpenSelectedRowContextMenu()
        {
            var row = GetControllerTargetRow();
            if (row == null)
            {
                return false;
            }

            return OpenContextMenuForRow(row, useControllerPlacement: true);
        }

        private DataGridRow GetControllerTargetRow()
        {
            var focusedRow = VisualTreeHelpers.FindVisualParent<DataGridRow>(
                Keyboard.FocusedElement as DependencyObject);
            if (focusedRow != null &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(focusedRow), CategoryDataGrid))
            {
                return focusedRow;
            }

            var index = CategoryDataGrid?.SelectedIndex ?? -1;
            if (index < 0 && CategoryDataGrid?.Items.Count > 0)
            {
                index = 0;
                CategoryDataGrid.SelectedIndex = index;
            }

            if (CategoryDataGrid == null || index < 0)
            {
                return null;
            }

            CategoryDataGrid.UpdateLayout();
            var row = CategoryDataGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
            if (row == null)
            {
                CategoryDataGrid.ScrollIntoView(CategoryDataGrid.Items[index]);
                CategoryDataGrid.UpdateLayout();
                row = CategoryDataGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
            }

            return row;
        }

        private bool OpenContextMenuForRow(DataGridRow row, bool useControllerPlacement = false)
        {
            if (!(row?.DataContext is ManageAchievementsCategoryItem item))
            {
                return false;
            }

            var menu = BuildRowContextMenu(item);
            if (menu == null || menu.Items.Count == 0)
            {
                return false;
            }

            ContextMenuStyleHelper.ApplyAchievementContextMenuStyle(this, menu);
            row.ContextMenu = menu;
            if (useControllerPlacement)
            {
                return FullscreenControllerNavigationService.OpenContextMenu(row, menu);
            }

            menu.PlacementTarget = row;
            menu.IsOpen = true;
            return true;
        }

        private ContextMenu BuildRowContextMenu(ManageAchievementsCategoryItem contextItem)
        {
            var menu = new ContextMenu();

            var addTypeMenu = new MenuItem
            {
                Header = L("LOCPlayAch_Common_AddType", "Add Type")
            };
            foreach (var categoryType in AchievementCategoryTypeHelper.AssignableCategoryTypes)
            {
                var capturedType = categoryType;
                addTypeMenu.Items.Add(CreateMenuItem(
                    ManageAchievementsCategoryViewModel.GetCategoryTypeDisplayName(capturedType),
                    () => AddTypeFromContext(contextItem, capturedType)));
            }
            menu.Items.Add(addTypeMenu);

            menu.Items.Add(CreateMenuItem(
                L("LOCPlayAch_Common_SetLabelEllipsis", "Set Label..."),
                () => SetLabelFromContext(contextItem)));

            menu.Items.Add(CreateMenuItem(
                L("LOCPlayAch_Button_Clear", "Clear"),
                () => ClearRowsFromContext(contextItem)));

            return menu;
        }

        private MenuItem CreateMenuItem(string header, Action onClick)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, __) => onClick?.Invoke();
            return item;
        }

        private void AddTypeFromContext(ManageAchievementsCategoryItem contextItem, string categoryType)
        {
            if (ViewModel == null)
            {
                return;
            }

            var rows = ResolveActionRows(contextItem);
            if (rows.Count == 0)
            {
                return;
            }

            ViewModel.AddCategoryTypesToSelection(rows, new[] { categoryType });
        }

        private void SetLabelFromContext(ManageAchievementsCategoryItem contextItem)
        {
            if (ViewModel == null)
            {
                return;
            }

            var rows = ResolveActionRows(contextItem);
            if (rows.Count == 0)
            {
                return;
            }

            var inputText = contextItem?.CategoryDisplay ?? string.Empty;
            var inputDialog = new TextInputDialog(
                L(
                    "LOCPlayAch_ManageAchievements_Category_Context_SetLabelHint",
                    "Enter a category label for the selected achievements."),
                inputText);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                L(
                    "LOCPlayAch_ManageAchievements_Category_Context_SetLabelTitle",
                    "Set Category Label"),
                inputDialog,
                new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = false,
                    Width = 500,
                    Height = 200
                });

            inputDialog.RequestClose += (s, e) => window.Close();
            window.ShowDialog();

            if (inputDialog.DialogResult != true)
            {
                return;
            }

            inputText = (inputDialog.InputText ?? string.Empty).Trim();
            ViewModel.SetCategoryLabelForSelection(rows, inputText);
        }

        private void ClearRowsFromContext(ManageAchievementsCategoryItem contextItem)
        {
            if (ViewModel == null)
            {
                return;
            }

            var rows = ResolveActionRows(contextItem);
            if (rows.Count == 0)
            {
                return;
            }

            var cleared = ViewModel.ClearSelectionOverrides(rows);
            if (cleared)
            {
                CategoryInputTextBox.Text = string.Empty;
                ViewModel.ResetBulkEditorInputs();
            }
        }

        private List<ManageAchievementsCategoryItem> ResolveActionRows(ManageAchievementsCategoryItem contextItem)
        {
            if (ViewModel == null)
            {
                return new List<ManageAchievementsCategoryItem>();
            }

            var selectedRows = ViewModel.GetAllSelectedRows();
            if (selectedRows.Count == 0)
            {
                return contextItem == null
                    ? new List<ManageAchievementsCategoryItem>()
                    : new List<ManageAchievementsCategoryItem> { contextItem };
            }

            if (contextItem == null || contextItem.IsSelected)
            {
                return selectedRows;
            }

            return new List<ManageAchievementsCategoryItem> { contextItem };
        }

        private void RowSelectionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ManageAchievementsCategoryItem item)
            {
                item.IsSelected = checkBox.IsChecked == true;
            }

            e.Handled = true;
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllSelectableRows(selected: true);
            e.Handled = true;
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllSelectableRows(selected: false);
            e.Handled = true;
        }

        private void ResetCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            var reset = ViewModel.ResetCategoryOverrides();
            if (reset)
            {
                CategoryInputTextBox.Text = string.Empty;
                ViewModel.ResetBulkEditorInputs();
            }

            e.Handled = true;
        }

        private void ResetCategoryMetadataButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.ResetCategoryMetadata() == true)
            {
                HideCategoryDropIndicator();
                HideCategoryDragCountPopup();
            }

            e.Handled = true;
        }

        private void SetAllSelectableRows(bool selected)
        {
            if (ViewModel == null)
            {
                return;
            }

            foreach (var item in ViewModel.AchievementRows.Where(i => i != null))
            {
                item.IsSelected = selected;
            }
        }

        private void EnsureCategoryManagerScrollViewer()
        {
            if (_categoryManagerScrollViewer != null)
            {
                return;
            }

            _categoryManagerScrollViewer = VisualTreeHelpers.FindVisualChild<ScrollViewer>(CategoryManagerDataGrid);
        }

        private void StartCategoryAutoScroll()
        {
            EnsureCategoryManagerScrollViewer();
            if (!_categoryAutoScrollTimer.IsEnabled)
            {
                _categoryAutoScrollTimer.Start();
            }
        }

        private void StopCategoryAutoScroll()
        {
            if (_categoryAutoScrollTimer.IsEnabled)
            {
                _categoryAutoScrollTimer.Stop();
            }
        }

        private void CategoryAutoScrollTimer_Tick(object sender, EventArgs e)
        {
            ApplyCategoryAutoScrollFromCursor();
        }

        private void ApplyCategoryAutoScrollFromCursor()
        {
            if (!_isCategoryDragging || _categoryManagerScrollViewer == null)
            {
                return;
            }

            if (!TryGetCursorPositionInCategoryGrid(out var cursorPosition))
            {
                return;
            }

            var delta = CalculateAutoScrollDelta(cursorPosition.Y, CategoryManagerDataGrid.ActualHeight);
            if (Math.Abs(delta) < 0.01)
            {
                return;
            }

            var currentOffset = _categoryManagerScrollViewer.VerticalOffset;
            var nextOffset = Math.Max(
                0,
                Math.Min(_categoryManagerScrollViewer.ScrollableHeight, currentOffset + delta));
            if (Math.Abs(nextOffset - currentOffset) > 0.01)
            {
                _categoryManagerScrollViewer.ScrollToVerticalOffset(nextOffset);
            }
        }

        private bool TryGetCursorPositionInCategoryGrid(out Point position)
        {
            if (!GetCursorPos(out var cursorPoint))
            {
                position = default;
                return false;
            }

            position = CategoryManagerDataGrid.PointFromScreen(new Point(cursorPoint.X, cursorPoint.Y));
            return true;
        }

        private static double CalculateAutoScrollDelta(double pointerY, double gridHeight)
        {
            if (gridHeight <= 0)
            {
                return 0;
            }

            if (pointerY < AutoScrollEdgeThreshold)
            {
                var pressure = (AutoScrollEdgeThreshold - pointerY) / AutoScrollEdgeThreshold;
                var factor = Math.Min(AutoScrollMaxFactor, Math.Max(0, pressure));
                return -(AutoScrollMinStep + (factor * AutoScrollVariableStep));
            }

            var lowerEdge = gridHeight - AutoScrollEdgeThreshold;
            if (pointerY > lowerEdge)
            {
                var pressure = (pointerY - lowerEdge) / AutoScrollEdgeThreshold;
                var factor = Math.Min(AutoScrollMaxFactor, Math.Max(0, pressure));
                return AutoScrollMinStep + (factor * AutoScrollVariableStep);
            }

            return 0;
        }

        private void UpdateCategoryDropIndicator(DragEventArgs e)
        {
            var row = VisualTreeHelpers.FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.DataContext is ManageAchievementsCategoryMetadataItem)
            {
                var pointerInRow = e.GetPosition(row);
                var insertAfter = pointerInRow.Y > row.ActualHeight / 2.0;
                var rowTop = row.TranslatePoint(new Point(0, 0), CategoryManagerDataGrid).Y;
                var lineY = insertAfter ? rowTop + row.ActualHeight : rowTop;
                ShowCategoryDropIndicator(lineY);
                return;
            }

            if (ViewModel?.CategoryRows?.Count > 0)
            {
                ShowCategoryDropIndicator(CategoryManagerDataGrid.ActualHeight - 1);
            }
            else
            {
                HideCategoryDropIndicator();
            }
        }

        private void ShowCategoryDropIndicator(double y)
        {
            if (double.IsNaN(y))
            {
                HideCategoryDropIndicator();
                return;
            }

            var maxTop = Math.Max(0, CategoryManagerDataGrid.ActualHeight - CategoryDropInsertLine.Height);
            var top = Math.Max(0, Math.Min(maxTop, y - (CategoryDropInsertLine.Height / 2.0)));
            CategoryDropInsertLine.Margin = new Thickness(0, top, 0, 0);
            CategoryDropInsertLine.Visibility = Visibility.Visible;
        }

        private void HideCategoryDropIndicator()
        {
            if (CategoryDropInsertLine != null)
            {
                CategoryDropInsertLine.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowCategoryDragCountPopup()
        {
            if (_categoryDragItemCount <= 0)
            {
                return;
            }

            CategoryDragCountText.Text = _categoryDragItemCount.ToString();
            CategoryDragCountPopup.IsOpen = true;
            UpdateCategoryDragCountPopupPosition();
        }

        private void HideCategoryDragCountPopup()
        {
            if (CategoryDragCountPopup?.IsOpen == true)
            {
                CategoryDragCountPopup.IsOpen = false;
            }
        }

        private void UpdateCategoryDragCountPopupPosition()
        {
            if (!GetCursorPos(out var cursorPoint))
            {
                return;
            }

            CategoryDragCountPopup.HorizontalOffset = cursorPoint.X + 18;
            CategoryDragCountPopup.VerticalOffset = cursorPoint.Y + 18;
        }

        private bool IsPointWithinCategoryManagerGrid(Point point)
        {
            return point.X >= 0 &&
                   point.Y >= 0 &&
                   point.X <= CategoryManagerDataGrid.ActualWidth &&
                   point.Y <= CategoryManagerDataGrid.ActualHeight;
        }

        private void RestoreCategoryManagerSelectionByLabels(IEnumerable<string> labels)
        {
            if (CategoryManagerDataGrid == null)
            {
                return;
            }

            var selectedLabels = new HashSet<string>(
                (labels ?? Enumerable.Empty<string>())
                    .Select(AchievementCategoryTypeHelper.NormalizeCategoryOrDefault)
                    .Where(label => !string.IsNullOrWhiteSpace(label)),
                StringComparer.OrdinalIgnoreCase);
            CategoryManagerDataGrid.SelectedItems.Clear();
            if (selectedLabels.Count == 0 || ViewModel?.CategoryRows == null)
            {
                return;
            }

            foreach (var row in ViewModel.CategoryRows)
            {
                if (!string.IsNullOrWhiteSpace(row?.CategoryLabel) && selectedLabels.Contains(row.CategoryLabel))
                {
                    CategoryManagerDataGrid.SelectedItems.Add(row);
                }
            }
        }

        private static bool IsDragColumnHit(DependencyObject source)
        {
            var cell = VisualTreeHelpers.FindVisualParent<DataGridCell>(source);
            return cell?.Column?.DisplayIndex == 0;
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
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
                return data.GetDataPresent(format)
                    ? data.GetData(format) as string
                    : null;
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

        private static void OpenSelectorContextMenu(Button button, ContextMenu menu)
        {
            if (button == null || menu == null)
            {
                return;
            }

            RoutedEventHandler onClosed = null;
            onClosed = (_, __) =>
            {
                menu.Closed -= onClosed;
                button.ReleaseMouseCapture();
            };

            menu.Closed += onClosed;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            if (button.IsKeyboardFocusWithin)
            {
                FullscreenControllerNavigationService.OpenContextMenu(button, menu);
            }
            else
            {
                menu.IsOpen = true;
            }
        }

        private static void OpenCategoryTypeContextMenu(
            Button button,
            ContextMenu menu,
            IEnumerable<CategoryTypeSelectionOption> options)
        {
            if (button == null || menu == null)
            {
                return;
            }

            menu.Items.Clear();

            var itemStyle = button.TryFindResource("AchievementMultiSelectMenuItemStyle") as Style;
            foreach (var option in options ?? Enumerable.Empty<CategoryTypeSelectionOption>())
            {
                if (option == null)
                {
                    continue;
                }

                var item = new MenuItem
                {
                    Header = option.DisplayName,
                    IsCheckable = true,
                    StaysOpenOnClick = true,
                    IsChecked = option.IsSelected
                };
                if (itemStyle != null)
                {
                    item.Style = itemStyle;
                }

                item.Click += (_, __) => option.IsSelected = item.IsChecked;
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
            {
                return;
            }

            OpenSelectorContextMenu(button, menu);
        }

        private static void OpenMultiSelectFilterContextMenu(
            Button button,
            ContextMenu menu,
            IEnumerable<string> options,
            Func<string, bool> isSelected,
            Action<string, bool> setSelection,
            Func<string, string> displayText = null)
        {
            if (button == null || menu == null || isSelected == null || setSelection == null)
            {
                return;
            }

            menu.Items.Clear();
            if (options == null)
            {
                return;
            }

            var itemStyle = button.TryFindResource("AchievementMultiSelectMenuItemStyle") as Style;
            foreach (var option in options.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var item = new MenuItem
                {
                    Header = displayText?.Invoke(option) ?? option,
                    IsCheckable = true,
                    StaysOpenOnClick = true,
                    IsChecked = isSelected(option)
                };
                if (itemStyle != null)
                {
                    item.Style = itemStyle;
                }
                item.Click += (_, __) => setSelection(option, item.IsChecked);
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
            {
                return;
            }

            OpenSelectorContextMenu(button, menu);
        }
    }
}

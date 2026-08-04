using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Showcase
{
    public partial class ShowcaseControl : UserControl, IDisposable
    {
        private const string WidgetDragFormat = "PlayniteAchievements.Showcase.Widget";
        private readonly OverviewViewModel _overview;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Action _persist;
        private readonly IPlayniteAPI _api;
        private bool _updatingPageSelector;
        private bool _publishingConfigurationChange;
        private bool _disposed;
        private Point _dragStart;
        private string _selectedBlockId;

        internal ShowcaseControl(
            OverviewViewModel overview,
            PlayniteAchievementsSettings settings,
            Action persist,
            IPlayniteAPI api)
        {
            InitializeComponent();
            _overview = overview ?? throw new ArgumentNullException(nameof(overview));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _persist = persist ?? throw new ArgumentNullException(nameof(persist));
            _api = api;
            _overview.SnapshotChanged += Overview_SnapshotChanged;
            ShowcaseConfigurationEvents.Changed += ShowcaseConfigurationEvents_Changed;
            EnsureLayout();
            Rebuild();
        }

        public void FocusInitialTarget()
        {
            PageSelector?.Focus();
        }

        public bool MovePage(int direction)
        {
            var pages = Layout.Pages;
            if (pages.Count <= 1)
            {
                return false;
            }

            var index = Math.Max(0, pages.IndexOf(CurrentPage));
            var target = (index + Math.Sign(direction) + pages.Count) % pages.Count;
            SelectPage(pages[target]);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _overview.SnapshotChanged -= Overview_SnapshotChanged;
            ShowcaseConfigurationEvents.Changed -= ShowcaseConfigurationEvents_Changed;
        }

        private ShowcaseSettings Layout => _settings.Persisted.Showcase;

        private ShowcasePageSettings CurrentPage =>
            Layout.Pages.FirstOrDefault(page =>
                string.Equals(
                    page.PageId,
                    Layout.LastSelectedPageId,
                    StringComparison.OrdinalIgnoreCase)) ??
            Layout.Pages.First();

        private void EnsureLayout()
        {
            ShowcaseLayoutService.Normalize(Layout);
        }

        private void Rebuild()
        {
            if (_disposed)
            {
                return;
            }

            EnsureLayout();
            UpdatePageSelector();
            BuildDashboard();
        }

        private void UpdatePageSelector()
        {
            _updatingPageSelector = true;
            PageSelector.ItemsSource = null;
            PageSelector.ItemsSource = Layout.Pages;
            PageSelector.SelectedItem = CurrentPage;
            var index = Layout.Pages.IndexOf(CurrentPage);
            PageCountText.Text = $"{index + 1} / {Layout.Pages.Count}";
            PreviousPageButton.IsEnabled = Layout.Pages.Count > 1;
            NextPageButton.IsEnabled = Layout.Pages.Count > 1;
            _updatingPageSelector = false;
        }

        private void BuildDashboard()
        {
            DashboardGrid.Children.Clear();
            DashboardGrid.RowDefinitions.Clear();
            DashboardGrid.ColumnDefinitions.Clear();
            for (var index = 0; index < ShowcaseLayoutService.GridSize; index++)
            {
                DashboardGrid.RowDefinitions.Add(
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                DashboardGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var snapshot = _overview.LatestSnapshot ?? new OverviewDataSnapshot();
            if (EditLayoutButton.IsChecked == true &&
                !CurrentPage.Blocks.Any(block => string.Equals(
                    block.BlockId,
                    _selectedBlockId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                _selectedBlockId = CurrentPage.Blocks.FirstOrDefault()?.BlockId;
            }

            foreach (var block in CurrentPage.Blocks)
            {
                var container = CreateBlockContainer(block, snapshot);
                Grid.SetRow(container, block.Row);
                Grid.SetColumn(container, block.Column);
                Grid.SetRowSpan(container, block.RowSpan);
                Grid.SetColumnSpan(container, block.ColumnSpan);
                DashboardGrid.Children.Add(container);
            }

            UpdateEditTools();
        }

        private FrameworkElement CreateBlockContainer(
            ShowcaseBlockSettings block,
            OverviewDataSnapshot snapshot)
        {
            var border = new Border
            {
                Margin = new Thickness(4),
                AllowDrop = true,
                Tag = block,
                Focusable = EditLayoutButton.IsChecked == true,
                BorderThickness = EditLayoutButton.IsChecked == true
                    ? new Thickness(2)
                    : new Thickness(0),
                Padding = EditLayoutButton.IsChecked == true
                    ? new Thickness(2)
                    : new Thickness(0)
            };
            border.SetResourceReference(Border.CornerRadiusProperty, "PlayAch.Radius.Section");
            border.SetResourceReference(
                Border.BorderBrushProperty,
                EditLayoutButton.IsChecked == true && string.Equals(
                    block.BlockId,
                    _selectedBlockId,
                    StringComparison.OrdinalIgnoreCase)
                    ? "PlayAch.Brush.Accent"
                    : "PlayAch.Brush.Border");
            border.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.GridSurface");
            border.Drop += Block_Drop;
            border.DragOver += Block_DragOver;
            border.PreviewMouseLeftButtonDown += Block_PreviewMouseLeftButtonDown;
            border.PreviewMouseMove += Block_PreviewMouseMove;
            border.GotKeyboardFocus += Block_GotKeyboardFocus;

            var widget = FindWidget(block.WidgetInstanceId);
            if (widget == null)
            {
                var emptyContent = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var addGlyph = new TextBlock
                {
                    Text = "\uE710",
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.72
                };
                addGlyph.SetResourceReference(TextBlock.FontFamilyProperty, "PlayAch.FontFamily.Icon");
                addGlyph.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Accent");
                emptyContent.Children.Add(addGlyph);
                var addLabel = new TextBlock
                {
                    Text = Localize("LOCPlayAch_Showcase_AddWidget", "Add Widget"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0),
                    Opacity = 0.78
                };
                addLabel.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
                emptyContent.Children.Add(addLabel);
                var add = new Button
                {
                    Content = emptyContent,
                    Tag = block,
                    Margin = new Thickness(8),
                    BorderThickness = new Thickness(0),
                    Visibility = EditLayoutButton.IsChecked == true
                        ? Visibility.Visible
                        : Visibility.Collapsed
                };
                add.SetResourceReference(Control.BackgroundProperty, "PlayAch.Brush.Overlay.Tint.08");
                add.Click += AddWidgetButton_Click;
                border.Child = add;
                return border;
            }

            var host = new ShowcaseWidgetControl();
            host.Apply(ShowcaseWidgetProjectionService.Build(snapshot, Layout, widget));
            if (EditLayoutButton.IsChecked == true)
            {
                host.ContextMenu = BuildPlacedWidgetMenu(block, widget);
                host.ToolTip = Localize(
                    "LOCPlayAch_Showcase_WidgetEditHint",
                    "Drag to swap. Right-click for widget actions.");
            }

            border.Child = host;
            return border;
        }

        private ContextMenu BuildPlacedWidgetMenu(
            ShowcaseBlockSettings block,
            ShowcaseWidgetInstanceSettings widget)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_WidgetSettings", "Widget settings"),
                () => OpenWidgetSettings(widget)));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_ReplaceWidget", "Replace widget"),
                () => OpenWidgetPicker(block, null)));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_DeleteWidget", "Delete widget"),
                () =>
                {
                    ShowcaseLayoutService.DeleteWidget(Layout, widget.InstanceId);
                    SaveAndRebuild();
                }));
            return menu;
        }

        private void AddWidgetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ShowcaseBlockSettings block)
            {
                OpenWidgetPicker(block, button);
            }
        }

        private void OpenWidgetPicker(ShowcaseBlockSettings block, FrameworkElement target)
        {
            var menu = new ContextMenu();
            foreach (var definition in ShowcaseWidgetCatalog.Definitions)
            {
                var captured = definition;
                var item = WidgetPickerItem(
                    captured,
                    () =>
                    {
                        var widget = ShowcaseLayoutService.CreateWidget(Layout, captured.Kind);
                        if (!ShowcaseLayoutService.PlaceWidget(
                                Layout,
                                CurrentPage.PageId,
                                block.BlockId,
                                widget.InstanceId))
                        {
                            ShowcaseLayoutService.DeleteWidget(Layout, widget.InstanceId);
                            return;
                        }

                        SaveAndRebuild();
                    });
                item.IsEnabled = !captured.SingleInstancePerPage ||
                    !CurrentPage.Blocks
                        .Select(candidate => FindWidget(candidate.WidgetInstanceId))
                        .Any(candidate => candidate?.Kind == captured.Kind);
                menu.Items.Add(item);
            }

            menu.PlacementTarget = target ?? DashboardSurface;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void OpenMergePicker(
            Button target,
            string firstBlockId,
            string secondBlockId)
        {
            var closure = ShowcaseLayoutService.GetMergeClosure(
                Layout,
                CurrentPage.PageId,
                firstBlockId,
                secondBlockId);
            var widgets = closure
                .Select(block => FindWidget(block.WidgetInstanceId))
                .Where(widget => widget != null)
                .GroupBy(widget => widget.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (widgets.Count <= 1)
            {
                if (ShowcaseLayoutService.TryMergeWithFallback(
                        Layout,
                        CurrentPage.PageId,
                        firstBlockId,
                        secondBlockId,
                        widgets.FirstOrDefault()?.InstanceId))
                {
                    SaveAndRebuild();
                }

                return;
            }

            var menu = new ContextMenu
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom
            };
            foreach (var widget in widgets)
            {
                var capturedWidget = widget;
                menu.Items.Add(MenuItem(
                    string.Format(
                        Localize(
                            "LOCPlayAch_Showcase_MergeKeepWidget",
                            "Keep {0} and delete the others"),
                        GetWidgetName(capturedWidget)),
                    () => MergeBlocks(
                        firstBlockId,
                        secondBlockId,
                        capturedWidget.InstanceId)));
            }

            menu.IsOpen = true;
        }

        private void MergeBlocks(
            string firstBlockId,
            string secondBlockId,
            string preferredWidgetInstanceId)
        {
            if (ShowcaseLayoutService.TryMergeWithFallback(
                    Layout,
                    CurrentPage.PageId,
                    firstBlockId,
                    secondBlockId,
                    preferredWidgetInstanceId))
            {
                SaveAndRebuild();
            }
        }

        private void Block_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            if (EditLayoutButton.IsChecked == true &&
                sender is Border border &&
                border.Tag is ShowcaseBlockSettings block &&
                !string.Equals(_selectedBlockId, block.BlockId, StringComparison.OrdinalIgnoreCase))
            {
                SelectBlock(block);
            }
        }

        private void Block_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (EditLayoutButton.IsChecked == true &&
                sender is Border border &&
                border.Tag is ShowcaseBlockSettings block)
            {
                SelectBlock(block);
            }
        }

        private void SelectBlock(ShowcaseBlockSettings block)
        {
            if (block == null)
            {
                return;
            }

            _selectedBlockId = block.BlockId;
            foreach (var container in DashboardGrid.Children.OfType<Border>())
            {
                var candidate = container.Tag as ShowcaseBlockSettings;
                container.SetResourceReference(
                    Border.BorderBrushProperty,
                    candidate != null && string.Equals(
                        candidate.BlockId,
                        _selectedBlockId,
                        StringComparison.OrdinalIgnoreCase)
                        ? "PlayAch.Brush.Accent"
                        : "PlayAch.Brush.Border");
            }

            UpdateEditTools();
        }

        private void Block_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (EditLayoutButton.IsChecked != true ||
                e.LeftButton != MouseButtonState.Pressed ||
                !(sender is Border border) ||
                !(border.Tag is ShowcaseBlockSettings block) ||
                string.IsNullOrWhiteSpace(block.WidgetInstanceId))
            {
                return;
            }

            var current = e.GetPosition(null);
            if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            DragDrop.DoDragDrop(
                border,
                new DataObject(WidgetDragFormat, block.WidgetInstanceId),
                DragDropEffects.Move);
        }

        private void Block_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = EditLayoutButton.IsChecked == true &&
                e.Data.GetDataPresent(WidgetDragFormat)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
            e.Handled = true;
        }

        private void Block_Drop(object sender, DragEventArgs e)
        {
            if (EditLayoutButton.IsChecked != true ||
                !(sender is Border border) ||
                !(border.Tag is ShowcaseBlockSettings block) ||
                !(e.Data.GetData(WidgetDragFormat) is string instanceId))
            {
                return;
            }

            if (ShowcaseLayoutService.PlaceWidget(
                    Layout,
                    CurrentPage.PageId,
                    block.BlockId,
                    instanceId))
            {
                SaveAndRebuild();
            }

            e.Handled = true;
        }

        private void SelectPage(ShowcasePageSettings page)
        {
            if (page == null)
            {
                return;
            }

            Layout.LastSelectedPageId = page.PageId;
            SaveAndRebuild();
        }

        private void SaveAndRebuild()
        {
            ShowcaseLayoutService.Normalize(Layout);
            ShowcaseLayoutService.PruneOrphanedWidgets(Layout);
            _persist();
            _publishingConfigurationChange = true;
            try
            {
                ShowcaseConfigurationEvents.RaiseChanged();
            }
            finally
            {
                _publishingConfigurationChange = false;
            }

            Rebuild();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Rebuild();
        }

        private void Overview_SnapshotChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(BuildDashboard));
        }

        private void ShowcaseConfigurationEvents_Changed(object sender, EventArgs e)
        {
            if (!_publishingConfigurationChange)
            {
                Dispatcher.BeginInvoke(new Action(Rebuild));
            }
        }

        private void PageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingPageSelector && PageSelector.SelectedItem is ShowcasePageSettings page)
            {
                SelectPage(page);
            }
        }

        private void PreviousPageButton_Click(object sender, RoutedEventArgs e) => MovePage(-1);

        private void NextPageButton_Click(object sender, RoutedEventArgs e) => MovePage(1);

        private void EditLayoutButton_Changed(object sender, RoutedEventArgs e)
        {
            if (EditLayoutButton.IsChecked == true && string.IsNullOrWhiteSpace(_selectedBlockId))
            {
                _selectedBlockId = CurrentPage.Blocks.FirstOrDefault()?.BlockId;
            }

            BuildDashboard();
        }

        private ShowcaseBlockSettings SelectedBlock => CurrentPage.Blocks.FirstOrDefault(block =>
            string.Equals(block.BlockId, _selectedBlockId, StringComparison.OrdinalIgnoreCase));

        private void UpdateEditTools()
        {
            var editing = EditLayoutButton.IsChecked == true;
            EditToolsPanel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (!editing)
            {
                return;
            }

            var block = SelectedBlock;
            var widget = FindWidget(block?.WidgetInstanceId);
            SelectedBlockText.Text = block == null
                ? Localize("LOCPlayAch_Showcase_SelectBlock", "Select a block")
                : string.Format(
                    Localize("LOCPlayAch_Showcase_SelectedBlockFormat", "{0} · {1}×{2}"),
                    widget == null
                        ? Localize("LOCPlayAch_Showcase_EmptyBlock", "Empty block")
                        : GetWidgetName(widget),
                    block.ColumnSpan,
                    block.RowSpan);
            WidgetActionButton.Content = widget == null
                ? Localize("LOCPlayAch_Showcase_AddWidget", "Add widget")
                : Localize("LOCPlayAch_Showcase_ChangeWidget", "Change widget");
            WidgetActionButton.IsEnabled = block != null;
            WidgetSettingsButton.IsEnabled = widget != null;
            DeleteWidgetButton.IsEnabled = widget != null;
            SplitColumnsButton.IsEnabled = block?.ColumnSpan > 1;
            SplitRowsButton.IsEnabled = block?.RowSpan > 1;
            MergeLeftButton.IsEnabled = HasAdjacentBlock(block, 0, -1);
            MergeRightButton.IsEnabled = HasAdjacentBlock(block, 0, 1);
            MergeUpButton.IsEnabled = HasAdjacentBlock(block, -1, 0);
            MergeDownButton.IsEnabled = HasAdjacentBlock(block, 1, 0);
        }

        private void WidgetActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedBlock != null)
            {
                OpenWidgetPicker(SelectedBlock, WidgetActionButton);
            }
        }

        private void WidgetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var widget = FindWidget(SelectedBlock?.WidgetInstanceId);
            if (widget != null)
            {
                OpenWidgetSettings(widget);
            }
        }

        private void DeleteWidgetButton_Click(object sender, RoutedEventArgs e)
        {
            var widget = FindWidget(SelectedBlock?.WidgetInstanceId);
            if (widget != null)
            {
                ShowcaseLayoutService.DeleteWidget(Layout, widget.InstanceId);
                SaveAndRebuild();
            }
        }

        private void SplitColumnsButton_Click(object sender, RoutedEventArgs e) =>
            OpenSplitPicker(SplitColumnsButton, vertical: true);

        private void SplitRowsButton_Click(object sender, RoutedEventArgs e) =>
            OpenSplitPicker(SplitRowsButton, vertical: false);

        private void MergeLeftButton_Click(object sender, RoutedEventArgs e) =>
            OpenDirectionalMerge(MergeLeftButton, 0, -1);

        private void MergeRightButton_Click(object sender, RoutedEventArgs e) =>
            OpenDirectionalMerge(MergeRightButton, 0, 1);

        private void MergeUpButton_Click(object sender, RoutedEventArgs e) =>
            OpenDirectionalMerge(MergeUpButton, -1, 0);

        private void MergeDownButton_Click(object sender, RoutedEventArgs e) =>
            OpenDirectionalMerge(MergeDownButton, 1, 0);

        private void OpenSplitPicker(Button target, bool vertical)
        {
            var block = SelectedBlock;
            if (block == null)
            {
                return;
            }

            var start = vertical ? block.Column : block.Row;
            var span = vertical ? block.ColumnSpan : block.RowSpan;
            var lines = Enumerable.Range(start + 1, Math.Max(0, span - 1)).ToList();
            if (lines.Count == 1)
            {
                SplitSelectedBlock(vertical, lines[0]);
                return;
            }

            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var line in lines)
            {
                var captured = line;
                menu.Items.Add(MenuItem(
                    string.Format(
                        Localize("LOCPlayAch_Showcase_SplitAtFormat", "Split {0} / {1}"),
                        line - start,
                        span - (line - start)),
                    () => SplitSelectedBlock(vertical, captured)));
            }

            menu.IsOpen = true;
        }

        private void SplitSelectedBlock(bool vertical, int gridLine)
        {
            var block = SelectedBlock;
            if (block != null && ShowcaseLayoutService.TrySplit(
                    Layout,
                    CurrentPage.PageId,
                    block.BlockId,
                    vertical,
                    gridLine))
            {
                SaveAndRebuild();
            }
        }

        private bool HasAdjacentBlock(ShowcaseBlockSettings block, int rowDirection, int columnDirection) =>
            FindAdjacentBlocks(block, rowDirection, columnDirection).Count > 0;

        private List<ShowcaseBlockSettings> FindAdjacentBlocks(
            ShowcaseBlockSettings block,
            int rowDirection,
            int columnDirection)
        {
            if (block == null)
            {
                return new List<ShowcaseBlockSettings>();
            }

            return CurrentPage.Blocks
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, block))
                .Where(candidate =>
                    rowDirection < 0
                        ? candidate.Row + candidate.RowSpan == block.Row &&
                          RangesOverlap(candidate.Column, candidate.ColumnSpan, block.Column, block.ColumnSpan)
                        : rowDirection > 0
                            ? block.Row + block.RowSpan == candidate.Row &&
                              RangesOverlap(candidate.Column, candidate.ColumnSpan, block.Column, block.ColumnSpan)
                            : columnDirection < 0
                                ? candidate.Column + candidate.ColumnSpan == block.Column &&
                                  RangesOverlap(candidate.Row, candidate.RowSpan, block.Row, block.RowSpan)
                                : block.Column + block.ColumnSpan == candidate.Column &&
                                  RangesOverlap(candidate.Row, candidate.RowSpan, block.Row, block.RowSpan))
                .OrderByDescending(candidate => rowDirection == 0
                    ? Overlap(candidate.Row, candidate.RowSpan, block.Row, block.RowSpan)
                    : Overlap(candidate.Column, candidate.ColumnSpan, block.Column, block.ColumnSpan))
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .ToList();
        }

        private void OpenDirectionalMerge(Button target, int rowDirection, int columnDirection)
        {
            var block = SelectedBlock;
            var adjacent = FindAdjacentBlocks(block, rowDirection, columnDirection);
            if (block == null || adjacent.Count == 0)
            {
                return;
            }

            if (adjacent.Count == 1)
            {
                OpenMergePicker(target, block.BlockId, adjacent[0].BlockId);
                return;
            }

            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var candidate in adjacent)
            {
                var captured = candidate;
                var widget = FindWidget(candidate.WidgetInstanceId);
                var label = widget == null
                    ? Localize("LOCPlayAch_Showcase_EmptyBlock", "Empty block")
                    : GetWidgetName(widget);
                menu.Items.Add(MenuItem(
                    string.Format(
                        Localize("LOCPlayAch_Showcase_MergeWithFormat", "Merge with {0}"),
                        label),
                    () => OpenMergePicker(target, block.BlockId, captured.BlockId)));
            }

            menu.IsOpen = true;
        }

        private static bool RangesOverlap(int firstStart, int firstSpan, int secondStart, int secondSpan) =>
            firstStart < secondStart + secondSpan && secondStart < firstStart + firstSpan;

        private static int Overlap(int firstStart, int firstSpan, int secondStart, int secondSpan) =>
            Math.Max(0, Math.Min(firstStart + firstSpan, secondStart + secondSpan) - Math.Max(firstStart, secondStart));

        private void PageActionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = PageActionsButton,
                Placement = PlacementMode.Bottom
            };
            var add = new MenuItem
            {
                Header = Localize("LOCPlayAch_Showcase_AddPage", "Add page")
            };
            add.Items.Add(PageTemplateItem(ShowcasePageTemplate.Blank));
            add.Items.Add(PageTemplateItem(ShowcasePageTemplate.Analytics));
            add.Items.Add(PageTemplateItem(ShowcasePageTemplate.Collection));
            menu.Items.Add(add);
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_DuplicatePage", "Duplicate page"),
                () =>
                {
                    ShowcaseLayoutService.DuplicatePage(
                        Layout,
                        CurrentPage.PageId,
                        Localize("LOCPlayAch_Showcase_CopySuffix", "Copy"));
                    SaveAndRebuild();
                }));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_RenamePage", "Rename page"),
                RenameCurrentPage));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_EditProfile", "Edit profile"),
                OpenProfileSettings));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_MovePageLeft", "Move page left"),
                () =>
                {
                    ShowcaseLayoutService.MovePage(Layout, CurrentPage.PageId, -1);
                    SaveAndRebuild();
                }));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_MovePageRight", "Move page right"),
                () =>
                {
                    ShowcaseLayoutService.MovePage(Layout, CurrentPage.PageId, 1);
                    SaveAndRebuild();
                }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_ResetPage", "Reset page"),
                () =>
                {
                    if (!Confirm(
                            "LOCPlayAch_Showcase_ResetPageConfirm",
                            "Reset this page? Its current widgets and layout will be deleted."))
                    {
                        return;
                    }

                    ShowcaseLayoutService.ResetPage(Layout, CurrentPage.PageId);
                    SaveAndRebuild();
                }));
            var delete = MenuItem(
                Localize("LOCPlayAch_Showcase_DeletePage", "Delete page"),
                DeleteCurrentPage);
            delete.IsEnabled = Layout.Pages.Count > 1;
            menu.Items.Add(delete);
            menu.IsOpen = true;
        }

        private MenuItem PageTemplateItem(ShowcasePageTemplate template)
        {
            return MenuItem(
                Localize(
                    $"LOCPlayAch_Showcase_Template_{template}",
                    template.ToString()),
                () =>
                {
                    ShowcaseLayoutService.AddPage(
                        Layout,
                        template,
                        Localize(
                            $"LOCPlayAch_Showcase_Template_{template}",
                            template.ToString()));
                    SaveAndRebuild();
                });
        }

        private void RenameCurrentPage()
        {
            var result = _api?.Dialogs?.SelectString(
                Localize("LOCPlayAch_Showcase_RenamePrompt", "Enter a page name:"),
                Localize("LOCPlayAch_Showcase_RenamePage", "Rename page"),
                CurrentPage.Name);
            var name = result?.Result == true ? result.SelectedString : null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                ShowcaseLayoutService.RenamePage(Layout, CurrentPage.PageId, name);
                SaveAndRebuild();
            }
        }

        private void OpenWidgetSettings(ShowcaseWidgetInstanceSettings widget)
        {
            if (ShowcaseWidgetSettingsDialog.Show(widget, Layout))
            {
                SaveAndRebuild();
            }
        }

        private void OpenProfileSettings()
        {
            var profileWidget = Layout.WidgetInstances.FirstOrDefault(widget =>
                widget?.Kind == ShowcaseWidgetKind.Profile);
            if (profileWidget == null)
            {
                profileWidget = new ShowcaseWidgetInstanceSettings
                {
                    Kind = ShowcaseWidgetKind.Profile
                };
            }

            if (ShowcaseWidgetSettingsDialog.Show(profileWidget, Layout))
            {
                SaveAndRebuild();
            }
        }

        private void DeleteCurrentPage()
        {
            if (Layout.Pages.Count <= 1 ||
                !Confirm(
                    "LOCPlayAch_Showcase_DeletePageConfirm",
                    "Delete this page and all widgets on it?"))
            {
                return;
            }

            ShowcaseLayoutService.DeletePage(Layout, CurrentPage.PageId);
            SaveAndRebuild();
        }

        private bool Confirm(string messageKey, string fallback)
        {
            return _api?.Dialogs?.ShowMessage(
                       Localize(messageKey, fallback),
                       Localize("LOCPlayAch_Showcase_Title", "Showcase"),
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private ShowcaseWidgetInstanceSettings FindWidget(string instanceId)
        {
            return Layout.WidgetInstances.FirstOrDefault(widget =>
                string.Equals(widget.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }

        private string GetWidgetName(ShowcaseWidgetInstanceSettings widget)
        {
            if (!string.IsNullOrWhiteSpace(widget?.CustomTitle))
            {
                return widget.CustomTitle;
            }

            return widget == null
                ? Localize("LOCPlayAch_Showcase_Widget", "Widget")
                : Localize(
                    ShowcaseWidgetCatalog.Get(widget.Kind).NameKey,
                    Humanize(widget.Kind));
        }

        private static MenuItem MenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, __) => action();
            return item;
        }

        private static MenuItem WidgetPickerItem(
            ShowcaseWidgetDefinition definition,
            Action action)
        {
            var item = new MenuItem();
            item.Click += (_, __) => action();
            var panel = new StackPanel { Margin = new Thickness(2, 1, 8, 2) };
            var name = new TextBlock
            {
                Text = Localize(definition.NameKey, Humanize(definition.Kind)),
                FontWeight = FontWeights.SemiBold
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            panel.Children.Add(name);
            var description = new TextBlock
            {
                Text = Localize(definition.DescriptionKey, string.Empty),
                MaxWidth = 320,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.68
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            panel.Children.Add(description);
            item.Header = panel;
            return item;
        }

        private static string Localize(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(value, key, StringComparison.Ordinal) ||
                   (value.StartsWith("<!", StringComparison.Ordinal) &&
                    value.EndsWith("!>", StringComparison.Ordinal))
                ? fallback
                : value;
        }

        private static string Humanize(ShowcaseWidgetKind kind)
        {
            return kind == ShowcaseWidgetKind.NativePoints
                ? "Native Points"
                : kind == ShowcaseWidgetKind.PinnedAchievements
                    ? "Pinned Achievements"
                    : kind == ShowcaseWidgetKind.FavoriteGames
                        ? "Favorite Games"
                        : kind == ShowcaseWidgetKind.IconMosaic
                            ? "Icon Mosaic"
                            : kind == ShowcaseWidgetKind.ScreenshotSlideshow
                                ? "Screenshot Slideshow"
                            : kind == ShowcaseWidgetKind.Statistics
                                ? "Overall Statistics"
                                : kind.ToString();
        }

    }
}

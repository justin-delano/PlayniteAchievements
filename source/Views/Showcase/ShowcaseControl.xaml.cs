using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;
using static PlayniteAchievements.Services.Showcase.ShowcaseGeometry;
using static PlayniteAchievements.Views.Showcase.ShowcaseUiText;

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
        private string _layoutSignature;
        private Point _dragStart;
        private string _selectedBlockId;
        private string _dragSourceBlockId;
        private readonly Dictionary<string, BlockVisualState> _blockVisuals =
            new Dictionary<string, BlockVisualState>(StringComparer.OrdinalIgnoreCase);

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

            ClearDragVisuals();
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
            if (_disposed)
            {
                return;
            }

            DashboardGrid.Children.Clear();
            _blockVisuals.Clear();
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

            _layoutSignature = ComputeLayoutSignature();
            UpdateEditTools();
        }

        // Captures everything that forces block containers to be recreated: the page set, the
        // current page's block partition, and which widget instance (and kind) each block hosts.
        // Widget options and custom titles are deliberately excluded - Apply() refreshes those in
        // place through the projection without discarding the visual tree.
        private string ComputeLayoutSignature()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var page in Layout.Pages)
            {
                builder.Append(page.PageId).Append('|').Append(page.Name).Append(';');
            }

            var current = CurrentPage;
            builder.Append('#').Append(current.PageId);
            foreach (var block in current.Blocks)
            {
                builder.Append('#')
                    .Append(block.BlockId).Append(',')
                    .Append(block.Row).Append(',')
                    .Append(block.Column).Append(',')
                    .Append(block.RowSpan).Append(',')
                    .Append(block.ColumnSpan).Append(',')
                    .Append(block.WidgetInstanceId ?? string.Empty);
                var widget = Layout.WidgetInstances.FirstOrDefault(instance =>
                    string.Equals(instance.InstanceId, block.WidgetInstanceId, StringComparison.OrdinalIgnoreCase));
                if (widget != null)
                {
                    builder.Append(',').Append((int)widget.Kind);
                }
            }

            return builder.ToString();
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
                BorderThickness = new Thickness(0),
                Padding = EditLayoutButton.IsChecked == true
                    ? new Thickness(2)
                    : new Thickness(0)
            };
            border.SetResourceReference(Border.CornerRadiusProperty, "PlayAch.Radius.Section");
            border.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Border");
            border.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.GridSurface");
            // Widgets can contain buttons, scroll viewers, and charts that consume bubbling
            // drag events. Tunneling at the block host keeps the illuminated target and the
            // committing drop on the same reliable path.
            border.PreviewDrop += Block_Drop;
            border.PreviewDragOver += Block_DragOver;
            border.PreviewDragLeave += Block_DragLeave;
            border.PreviewMouseLeftButtonDown += Block_PreviewMouseLeftButtonDown;
            border.PreviewMouseMove += Block_PreviewMouseMove;
            border.GotKeyboardFocus += Block_GotKeyboardFocus;

            UIElement content;
            ShowcaseWidgetControl widgetHost = null;
            Button addButton = null;
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
                    Text = Localize("LOCPlayAch_Showcase_AddWidget"),
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
                addButton = add;
                content = add;
            }
            else
            {
                widgetHost = new ShowcaseWidgetControl();
                // Defer the projection (and with it template inflation - data grids and charts are
                // expensive to build) to background priority so page switches and rebuilds paint
                // the dashboard frame immediately and widgets fill in without blocking the click.
                var deferredHost = widgetHost;
                var deferredWidget = widget;
                var deferredBlockId = block.BlockId;
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (_disposed ||
                            !_blockVisuals.TryGetValue(deferredBlockId, out var current) ||
                            !ReferenceEquals(current?.Host, deferredHost))
                        {
                            return;
                        }

                        deferredHost.Apply(ShowcaseWidgetProjectionService.Build(
                            _overview.LatestSnapshot ?? new OverviewDataSnapshot(),
                            Layout,
                            deferredWidget));
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
                // While editing, clicks select and drag blocks instead of tunneling into widget
                // content - embedded grids and charts otherwise run hit tests, focus moves, and
                // selection work on every click. The widget menu rides on the block container so
                // it stays reachable with the body inert.
                widgetHost.IsHitTestVisible = EditLayoutButton.IsChecked != true;
                if (EditLayoutButton.IsChecked == true)
                {
                    border.ContextMenu = BuildPlacedWidgetMenu(block, widget);
                }

                content = widgetHost;
            }

            var layers = new Grid();
            layers.Children.Add(content);
            var dropGlow = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Margin = new Thickness(2),
                BorderThickness = new Thickness(3),
                Opacity = 0
            };
            dropGlow.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.Accent");
            dropGlow.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Accent");
            dropGlow.SetResourceReference(Border.CornerRadiusProperty, "PlayAch.Radius.Section");
            layers.Children.Add(dropGlow);

            var dropStatus = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dropStatus.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            var dropStatusPanel = new Border
            {
                Child = dropStatus,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Margin = new Thickness(12),
                Padding = new Thickness(14, 10, 14, 10),
                BorderThickness = new Thickness(1),
                Opacity = 0.94,
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dropStatusPanel.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.Surface");
            dropStatusPanel.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Accent");
            dropStatusPanel.SetResourceReference(Border.CornerRadiusProperty, "PlayAch.Radius.Card");
            layers.Children.Add(dropStatusPanel);
            border.Child = layers;
            var visualState = new BlockVisualState
            {
                Block = block,
                Container = border,
                Glow = dropGlow,
                StatusPanel = dropStatusPanel,
                Status = dropStatus,
                Host = widgetHost,
                Widget = widget,
                AddButton = addButton
            };
            _blockVisuals[block.BlockId] = visualState;
            RefreshBlockChrome(visualState);
            return border;
        }

        private ContextMenu BuildPlacedWidgetMenu(
            ShowcaseBlockSettings block,
            ShowcaseWidgetInstanceSettings widget)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_WidgetSettings"),
                () => OpenWidgetSettings(widget)));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_ReplaceWidget"),
                () => OpenWidgetPicker(block, null)));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_DeleteWidget"),
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

        private void MergeSelectedWith(string secondBlockId)
        {
            var selected = SelectedBlock;
            if (selected == null)
            {
                return;
            }

            var closure = ShowcaseLayoutService.GetMergeClosure(
                Layout,
                CurrentPage.PageId,
                selected.BlockId,
                secondBlockId);
            var widgets = closure
                .Select(block => FindWidget(block.WidgetInstanceId))
                .Where(widget => widget != null)
                .GroupBy(widget => widget.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (widgets.Count > 1 &&
                !Confirm("LOCPlayAch_Showcase_MergeDeleteConfirm"))
            {
                return;
            }

            var selectedWidgetId = selected.WidgetInstanceId;
            var adjacentWidgetId = closure.FirstOrDefault(block => string.Equals(
                block.BlockId,
                secondBlockId,
                StringComparison.OrdinalIgnoreCase))?.WidgetInstanceId;
            var preferredWidgetInstanceId = widgets.Any(widget => string.Equals(
                widget.InstanceId,
                selectedWidgetId,
                StringComparison.OrdinalIgnoreCase))
                ? selectedWidgetId
                : widgets.Any(widget => string.Equals(
                    widget.InstanceId,
                    adjacentWidgetId,
                    StringComparison.OrdinalIgnoreCase))
                    ? adjacentWidgetId
                    : widgets.FirstOrDefault()?.InstanceId;
            if (ShowcaseLayoutService.TryMergeWithFallback(
                    Layout,
                    CurrentPage.PageId,
                    selected.BlockId,
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
            if (block == null ||
                string.Equals(_selectedBlockId, block.BlockId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedBlockId = block.BlockId;
            foreach (var state in _blockVisuals.Values)
            {
                RefreshBlockChrome(state);
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

            var widget = FindWidget(block.WidgetInstanceId);
            _dragSourceBlockId = block.BlockId;
            ShowDragStatus(
                block.BlockId,
                string.Format(
                    Localize("LOCPlayAch_Showcase_DragMoving"),
                    GetWidgetName(widget)),
                DragVisualKind.Source);
            try
            {
                DragDrop.DoDragDrop(
                    border,
                    new DataObject(WidgetDragFormat, block.WidgetInstanceId),
                    DragDropEffects.Move);
            }
            finally
            {
                ClearDragVisuals();
            }
        }

        private void Block_DragOver(object sender, DragEventArgs e)
        {
            if (EditLayoutButton.IsChecked != true ||
                !(sender is Border border) ||
                !(border.Tag is ShowcaseBlockSettings block) ||
                !(e.Data.GetData(WidgetDragFormat) is string instanceId))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var movingWidget = FindWidget(instanceId);
            var movingName = GetWidgetName(movingWidget);
            if (string.Equals(block.BlockId, _dragSourceBlockId, StringComparison.OrdinalIgnoreCase))
            {
                e.Effects = DragDropEffects.None;
                ShowDragStatus(
                    block.BlockId,
                    string.Format(
                        Localize("LOCPlayAch_Showcase_DropSame"),
                        movingName),
                    DragVisualKind.InvalidTarget);
            }
            else if (!ShowcaseLayoutService.CanPlaceWidget(
                         Layout,
                         CurrentPage.PageId,
                         block.BlockId,
                         instanceId))
            {
                e.Effects = DragDropEffects.None;
                ShowDragStatus(
                    block.BlockId,
                    Localize("LOCPlayAch_Showcase_DropUnavailable"),
                    DragVisualKind.InvalidTarget);
            }
            else
            {
                e.Effects = DragDropEffects.Move;
                var displacedWidget = FindWidget(block.WidgetInstanceId);
                var message = displacedWidget == null
                    ? string.Format(
                        Localize("LOCPlayAch_Showcase_DropMoveHere"),
                        movingName)
                    : string.Format(
                        Localize("LOCPlayAch_Showcase_DropSwap"),
                        movingName,
                        GetWidgetName(displacedWidget));
                ShowDragStatus(block.BlockId, message, DragVisualKind.ValidTarget);
            }

            e.Handled = true;
        }

        private void Block_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border && border.Tag is ShowcaseBlockSettings block)
            {
                RestoreSourceStatusOrHide(block.BlockId);
            }
        }

        private void Block_Drop(object sender, DragEventArgs e)
        {
            if (EditLayoutButton.IsChecked != true ||
                !(sender is Border border) ||
                !(border.Tag is ShowcaseBlockSettings block) ||
                !(e.Data.GetData(WidgetDragFormat) is string instanceId))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!string.Equals(block.BlockId, _dragSourceBlockId, StringComparison.OrdinalIgnoreCase) &&
                ShowcaseLayoutService.PlaceWidget(
                    Layout,
                    CurrentPage.PageId,
                    block.BlockId,
                    instanceId))
            {
                // Stop the target clock before rebuilding the visual tree. The drag source's
                // finally block also clears the newly built states after WPF ends the operation.
                ClearDragVisuals();
                SaveAndRebuild();
            }

            e.Handled = true;
        }

        private void ShowDragStatus(
            string blockId,
            string message,
            DragVisualKind visualKind)
        {
            if (!_blockVisuals.TryGetValue(blockId ?? string.Empty, out var state))
            {
                return;
            }

            var visualChanged = state.DragVisual != visualKind;
            if (state.Status.Text != message)
            {
                state.Status.Text = message ?? string.Empty;
            }

            state.DragVisual = visualKind;
            ApplyDragVisual(state, visualChanged);
            RefreshBlockChrome(state);
        }

        private void RestoreSourceStatusOrHide(string blockId)
        {
            if (!_blockVisuals.TryGetValue(blockId ?? string.Empty, out var state))
            {
                return;
            }

            if (string.Equals(blockId, _dragSourceBlockId, StringComparison.OrdinalIgnoreCase))
            {
                var movingWidget = FindWidget(state.Block.WidgetInstanceId);
                state.Status.Text = string.Format(
                    Localize("LOCPlayAch_Showcase_DragMoving"),
                    GetWidgetName(movingWidget));
                var visualChanged = state.DragVisual != DragVisualKind.Source;
                state.DragVisual = DragVisualKind.Source;
                ApplyDragVisual(state, visualChanged);
            }
            else
            {
                state.Status.Text = string.Empty;
                state.DragVisual = DragVisualKind.None;
                ApplyDragVisual(state, true);
            }

            RefreshBlockChrome(state);
        }

        private void ClearDragVisuals()
        {
            _dragSourceBlockId = null;
            foreach (var state in _blockVisuals.Values)
            {
                state.Status.Text = string.Empty;
                state.DragVisual = DragVisualKind.None;
                ApplyDragVisual(state, true);
                RefreshBlockChrome(state);
            }
        }

        private static void ApplyDragVisual(BlockVisualState state, bool visualChanged)
        {
            if (state?.Glow == null || state.StatusPanel == null)
            {
                return;
            }

            if (state.DragVisual == DragVisualKind.None)
            {
                state.Glow.BeginAnimation(UIElement.OpacityProperty, null);
                state.Glow.Opacity = 0;
                state.Glow.Visibility = Visibility.Collapsed;
                state.StatusPanel.Visibility = Visibility.Collapsed;
                return;
            }

            state.Glow.Visibility = Visibility.Visible;
            state.StatusPanel.Visibility = Visibility.Visible;
            if (state.DragVisual == DragVisualKind.ValidTarget)
            {
                if (visualChanged)
                {
                    state.Glow.BeginAnimation(
                        UIElement.OpacityProperty,
                        new DoubleAnimation
                        {
                            From = 0.12,
                            To = 0.30,
                            Duration = TimeSpan.FromMilliseconds(650),
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase
                            {
                                EasingMode = EasingMode.EaseInOut
                            }
                        },
                        HandoffBehavior.SnapshotAndReplace);
                }

                return;
            }

            state.Glow.BeginAnimation(UIElement.OpacityProperty, null);
            state.Glow.Opacity = state.DragVisual == DragVisualKind.Source ? 0.12 : 0.06;
        }

        private void RefreshBlockChrome(BlockVisualState state)
        {
            if (state?.Container == null || state.Block == null)
            {
                return;
            }

            var editing = EditLayoutButton.IsChecked == true;
            var isDragSource = string.Equals(
                state.Block.BlockId,
                _dragSourceBlockId,
                StringComparison.OrdinalIgnoreCase);
            var isValidTarget = state.DragVisual == DragVisualKind.ValidTarget;
            var emphasized = isDragSource || state.DragVisual != DragVisualKind.None ||
                (editing && string.Equals(
                    state.Block.BlockId,
                    _selectedBlockId,
                    StringComparison.OrdinalIgnoreCase));
            state.Container.BorderThickness = isValidTarget
                ? new Thickness(3)
                : editing
                    ? new Thickness(2)
                    : new Thickness(0);
            state.Container.SetResourceReference(
                Border.BorderBrushProperty,
                emphasized ? "PlayAch.Brush.Accent" : "PlayAch.Brush.Border");
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
            ShowcaseGridSurfaces.PruneOrphaned(_settings.Persisted?.GridOptions, Layout);
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
            Dispatcher.BeginInvoke(new Action(RefreshWidgetData));
        }

        // A snapshot change carries new data but the same layout, so update each widget host's
        // projection in place rather than tearing down and rebuilding every block container (which
        // would re-run the drag wiring and recreate every control). The per-kind view models update
        // their bindings without discarding their visual tree.
        private void RefreshWidgetData()
        {
            if (_disposed)
            {
                return;
            }

            if (_blockVisuals.Count == 0)
            {
                BuildDashboard();
                return;
            }

            var snapshot = _overview.LatestSnapshot ?? new OverviewDataSnapshot();
            foreach (var visual in _blockVisuals.Values)
            {
                if (visual?.Host != null && visual.Widget != null)
                {
                    visual.Host.Apply(
                        ShowcaseWidgetProjectionService.Build(snapshot, Layout, visual.Widget));
                }
            }
        }

        private void ShowcaseConfigurationEvents_Changed(object sender, EventArgs e)
        {
            if (!_publishingConfigurationChange)
            {
                Dispatcher.BeginInvoke(new Action(RefreshAfterExternalConfigurationChange));
            }
        }

        // External configuration changes (pin toggles, widget options, row-menu edits) usually keep
        // the block layout intact, so refresh projections in place; a full rebuild - which recreates
        // every widget control, including embedded data grids - only runs when the layout signature
        // actually changed.
        private void RefreshAfterExternalConfigurationChange()
        {
            if (_disposed)
            {
                return;
            }

            EnsureLayout();
            if (string.Equals(ComputeLayoutSignature(), _layoutSignature, StringComparison.Ordinal))
            {
                RefreshWidgetData();
                return;
            }

            Rebuild();
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

            if (_blockVisuals.Count == 0)
            {
                BuildDashboard();
                return;
            }

            // Toggling edit mode only changes block chrome and affordances; a full rebuild would
            // recreate every widget control (including the embedded data grids) and stall the click.
            var editing = EditLayoutButton.IsChecked == true;
            foreach (var state in _blockVisuals.Values)
            {
                state.Container.Focusable = editing;
                state.Container.Padding = editing ? new Thickness(2) : new Thickness(0);
                if (state.Host != null)
                {
                    state.Host.IsHitTestVisible = !editing;
                    state.Container.ContextMenu = editing
                        ? BuildPlacedWidgetMenu(state.Block, state.Widget)
                        : null;
                }

                if (state.AddButton != null)
                {
                    state.AddButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
                }

                RefreshBlockChrome(state);
            }

            UpdateEditTools();
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
                ? Localize("LOCPlayAch_Showcase_SelectBlock")
                : string.Format(
                    Localize("LOCPlayAch_Showcase_SelectedBlockFormat"),
                    widget == null
                        ? Localize("LOCPlayAch_Showcase_EmptyBlock")
                        : GetWidgetName(widget),
                    block.ColumnSpan,
                    block.RowSpan);
            WidgetActionButton.Content = widget == null
                ? Localize("LOCPlayAch_Showcase_AddWidget")
                : Localize("LOCPlayAch_Showcase_Widget");
            WidgetActionButton.IsEnabled = block != null;
            LayoutActionButton.IsEnabled = block != null;
        }

        private void WidgetActionButton_Click(object sender, RoutedEventArgs e)
        {
            var block = SelectedBlock;
            if (block == null)
            {
                return;
            }

            var widget = FindWidget(block.WidgetInstanceId);
            if (widget == null)
            {
                OpenWidgetPicker(block, WidgetActionButton);
                return;
            }

            var menu = BuildPlacedWidgetMenu(block, widget);
            menu.PlacementTarget = WidgetActionButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void LayoutActionButton_Click(object sender, RoutedEventArgs e)
        {
            var block = SelectedBlock;
            if (block == null)
            {
                return;
            }

            var menu = new ContextMenu
            {
                PlacementTarget = LayoutActionButton,
                Placement = PlacementMode.Bottom
            };
            var splitColumns = MenuItem(
                Localize("LOCPlayAch_Showcase_SplitColumns"),
                () => OpenSplitPicker(LayoutActionButton, vertical: true));
            splitColumns.IsEnabled = block.ColumnSpan > 1;
            menu.Items.Add(splitColumns);
            var splitRows = MenuItem(
                Localize("LOCPlayAch_Showcase_SplitRows"),
                () => OpenSplitPicker(LayoutActionButton, vertical: false));
            splitRows.IsEnabled = block.RowSpan > 1;
            menu.Items.Add(splitRows);

            var merge = new MenuItem
            {
                Header = Localize("LOCPlayAch_Common_Merge")
            };
            AddMergeDirection(merge, "LOCPlayAch_Showcase_MergeLeftLabel", 0, -1);
            AddMergeDirection(merge, "LOCPlayAch_Showcase_MergeUpLabel", -1, 0);
            AddMergeDirection(merge, "LOCPlayAch_Showcase_MergeDownLabel", 1, 0);
            AddMergeDirection(merge, "LOCPlayAch_Showcase_MergeRightLabel", 0, 1);
            merge.IsEnabled = merge.Items.Count > 0;
            menu.Items.Add(merge);
            menu.IsOpen = true;
        }

        private void AddMergeDirection(
            MenuItem parent,
            string localizationKey,
            int rowDirection,
            int columnDirection)
        {
            if (!HasAdjacentBlock(SelectedBlock, rowDirection, columnDirection))
            {
                return;
            }

            parent.Items.Add(MenuItem(
                Localize(localizationKey),
                () => MergeDirectional(rowDirection, columnDirection)));
        }

        private void OpenSplitPicker(Button target, bool vertical)
        {
            var block = SelectedBlock;
            if (block == null)
            {
                return;
            }

            var start = vertical ? block.Column : block.Row;
            var span = vertical ? block.ColumnSpan : block.RowSpan;
            var pageId = CurrentPage.PageId;
            var blockId = block.BlockId;
            var lines = Enumerable.Range(start + 1, Math.Max(0, span - 1)).ToList();
            if (lines.Count == 1)
            {
                SplitBlock(pageId, blockId, vertical, lines[0]);
                return;
            }

            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var line in lines)
            {
                var captured = line;
                var firstSpan = line - start;
                var secondSpan = span - firstSpan;
                var accessibleName = string.Format(
                    Localize("LOCPlayAch_Showcase_SplitAtFormat"),
                    firstSpan,
                    secondSpan);
                menu.Items.Add(SplitPreviewItem(
                    vertical,
                    new[] { firstSpan, secondSpan },
                    accessibleName,
                    () => SplitBlock(
                        pageId,
                        blockId,
                        vertical,
                        captured)));
            }

            if (span == ShowcaseLayoutService.GridSize)
            {
                var accessibleName = Localize("LOCPlayAch_Showcase_SplitThreeEqual");
                menu.Items.Insert(1, SplitPreviewItem(
                    vertical,
                    new[] { 1, 1, 1 },
                    accessibleName,
                    () => SplitBlockThreeWays(pageId, blockId, vertical)));
            }

            menu.IsOpen = true;
        }

        private void SplitBlock(
            string pageId,
            string blockId,
            bool vertical,
            int gridLine)
        {
            ApplySplit(
                pageId,
                blockId,
                () => ShowcaseLayoutService.TrySplit(
                    Layout,
                    pageId,
                    blockId,
                    vertical,
                    gridLine));
        }

        private void SplitBlockThreeWays(string pageId, string blockId, bool vertical)
        {
            ApplySplit(
                pageId,
                blockId,
                () => ShowcaseLayoutService.TrySplitThreeWays(
                    Layout,
                    pageId,
                    blockId,
                    vertical));
        }

        private void ApplySplit(string pageId, string blockId, Func<bool> split)
        {
            var page = Layout.Pages.FirstOrDefault(candidate => string.Equals(
                candidate?.PageId,
                pageId,
                StringComparison.OrdinalIgnoreCase));
            var block = page?.Blocks.FirstOrDefault(candidate => string.Equals(
                candidate?.BlockId,
                blockId,
                StringComparison.OrdinalIgnoreCase));
            var widgetInstanceId = block?.WidgetInstanceId;
            if (block == null || split == null || !split())
            {
                return;
            }

            page = Layout.Pages.FirstOrDefault(candidate => string.Equals(
                candidate?.PageId,
                pageId,
                StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(widgetInstanceId))
            {
                _selectedBlockId = page?.Blocks.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.WidgetInstanceId,
                        widgetInstanceId,
                        StringComparison.OrdinalIgnoreCase))?.BlockId ?? blockId;
            }
            else
            {
                _selectedBlockId = page?.Blocks.FirstOrDefault(candidate => string.Equals(
                    candidate.BlockId,
                    blockId,
                    StringComparison.OrdinalIgnoreCase))?.BlockId;
            }

            SaveAndRebuild();
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

        private void MergeDirectional(int rowDirection, int columnDirection)
        {
            var block = SelectedBlock;
            var adjacent = FindAdjacentBlocks(block, rowDirection, columnDirection);
            if (block == null || adjacent.Count == 0)
            {
                return;
            }

            MergeSelectedWith(adjacent[0].BlockId);
        }

        private void PageActionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = PageActionsButton,
                Placement = PlacementMode.Bottom
            };
            var add = new MenuItem
            {
                Header = Localize("LOCPlayAch_Showcase_AddPage")
            };
            add.Items.Add(PageTemplateItem(ShowcasePageTemplate.Blank));
            add.Items.Add(PageTemplateItem(ShowcasePageTemplate.Analytics));
            add.Items.Add(PageTemplateItem(ShowcasePageTemplate.Collection));
            menu.Items.Add(add);
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_DuplicatePage"),
                () =>
                {
                    ShowcaseLayoutService.DuplicatePage(
                        Layout,
                        CurrentPage.PageId,
                        Localize("LOCPlayAch_Showcase_CopySuffix"));
                    SaveAndRebuild();
                }));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_RenamePage"),
                RenameCurrentPage));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_EditProfile"),
                OpenProfileSettings));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_MovePageLeft"),
                () =>
                {
                    ShowcaseLayoutService.MovePage(Layout, CurrentPage.PageId, -1);
                    SaveAndRebuild();
                }));
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_MovePageRight"),
                () =>
                {
                    ShowcaseLayoutService.MovePage(Layout, CurrentPage.PageId, 1);
                    SaveAndRebuild();
                }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(
                Localize("LOCPlayAch_Showcase_ResetPage"),
                () =>
                {
                    if (!Confirm("LOCPlayAch_Showcase_ResetPageConfirm"))
                    {
                        return;
                    }

                    ShowcaseLayoutService.ResetPage(Layout, CurrentPage.PageId);
                    SaveAndRebuild();
                }));
            var delete = MenuItem(
                Localize("LOCPlayAch_Showcase_DeletePage"),
                DeleteCurrentPage);
            delete.IsEnabled = Layout.Pages.Count > 1;
            menu.Items.Add(delete);
            menu.IsOpen = true;
        }

        private MenuItem PageTemplateItem(ShowcasePageTemplate template)
        {
            return MenuItem(
                Localize($"LOCPlayAch_Showcase_Template_{template}"),
                () =>
                {
                    ShowcaseLayoutService.AddPage(
                        Layout,
                        template,
                        Localize($"LOCPlayAch_Showcase_Template_{template}"));
                    SaveAndRebuild();
                });
        }

        private void RenameCurrentPage()
        {
            var result = _api?.Dialogs?.SelectString(
                Localize("LOCPlayAch_Showcase_RenamePrompt"),
                Localize("LOCPlayAch_Showcase_RenamePage"),
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
                profileWidget = ShowcaseWidgetSettingsFactory.CreateDefault(
                    ShowcaseWidgetKind.Profile);
            }

            if (ShowcaseWidgetSettingsDialog.Show(profileWidget, Layout))
            {
                SaveAndRebuild();
            }
        }

        private void DeleteCurrentPage()
        {
            if (Layout.Pages.Count <= 1 ||
                !Confirm("LOCPlayAch_Showcase_DeletePageConfirm"))
            {
                return;
            }

            ShowcaseLayoutService.DeletePage(Layout, CurrentPage.PageId);
            SaveAndRebuild();
        }

        private bool Confirm(string messageKey)
        {
            return _api?.Dialogs?.ShowMessage(
                       Localize(messageKey),
                       Localize("LOCPlayAch_Showcase_Title"),
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
                ? Localize("LOCPlayAch_Showcase_Widget")
                : ShowcaseUiText.GetWidgetName(widget.Kind);
        }

        private static MenuItem MenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, __) => action();
            return item;
        }

        private static MenuItem SplitPreviewItem(
            bool vertical,
            IReadOnlyList<int> parts,
            string accessibleName,
            Action action)
        {
            var previewGrid = new Grid
            {
                Width = vertical ? 72 : 42,
                Height = vertical ? 26 : 48,
                ClipToBounds = true
            };
            foreach (var part in parts)
            {
                if (vertical)
                {
                    previewGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(part, GridUnitType.Star)
                    });
                }
                else
                {
                    previewGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(part, GridUnitType.Star)
                    });
                }
            }

            for (var index = 0; index < parts.Count; index++)
            {
                var section = new Border
                {
                    Margin = index == 0
                        ? new Thickness(0)
                        : vertical
                            ? new Thickness(2, 0, 0, 0)
                            : new Thickness(0, 2, 0, 0)
                };
                section.SetResourceReference(
                    Border.BackgroundProperty,
                    index % 2 == 0 ? "PlayAch.Brush.Accent" : "PlayAch.Brush.Text");
                if (vertical)
                {
                    Grid.SetColumn(section, index);
                }
                else
                {
                    Grid.SetRow(section, index);
                }

                previewGrid.Children.Add(section);
            }

            var frame = new Border
            {
                Child = previewGrid,
                Width = vertical ? 74 : 44,
                Height = vertical ? 28 : 50,
                Margin = new Thickness(2, 0, 2, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                ToolTip = accessibleName
            };
            frame.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Text");

            var item = new MenuItem
            {
                Header = frame,
                ToolTip = accessibleName,
                MinHeight = vertical ? 32 : 54,
                VerticalContentAlignment = VerticalAlignment.Center,
                ClipToBounds = false
            };
            AutomationProperties.SetName(item, accessibleName);
            item.Click += (_, __) => action();
            return item;
        }

        private static MenuItem WidgetPickerItem(
            ShowcaseWidgetDefinition definition,
            Action action)
        {
            var item = new MenuItem
            {
                Header = Localize(definition.NameKey)
            };
            item.Click += (_, __) => action();
            return item;
        }

        private sealed class BlockVisualState
        {
            public ShowcaseBlockSettings Block { get; set; }

            public Border Container { get; set; }

            public Border Glow { get; set; }

            public Border StatusPanel { get; set; }

            public TextBlock Status { get; set; }

            public DragVisualKind DragVisual { get; set; }

            // Present only for blocks that host a widget; lets a data-only snapshot change refresh
            // the widget's projection in place instead of rebuilding the block container.
            public ShowcaseWidgetControl Host { get; set; }

            public ShowcaseWidgetInstanceSettings Widget { get; set; }

            // Present only for empty blocks; lets the edit-mode toggle show and hide the add
            // affordance without rebuilding the block container.
            public Button AddButton { get; set; }
        }

        private enum DragVisualKind
        {
            None,
            Source,
            ValidTarget,
            InvalidTarget
        }

    }
}

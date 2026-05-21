using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK.Events;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Controls;
using PlayniteAchievements.Views.Helpers;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    public partial class SingleGameControl : UserControl, IFullscreenControllerNavigable
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;

        public SingleGameControl()
        {
            InitializeComponent();
        }

        public SingleGameControl(
            Guid gameId,
            RefreshRuntime refreshRuntime,
            AchievementDataService achievementDataService,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            InitializeComponent();

            _settings = settings;
            _logger = logger;
            DataContext = new SingleGameControlModel(gameId, refreshRuntime, achievementDataService, playniteApi, logger, settings);
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            // Subscribe to settings saved event to refresh when credentials change
            PlayniteAchievementsPlugin.SettingsSaved += Plugin_SettingsSaved;
        }

        private void Plugin_SettingsSaved(object sender, EventArgs e)
        {
            RefreshView();
            AchievementsDataGridControl?.Refresh();
            UpdateDefaultSortIndicator();
        }

        private SingleGameControlModel ViewModel => DataContext as SingleGameControlModel;

        public string WindowTitle => ViewModel?.GameName != null
            ? $"{ViewModel.GameName} - Achievements"
            : "Achievements";

        public void RefreshView()
        {
            ViewModel?.RefreshView();
        }

        public void Cleanup()
        {
            PlayniteAchievementsPlugin.SettingsSaved -= Plugin_SettingsSaved;
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            ViewModel?.Dispose();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDefaultSortIndicator();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.PropertyName == nameof(SingleGameControlModel.HasCustomAchievementOrder))
            {
                Dispatcher.BeginInvoke(new Action(UpdateDefaultSortIndicator));
            }
        }

        private void UpdateDefaultSortIndicator()
        {
            if (ViewModel == null)
            {
                AchievementsDataGridControl?.SetSortIndicator(null, null);
                return;
            }

            AchievementSortHelper.ApplySortIndicator(
                ViewModel.CurrentSortPath,
                ViewModel.CurrentSortDirection,
                _settings?.Persisted,
                AchievementSortSurface.SingleGame,
                (sortPath, sortDirection) => AchievementsDataGridControl?.SetSortIndicator(sortPath, sortDirection));
        }

        private void OnGridSorting(object sender, DataGridSortingEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            var sortAction = AchievementSortHelper.ResolveGridSortAction(
                e.Column?.SortMemberPath,
                ViewModel.CurrentSortPath,
                ViewModel.CurrentSortDirection,
                _settings?.Persisted,
                AchievementSortSurface.SingleGame);
            if (sortAction.Kind == AchievementGridSortActionKind.None)
            {
                return;
            }

            e.Handled = true;

            if (sortAction.Kind == AchievementGridSortActionKind.ResetToDefault)
            {
                ViewModel.ResetSortToDefault();
                AchievementsDataGridControl?.SetSortIndicator(null, null);
                return;
            }
            else if (sortAction.Direction.HasValue)
            {
                ViewModel.SortDataGrid(sortAction.SortMemberPath, sortAction.Direction.Value);
            }

            UpdateDefaultSortIndicator();
        }

        public bool HandleFullscreenControllerInput(ControllerInput input)
        {
            if (FullscreenControllerNavigationService.IsBackInput(input))
            {
                Window.GetWindow(this)?.Close();
                return true;
            }

            if (FullscreenControllerNavigationService.IsSecondaryClickInput(input))
            {
                return TryOpenFocusedSelectorContextMenu() ||
                       (AchievementsDataGridControl?.IsColumnHeaderFocusedForController() == true &&
                        AchievementsDataGridControl.OpenColumnVisibilityMenuForController());
            }

            if (FullscreenControllerNavigationService.IsAcceptInput(input))
            {
                if (AchievementsDataGridControl?.IsColumnHeaderFocusedForController() == true)
                {
                    return AchievementsDataGridControl.ActivateFocusedColumnHeaderForController();
                }

                if (AchievementsDataGridControl?.IsKeyboardFocusWithin == true)
                {
                    return AchievementsDataGridControl.ActivateSelectedItem();
                }

                return FullscreenControllerNavigationService.ActivateFocusedElement();
            }

            if (FullscreenControllerNavigationService.TryGetVerticalDelta(input, out var verticalDelta))
            {
                return TryHandleControllerVerticalNavigation(verticalDelta);
            }

            if (FullscreenControllerNavigationService.TryGetHorizontalDelta(input, out var horizontalDelta))
            {
                return TryMoveFocusWithinCurrentGroup(horizontalDelta);
            }

            return false;
        }

        private bool TryHandleControllerVerticalNavigation(int delta)
        {
            if (AchievementsDataGridControl?.IsColumnHeaderFocusedForController() == true)
            {
                return delta > 0
                    ? FocusAchievementsGrid()
                    : FocusFilterControls();
            }

            if (AchievementsDataGridControl?.IsKeyboardFocusWithin == true)
            {
                var grid = AchievementsDataGridControl.InternalDataGrid;
                if (delta < 0 && (grid?.SelectedIndex ?? -1) <= 0)
                {
                    return FocusAchievementColumnHeaders() || FocusFilterControls();
                }

                return AchievementsDataGridControl.MoveSelection(delta);
            }

            if (IsFocusWithinFilterControls())
            {
                return delta > 0
                    ? FocusAchievementColumnHeaders() || FocusAchievementsGrid()
                    : FocusSummaryControls();
            }

            if (IsFocusWithinSummaryControls())
            {
                return delta > 0 ? FocusFilterControls() : true;
            }

            return delta > 0
                ? FocusFilterControls()
                : FocusSummaryControls();
        }

        private bool TryMoveFocusWithinCurrentGroup(int delta)
        {
            if (AchievementsDataGridControl?.IsKeyboardFocusWithin == true)
            {
                if (AchievementsDataGridControl.IsColumnHeaderFocusedForController())
                {
                    return AchievementsDataGridControl.MoveColumnHeaderFocusForController(delta);
                }

                return true;
            }

            if (IsFocusWithinFilterControls())
            {
                return TryMoveFocusWithinGroup(GetFilterControllerElements(), delta);
            }

            if (IsFocusWithinSummaryControls())
            {
                return TryMoveFocusWithinGroup(GetSummaryControllerElements(), delta);
            }

            return TryMoveFocusWithinGroup(GetFilterControllerElements(), delta) ||
                   TryMoveFocusWithinGroup(GetSummaryControllerElements(), delta);
        }

        private bool FocusSummaryControls()
        {
            return FullscreenControllerNavigationService.FocusFirstElement(GetSummaryControllerElements());
        }

        private bool FocusFilterControls()
        {
            return FullscreenControllerNavigationService.FocusFirstElement(GetFilterControllerElements());
        }

        private bool FocusAchievementsGrid()
        {
            return FullscreenControllerNavigationService.FocusDataGrid(AchievementsDataGridControl?.InternalDataGrid);
        }

        private bool FocusAchievementColumnHeaders()
        {
            return AchievementsDataGridControl?.FocusColumnHeaderForController() == true;
        }

        private bool TryOpenFocusedSelectorContextMenu()
        {
            var focusedButton = FullscreenControllerNavigationService.FindAncestor<Button>(
                                    Keyboard.FocusedElement as DependencyObject)
                                ?? Keyboard.FocusedElement as Button;
            if (focusedButton == null)
            {
                return false;
            }

            if (ReferenceEquals(focusedButton, CategoryTypeFilterSelectionButton))
            {
                CategoryTypeFilterSelectionButton_Click(focusedButton, new RoutedEventArgs());
                return CategoryTypeFilterSelectionContextMenu?.IsOpen == true;
            }

            if (ReferenceEquals(focusedButton, CategoryLabelFilterSelectionButton))
            {
                CategoryLabelFilterSelectionButton_Click(focusedButton, new RoutedEventArgs());
                return CategoryLabelFilterSelectionContextMenu?.IsOpen == true;
            }

            return false;
        }

        private bool IsFocusWithinSummaryControls()
        {
            return GetSummaryControllerElements().Any(element => element.IsKeyboardFocusWithin);
        }

        private bool IsFocusWithinFilterControls()
        {
            return GetFilterControllerElements().Any(element => element.IsKeyboardFocusWithin);
        }

        private IList<UIElement> GetSummaryControllerElements()
        {
            return GetVisibleControllerElements(
                RefreshGameButton,
                StatsToggleButton,
                TimelineToggleButton,
                TimelineRangeSevenDaysButton,
                TimelineRangeFourteenDaysButton,
                TimelineRangeOneMonthButton,
                TimelineRangeThreeMonthsButton,
                TimelineRangeOneYearButton,
                TimelineRangeAllButton);
        }

        private IList<UIElement> GetFilterControllerElements()
        {
            return GetVisibleControllerElements(
                SearchTextBox,
                ClearSearchButton,
                CategoryTypeFilterSelectionButton,
                CategoryLabelFilterSelectionButton,
                ShowUnlockedCheckBox,
                ShowLockedCheckBox,
                ShowHiddenCheckBox);
        }

        private static IList<UIElement> GetVisibleControllerElements(params UIElement[] elements)
        {
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

        private static bool TryMoveFocusWithinGroup(IList<UIElement> elements, int delta)
        {
            if (elements == null || elements.Count == 0 || delta == 0)
            {
                return false;
            }

            var focused = Keyboard.FocusedElement as DependencyObject;
            var currentIndex = -1;
            for (var i = 0; i < elements.Count; i++)
            {
                if (ReferenceEquals(elements[i], focused) ||
                    FullscreenControllerNavigationService.IsDescendantOf(focused, elements[i]))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                return FullscreenControllerNavigationService.FocusFirstElement(elements);
            }

            var nextIndex = Math.Max(0, Math.Min(elements.Count - 1, currentIndex + delta));
            return FullscreenControllerNavigationService.FocusElement(elements[nextIndex]);
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ClearSearch();
        }

        private void CategoryTypeFilterSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            OpenMultiSelectFilterContextMenu(
                CategoryTypeFilterSelectionButton,
                ViewModel.CategoryTypeFilterOptions,
                option => ViewModel.IsCategoryTypeFilterSelected(option),
                (option, isSelected) => ViewModel.SetCategoryTypeFilterSelected(option, isSelected));
        }

        private void CategoryLabelFilterSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            OpenMultiSelectFilterContextMenu(
                CategoryLabelFilterSelectionButton,
                ViewModel.CategoryLabelFilterOptions,
                option => ViewModel.IsCategoryLabelFilterSelected(option),
                (option, isSelected) => ViewModel.SetCategoryLabelFilterSelected(option, isSelected));
        }

        private void OpenMultiSelectFilterContextMenu(
            Button button,
            System.Collections.Generic.IEnumerable<string> options,
            Func<string, bool> isSelected,
            Action<string, bool> setSelection)
        {
            if (button == null || isSelected == null || setSelection == null)
            {
                return;
            }

            var menu = button.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();
            if (options == null)
            {
                return;
            }

            var itemStyle = button.TryFindResource("AchievementMultiSelectMenuItemStyle") as Style;
            foreach (var option in options)
            {
                if (string.IsNullOrWhiteSpace(option))
                {
                    continue;
                }

                var item = new MenuItem
                {
                    Header = option,
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
    }
}



using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Services.UI
{
    internal sealed class FullscreenControllerNavigationService : IDisposable
    {
        private static readonly TimeSpan InitialInputBlock = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan RepeatedInputBlock = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan DirectionalInputBlock = TimeSpan.FromMilliseconds(200);
        private const string CellFocusTag = "ControllerCellFocus";

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly Dictionary<Window, IFullscreenControllerNavigable> _registeredWindows =
            new Dictionary<Window, IFullscreenControllerNavigable>();
        private readonly Dictionary<Window, DateTime> _inputBlockedUntilUtc =
            new Dictionary<Window, DateTime>();
        private readonly Dictionary<Window, HandledControllerInput> _lastHandledInput =
            new Dictionary<Window, HandledControllerInput>();

        public FullscreenControllerNavigationService(IPlayniteAPI api, ILogger logger)
        {
            _api = api;
            _logger = logger;
        }

        private Dispatcher UiDispatcher =>
            _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        public void RegisterWindow(Window window, IFullscreenControllerNavigable controllerHandler)
        {
            if (window == null || controllerHandler == null)
            {
                return;
            }

            void unregister()
            {
                _registeredWindows.Remove(window);
                _inputBlockedUntilUtc.Remove(window);
                _lastHandledInput.Remove(window);
            }

            RoutedEventHandler loadedHandler = null;
            loadedHandler = (_, __) =>
            {
                window.Loaded -= loadedHandler;
                BlockInputBriefly(window);
                PrepareWindowForControllerFocus(window);
                EnsureFocusInsideWindow(window);
            };

            EventHandler activatedHandler = null;
            activatedHandler = (_, __) =>
            {
                BlockInputBriefly(window);
                PrepareWindowForControllerFocus(window);
                EnsureFocusInsideWindow(window);
            };

            EventHandler closedHandler = null;
            closedHandler = (_, __) =>
            {
                window.Closed -= closedHandler;
                window.Loaded -= loadedHandler;
                window.Activated -= activatedHandler;
                unregister();
            };

            _registeredWindows[window] = controllerHandler;
            BlockInputBriefly(window);
            window.Loaded += loadedHandler;
            window.Activated += activatedHandler;
            window.Closed += closedHandler;

            if (window.IsLoaded)
            {
                PrepareWindowForControllerFocus(window);
                EnsureFocusInsideWindow(window);
            }
        }

        public bool TryHandleControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (args == null || args.State != ControllerInputState.Pressed)
            {
                return false;
            }

            try
            {
                var dispatcher = UiDispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    return TryHandleControllerInputOnUiThread(args.Button);
                }

                return dispatcher.Invoke(() => TryHandleControllerInputOnUiThread(args.Button));
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to route fullscreen controller input.");
                return false;
            }
        }

        public void Dispose()
        {
            _registeredWindows.Clear();
            _inputBlockedUntilUtc.Clear();
            _lastHandledInput.Clear();
        }

        private bool TryHandleControllerInputOnUiThread(ControllerInput input)
        {
            if (!IsFullscreenMode())
            {
                return false;
            }

            var activeWindow = GetActiveWindow();
            if (activeWindow == null ||
                !_registeredWindows.TryGetValue(activeWindow, out var controllerHandler))
            {
                return false;
            }

            if (IsInputBrieflyBlocked(activeWindow))
            {
                return true;
            }

            if (!IsKeyboardFocusOwnedByWindow(activeWindow))
            {
                EnsureFocusInsideWindow(activeWindow);
                if (!IsKeyboardFocusOwnedByWindow(activeWindow))
                {
                    return true;
                }
            }

            var inputKey = NormalizeInputKey(input);
            if (IsRepeatedInput(activeWindow, inputKey))
            {
                return true;
            }

            PrepareWindowForControllerFocus(activeWindow);

            var openContextMenu = FindOpenContextMenuForWindow(activeWindow);
            if (openContextMenu?.IsOpen == true)
            {
                if (TryHandleContextMenu(openContextMenu, input))
                {
                    RecordHandledInput(activeWindow, inputKey);
                }

                return true;
            }

            if (!controllerHandler.HandleFullscreenControllerInput(input))
            {
                return false;
            }

            RecordHandledInput(activeWindow, inputKey);
            return true;
        }

        private void BlockInputBriefly(Window window)
        {
            if (window != null)
            {
                _inputBlockedUntilUtc[window] = DateTime.UtcNow.Add(InitialInputBlock);
            }
        }

        private bool IsInputBrieflyBlocked(Window window)
        {
            if (window == null ||
                !_inputBlockedUntilUtc.TryGetValue(window, out var blockedUntilUtc))
            {
                return false;
            }

            if (DateTime.UtcNow < blockedUntilUtc)
            {
                return true;
            }

            _inputBlockedUntilUtc.Remove(window);
            return false;
        }

        private bool IsRepeatedInput(Window window, string inputKey)
        {
            if (window == null ||
                string.IsNullOrEmpty(inputKey) ||
                !_lastHandledInput.TryGetValue(window, out var lastInput) ||
                !string.Equals(lastInput.InputKey, inputKey, StringComparison.Ordinal))
            {
                return false;
            }

            var block = IsDirectionalInputKey(inputKey) ? DirectionalInputBlock : RepeatedInputBlock;
            return DateTime.UtcNow - lastInput.HandledAtUtc < block;
        }

        private void RecordHandledInput(Window window, string inputKey)
        {
            if (window != null && !string.IsNullOrEmpty(inputKey))
            {
                _lastHandledInput[window] = new HandledControllerInput(inputKey, DateTime.UtcNow);
            }
        }

        private bool IsFullscreenMode()
        {
            try
            {
                return _api?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
            }
            catch
            {
                return false;
            }
        }

        private static Window GetActiveWindow()
        {
            return Application.Current?.Windows?
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive);
        }

        private bool IsKeyboardFocusOwnedByWindow(Window window)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused == null)
            {
                return false;
            }

            if (ReferenceEquals(Window.GetWindow(focused), window))
            {
                return true;
            }

            var contextMenu = FindAncestor<ContextMenu>(focused);
            if (contextMenu?.IsOpen == true &&
                IsPlacementTargetInsideWindow(contextMenu.PlacementTarget, window))
            {
                return true;
            }

            var popup = FindAncestor<Popup>(focused);
            return popup?.IsOpen == true &&
                   IsPlacementTargetInsideWindow(popup.PlacementTarget, window);
        }

        private static bool IsPlacementTargetInsideWindow(UIElement placementTarget, Window window)
        {
            return placementTarget != null &&
                   window != null &&
                   ReferenceEquals(Window.GetWindow(placementTarget), window);
        }

        internal static bool IsAcceptInput(ControllerInput input)
        {
            return input == ControllerInput.A;
        }

        internal static bool IsBackInput(ControllerInput input)
        {
            return input == ControllerInput.B || input == ControllerInput.Back;
        }

        internal static bool IsSecondaryClickInput(ControllerInput input)
        {
            return input == ControllerInput.X;
        }

        internal static bool IsLeftShoulderInput(ControllerInput input)
        {
            return input == ControllerInput.LeftShoulder;
        }

        internal static bool IsRightShoulderInput(ControllerInput input)
        {
            return input == ControllerInput.RightShoulder;
        }

        internal static bool TryGetVerticalDelta(ControllerInput input, out int delta)
        {
            if (input == ControllerInput.DPadUp || input == ControllerInput.LeftStickUp)
            {
                delta = -1;
                return true;
            }

            if (input == ControllerInput.DPadDown || input == ControllerInput.LeftStickDown)
            {
                delta = 1;
                return true;
            }

            delta = 0;
            return false;
        }

        internal static bool TryGetHorizontalDelta(ControllerInput input, out int delta)
        {
            if (input == ControllerInput.DPadLeft || input == ControllerInput.LeftStickLeft)
            {
                delta = -1;
                return true;
            }

            if (input == ControllerInput.DPadRight || input == ControllerInput.LeftStickRight)
            {
                delta = 1;
                return true;
            }

            delta = 0;
            return false;
        }

        internal static bool ActivateFocusedDataGridColumnHeader(DataGrid grid)
        {
            var header = GetFocusedDataGridColumnHeader(grid);
            return header != null && ActivateElement(header);
        }

        internal static bool IsFocusWithinDataGridColumnHeader(DataGrid grid)
        {
            return GetFocusedDataGridColumnHeader(grid) != null;
        }

        internal static DataGridColumnHeader GetFocusedDataGridColumnHeader(DataGrid grid)
        {
            if (grid == null)
            {
                return null;
            }

            var header = FindAncestor<DataGridColumnHeader>(Keyboard.FocusedElement as DependencyObject);
            if (header?.Column == null ||
                !IsDescendantOf(header, grid))
            {
                return null;
            }

            return header;
        }

        internal static DataGridRow GetTargetDataGridRow(DataGrid grid)
        {
            if (grid == null)
            {
                return null;
            }

            var focusedRow = FindAncestor<DataGridRow>(Keyboard.FocusedElement as DependencyObject);
            if (focusedRow != null &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(focusedRow), grid))
            {
                return focusedRow;
            }

            var index = grid.SelectedIndex;
            if (index < 0 && grid.Items.Count > 0)
            {
                index = 0;
                grid.SelectedIndex = index;
            }

            if (index < 0)
            {
                return null;
            }

            grid.UpdateLayout();
            grid.ScrollIntoView(grid.Items[index]);
            grid.UpdateLayout();
            return grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }

        internal static bool FocusElement(UIElement element)
        {
            if (element == null || !element.IsEnabled || !element.IsVisible)
            {
                return false;
            }

            element.Focusable = true;
            element.Focus();
            Keyboard.Focus(element);

            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.BringIntoView();
            }

            return element.IsKeyboardFocusWithin || ReferenceEquals(Keyboard.FocusedElement, element);
        }

        internal static bool FocusFirstElement(params UIElement[] elements)
        {
            return FocusFirstElement((IEnumerable<UIElement>)elements);
        }

        internal static bool FocusFirstElement(IEnumerable<UIElement> elements)
        {
            if (elements == null)
            {
                return false;
            }

            foreach (var element in elements)
            {
                if (FocusControllerElement(element))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool FocusElementByDelta(IList<UIElement> elements, int delta, bool wrap = false)
        {
            if (elements == null || elements.Count == 0 || delta == 0)
            {
                return false;
            }

            var focused = Keyboard.FocusedElement as DependencyObject;
            var currentIndex = FindFocusedElementIndex(elements, focused);
            var nextIndex = currentIndex < 0
                ? (delta > 0 ? 0 : elements.Count - 1)
                : currentIndex + delta;

            if (wrap)
            {
                nextIndex = (nextIndex + elements.Count) % elements.Count;
            }
            else
            {
                nextIndex = Math.Max(0, Math.Min(elements.Count - 1, nextIndex));
                if (nextIndex == currentIndex)
                {
                    return true;
                }
            }

            var step = delta > 0 ? 1 : -1;
            var attempts = 0;
            while (nextIndex >= 0 && nextIndex < elements.Count && attempts < elements.Count)
            {
                attempts++;
                if (FocusControllerElement(elements[nextIndex]))
                {
                    return true;
                }

                nextIndex += step;
                if (wrap)
                {
                    nextIndex = (nextIndex + elements.Count) % elements.Count;
                    if (currentIndex >= 0 && nextIndex == currentIndex)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        internal static List<UIElement> GetVisibleFocusableElements(DependencyObject root)
        {
            return EnumerateVisualDescendants<UIElement>(root)
                .Where(IsControllerFocusable)
                .Distinct()
                .ToList();
        }

        internal static List<T> GetVisibleDescendantElements<T>(DependencyObject root)
            where T : UIElement
        {
            return EnumerateVisualDescendants<T>(root)
                .Where(element => element.IsVisible && element.IsEnabled)
                .Distinct()
                .ToList();
        }

        internal static bool ActivateFocusedElement()
        {
            return ActivateElement(Keyboard.FocusedElement as DependencyObject);
        }

        internal static bool ActivateElement(DependencyObject focused)
        {
            if (focused == null)
            {
                return false;
            }

            var comboBox = FindAncestor<ComboBox>(focused) ?? focused as ComboBox;
            if (comboBox != null)
            {
                comboBox.IsDropDownOpen = !comboBox.IsDropDownOpen;
                return true;
            }

            var datePicker = FindAncestor<DatePicker>(focused) ?? focused as DatePicker;
            if (datePicker != null)
            {
                datePicker.IsDropDownOpen = true;
                return true;
            }

            var expander = FindAncestor<Expander>(focused) ?? focused as Expander;
            if (expander != null)
            {
                expander.IsExpanded = !expander.IsExpanded;
                return true;
            }

            var button = FindAncestor<ButtonBase>(focused) ?? focused as ButtonBase;
            return button != null && ActivateButtonBase(button);
        }

        internal static bool OpenContextMenu(FrameworkElement owner, ContextMenu menu)
        {
            if (owner == null ||
                menu == null ||
                menu.Items.Count == 0 ||
                !owner.IsEnabled ||
                owner.Visibility != Visibility.Visible)
            {
                return false;
            }

            menu.PlacementTarget = owner;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
            FocusFirstContextMenuItem(menu);
            return true;
        }

        internal static T FindAncestor<T>(DependencyObject current)
            where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = GetParent(current);
            }

            return null;
        }

        internal static bool IsDescendantOf(DependencyObject current, DependencyObject ancestor)
        {
            if (current == null || ancestor == null)
            {
                return false;
            }

            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = GetParent(current);
            }

            return false;
        }

        private static bool TryHandleContextMenu(ContextMenu menu, ControllerInput input)
        {
            if (menu == null || !menu.IsOpen)
            {
                return false;
            }

            if (IsBackInput(input))
            {
                menu.IsOpen = false;
                return true;
            }

            if (TryGetVerticalDelta(input, out var delta))
            {
                MoveContextMenuSelection(menu, delta);
                return true;
            }

            if (IsAcceptInput(input))
            {
                ActivateFocusedMenuItem(menu);
                return true;
            }

            return false;
        }

        private static bool ActivateButtonBase(ButtonBase button)
        {
            if (button == null || !button.IsEnabled || button.Visibility != Visibility.Visible)
            {
                return false;
            }

            if (button is RadioButton radioButton)
            {
                radioButton.IsChecked = true;
            }
            else if (button is ToggleButton toggleButton)
            {
                toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
            }

            ExecuteCommand(button.Command, button.CommandParameter, button.CommandTarget);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

            if (button is Button plainButton &&
                plainButton.ContextMenu?.IsOpen == true)
            {
                FocusFirstContextMenuItem(plainButton.ContextMenu);
            }

            return true;
        }

        private static void ExecuteCommand(ICommand command, object parameter, IInputElement target)
        {
            if (command == null)
            {
                return;
            }

            if (command is RoutedCommand routedCommand)
            {
                if (routedCommand.CanExecute(parameter, target))
                {
                    routedCommand.Execute(parameter, target);
                }

                return;
            }

            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }

        private static ContextMenu FindOpenContextMenuForWindow(Window window)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            var focusedMenu = FindAncestor<ContextMenu>(focused);
            if (focusedMenu?.IsOpen == true &&
                IsPlacementTargetInsideWindow(focusedMenu.PlacementTarget, window))
            {
                return focusedMenu;
            }

            return FindOpenContextMenuInTree(window);
        }

        private static ContextMenu FindOpenContextMenuInTree(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is FrameworkElement element &&
                element.ContextMenu?.IsOpen == true)
            {
                return element.ContextMenu;
            }

            var childCount = GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var nested = FindOpenContextMenuInTree(VisualTreeHelper.GetChild(root, i));
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void MoveContextMenuSelection(ContextMenu menu, int delta)
        {
            var items = GetFocusableMenuItems(menu);
            if (items.Count == 0)
            {
                return;
            }

            var current = FindAncestor<MenuItem>(Keyboard.FocusedElement as DependencyObject);
            var currentIndex = current != null ? items.IndexOf(current) : -1;
            var nextIndex = currentIndex < 0
                ? (delta > 0 ? 0 : items.Count - 1)
                : (currentIndex + delta + items.Count) % items.Count;

            FocusElement(items[nextIndex]);
        }

        private static void ActivateFocusedMenuItem(ContextMenu menu)
        {
            var item = FindAncestor<MenuItem>(Keyboard.FocusedElement as DependencyObject)
                       ?? GetFocusableMenuItems(menu).FirstOrDefault();
            if (item == null || !item.IsEnabled)
            {
                return;
            }

            if (item.HasItems)
            {
                item.IsSubmenuOpen = true;
                FocusFirstSubmenuItem(item);
                return;
            }

            if (item.IsCheckable)
            {
                item.IsChecked = !item.IsChecked;
            }

            ExecuteCommand(item.Command, item.CommandParameter, item.CommandTarget);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));

            if (!item.StaysOpenOnClick)
            {
                menu.IsOpen = false;
            }
            else
            {
                FocusElement(item);
            }
        }

        private static void FocusFirstSubmenuItem(MenuItem item)
        {
            if (item == null)
            {
                return;
            }

            item.Dispatcher.BeginInvoke(new Action(() =>
            {
                var firstChild = item.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(child => child.IsEnabled && child.Visibility == Visibility.Visible);
                if (firstChild != null)
                {
                    FocusElement(firstChild);
                }
            }), DispatcherPriority.Input);
        }

        private static void FocusFirstContextMenuItem(ContextMenu menu)
        {
            if (menu == null)
            {
                return;
            }

            menu.Dispatcher.BeginInvoke(new Action(() =>
            {
                var first = GetFocusableMenuItems(menu).FirstOrDefault();
                if (first != null)
                {
                    FocusElement(first);
                }
            }), DispatcherPriority.Input);
        }

        private static List<MenuItem> GetFocusableMenuItems(ContextMenu menu)
        {
            if (menu == null)
            {
                return new List<MenuItem>();
            }

            menu.UpdateLayout();
            return menu.Items
                .OfType<MenuItem>()
                .Where(item => item.IsEnabled && item.Visibility == Visibility.Visible)
                .ToList();
        }

        private static int FindFocusedElementIndex(IList<UIElement> elements, DependencyObject focused)
        {
            if (elements == null || focused == null)
            {
                return -1;
            }

            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                if (ReferenceEquals(element, focused) || IsDescendantOf(focused, element))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsControllerFocusable(UIElement element)
        {
            if (element == null || !element.Focusable || !element.IsEnabled || !element.IsVisible)
            {
                return false;
            }

            if (element is ScrollViewer || element is ScrollBar || element is Thumb)
            {
                return false;
            }

            return element is ButtonBase ||
                   element is TextBoxBase ||
                   element is ComboBox ||
                   element is DatePicker ||
                   element is Expander ||
                   element is DataGrid ||
                   element is DataGridRow ||
                   element is ListBoxItem;
        }

        private static bool FocusControllerElement(UIElement element)
        {
            return FocusElement(element);
        }

        private static List<DataGridColumnHeader> GetVisibleDataGridColumnHeaders(DataGrid grid)
        {
            if (grid == null)
            {
                return new List<DataGridColumnHeader>();
            }

            grid.UpdateLayout();
            return EnumerateVisualDescendants<DataGridColumnHeader>(grid)
                .Where(header =>
                    header.Column != null &&
                    header.Column.Visibility == Visibility.Visible &&
                    header.IsEnabled &&
                    header.Visibility == Visibility.Visible)
                .OrderBy(header => header.Column.DisplayIndex)
                .ToList();
        }

        private static T FindVisualDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            return EnumerateVisualDescendants<T>(root).FirstOrDefault();
        }

        private static void PrepareWindowForControllerFocus(Window window)
        {
            foreach (var button in EnumerateVisualDescendants<ButtonBase>(window))
            {
                if (button.ContextMenu != null)
                {
                    button.Focusable = true;
                }
            }
        }

        private static void EnsureFocusInsideWindow(Window window)
        {
            if (window == null || !window.IsActive || window.IsKeyboardFocusWithin)
            {
                return;
            }

            var target = GetVisibleFocusableElements(window).FirstOrDefault();
            if (target != null)
            {
                FocusElement(target);
            }
        }

        private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            var childCount = GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (var nested in EnumerateVisualDescendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static int GetChildrenCount(DependencyObject root)
        {
            if (root == null || !(root is Visual || root is Visual3D))
            {
                return 0;
            }

            return VisualTreeHelper.GetChildrenCount(root);
        }

        private static DependencyObject GetParent(DependencyObject current)
        {
            if (current == null)
            {
                return null;
            }

            if (current is Visual || current is Visual3D)
            {
                var visualParent = VisualTreeHelper.GetParent(current);
                if (visualParent != null)
                {
                    return visualParent;
                }
            }

            if (current is FrameworkContentElement frameworkContentElement)
            {
                return frameworkContentElement.Parent ?? ContentOperations.GetParent(frameworkContentElement);
            }

            return LogicalTreeHelper.GetParent(current) ?? (current as FrameworkElement)?.Parent;
        }

        private static string NormalizeInputKey(ControllerInput input)
        {
            if (TryGetVerticalDelta(input, out var vertical))
            {
                return vertical < 0 ? "Up" : "Down";
            }

            if (TryGetHorizontalDelta(input, out var horizontal))
            {
                return horizontal < 0 ? "Left" : "Right";
            }

            return input.ToString();
        }

        private static bool IsDirectionalInputKey(string inputKey)
        {
            return string.Equals(inputKey, "Up", StringComparison.Ordinal) ||
                   string.Equals(inputKey, "Down", StringComparison.Ordinal) ||
                   string.Equals(inputKey, "Left", StringComparison.Ordinal) ||
                   string.Equals(inputKey, "Right", StringComparison.Ordinal);
        }

        private static bool IsDirectionalKey(Key key)
        {
            return key == Key.Up ||
                   key == Key.Down ||
                   key == Key.Left ||
                   key == Key.Right;
        }

        private struct HandledControllerInput
        {
            public HandledControllerInput(string inputKey, DateTime handledAtUtc)
            {
                InputKey = inputKey;
                HandledAtUtc = handledAtUtc;
            }

            public string InputKey { get; }

            public DateTime HandledAtUtc { get; }
        }
    }
}

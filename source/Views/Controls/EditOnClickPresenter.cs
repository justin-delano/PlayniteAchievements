using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Shows a cheap display template and builds the editor template only for the cell being
    /// edited. A press on the display swaps the editor in and hands it the click: a text box gets
    /// the caret at the pressed character, a date picker or drop-down opens. Once keyboard focus
    /// has left and no drop-down of the editor is open, the display comes back.
    /// </summary>
    /// <remarks>
    /// For grid cells that carry live editors in every row. Building those per realized row was
    /// most of what a filter change or scroll cost in the Manage Achievements editor; this pays
    /// for one editor at a time instead.
    ///
    /// The swap is driven by a press, not by hover: a wheel scroll keeps the pointer over the
    /// grid, so a hover swap rebuilt an editor on every scroll step and measured slower than
    /// keeping the editors live.
    /// </remarks>
    public sealed class EditOnClickPresenter : Decorator
    {
        public static readonly DependencyProperty DisplayTemplateProperty =
            DependencyProperty.Register(
                nameof(DisplayTemplate),
                typeof(DataTemplate),
                typeof(EditOnClickPresenter),
                new PropertyMetadata(null, OnTemplatesChanged));

        public static readonly DependencyProperty EditorTemplateProperty =
            DependencyProperty.Register(
                nameof(EditorTemplate),
                typeof(DataTemplate),
                typeof(EditOnClickPresenter),
                new PropertyMetadata(null, OnTemplatesChanged));

        private static readonly DependencyPropertyKey IsEditingPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsEditing),
                typeof(bool),
                typeof(EditOnClickPresenter),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsEditingProperty = IsEditingPropertyKey.DependencyProperty;

        private readonly List<Action> _unsubscribe = new List<Action>();
        private bool _endQueued;

        public EditOnClickPresenter()
        {
            Focusable = false;
            DataContextChanged += (_, __) => EndEdit();
        }

        public DataTemplate DisplayTemplate
        {
            get => (DataTemplate)GetValue(DisplayTemplateProperty);
            set => SetValue(DisplayTemplateProperty, value);
        }

        public DataTemplate EditorTemplate
        {
            get => (DataTemplate)GetValue(EditorTemplateProperty);
            set => SetValue(EditorTemplateProperty, value);
        }

        public bool IsEditing
        {
            get => (bool)GetValue(IsEditingProperty);
            private set => SetValue(IsEditingPropertyKey, value);
        }

        private static void OnTemplatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Only the template on screen is rebuilt: XAML sets both, and loading the display again
            // when the editor template arrives would build every resting cell twice.
            var presenter = (EditOnClickPresenter)d;
            var isActive = presenter.IsEditing
                ? e.Property == EditorTemplateProperty
                : e.Property == DisplayTemplateProperty;
            if (isActive)
            {
                presenter.Child = (e.NewValue as DataTemplate)?.LoadContent() as UIElement;
            }
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            if (IsEditing || EditorTemplate == null || IsInsideButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            var point = e.GetPosition(this);
            BeginEdit();

            // After the press has finished routing: the grid cell focuses itself on mouse-down,
            // which would take focus straight back from an editor focused here.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusEditorAt(point)));
        }

        protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusWithinChanged(e);
            if (!(bool)e.NewValue)
            {
                QueueEndEdit();
            }
        }

        private void BeginEdit()
        {
            IsEditing = true;
            Child = EditorTemplate?.LoadContent() as UIElement;
            UpdateLayout();
            SubscribeToDropDowns();
        }

        private void QueueEndEdit()
        {
            if (!IsEditing || _endQueued)
            {
                return;
            }

            // Deferred so a focus move between the editor's own parts, or into a drop-down it just
            // opened, has settled before deciding the edit is over.
            _endQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _endQueued = false;
                if (IsKeyboardFocusWithin || IsAnyDropDownOpen())
                {
                    return;
                }

                EndEdit();
            }));
        }

        private void EndEdit()
        {
            if (!IsEditing)
            {
                return;
            }

            foreach (var unsubscribe in _unsubscribe)
            {
                unsubscribe();
            }

            _unsubscribe.Clear();
            IsEditing = false;
            Child = DisplayTemplate?.LoadContent() as UIElement;
        }

        private void FocusEditorAt(Point point)
        {
            if (!IsEditing)
            {
                return;
            }

            var target = FindEditorAt(point) ?? FindFirst<TextBox>(this);
            switch (target)
            {
                case TextBox textBox:
                    textBox.Focus();
                    textBox.CaretIndex = ResolveCaretIndex(textBox, TranslatePoint(point, textBox));
                    break;

                case DatePicker datePicker:
                    datePicker.Focus();
                    datePicker.IsDropDownOpen = true;
                    break;

                case ComboBox comboBox:
                    comboBox.Focus();
                    comboBox.IsDropDownOpen = true;
                    break;

                default:
                    // Nothing to hand the click to; give the display back rather than leave an
                    // editor up that nothing has focused and nothing will close.
                    EndEdit();
                    break;
            }
        }

        private FrameworkElement FindEditorAt(Point point)
        {
            for (var node = InputHitTest(point) as DependencyObject; node != null && !ReferenceEquals(node, this); node = GetParent(node))
            {
                if (node is TextBox || node is DatePicker || node is ComboBox)
                {
                    return (FrameworkElement)node;
                }
            }

            return null;
        }

        private static int ResolveCaretIndex(TextBox textBox, Point point)
        {
            var length = textBox.Text?.Length ?? 0;
            var index = textBox.GetCharacterIndexFromPoint(point, snapToText: true);
            if (index < 0)
            {
                return length;
            }

            // The index is the character under the point; a press on its right half means after it.
            var leading = textBox.GetRectFromCharacterIndex(index, trailingEdge: false);
            var trailing = textBox.GetRectFromCharacterIndex(index, trailingEdge: true);
            if (!leading.IsEmpty && !trailing.IsEmpty && point.X > (leading.Left + trailing.Left) / 2)
            {
                index++;
            }

            return Math.Max(0, Math.Min(index, length));
        }

        private void SubscribeToDropDowns()
        {
            foreach (var child in Descendants(this))
            {
                if (child is DatePicker datePicker)
                {
                    RoutedEventHandler closed = (_, __) => QueueEndEdit();
                    datePicker.CalendarClosed += closed;
                    _unsubscribe.Add(() => datePicker.CalendarClosed -= closed);
                }
                else if (child is ComboBox comboBox)
                {
                    EventHandler closed = (_, __) => QueueEndEdit();
                    comboBox.DropDownClosed += closed;
                    _unsubscribe.Add(() => comboBox.DropDownClosed -= closed);
                }
            }
        }

        private bool IsAnyDropDownOpen()
        {
            foreach (var child in Descendants(this))
            {
                if ((child is DatePicker datePicker && datePicker.IsDropDownOpen) ||
                    (child is ComboBox comboBox && comboBox.IsDropDownOpen))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInsideButton(DependencyObject source)
        {
            for (var node = source; node != null && !ReferenceEquals(node, this); node = GetParent(node))
            {
                if (node is ButtonBase)
                {
                    return true;
                }
            }

            return false;
        }

        private static T FindFirst<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in Descendants(root))
            {
                if (child is T match)
                {
                    return match;
                }
            }

            return null;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static DependencyObject GetParent(DependencyObject node)
        {
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
            }

            return LogicalTreeHelper.GetParent(node);
        }
    }
}

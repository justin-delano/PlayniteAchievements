using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Builds its template only while <see cref="IsActive"/> is true, and drops it again when the
    /// flag clears.
    /// </summary>
    /// <remarks>
    /// For the parts of a grid cell that most rows never show, such as a hidden achievement's
    /// reveal overlay or a customization marker. WPF creates and binds a Collapsed element as
    /// fully as a visible one, so a cell that carries those parts collapsed still pays for them on
    /// every row it realizes; in the Manage Achievements editor that measured about 0.9 ms per
    /// cell. Built on demand, a row that does not show them costs nothing for them.
    /// </remarks>
    public sealed class LazyContent : Decorator
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(
                nameof(IsActive),
                typeof(bool),
                typeof(LazyContent),
                new PropertyMetadata(false, OnChanged));

        public static readonly DependencyProperty TemplateProperty =
            DependencyProperty.Register(
                nameof(Template),
                typeof(DataTemplate),
                typeof(LazyContent),
                new PropertyMetadata(null, OnChanged));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public DataTemplate Template
        {
            get => (DataTemplate)GetValue(TemplateProperty);
            set => SetValue(TemplateProperty, value);
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var lazy = (LazyContent)d;
            if (!lazy.IsActive || lazy.Template == null)
            {
                lazy.Child = null;
            }
            else if (lazy.Child == null || e.Property == TemplateProperty)
            {
                lazy.Child = lazy.Template.LoadContent() as UIElement;
            }
        }
    }
}

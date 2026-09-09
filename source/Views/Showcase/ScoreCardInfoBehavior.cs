using System.Windows;
using PlayniteAchievements.Views.Controls;
using PlayniteAchievements.Views.Dialogs;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Opens the score-info dialog when a hosted <see cref="ScoreCardControl"/> raises
    /// InfoRequested, letting the Scores widget template stay declarative instead of wiring the
    /// event in code-behind.
    /// </summary>
    public static class ScoreCardInfoBehavior
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(ScoreCardInfoBehavior),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject element) =>
            (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) =>
            element.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is ScoreCardControl control))
            {
                return;
            }

            control.InfoRequested -= OnInfoRequested;
            if (e.NewValue is bool enabled && enabled)
            {
                control.InfoRequested += OnInfoRequested;
            }
        }

        private static void OnInfoRequested(object sender, RoutedEventArgs e)
        {
            ScoreInfoDialogPresenter.Show();
        }
    }
}

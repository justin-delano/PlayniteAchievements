using System.Windows;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// Provides an attached property for tracking reveal state of hidden achievements.
    /// Used by legacy theme controls to implement click-to-reveal functionality.
    /// </summary>
    public static class HiddenRevealHelper
    {
        /// <summary>
        /// Attached property that tracks whether a hidden achievement has been revealed.
        /// Binds two-way so the converter can read the current state.
        /// </summary>
        public static readonly DependencyProperty IsRevealedProperty =
            DependencyProperty.RegisterAttached(
                "IsRevealed",
                typeof(bool),
                typeof(HiddenRevealHelper),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>
        /// Gets the reveal state of the element.
        /// </summary>
        public static bool GetIsRevealed(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsRevealedProperty);
        }

        /// <summary>
        /// Sets the reveal state of the element.
        /// </summary>
        public static void SetIsRevealed(DependencyObject obj, bool value)
        {
            obj.SetValue(IsRevealedProperty, value);
        }
    }
}

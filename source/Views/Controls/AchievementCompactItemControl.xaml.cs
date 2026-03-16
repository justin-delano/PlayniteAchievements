using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Reusable compact achievement item control with icon, progress bar, and rarity glow.
    /// Designed for horizontal scrolling lists in theme integration.
    /// </summary>
    public partial class AchievementCompactItemControl : UserControl
    {
        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(AchievementCompactItemControl),
                new PropertyMetadata(48.0, OnIconSizeChanged));

        /// <summary>
        /// Gets or sets the size of the achievement icon (both width and height).
        /// Default is 48 to match legacy SuccessStory styling.
        /// </summary>
        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        /// <summary>
        /// Identifies the ShowRarityGlow dependency property.
        /// When set, overrides the item's ShowRarityGlow property.
        /// </summary>
        public static readonly DependencyProperty ShowRarityGlowOverrideProperty =
            DependencyProperty.Register(nameof(ShowRarityGlowOverride), typeof(bool?),
                typeof(AchievementCompactItemControl), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets an override for the rarity glow visibility.
        /// When null, uses the item's ShowRarityGlow property.
        /// </summary>
        public bool? ShowRarityGlowOverride
        {
            get => (bool?)GetValue(ShowRarityGlowOverrideProperty);
            set => SetValue(ShowRarityGlowOverrideProperty, value);
        }

        /// <summary>
        /// Identifies the ShowRarityBarOverride dependency property.
        /// When set, overrides the item's ShowRarityBar property.
        /// </summary>
        public static readonly DependencyProperty ShowRarityBarOverrideProperty =
            DependencyProperty.Register(nameof(ShowRarityBarOverride), typeof(bool?),
                typeof(AchievementCompactItemControl), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets an override for the rarity bar visibility.
        /// When null, uses the item's ShowRarityBar property.
        /// </summary>
        public bool? ShowRarityBarOverride
        {
            get => (bool?)GetValue(ShowRarityBarOverrideProperty);
            set => SetValue(ShowRarityBarOverrideProperty, value);
        }

        private static void OnIconSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AchievementCompactItemControl control && e.NewValue is double size)
            {
                // Match legacy behavior - control size matches icon size
                control.Width = size;
                control.Height = size;
            }
        }

        public AchievementCompactItemControl()
        {
            InitializeComponent();
            Width = IconSize;
            Height = IconSize;

            // Handle click to reveal hidden achievements
            MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is AchievementDisplayItem item && item.CanReveal)
            {
                item.ToggleReveal();
                e.Handled = true;
            }
        }
    }
}

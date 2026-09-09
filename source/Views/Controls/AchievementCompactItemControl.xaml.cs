using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Reusable compact achievement item control with icon, progress bar, and rarity glow.
    /// Designed for horizontal scrolling lists in theme integration.
    /// </summary>
    public partial class AchievementCompactItemControl : UserControl
    {
        private bool _reopenToolTipAfterReveal;

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(AchievementCompactItemControl),
                new PropertyMetadata(48.0, OnIconSizeChanged));

        public static readonly DependencyProperty ShowRarityGlowProperty =
            DependencyProperty.Register(
                nameof(ShowRarityGlow),
                typeof(bool),
                typeof(AchievementCompactItemControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty AnimateRarityGlowsProperty =
            DependencyProperty.Register(
                nameof(AnimateRarityGlows),
                typeof(bool),
                typeof(AchievementCompactItemControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty UseLargeRarityGlowProperty =
            DependencyProperty.Register(
                nameof(UseLargeRarityGlow),
                typeof(bool),
                typeof(AchievementCompactItemControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty SoftGlowTiersProperty =
            DependencyProperty.Register(
                nameof(SoftGlowTiers),
                typeof(RaritySelection),
                typeof(AchievementCompactItemControl),
                new PropertyMetadata(RaritySelection.All));

        public static readonly DependencyProperty RayGlowTiersProperty =
            DependencyProperty.Register(
                nameof(RayGlowTiers),
                typeof(RaritySelection),
                typeof(AchievementCompactItemControl),
                new PropertyMetadata(RaritySelection.None));

        public static readonly DependencyProperty ShowHardcoreBorderProperty =
            DependencyProperty.Register(
                nameof(ShowHardcoreBorder),
                typeof(bool),
                typeof(AchievementCompactItemControl),
                new PropertyMetadata(true));

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
        /// Gets or sets whether this reusable item renders its rarity glow. Hosts pass their
        /// established setting explicitly so the item does not depend on a particular ancestor.
        /// </summary>
        public bool ShowRarityGlow
        {
            get => (bool)GetValue(ShowRarityGlowProperty);
            set => SetValue(ShowRarityGlowProperty, value);
        }

        /// <summary>Gets or sets whether the rarity glow gently pulses.</summary>
        public bool AnimateRarityGlows
        {
            get => (bool)GetValue(AnimateRarityGlowsProperty);
            set => SetValue(AnimateRarityGlowsProperty, value);
        }

        /// <summary>
        /// Uses the full achievement-grid glow instead of the compact-list glow. Dense horizontal
        /// lists retain the compact default; hosts with room around each icon can opt in.
        /// </summary>
        public bool UseLargeRarityGlow
        {
            get => (bool)GetValue(UseLargeRarityGlowProperty);
            set => SetValue(UseLargeRarityGlowProperty, value);
        }

        /// <summary>
        /// Which tiers show the soft halo. Bound to the global setting in the constructor so the
        /// item renders the same glow wherever it is hosted, with or without a list ancestor.
        /// </summary>
        public RaritySelection SoftGlowTiers
        {
            get => (RaritySelection)GetValue(SoftGlowTiersProperty);
            set => SetValue(SoftGlowTiersProperty, value);
        }

        /// <summary>Ray counterpart to <see cref="SoftGlowTiers"/>.</summary>
        public RaritySelection RayGlowTiers
        {
            get => (RaritySelection)GetValue(RayGlowTiersProperty);
            set => SetValue(RayGlowTiersProperty, value);
        }

        /// <summary>Whether hardcore unlocks trade the glow for a solid border.</summary>
        public bool ShowHardcoreBorder
        {
            get => (bool)GetValue(ShowHardcoreBorderProperty);
            set => SetValue(ShowHardcoreBorderProperty, value);
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

            // The glow tiers and hardcore border are global appearance settings, so the item
            // sources them itself instead of reaching for a hosting list that may not exist
            // (the showcase mosaic hosts these icons with no list ancestor).
            RarityAppearanceHelper.BindSoftGlowTiers(this, SoftGlowTiersProperty);
            RarityAppearanceHelper.BindRayGlowTiers(this, RayGlowTiersProperty);
            RarityAppearanceHelper.BindShowHardcoreBorder(this, ShowHardcoreBorderProperty);

            // Handle click to reveal hidden achievements
            PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            MouseLeave += OnMouseLeave;
            Unloaded += OnUnloaded;
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Consume only the click that reveals an obscured achievement; revealed
            // (or never-obscured) items let the click continue so the hosting list can
            // open the achievements window focused on this achievement.
            if (DataContext is AchievementDisplayItem item && item.CanReveal && !item.IsRevealed)
            {
                _reopenToolTipAfterReveal = ItemToolTip?.IsOpen == true;
                item.ToggleReveal();
                e.Handled = true;
            }
        }

        private void ItemToolTip_OnClosed(object sender, RoutedEventArgs e)
        {
            if (!_reopenToolTipAfterReveal)
            {
                return;
            }

            _reopenToolTipAfterReveal = false;
            Dispatcher.BeginInvoke(new Action(ReopenToolTipIfHovered), DispatcherPriority.Input);
        }

        private void ReopenToolTipIfHovered()
        {
            if (ItemToolTip == null || !IsLoaded || !IsVisible || !IsMouseOver)
            {
                return;
            }

            ItemToolTip.IsOpen = true;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _reopenToolTipAfterReveal = false;

            if (ItemToolTip?.IsOpen == true)
            {
                ItemToolTip.IsOpen = false;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _reopenToolTipAfterReveal = false;

            if (ItemToolTip?.IsOpen == true)
            {
                ItemToolTip.IsOpen = false;
            }
        }
    }
}

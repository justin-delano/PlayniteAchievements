using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Controls
{
    public partial class ScoreCardControl : UserControl
    {
        public static readonly DependencyProperty ScoreCardProperty =
            DependencyProperty.Register(
                nameof(ScoreCard),
                typeof(ScoreCardViewModel),
                typeof(ScoreCardControl),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsFeaturedProperty =
            DependencyProperty.Register(
                nameof(IsFeatured),
                typeof(bool),
                typeof(ScoreCardControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty FlatProperty =
            DependencyProperty.Register(
                nameof(Flat),
                typeof(bool),
                typeof(ScoreCardControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty CompactProperty =
            DependencyProperty.Register(
                nameof(Compact),
                typeof(bool),
                typeof(ScoreCardControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty BadgeOnlyProperty =
            DependencyProperty.Register(
                nameof(BadgeOnly),
                typeof(bool),
                typeof(ScoreCardControl),
                new PropertyMetadata(false));

        public ScoreCardControl()
        {
            InitializeComponent();

            // Every host's card recolors from here when the badge appearance settings change. The
            // static event roots its handlers, so the hook lives only while the card is loaded.
            Loaded += (_, __) =>
            {
                RarityAppearanceHelper.AppearanceChanged -= OnAppearanceChanged;
                RarityAppearanceHelper.AppearanceChanged += OnAppearanceChanged;
            };
            Unloaded += (_, __) => RarityAppearanceHelper.AppearanceChanged -= OnAppearanceChanged;
        }

        private void OnAppearanceChanged(object sender, EventArgs e)
        {
            var card = ScoreCard;
            card?.RefreshBadgeStyle(
                PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.UseUniformRarityBadges
                ?? card.UseUniformRarityBadges);
        }

        public ScoreCardViewModel ScoreCard
        {
            get => (ScoreCardViewModel)GetValue(ScoreCardProperty);
            set => SetValue(ScoreCardProperty, value);
        }

        public bool IsFeatured
        {
            get => (bool)GetValue(IsFeaturedProperty);
            set => SetValue(IsFeaturedProperty, value);
        }

        /// <summary>
        /// Drops the card's own accent background and border for hosts that already provide
        /// chrome (showcase widget blocks).
        /// </summary>
        public bool Flat
        {
            get => (bool)GetValue(FlatProperty);
            set => SetValue(FlatProperty, value);
        }

        /// <summary>
        /// Folds the label into the tier line and moves the level caption into the tooltip, for
        /// hosts with little vertical room (the Overview header).
        /// </summary>
        public bool Compact
        {
            get => (bool)GetValue(CompactProperty);
            set => SetValue(CompactProperty, value);
        }

        /// <summary>
        /// Shows only the badge (and mastery line), with the tier and points moved into the
        /// tooltip, for hosts too narrow for the text.
        /// </summary>
        public bool BadgeOnly
        {
            get => (bool)GetValue(BadgeOnlyProperty);
            set => SetValue(BadgeOnlyProperty, value);
        }
    }
}

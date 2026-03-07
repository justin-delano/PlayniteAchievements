using System.Windows;
using System.Windows.Controls;
using Playnite.SDK.Controls;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements compact list control for theme integration.
    /// Displays achievement icons in a horizontal wrap panel.
    /// </summary>
    public partial class AchievementCompactListControl : ThemeControlBase
    {
        /// <summary>
        /// Gets a value indicating whether this control should subscribe to theme data change notifications.
        /// </summary>
        protected override bool EnableAutomaticThemeDataUpdates => true;

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(AchievementCompactListControl),
                new PropertyMetadata(48.0));

        /// <summary>
        /// Gets or sets the size of each achievement icon.
        /// </summary>
        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        public AchievementCompactListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Determines whether a change raised from ThemeData should trigger a refresh.
        /// </summary>
        protected override bool ShouldHandleThemeDataChange(string propertyName)
        {
            return propertyName == nameof(Models.ThemeIntegration.ThemeData.AllAchievementDisplayItems);
        }

        /// <summary>
        /// Called when theme data changes and the compact list should be refreshed.
        /// Forces the ListBox to refresh its ItemsSource binding.
        /// </summary>
        protected override void OnThemeDataUpdated()
        {
            // Force the ItemsSource binding to refresh
            // This ensures the ListBox picks up changes to Theme.AllAchievements
            if (AchievementsList != null)
            {
                var binding = AchievementsList.GetBindingExpression(ItemsControl.ItemsSourceProperty);
                binding?.UpdateTarget();
            }
        }
    }
}

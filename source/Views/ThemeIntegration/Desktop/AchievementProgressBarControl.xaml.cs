using PlayniteAchievements.Views.ThemeIntegration.Base;
using PlayniteAchievements.Models.ThemeIntegration;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements progress bar control for theme integration.
    /// Displays progress bar with percentage overlay and rarity badges.
    /// Uses the effective theme source so settings previews can inject mock data.
    /// </summary>
    public partial class AchievementProgressBarControl : ThemeControlBase
    {
        /// <summary>
        /// Gets a value indicating whether this control should subscribe to theme data change notifications.
        /// </summary>
        protected override bool EnableAutomaticThemeDataUpdates => true;
        protected override bool UsesThemeBindings => true;

        public AchievementProgressBarControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Determines whether a change raised from native theme bindings should trigger a refresh.
        /// </summary>
        protected override bool ShouldHandleThemeDataChange(string propertyName)
        {
            return propertyName == nameof(NativeThemeBindings.UltraRare) ||
                   propertyName == nameof(NativeThemeBindings.Rare) ||
                   propertyName == nameof(NativeThemeBindings.Uncommon) ||
                   propertyName == nameof(NativeThemeBindings.Common);
        }

        /// <summary>
        /// Called when theme data changes. Updates the rarity badges.
        /// </summary>
        protected override void OnThemeDataUpdated()
        {
            var theme = EffectiveTheme;
            if (theme != null)
            {
                Badges?.UpdateFromRarityStats(theme.UltraRare, theme.Rare, theme.Uncommon, theme.Common);
            }
        }
    }
}

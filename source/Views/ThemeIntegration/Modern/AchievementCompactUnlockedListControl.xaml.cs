using System.Collections.Generic;
using System.Windows.Controls;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.ThemeIntegration;

namespace PlayniteAchievements.Views.ThemeIntegration.Modern
{
    /// <summary>
    /// Modern theme integration control displaying unlocked achievements in a horizontal scrolling row.
    /// Shows compact achievement icons with progress bars and rarity glow effects.
    /// Filters to show only unlocked achievements.
    /// </summary>
    public partial class AchievementCompactUnlockedListControl : AchievementCompactListControlBase
    {
        public AchievementCompactUnlockedListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Returns true only for unlocked achievements.
        /// </summary>
        protected override bool FilterAchievement(AchievementDetail achievement) => achievement.Unlocked;

        /// <summary>
        /// Selects the pre-sorted achievement collection based on CompactUnlockedListSortMode and direction.
        /// None (default) uses the selected-game canonical base order.
        /// </summary>
        protected override List<AchievementDetail> GetOrderedAchievements(ModernThemeBindings theme)
        {
            if (theme == null)
            {
                return new List<AchievementDetail>();
            }

            var settings = EffectiveSettings?.Persisted;
            if (settings == null)
            {
                return theme.AllAchievements ?? new List<AchievementDetail>();
            }

            switch (settings.CompactUnlockedListSortMode)
            {
                case CompactListSortMode.UnlockTime:
                    return settings.CompactUnlockedListSortDescending
                        ? theme.AchievementsNewestFirst ?? theme.AllAchievements
                        : theme.AchievementsOldestFirst ?? theme.AllAchievements;
                case CompactListSortMode.Rarity:
                    return settings.CompactUnlockedListSortDescending
                        ? theme.AchievementsRarityDesc ?? theme.AllAchievements
                        : theme.AchievementsRarityAsc ?? theme.AllAchievements;
                default:
                    return theme.AllAchievements ?? new List<AchievementDetail>();
            }
        }

        protected override string GetOrderedAchievementsPropertyName()
        {
            var settings = EffectiveSettings?.Persisted;
            if (settings == null)
            {
                return nameof(ModernThemeBindings.AllAchievements);
            }

            switch (settings.CompactUnlockedListSortMode)
            {
                case CompactListSortMode.UnlockTime:
                    return settings.CompactUnlockedListSortDescending
                        ? nameof(ModernThemeBindings.AchievementsNewestFirst)
                        : nameof(ModernThemeBindings.AchievementsOldestFirst);
                case CompactListSortMode.Rarity:
                    return settings.CompactUnlockedListSortDescending
                        ? nameof(ModernThemeBindings.AchievementsRarityDesc)
                        : nameof(ModernThemeBindings.AchievementsRarityAsc);
                default:
                    return nameof(ModernThemeBindings.AllAchievements);
            }
        }

        /// <summary>
        /// Refreshes the ItemsControl ItemsSource binding.
        /// </summary>
        protected override void RefreshItemsSource()
        {
            if (AchievementsList != null)
            {
                AchievementsList.ItemsSource = DisplayItems;
            }
        }
    }
}


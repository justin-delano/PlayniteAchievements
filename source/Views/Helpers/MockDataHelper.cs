using System;
using System.Collections.Generic;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Helper class for creating mock achievement data for settings previews.
    /// </summary>
    public static class MockDataHelper
    {
        private const string UnlockedIconPath = "pack://application:,,,/PlayniteAchievements;component/Resources/UnlockedAchIcon.png";
        private const string LockedIconPath = "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";

        /// <summary>
        /// Creates a mock AchievementDisplayItem for preview purposes.
        /// </summary>
        /// <param name="unlocked">Whether the achievement is unlocked.</param>
        /// <param name="hidden">Whether the achievement is a hidden achievement.</param>
        /// <param name="globalPercent">Global unlock percentage (null for no rarity data).</param>
        /// <param name="displayName">Display name for the achievement.</param>
        /// <param name="description">Description for the achievement.</param>
        /// <param name="showRarityBar">Whether to show the rarity bar.</param>
        /// <param name="showRarityGlow">Whether to show the rarity glow.</param>
        /// <returns>A mock AchievementDisplayItem.</returns>
        public static AchievementDisplayItem CreateMockAchievement(
            bool unlocked = true,
            bool hidden = false,
            double? globalPercent = 45.0,
            string displayName = "Mock Achievement",
            string description = "Mock description for preview",
            bool showRarityBar = true,
            bool showRarityGlow = true)
        {
            var item = new AchievementDisplayItem
            {
                DisplayName = displayName,
                Description = description,
                Unlocked = unlocked,
                Hidden = hidden,
                GlobalPercentUnlocked = globalPercent,
                IconPath = unlocked ? UnlockedIconPath : LockedIconPath,
                ShowHiddenIcon = true,
                ShowHiddenTitle = true,
                ShowHiddenDescription = true,
                ShowLockedIcon = true,
                ShowRarityGlow = showRarityGlow,
                ShowRarityBar = showRarityBar,
                GameName = "Preview Game"
            };

            if (unlocked)
            {
                item.UnlockTimeUtc = DateTime.UtcNow.AddDays(-1);
            }

            return item;
        }

        /// <summary>
        /// Creates a list of mock AchievementDisplayItems for compact list preview.
        /// </summary>
        /// <param name="count">Number of items to create.</param>
        /// <param name="showRarityBar">Whether to show the rarity bar.</param>
        /// <param name="showRarityGlow">Whether to show the rarity glow.</param>
        /// <returns>List of mock achievement items.</returns>
        public static List<AchievementDisplayItem> CreateMockCompactListItems(int count = 5, bool showRarityBar = true, bool showRarityGlow = true)
        {
            var items = new List<AchievementDisplayItem>();

            // Create varied achievements for preview
            var achievements = new[]
            {
                new { Name = "Ultra Rare Victory", Percent = 2.5, Unlocked = true, Hidden = false },
                new { Name = "Rare Challenge", Percent = 8.0, Unlocked = true, Hidden = false },
                new { Name = "Uncommon Feat", Percent = 25.0, Unlocked = true, Hidden = false },
                new { Name = "Hidden Secret", Percent = 15.0, Unlocked = false, Hidden = true },
                new { Name = "Common Task", Percent = 75.0, Unlocked = true, Hidden = false }
            };

            for (int i = 0; i < count && i < achievements.Length; i++)
            {
                var a = achievements[i];
                items.Add(CreateMockAchievement(
                    unlocked: a.Unlocked,
                    hidden: a.Hidden,
                    globalPercent: a.Percent,
                    displayName: a.Name,
                    description: $"Preview description for {a.Name}",
                    showRarityBar: showRarityBar,
                    showRarityGlow: showRarityGlow
                ));
            }

            return items;
        }

        /// <summary>
        /// Updates the ShowRarityBar property on all items in the list.
        /// </summary>
        /// <param name="items">The list of items to update.</param>
        /// <param name="showRarityBar">Whether to show the rarity bar.</param>
        public static void UpdateRarityBarVisibility(IList<AchievementDisplayItem> items, bool showRarityBar)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                item.ShowRarityBar = showRarityBar;
            }
        }

        /// <summary>
        /// Updates the ShowRarityGlow property on all items in the list.
        /// </summary>
        /// <param name="items">The list of items to update.</param>
        /// <param name="showRarityGlow">Whether to show the rarity glow.</param>
        public static void UpdateRarityGlowVisibility(IList<AchievementDisplayItem> items, bool showRarityGlow)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                item.ShowRarityGlow = showRarityGlow;
            }
        }
    }
}

// --SUCCESSSTORY--
using System.Collections.Generic;
using System.Windows.Controls;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Views.ThemeIntegration.Base;
using PlayniteAchievements.Views.ThemeIntegration.SuccessStory.Controls;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible compact locked control for theme integration.
    /// Displays locked achievements in a horizontal grid, with "+X more" in the last column.
    /// </summary>
    public partial class SuccessStoryPluginCompactLockedControl : CompactAchievementControlBase
    {
        protected override Grid CompactViewGrid => PART_ScCompactView;

        public SuccessStoryPluginCompactLockedControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        protected override List<AchievementDetail> GetSourceAchievements()
        {
            var all = Plugin?.Settings?.SuccessStoryTheme?.ListAchievements;
            if (all == null || all.Count == 0)
            {
                return new List<AchievementDetail>();
            }

            var result = new List<AchievementDetail>();
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a != null && !a.Unlocked)
                {
                    result.Add(a);
                }
            }

            return result;
        }

        protected override int GetTotalCount()
        {
            return Plugin?.Settings?.SuccessStoryTheme?.Locked ?? 0;
        }

        protected override string GetHasAchievementPropertyName()
        {
            return nameof(HasLocked);
        }

        protected override string GetTotalCountPropertyName()
        {
            return nameof(TotalLocked);
        }

        protected override string[] GetSettingsPropertyNames()
        {
            return new[]
            {
                nameof(SuccessStoryThemeData.ListAchievements),
                nameof(SuccessStoryThemeData.Locked)
            };
        }

        protected override AchievementImage CreateAchievementImage(AchievementDetail achievement)
        {
            return new AchievementImage
            {
                Width = IconHeight,
                Height = IconHeight,
                Margin = new System.Windows.Thickness(4),
                ToolTip = achievement.DisplayName,
                // Use the locked icon directly; grayscale is handled automatically when needed.
                Icon = achievement.LockedIconDisplay,
                IconCustom = string.Empty,
                IsLocked = true,
                Percent = achievement.GlobalPercentUnlocked ?? 0,
                EnableRaretyIndicator = true,
                DisplayRaretyValue = false
            };
        }

        public bool HasLocked => GetTotalCount() > 0;

        public int TotalLocked => GetTotalCount();

        // RemainingCount is provided by the base class
    }
}
// --END SUCCESSSTORY--

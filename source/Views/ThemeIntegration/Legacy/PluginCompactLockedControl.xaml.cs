// --SUCCESSSTORY--
using System.Collections.Generic;
using System.Windows.Controls;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Views.ThemeIntegration.Base;
using PlayniteAchievements.Views.ThemeIntegration.Legacy.Controls;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible compact locked control for theme integration.
    /// Displays locked achievements in a horizontal grid, with "+X more" in the last column.
    /// </summary>
    public partial class PluginCompactLockedControl : CompactAchievementControlBase
    {
        protected override Grid CompactViewGrid => PART_ScCompactView;

        public PluginCompactLockedControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        protected override List<AchievementDetail> GetSourceAchievements()
        {
            var all = Plugin?.Settings?.LegacyTheme?.ListAchievements;
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
            return Plugin?.Settings?.LegacyTheme?.Locked ?? 0;
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
                nameof(LegacyThemeData.ListAchievements),
                nameof(LegacyThemeData.Locked)
            };
        }

        protected override AchievementImage CreateAchievementImage(AchievementDetail achievement)
        {
            return new AchievementImage
            {
                Width = IconHeight,
                Height = IconHeight,
                ToolTip = achievement.DisplayName,
                // Use UnlockedIconDisplay for both states; grayscale is applied when IsLocked=true
                Icon = achievement.UnlockedIconDisplay,
                IconCustom = achievement.UnlockedIconDisplay,
                IsLocked = true,
                Percent = achievement.GlobalPercentUnlocked ?? 0,
                EnableRaretyIndicator = true,
                DisplayRaretyValue = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        }

        public bool HasLocked => GetTotalCount() > 0;

        public int TotalLocked => GetTotalCount();

        // RemainingCount is provided by the base class
    }
}
// --END SUCCESSSTORY--

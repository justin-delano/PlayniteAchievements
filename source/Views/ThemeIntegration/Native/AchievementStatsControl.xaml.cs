using Playnite.SDK.Controls;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Native
{
    /// <summary>
    /// Native PlayniteAchievements stats control for theme integration.
    /// Receives game context changes via GameContextChanged and updates achievement data.
    /// </summary>
    public partial class AchievementStatsControl : AchievementThemeControlBase
    {
        public AchievementStatsControl()
        {
            InitializeComponent();
        }
    }
}

using Playnite.SDK.Controls;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Native
{
    /// <summary>
    /// Native PlayniteAchievements button control for theme integration.
    /// Receives game context changes via GameContextChanged and updates achievement data.
    /// </summary>
    public partial class AchievementButtonControl : ThemeControlBase
    {
        public AchievementButtonControl()
        {
            InitializeComponent();
        }
    }
}

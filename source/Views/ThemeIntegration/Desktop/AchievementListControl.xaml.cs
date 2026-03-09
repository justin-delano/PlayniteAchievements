using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements list control for theme integration.
    /// Displays achievements in a DataGrid with sorting and virtualization.
    /// Binds directly to Plugin.Settings.Theme.AllAchievementDisplayItems.
    /// </summary>
    public partial class AchievementListControl : ThemeControlBase
    {
        public AchievementListControl()
        {
            InitializeComponent();
        }
    }
}

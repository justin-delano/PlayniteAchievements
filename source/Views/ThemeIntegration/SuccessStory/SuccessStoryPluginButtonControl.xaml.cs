// --SUCCESSSTORY--
using System;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible button control for theme integration.
    /// Uses native PlayniteAchievements properties (AchievementCount, UnlockedCount, AllUnlocked).
    /// Matches the original SuccessStory plugin styling.
    /// </summary>
    public partial class SuccessStoryPluginButtonControl : SuccessStoryThemeControlBase
    {
        #region DisplayDetails Property

        public static readonly DependencyProperty DisplayDetailsProperty =
            PlayniteAchievements.Views.ThemeIntegration.Base.DependencyPropertyHelper.RegisterBoolProperty(
                nameof(DisplayDetails),
                typeof(SuccessStoryPluginButtonControl),
                defaultValue: true);

        public bool DisplayDetails
        {
            get => (bool)GetValue(DisplayDetailsProperty);
            set => SetValue(DisplayDetailsProperty, value);
        }

        #endregion

        public SuccessStoryPluginButtonControl()
        {
            InitializeComponent();
        }

        private void PART_PluginButton_Click(object sender, RoutedEventArgs e)
        {
            // Handle button click - could open achievements view or details
        }
    }
}
// --END SUCCESSSTORY--

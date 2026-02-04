// --SUCCESSSTORY--
using System;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible progress bar control for theme integration.
    /// Uses native PlayniteAchievements properties (AchievementCount, UnlockedCount, ProgressPercentage).
    /// Matches the original SuccessStory plugin styling.
    /// </summary>
    public partial class SuccessStoryPluginProgressBarControl : SuccessStoryThemeControlBase
    {
        #region IntegrationShowProgressBarIndicator Property

        public static readonly DependencyProperty IntegrationShowProgressBarIndicatorProperty =
            PlayniteAchievements.Views.ThemeIntegration.Base.DependencyPropertyHelper.RegisterBoolProperty(
                nameof(IntegrationShowProgressBarIndicator),
                typeof(SuccessStoryPluginProgressBarControl));

        public bool IntegrationShowProgressBarIndicator
        {
            get => (bool)GetValue(IntegrationShowProgressBarIndicatorProperty);
            set => SetValue(IntegrationShowProgressBarIndicatorProperty, value);
        }

        #endregion

        #region IntegrationShowProgressBarPercent Property

        public static readonly DependencyProperty IntegrationShowProgressBarPercentProperty =
            PlayniteAchievements.Views.ThemeIntegration.Base.DependencyPropertyHelper.RegisterBoolProperty(
                nameof(IntegrationShowProgressBarPercent),
                typeof(SuccessStoryPluginProgressBarControl));

        public bool IntegrationShowProgressBarPercent
        {
            get => (bool)GetValue(IntegrationShowProgressBarPercentProperty);
            set => SetValue(IntegrationShowProgressBarPercentProperty, value);
        }

        #endregion

        public SuccessStoryPluginProgressBarControl()
        {
            InitializeComponent();
        }
    }
}
// --END SUCCESSSTORY--

// --SUCCESSSTORY--
using System;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible view item control for theme integration.
    /// Matches the original SuccessStory plugin styling.
    /// </summary>
    public partial class SuccessStoryPluginViewItemControl : SuccessStoryThemeControlBase
    {
        #region IntegrationViewItemWithProgressBar Property

        public static readonly DependencyProperty IntegrationViewItemWithProgressBarProperty =
            PlayniteAchievements.Views.ThemeIntegration.Base.DependencyPropertyHelper.RegisterBoolProperty(
                nameof(IntegrationViewItemWithProgressBar),
                typeof(SuccessStoryPluginViewItemControl));

        public bool IntegrationViewItemWithProgressBar
        {
            get => (bool)GetValue(IntegrationViewItemWithProgressBarProperty);
            set => SetValue(IntegrationViewItemWithProgressBarProperty, value);
        }

        #endregion

        public SuccessStoryPluginViewItemControl()
        {
            InitializeComponent();
        }
    }
}
// --END SUCCESSSTORY--

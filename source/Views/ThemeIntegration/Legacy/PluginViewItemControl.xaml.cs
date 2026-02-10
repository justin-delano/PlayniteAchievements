// --SUCCESSSTORY--
using System;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible view item control for theme integration.
    /// Matches the original SuccessStory plugin styling.
    /// </summary>
    public partial class PluginViewItemControl : ThemeControlBase
    {
        #region IntegrationViewItemWithProgressBar Property

        public static readonly DependencyProperty IntegrationViewItemWithProgressBarProperty =
            ThemeIntegration.Base.DependencyPropertyHelper.RegisterBoolProperty(
                nameof(IntegrationViewItemWithProgressBar),
                typeof(PluginViewItemControl));

        public bool IntegrationViewItemWithProgressBar
        {
            get => (bool)GetValue(IntegrationViewItemWithProgressBarProperty);
            set => SetValue(IntegrationViewItemWithProgressBarProperty, value);
        }

        #endregion

        public PluginViewItemControl()
        {
            InitializeComponent();
        }
    }
}
// --END SUCCESSSTORY--

// --SUCCESSSTORY--
using System;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// SuccessStory-compatible compact list control for theme integration.
    /// Matches the original SuccessStory plugin styling.
    /// </summary>
    public partial class SuccessStoryPluginCompactListControl : AchievementThemeControlBase
    {
        #region IconHeight Property

        public static readonly DependencyProperty IconHeightProperty = DependencyProperty.Register(
            nameof(IconHeight),
            typeof(double),
            typeof(SuccessStoryPluginCompactListControl),
            new FrameworkPropertyMetadata(64.0));

        public double IconHeight
        {
            get => (double)GetValue(IconHeightProperty);
            set => SetValue(IconHeightProperty, value);
        }

        #endregion

        #region ShowHiddenIcon Property

        public static readonly DependencyProperty ShowHiddenIconProperty = DependencyProperty.Register(
            nameof(ShowHiddenIcon),
            typeof(bool),
            typeof(SuccessStoryPluginCompactListControl),
            new FrameworkPropertyMetadata(true));

        public bool ShowHiddenIcon
        {
            get => (bool)GetValue(ShowHiddenIconProperty);
            set => SetValue(ShowHiddenIconProperty, value);
        }

        #endregion

        public SuccessStoryPluginCompactListControl()
        {
            InitializeComponent();
            IconHeight = 64.0;
        }
    }
}
// --END SUCCESSSTORY--

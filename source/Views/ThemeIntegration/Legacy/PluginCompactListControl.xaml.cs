// --SUCCESSSTORY--
using System;
using System.Windows;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible compact list control for theme integration.
    /// Matches the original SuccessStory plugin styling.
    /// </summary>
    public partial class PluginCompactListControl : ThemeControlBase
    {
        #region IconHeight Property

        public static readonly DependencyProperty IconHeightProperty = DependencyProperty.Register(
            nameof(IconHeight),
            typeof(double),
            typeof(PluginCompactListControl),
            new FrameworkPropertyMetadata(48.0));

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
            typeof(PluginCompactListControl),
            new FrameworkPropertyMetadata(true));

        public bool ShowHiddenIcon
        {
            get => (bool)GetValue(ShowHiddenIconProperty);
            set => SetValue(ShowHiddenIconProperty, value);
        }

        #endregion

        public PluginCompactListControl()
        {
            InitializeComponent();
            IconHeight = 48.0;
        }
    }
}
// --END SUCCESSSTORY--

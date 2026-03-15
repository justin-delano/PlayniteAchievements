using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Preview control for the progress bar with rarity badges.
    /// Uses the same visual styling as AchievementProgressBarControl but accepts mock data.
    /// </summary>
    public partial class PreviewProgressBarControl : UserControl
    {
        public PreviewProgressBarControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ProgressPercentageProperty =
            DependencyProperty.Register(nameof(ProgressPercentage), typeof(double),
                typeof(PreviewProgressBarControl), new PropertyMetadata(40.0));

        public double ProgressPercentage
        {
            get => (double)GetValue(ProgressPercentageProperty);
            set => SetValue(ProgressPercentageProperty, value);
        }

        public static readonly DependencyProperty MockThemeDataProperty =
            DependencyProperty.Register(nameof(MockThemeData), typeof(MockThemeData),
                typeof(PreviewProgressBarControl), new PropertyMetadata(null));

        public MockThemeData MockThemeData
        {
            get => (MockThemeData)GetValue(MockThemeDataProperty);
            set => SetValue(MockThemeDataProperty, value);
        }
    }
}

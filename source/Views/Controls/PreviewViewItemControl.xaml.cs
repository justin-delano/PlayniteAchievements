using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Preview control for game list view items.
    /// Uses the same visual styling as AchievementViewItemControl but accepts mock data.
    /// </summary>
    public partial class PreviewViewItemControl : UserControl
    {
        public PreviewViewItemControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty UnlockedCountProperty =
            DependencyProperty.Register(nameof(UnlockedCount), typeof(int),
                typeof(PreviewViewItemControl), new PropertyMetadata(2));

        public int UnlockedCount
        {
            get => (int)GetValue(UnlockedCountProperty);
            set => SetValue(UnlockedCountProperty, value);
        }

        public static readonly DependencyProperty AchievementCountProperty =
            DependencyProperty.Register(nameof(AchievementCount), typeof(int),
                typeof(PreviewViewItemControl), new PropertyMetadata(5));

        public int AchievementCount
        {
            get => (int)GetValue(AchievementCountProperty);
            set => SetValue(AchievementCountProperty, value);
        }

        public static readonly DependencyProperty ShowProgressBarProperty =
            DependencyProperty.Register(nameof(ShowProgressBar), typeof(bool),
                typeof(PreviewViewItemControl), new PropertyMetadata(false));

        public bool ShowProgressBar
        {
            get => (bool)GetValue(ShowProgressBarProperty);
            set => SetValue(ShowProgressBarProperty, value);
        }
    }
}

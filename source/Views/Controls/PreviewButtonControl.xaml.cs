using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Preview control for the achievement button.
    /// Uses the same visual styling as AchievementButtonControl but accepts mock data.
    /// </summary>
    public partial class PreviewButtonControl : UserControl
    {
        public PreviewButtonControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty UnlockedCountProperty =
            DependencyProperty.Register(nameof(UnlockedCount), typeof(int),
                typeof(PreviewButtonControl), new PropertyMetadata(2));

        public int UnlockedCount
        {
            get => (int)GetValue(UnlockedCountProperty);
            set => SetValue(UnlockedCountProperty, value);
        }

        public static readonly DependencyProperty AchievementCountProperty =
            DependencyProperty.Register(nameof(AchievementCount), typeof(int),
                typeof(PreviewButtonControl), new PropertyMetadata(5));

        public int AchievementCount
        {
            get => (int)GetValue(AchievementCountProperty);
            set => SetValue(AchievementCountProperty, value);
        }

        public static readonly DependencyProperty IsCompletedProperty =
            DependencyProperty.Register(nameof(IsCompleted), typeof(bool),
                typeof(PreviewButtonControl), new PropertyMetadata(false));

        public bool IsCompleted
        {
            get => (bool)GetValue(IsCompletedProperty);
            set => SetValue(IsCompletedProperty, value);
        }

        public static readonly DependencyProperty DisplayDetailsProperty =
            DependencyProperty.Register(nameof(DisplayDetails), typeof(bool),
                typeof(PreviewButtonControl), new PropertyMetadata(true));

        public bool DisplayDetails
        {
            get => (bool)GetValue(DisplayDetailsProperty);
            set => SetValue(DisplayDetailsProperty, value);
        }

        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double),
                typeof(PreviewButtonControl), new PropertyMetadata(14.0));

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }
    }
}

using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Preview control for achievement stats display.
    /// Uses the same visual styling as AchievementStatsControl but accepts mock data.
    /// </summary>
    public partial class PreviewStatsControl : UserControl
    {
        public PreviewStatsControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty UltraRareUnlockedProperty =
            DependencyProperty.Register(nameof(UltraRareUnlocked), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(1));

        public int UltraRareUnlocked
        {
            get => (int)GetValue(UltraRareUnlockedProperty);
            set => SetValue(UltraRareUnlockedProperty, value);
        }

        public static readonly DependencyProperty UltraRareTotalProperty =
            DependencyProperty.Register(nameof(UltraRareTotal), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(1));

        public int UltraRareTotal
        {
            get => (int)GetValue(UltraRareTotalProperty);
            set => SetValue(UltraRareTotalProperty, value);
        }

        public static readonly DependencyProperty RareUnlockedProperty =
            DependencyProperty.Register(nameof(RareUnlocked), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(1));

        public int RareUnlocked
        {
            get => (int)GetValue(RareUnlockedProperty);
            set => SetValue(RareUnlockedProperty, value);
        }

        public static readonly DependencyProperty RareTotalProperty =
            DependencyProperty.Register(nameof(RareTotal), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(1));

        public int RareTotal
        {
            get => (int)GetValue(RareTotalProperty);
            set => SetValue(RareTotalProperty, value);
        }

        public static readonly DependencyProperty UncommonUnlockedProperty =
            DependencyProperty.Register(nameof(UncommonUnlocked), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(0));

        public int UncommonUnlocked
        {
            get => (int)GetValue(UncommonUnlockedProperty);
            set => SetValue(UncommonUnlockedProperty, value);
        }

        public static readonly DependencyProperty UncommonTotalProperty =
            DependencyProperty.Register(nameof(UncommonTotal), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(1));

        public int UncommonTotal
        {
            get => (int)GetValue(UncommonTotalProperty);
            set => SetValue(UncommonTotalProperty, value);
        }

        public static readonly DependencyProperty CommonUnlockedProperty =
            DependencyProperty.Register(nameof(CommonUnlocked), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(0));

        public int CommonUnlocked
        {
            get => (int)GetValue(CommonUnlockedProperty);
            set => SetValue(CommonUnlockedProperty, value);
        }

        public static readonly DependencyProperty CommonTotalProperty =
            DependencyProperty.Register(nameof(CommonTotal), typeof(int),
                typeof(PreviewStatsControl), new PropertyMetadata(2));

        public int CommonTotal
        {
            get => (int)GetValue(CommonTotalProperty);
            set => SetValue(CommonTotalProperty, value);
        }
    }
}

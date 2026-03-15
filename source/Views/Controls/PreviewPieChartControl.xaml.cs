using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Preview control for pie chart display.
    /// Shows a visual mockup of the pie chart with legend using actual badge icons.
    /// </summary>
    public partial class PreviewPieChartControl : UserControl
    {
        public PreviewPieChartControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty UltraRareTotalProperty =
            DependencyProperty.Register(nameof(UltraRareTotal), typeof(int),
                typeof(PreviewPieChartControl), new PropertyMetadata(1));

        public int UltraRareTotal
        {
            get => (int)GetValue(UltraRareTotalProperty);
            set => SetValue(UltraRareTotalProperty, value);
        }

        public static readonly DependencyProperty RareTotalProperty =
            DependencyProperty.Register(nameof(RareTotal), typeof(int),
                typeof(PreviewPieChartControl), new PropertyMetadata(1));

        public int RareTotal
        {
            get => (int)GetValue(RareTotalProperty);
            set => SetValue(RareTotalProperty, value);
        }

        public static readonly DependencyProperty UncommonTotalProperty =
            DependencyProperty.Register(nameof(UncommonTotal), typeof(int),
                typeof(PreviewPieChartControl), new PropertyMetadata(1));

        public int UncommonTotal
        {
            get => (int)GetValue(UncommonTotalProperty);
            set => SetValue(UncommonTotalProperty, value);
        }

        public static readonly DependencyProperty CommonTotalProperty =
            DependencyProperty.Register(nameof(CommonTotal), typeof(int),
                typeof(PreviewPieChartControl), new PropertyMetadata(2));

        public int CommonTotal
        {
            get => (int)GetValue(CommonTotalProperty);
            set => SetValue(CommonTotalProperty, value);
        }
    }
}

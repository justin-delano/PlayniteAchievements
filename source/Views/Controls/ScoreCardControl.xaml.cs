using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Controls
{
    public partial class ScoreCardControl : UserControl
    {
        public static readonly DependencyProperty ScoreCardProperty =
            DependencyProperty.Register(
                nameof(ScoreCard),
                typeof(ScoreCardViewModel),
                typeof(ScoreCardControl),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsFeaturedProperty =
            DependencyProperty.Register(
                nameof(IsFeatured),
                typeof(bool),
                typeof(ScoreCardControl),
                new PropertyMetadata(false));

        public ScoreCardControl()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler InfoRequested;

        public ScoreCardViewModel ScoreCard
        {
            get => (ScoreCardViewModel)GetValue(ScoreCardProperty);
            set => SetValue(ScoreCardProperty, value);
        }

        public bool IsFeatured
        {
            get => (bool)GetValue(IsFeaturedProperty);
            set => SetValue(IsFeaturedProperty, value);
        }

        private void ScoreInfoButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            InfoRequested?.Invoke(this, e);
        }
    }
}

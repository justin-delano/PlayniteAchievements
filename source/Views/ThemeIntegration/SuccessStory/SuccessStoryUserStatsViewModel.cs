using System.Collections.ObjectModel;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    public class SuccessStoryUserStatsItem
    {
        public string NameShow { get; set; }
        public string ValueShow { get; set; }
    }

    public class SuccessStoryUserStatsViewModel : ObservableObject
    {
        private ObservableCollection<SuccessStoryUserStatsItem> itemsSource = new ObservableCollection<SuccessStoryUserStatsItem>();
        public ObservableCollection<SuccessStoryUserStatsItem> ItemsSource
        {
            get => itemsSource;
            set => SetValue(ref itemsSource, value);
        }
    }
}

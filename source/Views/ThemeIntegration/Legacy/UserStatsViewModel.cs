using System.Collections.ObjectModel;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    public class UserStatsItem
    {
        public string NameShow { get; set; }
        public string ValueShow { get; set; }
    }

    public class UserStatsViewModel : ObservableObject
    {
        private ObservableCollection<UserStatsItem> itemsSource = new ObservableCollection<UserStatsItem>();
        public ObservableCollection<UserStatsItem> ItemsSource
        {
            get => itemsSource;
            set => SetValue(ref itemsSource, value);
        }
    }
}

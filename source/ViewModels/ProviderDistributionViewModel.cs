using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LiveCharts;
using LiveCharts.Wpf;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class ProviderDistributionViewModel : ObservableObject
    {
        public SeriesCollection ProviderSeries { get; } = new SeriesCollection();
        public ObservableCollection<string> ProviderLabels { get; } = new ObservableCollection<string>();

        public void SetProviderData(Dictionary<string, int> achievementsByProvider)
        {
            ProviderSeries.Clear();
            ProviderLabels.Clear();

            if (achievementsByProvider == null || !achievementsByProvider.Any())
                return;

            var sortedProviders = achievementsByProvider
                .OrderByDescending(kvp => kvp.Value)
                .ToList();

            foreach (var provider in sortedProviders)
                ProviderLabels.Add(provider.Key);

            var values = new ChartValues<int>(sortedProviders.Select(kvp => kvp.Value));
            ProviderSeries.Add(new ColumnSeries
            {
                Title = "Achievements",
                Values = values
            });
        }
    }
}

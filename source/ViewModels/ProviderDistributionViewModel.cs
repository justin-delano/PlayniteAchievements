using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class ProviderDistributionViewModel : ObservableObject
    {
        public SeriesCollection ProviderSeries { get; } = new SeriesCollection();

        public void SetProviderData(Dictionary<string, int> achievementsByProvider)
        {
            ProviderSeries.Clear();

            if (achievementsByProvider == null || !achievementsByProvider.Any())
                return;

            foreach (var provider in achievementsByProvider.OrderByDescending(kvp => kvp.Value))
            {
                ProviderSeries.Add(new PieSeries
                {
                    Title = provider.Key,
                    Values = new ChartValues<double> { provider.Value },
                    DataLabels = false
                });
            }
        }
    }
}

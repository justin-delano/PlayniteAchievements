using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class PieChartViewModel : ObservableObject
    {
        public SeriesCollection PieSeries { get; } = new SeriesCollection();

        public void SetGameData(int totalGames, int perfectGames, string perfectLabel, string incompleteLabel)
        {
            PieSeries.Clear();
            if (totalGames == 0) return;

            PieSeries.Add(new PieSeries
            {
                Title = perfectLabel,
                Values = new ChartValues<double> { perfectGames },
                Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                DataLabels = true
            });

            var incomplete = totalGames - perfectGames;
            if (incomplete > 0)
            {
                PieSeries.Add(new PieSeries
                {
                    Title = incompleteLabel,
                    Values = new ChartValues<double> { incomplete },
                    Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)),
                    DataLabels = true
                });
            }
        }

        public void SetRarityData(int common, int uncommon, int rare, int ultraRare, int locked,
            string commonLabel, string uncommonLabel, string rareLabel, string ultraRareLabel, string lockedLabel)
        {
            PieSeries.Clear();

            AddPieSection(ultraRareLabel, ultraRare, Color.FromRgb(156, 39, 176));
            AddPieSection(rareLabel, rare, Color.FromRgb(255, 193, 7));
            AddPieSection(uncommonLabel, uncommon, Color.FromRgb(158, 158, 158));
            AddPieSection(commonLabel, common, Color.FromRgb(139, 69, 19));
            AddPieSection(lockedLabel, locked, Color.FromRgb(97, 97, 97));
        }

        private void AddPieSection(string title, int value, Color color)
        {
            if (value > 0)
            {
                PieSeries.Add(new PieSeries
                {
                    Title = title,
                    Values = new ChartValues<double> { value },
                    Fill = new SolidColorBrush(color),
                    DataLabels = true
                });
            }
        }
    }
}

using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Common;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class PieChartViewModel : ObservableObject
    {
        public SeriesCollection PieSeries { get; } = new SeriesCollection();

        // Rarity count properties for custom legend
        private int _ultraRareCount;
        public int UltraRareCount
        {
            get => _ultraRareCount;
            private set => SetValue(ref _ultraRareCount, value);
        }

        private int _rareCount;
        public int RareCount
        {
            get => _rareCount;
            private set => SetValue(ref _rareCount, value);
        }

        private int _uncommonCount;
        public int UncommonCount
        {
            get => _uncommonCount;
            private set => SetValue(ref _uncommonCount, value);
        }

        private int _commonCount;
        public int CommonCount
        {
            get => _commonCount;
            private set => SetValue(ref _commonCount, value);
        }

        private int _lockedCount;
        public int LockedCount
        {
            get => _lockedCount;
            private set => SetValue(ref _lockedCount, value);
        }

        public bool ShowUltraRare => UltraRareCount > 0;
        public bool ShowRare => RareCount > 0;
        public bool ShowUncommon => UncommonCount > 0;
        public bool ShowCommon => CommonCount > 0;
        public bool ShowLocked => LockedCount > 0;

        public void SetGameData(int totalGames, int perfectGames, string perfectLabel, string incompleteLabel)
        {
            PieSeries.Clear();
            if (totalGames == 0) return;

            // Perfect games - use rainbow gradient purple color (from FillRainbow gradient)
            PieSeries.Add(new PieSeries
            {
                Title = perfectLabel,
                Values = new ChartValues<double> { perfectGames },
                Fill = new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                DataLabels = false
            });

            var incomplete = totalGames - perfectGames;
            if (incomplete > 0)
            {
                PieSeries.Add(new PieSeries
                {
                    Title = incompleteLabel,
                    Values = new ChartValues<double> { incomplete },
                    Fill = new SolidColorBrush(Color.FromArgb(60, 158, 158, 158)),
                    DataLabels = false
                });
            }
        }

        public void SetRarityData(int common, int uncommon, int rare, int ultraRare, int locked,
            string commonLabel, string uncommonLabel, string rareLabel, string ultraRareLabel, string lockedLabel)
        {
            PieSeries.Clear();

            // Update count properties for legend
            UltraRareCount = ultraRare;
            RareCount = rare;
            UncommonCount = uncommon;
            CommonCount = common;
            LockedCount = locked;

            // Notify visibility properties changed
            OnPropertyChanged(nameof(ShowUltraRare));
            OnPropertyChanged(nameof(ShowRare));
            OnPropertyChanged(nameof(ShowUncommon));
            OnPropertyChanged(nameof(ShowCommon));
            OnPropertyChanged(nameof(ShowLocked));

            AddPieSection(ultraRareLabel, ultraRare, Color.FromRgb(135, 206, 250));
            AddPieSection(rareLabel, rare, Color.FromRgb(255, 193, 7));
            AddPieSection(uncommonLabel, uncommon, Color.FromRgb(158, 158, 158));
            AddPieSection(commonLabel, common, Color.FromRgb(139, 69, 19));
            AddPieSection(lockedLabel, locked, Color.FromArgb(60, 97, 97, 97));
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
                    DataLabels = false
                });
            }
        }
    }
}

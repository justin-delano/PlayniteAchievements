using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.PieChart;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class PieChartViewModel : ObservableObject
    {
        public SeriesCollection PieSeries { get; } = new SeriesCollection();
        public ObservableCollection<LegendItem> LegendItems { get; } = new ObservableCollection<LegendItem>();

        /// <summary>
        /// Sets the pie chart data for Games completion (Perfect vs Incomplete).
        /// </summary>
        public void SetGameData(int totalGames, int perfectGames, string perfectLabel, string incompleteLabel)
        {
            PieSeries.Clear();
            LegendItems.Clear();

            if (totalGames == 0) return;

            var incomplete = totalGames - perfectGames;

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color)>();
            dataPoints.Add((perfectLabel, perfectGames, "BadgePerfectGame", Color.FromRgb(33, 150, 243)));
            // Incomplete/locked not shown in pie chart (transparent), but added to legend below

            ApplyMinimumVisibilityRule(dataPoints);

            // Add incomplete/locked to legend only (not shown in pie chart)
            if (incomplete > 0)
            {
                LegendItems.Add(new LegendItem
                {
                    Label = incompleteLabel,
                    Count = incomplete,
                    IconKey = "BadgeLocked",
                    ColorHex = "#666666"
                });
            }
        }

        /// <summary>
        /// Sets the pie chart data for Rarity distribution (Ultra Rare, Rare, Uncommon, Common, Locked).
        /// </summary>
        public void SetRarityData(int common, int uncommon, int rare, int ultraRare, int locked,
            string commonLabel, string uncommonLabel, string rareLabel, string ultraRareLabel, string lockedLabel)
        {
            PieSeries.Clear();
            LegendItems.Clear();

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color)>();
            dataPoints.Add((ultraRareLabel, ultraRare, "BadgePlatinumHexagon", Color.FromRgb(135, 206, 250)));
            dataPoints.Add((rareLabel, rare, "BadgeGoldPentagon", Color.FromRgb(255, 193, 7)));
            dataPoints.Add((uncommonLabel, uncommon, "BadgeSilverSquare", Color.FromRgb(158, 158, 158)));
            dataPoints.Add((commonLabel, common, "BadgeBronzeTriangle", Color.FromRgb(139, 69, 19)));
            // Locked not shown in pie chart (transparent), but added to legend below

            // Filter to only include non-zero values
            dataPoints = dataPoints.Where(d => d.Count > 0).ToList();

            ApplyMinimumVisibilityRule(dataPoints);

            // Add locked to legend only (not shown in pie chart)
            if (locked > 0)
            {
                LegendItems.Add(new LegendItem
                {
                    Label = lockedLabel,
                    Count = locked,
                    IconKey = "BadgeLocked",
                    ColorHex = "#616161"
                });
            }
        }

        /// <summary>
        /// Sets the pie chart data for Provider distribution.
        /// </summary>
        /// <param name="unlockedByProvider">Dictionary of provider name to unlocked count</param>
        /// <param name="totalLocked">Total locked achievements</param>
        /// <param name="lockedLabel">Localized label for locked achievements</param>
        /// <param name="providerLookup">Dictionary of provider name to (iconKey, colorHex) tuple</param>
        public void SetProviderData(
            Dictionary<string, int> unlockedByProvider,
            int totalLocked,
            string lockedLabel,
            Dictionary<string, (string iconKey, string colorHex)> providerLookup)
        {
            PieSeries.Clear();
            LegendItems.Clear();

            if (unlockedByProvider == null || !unlockedByProvider.Any())
                return;

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color)>();

            // Sort providers by count descending
            foreach (var provider in unlockedByProvider.OrderByDescending(p => p.Value))
            {
                var providerName = provider.Key;
                var count = provider.Value;

                // Look up provider color (no icons yet, so we use empty string)
                string colorHex = "#888888";
                if (providerLookup != null && providerLookup.TryGetValue(providerName, out var metadata))
                {
                    colorHex = metadata.colorHex;
                }

                // Parse color hex to Color
                if (ColorConverter.ConvertFromString(colorHex) is Color color)
                {
                    // Use empty icon key - no provider icons until you add them
                    dataPoints.Add((providerName, count, string.Empty, color));
                }
                else
                {
                    dataPoints.Add((providerName, count, string.Empty, Colors.Gray));
                }
            }

            // Locked is not shown in pie chart (transparent), but added to legend below

            ApplyMinimumVisibilityRule(dataPoints);

            // Add locked to legend only (not shown in pie chart)
            if (totalLocked > 0)
            {
                LegendItems.Add(new LegendItem
                {
                    Label = lockedLabel,
                    Count = totalLocked,
                    IconKey = "BadgeLocked",
                    ColorHex = "#616161"
                });
            }
        }

        private void ApplyMinimumVisibilityRule(List<(string Label, int Count, string IconKey, Color Color)> dataPoints)
        {
            if (dataPoints.Count == 0) return;

            var totalCount = dataPoints.Sum(d => d.Count);
            var minSlice = totalCount * 0.05;
            var adjustments = new Dictionary<int, double>();
            var totalAdjustment = 0.0;

            // First pass: identify slices below 5% and calculate adjustments
            for (int i = 0; i < dataPoints.Count; i++)
            {
                if (dataPoints[i].Count < minSlice && dataPoints[i].Count > 0)
                {
                    var adjustment = minSlice - dataPoints[i].Count;
                    adjustments[i] = adjustment;
                    totalAdjustment += adjustment;
                }
            }

            // Find the largest slice to subtract from (preferably locked if it exists)
            int largestSliceIndex = dataPoints
                .Select((d, i) => (d.Count, i))
                .OrderByDescending(x => x.Count)
                .First().Item2;

            // Create pie series and legend items
            for (int i = 0; i < dataPoints.Count; i++)
            {
                double chartValue;
                if (adjustments.ContainsKey(i))
                {
                    // Small slice: bump to 5%
                    chartValue = minSlice;
                }
                else if (i == largestSliceIndex && totalAdjustment > 0)
                {
                    // Largest slice: subtract the total adjustment
                    chartValue = dataPoints[i].Count - totalAdjustment;
                }
                else
                {
                    // Normal slice: use actual value
                    chartValue = dataPoints[i].Count;
                }

                PieSeries.Add(new PieSeries
                {
                    Title = dataPoints[i].Label,
                    Values = new ChartValues<double> { chartValue },
                    Fill = new SolidColorBrush(dataPoints[i].Color),
                    DataLabels = false
                });

                // Add to legend (always show, even if count is 0)
                LegendItems.Add(new LegendItem
                {
                    Label = dataPoints[i].Label,
                    Count = dataPoints[i].Count,
                    IconKey = dataPoints[i].IconKey,
                    ColorHex = $"#{dataPoints[i].Color.R:X2}{dataPoints[i].Color.G:X2}{dataPoints[i].Color.B:X2}"
                });
            }
        }
    }
}

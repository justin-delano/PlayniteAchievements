using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class PieChartViewModel : ObservableObject
    {
        public SeriesCollection PieSeries { get; } = new SeriesCollection();
        public ObservableCollection<LegendItem> LegendItems { get; } = new ObservableCollection<LegendItem>();

        // Consistent transparent locked color for all pie charts
        private static readonly Color LockedTransparent = Color.FromArgb(0, 102, 102, 102);
        private const string LockedLegendColor = "#666666";

        /// <summary>
        /// Sets the pie chart data for Games completion (Perfect vs Incomplete).
        /// </summary>
        public void SetGameData(int totalGames, int perfectGames, string perfectLabel, string incompleteLabel)
        {
            PieSeries.Clear();
            LegendItems.Clear();

            if (totalGames == 0) return;

            var incomplete = totalGames - perfectGames;

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex)>();
            dataPoints.Add((perfectLabel, perfectGames, "BadgePerfectGame", Color.FromRgb(33, 150, 243), string.Empty));
            if (incomplete > 0)
                dataPoints.Add((incompleteLabel, incomplete, "BadgeLocked", LockedTransparent, string.Empty));

            ApplyMinimumVisibilityRule(dataPoints);
        }

        /// <summary>
        /// Sets the pie chart data for Rarity distribution (Ultra Rare, Rare, Uncommon, Common, Locked).
        /// </summary>
        public void SetRarityData(int common, int uncommon, int rare, int ultraRare, int locked,
            string commonLabel, string uncommonLabel, string rareLabel, string ultraRareLabel, string lockedLabel)
        {
            PieSeries.Clear();
            LegendItems.Clear();

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex)>();
            dataPoints.Add((ultraRareLabel, ultraRare, "BadgePlatinumHexagon", Color.FromRgb(135, 206, 250), string.Empty));
            dataPoints.Add((rareLabel, rare, "BadgeGoldPentagon", Color.FromRgb(255, 193, 7), string.Empty));
            dataPoints.Add((uncommonLabel, uncommon, "BadgeSilverSquare", Color.FromRgb(158, 158, 158), string.Empty));
            dataPoints.Add((commonLabel, common, "BadgeBronzeTriangle", Color.FromRgb(139, 69, 19), string.Empty));
            if (locked > 0)
                dataPoints.Add((lockedLabel, locked, "BadgeLocked", LockedTransparent, string.Empty));

            // Filter to only include non-zero values
            dataPoints = dataPoints.Where(d => d.Count > 0).ToList();

            ApplyMinimumVisibilityRule(dataPoints);
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

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex)>();

            // Sort providers by count descending
            foreach (var provider in unlockedByProvider.OrderByDescending(p => p.Value))
            {
                var providerName = provider.Key;
                var count = provider.Value;

                // Look up provider icon and color
                string colorHex = "#888888";
                string iconKey = string.Empty;
                if (providerLookup != null && providerLookup.TryGetValue(providerName, out var metadata))
                {
                    colorHex = metadata.colorHex;
                    iconKey = metadata.iconKey ?? string.Empty;
                }

                // Parse color hex to Color
                if (ColorConverter.ConvertFromString(colorHex) is Color color)
                {
                    dataPoints.Add((providerName, count, iconKey, color, colorHex));
                }
                else
                {
                    dataPoints.Add((providerName, count, iconKey, Colors.Gray, "#888888"));
                }
            }

            if (totalLocked > 0)
                dataPoints.Add((lockedLabel, totalLocked, "BadgeLocked", LockedTransparent, string.Empty));

            ApplyMinimumVisibilityRule(dataPoints);
        }

        private void ApplyMinimumVisibilityRule(List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex)> dataPoints)
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

                // Determine legend color: use OriginalColorHex for providers, otherwise generate from Color
                string legendColor;
                if (!string.IsNullOrEmpty(dataPoints[i].OriginalColorHex))
                {
                    // Use original color hex from provider (e.g., "#B0B0B0")
                    legendColor = dataPoints[i].OriginalColorHex;
                }
                else if (dataPoints[i].IconKey == "BadgeLocked")
                {
                    // Locked items use consistent color
                    legendColor = LockedLegendColor;
                }
                else
                {
                    // Generate from Color (for badges)
                    legendColor = $"#{dataPoints[i].Color.R:X2}{dataPoints[i].Color.G:X2}{dataPoints[i].Color.B:X2}";
                }

                LegendItems.Add(new LegendItem
                {
                    Label = dataPoints[i].Label,
                    Count = dataPoints[i].Count,
                    IconKey = dataPoints[i].IconKey,
                    ColorHex = legendColor
                });
            }
        }
    }
}
